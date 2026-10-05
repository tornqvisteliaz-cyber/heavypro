using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

public sealed class AxisState
{
    public double PreviousInput { get; set; }
    public double Output { get; set; }
    public double Velocity { get; set; }
    public double Acceleration { get; set; }
    public double ReleaseTimer { get; set; }
}

public readonly record struct AxisTune(double Acceleration, double MaxAccel, double MaxVelocity, double Damping, double ReverseBrake);

/// <summary>
/// Processes incoming, normalized joystick axes with per-axis rate and acceleration limits.
/// It changes only the control commands sent to MSFS through standard SimConnect axis events.
/// </summary>
public class InputDynamicsEngine
{
    private readonly AxisState _pitch = new();
    private readonly AxisState _roll = new();
    private readonly AxisState _yaw = new();
    private readonly AxisState _throttle = new();
    private readonly Queue<(double T, double Diff)> _diffs = new();
    private double _clock;
    private bool _wasGround = true;

    public ControlFrame Last { get; private set; } = ControlFrame.Idle;

    public ControlFrame Step(AppSettings settings, FlightSnapshot snap, bool masterEnable, double dt, string? selectedProfile = null)
    {
        dt = Numeric.Clamp(dt, 0.001, 0.05);
        if (!masterEnable || snap.AutopilotMaster)
        {
            Reset();
            Last = ControlFrame.Idle with { Reason = !masterEnable ? "Master off" : "Autopilot on — writes paused" };
            return Last;
        }

        var profile = InputDynamicsProfiles.Resolve(snap, selectedProfile ?? settings.AircraftProfile);
        var ground = snap.OnGround;
        var flare = !ground && snap.AltitudeAglFeet is > 5 and < 80;
        var touched = _wasGround == false && ground;
        _wasGround = ground;
        _clock += dt;

        var rawPitch = Clamp(snap.HasRawElevatorInput ? snap.RawElevatorInput : snap.StickY);
        var rawRoll = Clamp(snap.HasRawAileronInput ? snap.RawAileronInput : snap.StickX);
        var rawYaw = Clamp(snap.HasRawRudderInput ? snap.RawRudderInput : snap.RudderPedal);
        var curve = settings.InputCurve;
        var deadzone = Numeric.Clamp(settings.InputDeadzonePercent / 100.0, 0, 0.2);
        var expo = Numeric.Clamp(settings.InputExpoPercent / 100.0, 0, 1);
        var filteredPitch = Shape(rawPitch, deadzone, profile.Elevator.Sensitivity, expo, curve);
        var filteredRoll = Shape(rawRoll, deadzone, profile.Aileron.Sensitivity, expo, curve);
        var filteredYaw = Shape(rawYaw, deadzone, profile.Rudder.Sensitivity, expo, curve);

        var airspeedFactor = settings.AirspeedScheduling ? SpeedFactor(snap.AirspeedIndicatedKnots, profile) : 1.0;
        var response = Numeric.Clamp(0.75 + settings.ControlResponse / 100.0 * 0.75, 0.4, 1.5);
        var inertia = Numeric.Clamp(1.0 - settings.Inertia / 100.0 * 0.30, 0.55, 1.0);
        var feel = FeelMultiplier(settings.FeelLevel);
        var tunePitch = Tune(profile.Elevator, settings.ElevatorRateLimit, response, inertia * feel, airspeedFactor, settings.PitchDamping, flare, touched, ground, settings.StickReleaseMode);
        var tuneRoll = Tune(profile.Aileron, settings.AileronRateLimit, response, inertia * feel, airspeedFactor, settings.RollDamping, false, touched, ground, settings.StickReleaseMode);
        var tuneYaw = Tune(profile.Rudder, settings.RudderRateLimit, response, inertia * feel, airspeedFactor, settings.YawDamping, false, touched, ground, settings.StickReleaseMode);
        var releaseMode = settings.StickReleaseMode == "Aircraft Profile" ? profile.ReleaseMode : settings.StickReleaseMode;

        if (settings.InputDynamicsEnabled)
        {
            StepAxis(_pitch, filteredPitch, dt, tunePitch, releaseMode, profile.ReturnDelaySeconds, touched);
            StepAxis(_roll, filteredRoll, dt, tuneRoll, releaseMode, profile.ReturnDelaySeconds, touched);
            StepAxis(_yaw, filteredYaw, dt, tuneYaw, releaseMode, profile.ReturnDelaySeconds, touched);
        }
        else
        {
            _pitch.Output = filteredPitch;
            _roll.Output = filteredRoll;
            _yaw.Output = filteredYaw;
            _pitch.Velocity = _roll.Velocity = _yaw.Velocity = 0;
        }
        var thrIn = Clamp01(snap.Throttle1Percent / 100.0);
        _throttle.Output = thrIn;

        var moving = Math.Abs(rawPitch) + Math.Abs(rawRoll) + Math.Abs(rawYaw) > 0.035;
        var settling = Math.Abs(_pitch.Output) + Math.Abs(_roll.Output) + Math.Abs(_yaw.Output) > 0.01;
        if (!moving && !settling)
        {
            Reset();
            Last = ControlFrame.Idle with { Reason = "Stick centred — no writes" };
            return Last;
        }

        var diff = (Math.Abs(filteredPitch - _pitch.Output) + Math.Abs(filteredRoll - _roll.Output) + Math.Abs(filteredYaw - _yaw.Output)) / 3.0;
        _diffs.Enqueue((_clock, diff));
        while (_diffs.Count > 0 && _clock - _diffs.Peek().T > 5)
            _diffs.Dequeue();
        var avg = _diffs.Count == 0 ? 0 : _diffs.Average(x => x.Diff);
        var max = _diffs.Count == 0 ? 0 : _diffs.Max(x => x.Diff);
        var effect = avg > 0.015;
        var addon = snap.Aircraft.IsFenixA320 || snap.Aircraft.IsPmdg777 || snap.Aircraft.IsPmdg737;

        Last = new ControlFrame
        {
            Active = true,
            Profile = profile.Name,
            RawPitch = rawPitch,
            FilteredPitch = filteredPitch,
            PitchIn = filteredPitch,
            PitchOut = _pitch.Output,
            PitchVelocity = _pitch.Velocity,
            PitchAccel = _pitch.Acceleration,
            RawRoll = rawRoll,
            FilteredRoll = filteredRoll,
            RollIn = filteredRoll,
            RollOut = _roll.Output,
            RawYaw = rawYaw,
            FilteredYaw = filteredYaw,
            YawIn = filteredYaw,
            YawOut = _yaw.Output,
            ThrottleIn = thrIn,
            ThrottleOut = thrIn,
            WeightFactor = WeightFactor(snap, profile.ReferenceWeightLb),
            SpeedFactor = airspeedFactor,
            Damping = tunePitch.Damping,
            AverageDifference = avg,
            MaxDifference = max,
            EffectDetected = effect,
            AddonMayIgnore = addon,
            Reason = addon ? "Add-on may ignore generic SimConnect axis events" : effect ? $"{profile.Name} input dynamics" : "Input dynamics active"
        };
        return Last;
    }

