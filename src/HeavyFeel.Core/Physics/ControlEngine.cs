using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

public sealed class AxisState
{
    public double PreviousInput { get; set; }
    public double Output { get; set; }
    public double Velocity { get; set; }
    public double Acceleration { get; set; }
}

public readonly record struct AxisTune(double Acceleration, double MaxAccel, double MaxVelocity, double Damping, double ReverseBrake);

public sealed class ControlEngine
{
    private readonly AxisState _pitch = new();
    private readonly AxisState _roll = new();
    private readonly AxisState _yaw = new();
    private readonly AxisState _throttle = new();
    private readonly Queue<(double T, double Diff)> _diffs = new();
    private double _clock;
    private bool _wasGround = true;

    public ControlFrame Last { get; private set; } = ControlFrame.Idle;

    public ControlFrame Step(AppSettings settings, FlightSnapshot snap, bool masterEnable, double dt)
    {
        dt = Numeric.Clamp(dt, 0.001, 0.05);
        if (!masterEnable || snap.AutopilotMaster)
        {
            Reset();
            Last = ControlFrame.Idle with { Reason = !masterEnable ? "Master off" : "Autopilot on — writes paused" };
            return Last;
        }

        var profile = AircraftProfiles.For(snap);
        var feel = FeelScale(settings);
        var weight = WeightFactor(snap, profile);
        var speed = SpeedFactor(snap.AirspeedIndicatedKnots);
        var ground = snap.OnGround;
        var flare = !ground && snap.AltitudeAglFeet is > 5 and < 80;
        var touched = _wasGround == false && ground;
        _wasGround = ground;
        _clock += dt;

        var pitchDamping = Numeric.Clamp(settings.PitchDamping / 100.0, 0, 1);
        var controlResponse = Numeric.Clamp(settings.ControlResponse / 100.0, 0, 1);
        var pitchAuthority = 1.0 - pitchDamping * 0.07;
        var pitchIn = Clamp(snap.StickY * pitchAuthority);
        var rollIn = Clamp(snap.StickX);
        var yawIn = Clamp(snap.RudderPedal);
        var thrIn = Clamp01(snap.Throttle1Percent / 100.0);
        var moving = Math.Abs(pitchIn) + Math.Abs(rollIn) + Math.Abs(yawIn) > 0.08;

        var pitchResponse = (profile.Name == "LIGHT GA" ? 0.85 : 1.0)
            * (1.0 - pitchDamping * 0.15)
            * (0.90 + controlResponse * 0.10);
        var pitchDampingTune = profile.Damping - pitchDamping * 0.02;
        var pitchTune = Tune(profile.PitchAccel * pitchResponse / feel / weight * speed,
            profile.MaxVel * speed / feel * pitchResponse,
            pitchDampingTune + (flare ? 0.08 : 0), profile.Reverse);
        var rollTune = Tune(profile.RollAccel / feel / weight * speed, profile.MaxVel * speed / feel, profile.Damping, profile.Reverse);
        var yawAccel = profile.YawAccel / feel / weight;
        if (ground)
            yawAccel *= snap.GroundSpeedKnots < 20 ? 1.35 : 0.7;
        var yawTune = Tune(yawAccel, profile.MaxVel / feel, profile.Damping + (ground ? 0.05 : 0), profile.Reverse);
        var thrTune = Tune(profile.ThrottleAccel / feel / Math.Max(0.8, weight), 0.8 / feel, 0.9, 0.4);

        var turb = moving ? settings.TurbulenceResponse / 100.0 * 0.04 * SmoothNoise(_clock) : 0;
        StepAxis(_pitch, pitchIn + turb, dt, pitchTune, touched);
        StepAxis(_roll, rollIn + turb * 0.6, dt, rollTune, touched);
        StepAxis(_yaw, yawIn, dt, yawTune, touched);
        StepAxis(_throttle, thrIn, dt, thrTune, false);

        var settling = Math.Abs(_pitch.Output) + Math.Abs(_roll.Output) + Math.Abs(_yaw.Output) > 0.01;
        if (!moving && !settling)
        {
            Reset();
            Last = ControlFrame.Idle with { Reason = "Stick centred — no writes" };
            return Last;
        }

        var diff = (Math.Abs(pitchIn - _pitch.Output) + Math.Abs(rollIn - _roll.Output) + Math.Abs(yawIn - _yaw.Output)) / 3.0;
        _diffs.Enqueue((_clock, diff));
        while (_diffs.Count > 0 && _clock - _diffs.Peek().T > 5)
            _diffs.Dequeue();
        var avg = _diffs.Count == 0 ? 0 : _diffs.Average(x => x.Diff);
        var max = _diffs.Count == 0 ? 0 : _diffs.Max(x => x.Diff);
        var effect = !moving || avg > 0.04;
        var addon = snap.Aircraft.IsFenixA320 || snap.Aircraft.IsPmdg777;

        Last = new ControlFrame
        {
            Active = true,
            Profile = profile.Name,
            PitchIn = pitchIn,
            PitchOut = _pitch.Output,
            PitchVelocity = _pitch.Velocity,
            PitchAccel = _pitch.Acceleration,
            RollIn = rollIn,
            RollOut = _roll.Output,
            YawIn = yawIn,
            YawOut = _yaw.Output,
            ThrottleIn = thrIn,
            ThrottleOut = Clamp01(_throttle.Output),
            WeightFactor = weight,
            SpeedFactor = speed,
            Damping = pitchTune.Damping,
            AverageDifference = avg,
            MaxDifference = max,
            EffectDetected = effect,
            AddonMayIgnore = addon,
            Reason = addon
                ? "Fenix/PMDG may ignore SimConnect output"
                : effect ? $"{profile.Name} control engine" : "NO EFFECT DETECTED"
        };
        return Last;
    }