    public void Reset()
    {
        ResetAxis(_pitch);
        ResetAxis(_roll);
        ResetAxis(_yaw);
        ResetAxis(_throttle);
        _diffs.Clear();
    }

    private static void ResetAxis(AxisState state)
    {
        state.Output = state.Velocity = state.Acceleration = state.ReleaseTimer = state.PreviousInput = 0;
    }

    private static void StepAxis(AxisState state, double target, double dt, AxisTune tune, string releaseMode, double returnDelay, bool brakeNow)
    {
        target = Clamp(target);
        if (Math.Abs(target) < 0.0001)
        {
            state.ReleaseTimer += dt;
            if (releaseMode == "Delayed" && state.ReleaseTimer < returnDelay)
            {
                state.Velocity *= Math.Pow(tune.Damping, dt * 60.0);
                state.PreviousInput = target;
                return;
            }
        }
        else
            state.ReleaseTimer = 0;

        if (brakeNow)
            state.Velocity *= 0.25;
        var error = target - state.Output;
        var maxRate = tune.MaxVelocity;
        var desiredVelocity = Numeric.Clamp(error * tune.Acceleration, -maxRate, maxRate);
        var accelerating = Math.Abs(desiredVelocity) > Math.Abs(state.Velocity) && Math.Sign(desiredVelocity) == Math.Sign(state.Velocity);
        var accelLimit = accelerating ? tune.MaxAccel : tune.MaxAccel * tune.ReverseBrake;
        if (releaseMode == "Immediate" && Math.Abs(target) < 0.0001)
        {
            state.Output = 0;
            state.Velocity = state.Acceleration = 0;
            state.PreviousInput = target;
            return;
        }

        state.Acceleration = Numeric.Clamp((desiredVelocity - state.Velocity) / dt, -accelLimit, accelLimit);
        state.Velocity += state.Acceleration * dt;
        state.Velocity *= Math.Pow(tune.Damping, dt * 60.0);
        state.Velocity = Numeric.Clamp(state.Velocity, -maxRate, maxRate);
        var next = state.Output + state.Velocity * dt;
        if ((target - state.Output) * (target - next) <= 0 || Math.Abs(error) < 0.0005)
        {
            next = target;
            state.Velocity = 0;
            state.Acceleration = 0;
        }
        state.Output = Clamp(next);
        state.PreviousInput = target;
    }

    private static AxisTune Tune(AxisDynamicsProfile profile, double rateLimit, double response, double inertia, double speed, double dampingSetting, bool flare, bool touched, bool ground, string release)
    {
        var baseUiRate = profile.MaxRate == 0 ? 1 : profile.MaxRate;
        var referenceUiRate = ReferenceUiRate(profile);
        var rate = baseUiRate * Numeric.Clamp(rateLimit / referenceUiRate, 0.15, 3.0);
        var rateScale = response * inertia * speed;
        if (ground && profile.GroundRateMultiplier > 0)
            rateScale *= profile.GroundRateMultiplier;
        var dampingControl = Numeric.Clamp(dampingSetting / 100.0, 0, 1);
        var damping = Numeric.Clamp(profile.Damping + dampingControl * 0.035 + (flare ? 0.025 : 0) + (ground ? 0.015 : 0), 0.82, 0.999);
        var acceleration = profile.ResponseRate * response * inertia;
        return new AxisTune(acceleration, profile.AccelerationLimit * response * inertia, rate * rateScale, damping, profile.DecelerationRatio);
    }

    private static double ReferenceUiRate(AxisDynamicsProfile profile) => profile.MaxRate switch
    {
        <= 1.0 => 1.0,
        <= 1.4 => 1.2,
        <= 2.0 => 1.0,
        _ => 1.8
    };

    private static double Shape(double value, double deadzone, double sensitivity, double expo, string curve)
    {
        var sign = Math.Sign(value);
        var magnitude = Math.Abs(value);
        if (magnitude <= deadzone)
            return 0;
        magnitude = (magnitude - deadzone) / (1 - deadzone);
        magnitude = Numeric.Clamp(magnitude * sensitivity, 0, 1);
        var shaped = curve switch
        {
            "Linear" => magnitude,
            "S-curve" => magnitude * magnitude * (3 - 2 * magnitude),
            _ => magnitude * (1 - expo) + magnitude * magnitude * magnitude * expo
        };
        return sign * Numeric.Clamp(shaped, 0, 1);
    }

    private static double FeelMultiplier(string feel) => feel switch
    {
        "Normal" => 1.25,
        "Medium" => 1.0,
        "Realistic" => 0.92,
        "RealisticPlus" => 0.88,
        _ => 0.95
    };

    private static double WeightFactor(FlightSnapshot snap, double referenceWeight)
    {
        if (snap.TotalWeightPounds < 500)
            return 1;
        return Numeric.Clamp(snap.TotalWeightPounds / referenceWeight, 0.65, 1.8);
    }

    public static double WeightFactor(FlightSnapshot snap, PhysicsProfile profile) => WeightFactor(snap, profile.ReferenceWeightLb);

    private static double SpeedFactor(double iasKnots, InputDynamicsProfile profile)
    {
        var t = Numeric.Clamp((iasKnots - profile.ScheduleStartKnots) / Math.Max(1, profile.ScheduleEndKnots - profile.ScheduleStartKnots), 0, 1);
        return 1.0 - t * profile.HighSpeedRateReduction;
    }