    public void Reset()
    {
        _pitch.Output = _pitch.Velocity = _pitch.Acceleration = 0;
        _roll.Output = _roll.Velocity = _roll.Acceleration = 0;
        _yaw.Output = _yaw.Velocity = _yaw.Acceleration = 0;
        _throttle.Output = _throttle.Velocity = _throttle.Acceleration = 0;
        _diffs.Clear();
    }

    private static void StepAxis(AxisState state, double input, double dt, AxisTune tune, bool brakeNow)
    {
        input = Clamp(input);
        if (brakeNow)
            state.Velocity *= 0.2;
        if (Math.Abs(input) > 0.02 && Math.Abs(state.Velocity) > 0.02 && Math.Sign(input) != Math.Sign(state.Velocity))
            state.Velocity *= tune.ReverseBrake;
        var error = input - state.Output;
        var accel = Numeric.Clamp(error * tune.Acceleration, -tune.MaxAccel, tune.MaxAccel);
        state.Acceleration = accel;
        state.Velocity += accel * dt;
        state.Velocity *= Math.Pow(tune.Damping, dt * 60.0);
        state.Velocity = Numeric.Clamp(state.Velocity, -tune.MaxVelocity, tune.MaxVelocity);
        state.Output = Clamp(state.Output + state.Velocity * dt);
        state.PreviousInput = input;
    }

    private static AxisTune Tune(double accel, double maxVel, double damping, double reverse) =>
        new(Numeric.Clamp(accel, 0.4, 14), Numeric.Clamp(accel * 1.4, 0.5, 18), Numeric.Clamp(maxVel, 0.15, 3.5), Numeric.Clamp(damping, 0.82, 0.995), reverse);

    private static double FeelScale(AppSettings settings)
    {
        var level = settings.FeelLevel;
        if (level == "RealisticPlus") return 2.4;
        if (level == "Realistic") return 1.7;
        if (level == "Normal") return 0.75;
        return 0.7 + settings.Inertia / 80.0;
    }

    public static double WeightFactor(FlightSnapshot snap, PhysicsProfile profile)
    {
        if (snap.TotalWeightPounds < 500)
            return 1;
        return Numeric.Clamp(snap.TotalWeightPounds / profile.ReferenceWeightLb, 0.65, 1.8);
    }

    public static double SpeedFactor(double iasKnots)
    {
        var t = Numeric.Clamp((iasKnots - 70) / 230.0, 0, 1);
        return 1.0 - t * 0.45;
    }

    private static double SmoothNoise(double t) =>
        Math.Sin(t * 1.7) * 0.6 + Math.Sin(t * 0.37 + 1.2) * 0.4;

    private static double Clamp(double v) => Numeric.Clamp(v, -1, 1);
    private static double Clamp01(double v) => Numeric.Clamp(v, 0, 1);
}

public sealed record ControlFrame
{
    public bool Active { get; init; }
    public string Profile { get; init; } = "";
    public double PitchIn { get; init; }
    public double PitchOut { get; init; }
    public double PitchVelocity { get; init; }
    public double PitchAccel { get; init; }
    public double RollIn { get; init; }
    public double RollOut { get; init; }
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

public readonly record struct PhysicsProfile(
    string Name,
    double ReferenceWeightLb,
    double PitchAccel,
    double RollAccel,
    double YawAccel,
    double ThrottleAccel,
    double MaxVel,
    double Damping,
    double Reverse);

public static class AircraftProfiles
{
    public static PhysicsProfile For(FlightSnapshot snap)
    {
        var name = (snap.Aircraft.DisplayName + " " + snap.Aircraft.AtcModel).ToUpperInvariant();
        if (snap.Aircraft.IsFenixA320 || snap.Aircraft.IsPmdg777 || snap.Class is AircraftClass.NarrowBody or AircraftClass.WideBody or AircraftClass.HeavyWide)
            return new("AIRLINER", 150000, 1.5, 1.3, 1.1, 0.8, 0.45, 0.90, 0.25);
        if (name.Contains("KING AIR") || name.Contains("KINGAIR") || snap.Class == AircraftClass.Regional)
            return new("TURBOPROP", 12000, 2.4, 2.2, 1.8, 1.2, 0.7, 0.92, 0.35);
        if (name.Contains("BONANZA") || name.Contains("TBM") || name.Contains("PC-12") || name.Contains("PC12"))
            return new("HIGH-PERFORMANCE GA", 3800, 3.6, 3.4, 2.8, 1.8, 1.1, 0.94, 0.45);
        return new("LIGHT GA", 2400, 6.5, 6.0, 5.0, 2.4, 1.8, 0.96, 0.6);
    }
}