    public static double SpeedFactor(double iasKnots) => 1.0 - Numeric.Clamp((iasKnots - 70) / 230.0, 0, 1) * 0.45;
    private static double Clamp(double value) => Numeric.Clamp(value, -1, 1);
    private static double Clamp01(double value) => Numeric.Clamp(value, 0, 1);
}

/// <summary>Compatibility name for callers built against the earlier engine.</summary>
public sealed class ControlEngine : InputDynamicsEngine { }

public sealed record ControlFrame
{
    public bool Active { get; init; }
    public string Profile { get; init; } = "";
    public double RawPitch { get; init; }
    public double FilteredPitch { get; init; }
    public double PitchIn { get; init; }
    public double PitchOut { get; init; }
    public double PitchVelocity { get; init; }
    public double PitchAccel { get; init; }
    public double RawRoll { get; init; }
    public double FilteredRoll { get; init; }
    public double RollIn { get; init; }
    public double RollOut { get; init; }
    public double RawYaw { get; init; }
    public double FilteredYaw { get; init; }
    public double YawIn { get; init; }
    public double YawOut { get; init; }
    public double ThrottleIn { get; init; }
    public double ThrottleOut { get; init; }
    public double WeightFactor { get; init; }
    public double SpeedFactor { get; init; }
    public double Damping { get; init; }
    public double AverageDifference { get; init; }
    public double MaxDifference { get; init; }
    public bool EffectDetected { get; init; }
    public bool AddonMayIgnore { get; init; }
    public string Reason { get; init; } = "";
    public static ControlFrame Idle { get; } = new();
}

public sealed record InputDynamicsProfile
{
    public string Name { get; init; } = "Generic Airliner";
    public double ReferenceWeightLb { get; init; } = 150000;
    public AxisDynamicsProfile Elevator { get; init; } = new();
    public AxisDynamicsProfile Aileron { get; init; } = new();
    public AxisDynamicsProfile Rudder { get; init; } = new();
    public double ScheduleStartKnots { get; init; } = 70;
    public double ScheduleEndKnots { get; init; } = 300;
    public double HighSpeedRateReduction { get; init; } = 0.35;
    public double ReturnDelaySeconds { get; init; } = 0.1;
    public string ReleaseMode { get; init; } = "Damped";
}

public sealed record AxisDynamicsProfile
{
    public double ResponseRate { get; init; } = 3;
    public double AccelerationLimit { get; init; } = 5;
    public double DecelerationRatio { get; init; } = 0.8;
    public double Damping { get; init; } = 0.96;
    public double Sensitivity { get; init; } = 1;
    public double MaxRate { get; init; } = 1.2;
    public double GroundRateMultiplier { get; init; } = 1;
}

public static class InputDynamicsProfiles
{
    private static AxisDynamicsProfile Axis(double response, double accel, double maxRate, double damping, double sensitivity = 1, double ground = 1) =>
        new() { ResponseRate = response, AccelerationLimit = accel, MaxRate = maxRate, Damping = damping, Sensitivity = sensitivity, GroundRateMultiplier = ground };

    public static InputDynamicsProfile GenericGa { get; } = new()
    {
        Name = "Generic GA", ReferenceWeightLb = 2400, ScheduleStartKnots = 45, ScheduleEndKnots = 180, HighSpeedRateReduction = 0.28,
        Elevator = Axis(4.0, 8.0, 2.0, 0.94), Aileron = Axis(4.5, 9.0, 2.6, 0.93), Rudder = Axis(3.2, 7.0, 1.7, 0.93, 0.95, 1.12)
    };
    public static InputDynamicsProfile GenericAirliner { get; } = new()
    {
        Name = "Generic Airliner", ReferenceWeightLb = 150000, ScheduleStartKnots = 90, ScheduleEndKnots = 340, HighSpeedRateReduction = 0.40,
        Elevator = Axis(2.8, 5.0, 1.15, 0.965), Aileron = Axis(3.0, 5.5, 1.65, 0.96), Rudder = Axis(2.2, 4.5, 0.95, 0.965)
    };
    public static InputDynamicsProfile FenixA320 { get; } = new()
    {
        Name = "Fenix A320", ReferenceWeightLb = 154000, ScheduleStartKnots = 90, ScheduleEndKnots = 340, HighSpeedRateReduction = 0.42,
        Elevator = Axis(2.5, 4.7, 1.0, 0.972), Aileron = Axis(3.0, 5.0, 1.55, 0.968), Rudder = Axis(2.0, 4.0, 0.82, 0.972)
    };
    public static InputDynamicsProfile Pmdg737 { get; } = new()
    {
        Name = "PMDG 737", ReferenceWeightLb = 145000, ScheduleStartKnots = 90, ScheduleEndKnots = 320, HighSpeedRateReduction = 0.40,
        Elevator = Axis(2.6, 4.8, 1.05, 0.968), Aileron = Axis(3.0, 5.2, 1.6, 0.965), Rudder = Axis(2.1, 4.0, 0.88, 0.968)
    };
    public static InputDynamicsProfile Pmdg777 { get; } = new()
    {
        Name = "PMDG 777", ReferenceWeightLb = 500000, ScheduleStartKnots = 100, ScheduleEndKnots = 360, HighSpeedRateReduction = 0.45, ReleaseMode = "Delayed", ReturnDelaySeconds = 0.16,
        Elevator = Axis(2.0, 3.8, 0.72, 0.978), Aileron = Axis(2.4, 4.2, 1.25, 0.975), Rudder = Axis(1.8, 3.5, 0.68, 0.978)
    };
    public static InputDynamicsProfile Asobo787 { get; } = new()
    {
        Name = "Asobo 787", ReferenceWeightLb = 500000, ScheduleStartKnots = 100, ScheduleEndKnots = 360, HighSpeedRateReduction = 0.43, ReleaseMode = "Delayed", ReturnDelaySeconds = 0.12,
        Elevator = Axis(2.2, 4.0, 0.85, 0.976), Aileron = Axis(2.6, 4.5, 1.4, 0.972), Rudder = Axis(1.9, 3.7, 0.75, 0.976)
    };

    public static InputDynamicsProfile Resolve(FlightSnapshot snap, string? selection)
    {
        if (!string.IsNullOrWhiteSpace(selection) && selection != "Auto Detect")
        {
            var selected = selection.Trim();
            if (selected.Equals("Generic GA", StringComparison.OrdinalIgnoreCase)) return GenericGa;
            if (selected.Equals("Generic Airliner", StringComparison.OrdinalIgnoreCase) || selected.Equals("Generic", StringComparison.OrdinalIgnoreCase)) return GenericAirliner;
            if (selected.Equals("Fenix A320", StringComparison.OrdinalIgnoreCase)) return FenixA320;
            if (selected.Equals("PMDG 737", StringComparison.OrdinalIgnoreCase)) return Pmdg737;
            if (selected.Equals("PMDG 777", StringComparison.OrdinalIgnoreCase)) return Pmdg777;
            if (selected.Equals("Asobo 787", StringComparison.OrdinalIgnoreCase)) return Asobo787;
        }

        if (snap.Aircraft.IsFenixA320) return FenixA320;
        if (snap.Aircraft.IsPmdg737) return Pmdg737;
        if (snap.Aircraft.IsPmdg777) return Pmdg777;
        if (snap.Aircraft.IsAsobo787) return Asobo787;
        return snap.Class switch
        {
            AircraftClass.LightGa => GenericGa,
            AircraftClass.Regional => GenericAirliner with { Name = "Generic Turboprop", ReferenceWeightLb = 12000 },
            _ => GenericAirliner
        };
    }
}

public readonly record struct PhysicsProfile(string Name, double ReferenceWeightLb, double PitchAccel, double RollAccel, double YawAccel, double ThrottleAccel, double MaxVel, double Damping, double Reverse);

public static class AircraftProfiles
{
    public static PhysicsProfile For(FlightSnapshot snap)
    {
        var p = InputDynamicsProfiles.Resolve(snap, "Auto Detect");
        return new PhysicsProfile(p.Name, p.ReferenceWeightLb, p.Elevator.ResponseRate, p.Aileron.ResponseRate, p.Rudder.ResponseRate, 0, Math.Max(p.Elevator.MaxRate, p.Aileron.MaxRate), p.Elevator.Damping, p.Elevator.DecelerationRatio);
    }
}
