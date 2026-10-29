using HeavyFeel.Core.Models;
using HeavyFeel.Core.Services;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// Shapes pilot axis commands before they are sent back through SimConnect.
/// This class changes control input only; it does not modify aircraft forces.
/// </summary>
public sealed class InputDynamicsEngine
{
    private readonly AxisDynamicsState _elevator = new();
    private readonly AxisDynamicsState _aileron = new();
    private readonly AxisDynamicsState _rudder = new();
    private bool _seed;
    private double _clock;

    public InputDynamicsFrame Step(AppSettings settings, FlightSnapshot snapshot, double deltaTime)
        => Step(settings, snapshot, deltaTime, CommunityProfile.TravelScale());

    public InputDynamicsFrame Step(AppSettings settings, FlightSnapshot snapshot, double deltaTime, double travelScale)
    {
        if (!IsFinite(deltaTime) || deltaTime <= 0)
            deltaTime = 1.0 / 60.0;
        deltaTime = Numeric.Clamp(deltaTime, 1.0 / 240.0, 0.1);
        if (Real1.IsActive(settings))
            return StepReal1(snapshot, deltaTime);

        var profile = InputDynamicsProfile.For(snapshot, settings.AircraftProfile);
        var curve = ParseCurve(settings.InputCurve);
        var deadzone = Numeric.Clamp(settings.InputDeadzone, 0, 0.10);
        var sensitivity = Numeric.Clamp(settings.InputSensitivity, 0.25, 2.0);
        var expoStrength = Numeric.Clamp(settings.ExpoStrength, 0, 1);

        var rawElevator = Clamp(snapshot.StickY);
        var rawAileron = Clamp(snapshot.StickX);
        var rawRudder = Clamp(snapshot.RudderInput);
        var shapedElevator = Shape(rawElevator, deadzone, sensitivity, expoStrength, curve);
        var shapedAileron = Shape(rawAileron, deadzone, sensitivity, expoStrength, curve);
        var shapedRudder = Shape(rawRudder, deadzone, sensitivity, expoStrength, curve);

        var feel = ControlFeel.For(snapshot);
        if (settings.RealisticRudder)
            shapedRudder = ControlFeel.RudderPedalCurve(shapedRudder);
        var rudderAuthority = feel.RudderAuthority(settings, snapshot.AirspeedIndicatedKnots);
        shapedRudder *= rudderAuthority;

        if (_seed)
        {
            // Until now the sim had the stick unshaped. Start from that position
            // so turning shaping on, or the autopilot off, never jumps the yoke.
            _elevator.Position = shapedElevator;
            _aileron.Position = shapedAileron;
            _rudder.Position = shapedRudder;
            _seed = false;
        }

        var phase = FlightPhaseResolver.Resolve(snapshot);
        var speedScale = feel.SpeedScale(settings, snapshot);
        // Each slider is a travel-time multiplier on an exponential scale, 1x at 50,
        // so moving a slider is always clearly felt in both directions.
        var scale = ControlInertia.PhaseScale(phase) * speedScale * SliderScale(settings.ControlResponse, 1.8, 0.55);
        var tune = SliderScale(settings.Inertia, 0.35, 2.8) * Numeric.Clamp(travelScale, 0.5, 3) * feel.BaseScale;
        if (snapshot.OnGround)
            tune *= SliderScale(settings.GroundInertia, 0.6, 1.7);

        var pitchRate = settings.PitchRateLimit > 0 ? settings.PitchRateLimit : profile.Elevator.MaxRate;
        var rollRate = settings.RollRateLimit > 0 ? settings.RollRateLimit : profile.Aileron.MaxRate;
        var yawRate = settings.YawRateLimit > 0 ? settings.YawRateLimit : profile.Rudder.MaxRate;
        var pitchTime = ControlInertia.TravelSeconds(snapshot.PitchMoi, snapshot.TotalWeightPounds, pitchRate > 0 ? 1.2 / pitchRate : 0.35, snapshot.Class) * scale * tune * SliderScale(settings.PitchDamping, 0.5, 2.0);
        var rollTime = ControlInertia.TravelSeconds(snapshot.RollMoi, snapshot.TotalWeightPounds, rollRate > 0 ? 1.0 / rollRate : 0.25, snapshot.Class) * scale * tune * SliderScale(settings.RollDamping, 0.5, 2.0);
        var yawTime = ControlInertia.TravelSeconds(snapshot.YawMoi, snapshot.TotalWeightPounds, yawRate > 0 ? 1.1 / yawRate : 0.35, snapshot.Class) * scale * tune * SliderScale(settings.YawDamping, 0.5, 2.0);
        pitchTime = Numeric.Clamp(pitchTime, feel.MinTravelSeconds, feel.MaxTravelSeconds);
        rollTime = Numeric.Clamp(rollTime, feel.MinTravelSeconds, feel.MaxTravelSeconds);
        yawTime = Numeric.Clamp(yawTime * feel.RudderFeelScale(settings, snapshot), feel.MinTravelSeconds, feel.MaxTravelSeconds);

        var accel = settings.InputAccelerationScale;
        var decel = settings.InputDecelerationScale;
        var pitchRelease = Release(settings, profile.Elevator);
        var rollRelease = Release(settings, profile.Aileron);
        var yawRelease = Release(settings, profile.Rudder);
        var elevator = ControlInertia.Step(_elevator, shapedElevator, deltaTime, pitchTime, accel, decel, DampingScale(settings.PitchDamping), pitchRelease.Delay, pitchRelease.Rate);
        var aileron = ControlInertia.Step(_aileron, shapedAileron, deltaTime, rollTime, accel, decel, DampingScale(settings.RollDamping), rollRelease.Delay, rollRelease.Rate);
        var rudder = ControlInertia.Step(_rudder, shapedRudder, deltaTime, yawTime, accel, decel, DampingScale(settings.YawDamping), yawRelease.Delay, yawRelease.Rate);

        // Turbulence: a gentle buffet on the controls in the air, growing with airspeed.
        _clock += deltaTime;
        var buffet = snapshot.OnGround
            ? 0
            : Numeric.Clamp(settings.TurbulenceResponse, 0, 100) / 100.0 * 0.03
              * Numeric.Clamp(ControlFeel.DynamicPressure(snapshot) / feel.ReferenceQ, 0, 1.5);
        // Only while the pilot is holding the stick off centre: hands off means no writes.
        var finalElevator = Numeric.Clamp(elevator.Output + buffet * Math.Min(1, Math.Abs(elevator.Target) * 5) * Noise(_clock, 0), -1, 1);
        var finalAileron = Numeric.Clamp(aileron.Output + buffet * 0.7 * Math.Min(1, Math.Abs(aileron.Target) * 5) * Noise(_clock, 1.7), -1, 1);

        return new InputDynamicsFrame
        {
            Profile = profile.Name,
            AirspeedFactor = speedScale,
            Feel = feel.Label,
            RudderAuthority = rudderAuthority,
            RawElevator = rawElevator,
            FilteredElevator = elevator.Target,
            FinalElevator = finalElevator,
            RawAileron = rawAileron,
            FilteredAileron = aileron.Target,
            FinalAileron = finalAileron,
            RawRudder = rawRudder,
            FilteredRudder = rudder.Target,
            FinalRudder = rudder.Output,
            PitchVelocity = elevator.Velocity,
            PitchAcceleration = elevator.Acceleration,
            RollVelocity = aileron.Velocity,
            RollAcceleration = aileron.Acceleration,
            YawVelocity = rudder.Velocity,
            YawAcceleration = rudder.Acceleration,
            PitchSeconds = pitchTime,
            RollSeconds = rollTime,
            YawSeconds = yawTime,
            Compatibility = ControlInertia.Compatibility(snapshot)
        };
    }

    /// <summary>
    /// REAL1: everything from the aircraft family's tuning (see <see cref="Real1"/>), none
    /// of it from the sliders or the global travel scale, so every aircraft gets the feel
    /// it should have out of the box.
    /// </summary>
    private InputDynamicsFrame StepReal1(FlightSnapshot snapshot, double deltaTime)
    {
        var tuning = Real1.For(snapshot);
        var feel = ControlFeel.For(snapshot);
        var rudderSettings = new AppSettings { RealisticRudder = tuning.RealisticRudder, RudderLimiter = true };

        var rawElevator = Clamp(snapshot.StickY);
        var rawAileron = Clamp(snapshot.StickX);
        var rawRudder = Clamp(snapshot.RudderInput);
        var shapedElevator = Real1.Shape(rawElevator, tuning);
        var shapedAileron = Real1.Shape(rawAileron, tuning);
        var shapedRudder = Real1.Shape(rawRudder, tuning);
        if (tuning.RealisticRudder)
            shapedRudder = ControlFeel.RudderPedalCurve(shapedRudder);
        var rudderAuthority = feel.RudderAuthority(rudderSettings, snapshot.AirspeedIndicatedKnots);
        shapedRudder *= rudderAuthority;

        if (_seed)
        {
            _elevator.Position = shapedElevator;
            _aileron.Position = shapedAileron;
            _rudder.Position = shapedRudder;
            _seed = false;
        }

        var phase = FlightPhaseResolver.Resolve(snapshot);
        var speed = Real1.AirspeedScale(tuning, snapshot, feel);
        var scale = ControlInertia.PhaseScale(phase) * speed;
        var pitchTime = Numeric.Clamp(tuning.PitchSeconds * scale, 0.08, 3.0);
        var rollTime = Numeric.Clamp(tuning.RollSeconds * scale, 0.08, 3.0);
        var yawTime = Numeric.Clamp(tuning.YawSeconds * scale * feel.RudderFeelScale(rudderSettings, snapshot), 0.08, 3.0);
        var damping = DampingScale(tuning.Damping);

        var elevator = ControlInertia.Step(_elevator, shapedElevator, deltaTime, pitchTime, 1, 1, damping, tuning.ReturnDelaySeconds, tuning.ReturnRate);
        var aileron = ControlInertia.Step(_aileron, shapedAileron, deltaTime, rollTime, 1, 1, damping, tuning.ReturnDelaySeconds, tuning.ReturnRate);
        var rudder = ControlInertia.Step(_rudder, shapedRudder, deltaTime, yawTime, 1, 1, damping, tuning.ReturnDelaySeconds, tuning.ReturnRate);

        _clock += deltaTime;
        var buffet = snapshot.OnGround
            ? 0
            : 0.16 * 0.03 * Numeric.Clamp(ControlFeel.DynamicPressure(snapshot) / feel.ReferenceQ, 0, 1.5);
        var finalElevator = Numeric.Clamp(elevator.Output + buffet * Math.Min(1, Math.Abs(elevator.Target) * 5) * Noise(_clock, 0), -1, 1);
        var finalAileron = Numeric.Clamp(aileron.Output + buffet * 0.7 * Math.Min(1, Math.Abs(aileron.Target) * 5) * Noise(_clock, 1.7), -1, 1);

        return new InputDynamicsFrame
        {
            Profile = Real1.DisplayName,
            AirspeedFactor = speed,
            Feel = tuning.Family,
            RudderAuthority = rudderAuthority,
            RawElevator = rawElevator,
            FilteredElevator = elevator.Target,
            FinalElevator = finalElevator,
            RawAileron = rawAileron,
            FilteredAileron = aileron.Target,
            FinalAileron = finalAileron,
            RawRudder = rawRudder,
            FilteredRudder = rudder.Target,
            FinalRudder = rudder.Output,
            PitchVelocity = elevator.Velocity,
            PitchAcceleration = elevator.Acceleration,
            RollVelocity = aileron.Velocity,
            RollAcceleration = aileron.Acceleration,
            YawVelocity = rudder.Velocity,
            YawAcceleration = rudder.Acceleration,
            PitchSeconds = pitchTime,
            RollSeconds = rollTime,
            YawSeconds = yawTime,
            Compatibility = ControlInertia.Compatibility(snapshot)
        };
    }

    public void Reset()
    {
        _elevator.Reset();
        _aileron.Reset();
        _rudder.Reset();
        _seed = true;
    }

    /// <summary>
    /// Deadzone, curve and centre sensitivity. Always maps ±1 to ±1 so full
    /// endpoint authority is kept.
    /// </summary>
    public static double Shape(double raw, double deadzone, double sensitivity, double expoStrength, InputCurve curve)
    {
        var magnitude = Math.Abs(raw);
        if (magnitude <= deadzone)
            return 0;

        if (magnitude >= 1.0)
            return Math.Sign(raw);

        var x = (magnitude - deadzone) / (1.0 - deadzone);
        switch (curve)
        {
            case InputCurve.Expo:
                x = (1.0 - expoStrength) * x + expoStrength * x * x * x;
                break;
            case InputCurve.SCurve:
                x = x * x * (3.0 - 2.0 * x);
                break;
        }

        // Slope at centre equals sensitivity; the endpoint stays at 1.
        x = x * sensitivity / (1.0 + (sensitivity - 1.0) * x);
        return Math.Sign(raw) * Numeric.Clamp(x, 0, 1);
    }

    // ControlResponse 40 (the default) leaves travel time unchanged.
    /// <summary>Slider 0..100 to a multiplier from <paramref name="at0"/> to <paramref name="at100"/>, geometric so 50 is the midpoint.</summary>
    public static double SliderScale(double value, double at0, double at100) =>
        at0 * Math.Pow(at100 / at0, Numeric.Clamp(double.IsNaN(value) ? 50 : value, 0, 100) / 100.0);

    // Smooth, non-repeating-looking wobble in -1..1.
    private static double Noise(double t, double phase) =>
        0.55 * Math.Sin(t * 1.9 + phase) + 0.3 * Math.Sin(t * 4.3 + phase * 2.1) + 0.15 * Math.Sin(t * 9.1 + phase * 3.7);

    // Damping 50 leaves the acceleration phase unchanged.
    private static double DampingScale(double damping) => (0.8 + Numeric.Clamp(damping, 0, 100) / 100.0) / 1.3;

    private static (double Delay, double Rate) Release(AppSettings settings, InputAxisProfile axis)
    {
        var configuredDelay = Numeric.Clamp(settings.ReturnDelayMs, 0, 1500) / 1000.0;
        return settings.StickReleaseMode switch
        {
            "Immediate" => (0, 1),
            "Damped" => (configuredDelay, 0.5),
            "Delayed" => (Math.Max(0.3, configuredDelay), 0.5),
            _ => (Math.Max(configuredDelay, axis.ReturnDelaySeconds), axis.ReturnRateMultiplier)
        };
    }

    private static InputCurve ParseCurve(string? curve) =>
        string.Equals(curve, "S-Curve", StringComparison.OrdinalIgnoreCase)
            ? InputCurve.SCurve
            : Enum.TryParse(curve, true, out InputCurve parsed) ? parsed : InputCurve.Linear;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double Clamp(double value) => Numeric.Clamp(IsFinite(value) ? value : 0, -1, 1);
}

public enum InputCurve
{
    Linear,
    Expo,
    SCurve
}

public sealed record InputAxisProfile(
    double ResponseRate,
    double AccelerationLimit,
    double DecelerationLimit,
    double MaxRate,
    double Damping,
    double ReturnDelaySeconds,
    double ReturnRateMultiplier);

public sealed record InputDynamicsFrame
{
    public string Profile { get; init; } = "";
    public double AirspeedFactor { get; init; } = 1;
    public string Feel { get; init; } = "";
    public double RudderAuthority { get; init; } = 1;
    public double RawElevator { get; init; }
    public double FilteredElevator { get; init; }
    public double FinalElevator { get; init; }
    public double RawAileron { get; init; }
    public double FilteredAileron { get; init; }
    public double FinalAileron { get; init; }
    public double RawRudder { get; init; }
    public double FilteredRudder { get; init; }
    public double FinalRudder { get; init; }
    public double PitchVelocity { get; init; }
    public double PitchAcceleration { get; init; }
    public double RollVelocity { get; init; }
    public double RollAcceleration { get; init; }
    public double YawVelocity { get; init; }
    public double YawAcceleration { get; init; }
    public double PitchSeconds { get; init; }
    public double RollSeconds { get; init; }
    public double YawSeconds { get; init; }
    public string Compatibility { get; init; } = "UNKNOWN";
}

/// <summary>Illustrative starting points, not manufacturer control-system data.</summary>
public sealed record InputDynamicsProfile(
    string Name,
    InputAxisProfile Elevator,
    InputAxisProfile Aileron,
    InputAxisProfile Rudder)
{
    private static readonly InputAxisProfile LightPitch = new(5.2, 8.0, 9.0, 3.2, 0.25, 0, 0.9);
    private static readonly InputAxisProfile LightRoll = new(5.0, 8.5, 9.5, 3.6, 0.2, 0, 0.9);
    private static readonly InputAxisProfile LightYaw = new(4.2, 6.0, 7.0, 2.4, 0.4, 0, 0.8);
    private static readonly InputAxisProfile AirlinerPitch = new(3.0, 4.2, 5.0, 1.45, 1.0, 0.04, 0.7);
    private static readonly InputAxisProfile AirlinerRoll = new(2.8, 4.0, 4.8, 1.7, 1.1, 0.04, 0.7);
    private static readonly InputAxisProfile AirlinerYaw = new(2.2, 3.0, 3.8, 1.25, 1.35, 0.06, 0.65);

    public static InputDynamicsProfile For(FlightSnapshot snapshot, string? selectedProfile = null)
    {
        switch (selectedProfile?.Trim())
        {
            case "Fenix A320":
                return new("Fenix A320", Damp(AirlinerPitch), Damp(AirlinerRoll), Damp(AirlinerYaw));
            case "PMDG 737":
                return new("PMDG 737", AirlinerPitch, AirlinerRoll, AirlinerYaw);
            case "PMDG 777":
                return new("PMDG 777", Slow(AirlinerPitch, 0.72), Slow(AirlinerRoll, 0.8), Slow(AirlinerYaw, 0.8));
            case "ASOBO 787":
                return new("ASOBO 787", Slow(AirlinerPitch, 0.88), Slow(AirlinerRoll, 0.9), AirlinerYaw);
            case "Generic GA":
                return new("Generic GA", LightPitch, LightRoll, LightYaw);
            case "Generic Airliner":
                return new("Generic Airliner", AirlinerPitch, AirlinerRoll, AirlinerYaw);
        }

        var identity = (snapshot.Aircraft.DisplayName + " " + snapshot.Aircraft.AtcModel + " " + snapshot.Aircraft.AtcId)
            .ToUpperInvariant();

        // MSFS 2024 names the PMDG 777 just "777-300ER" / "777F", without "PMDG".
        if (snapshot.Aircraft.IsPmdg777 || (identity.Contains("PMDG") && identity.Contains("777")))
            return new("PMDG 777", Slow(AirlinerPitch, 0.72), Slow(AirlinerRoll, 0.8), Slow(AirlinerYaw, 0.8));
        if (identity.Contains("PMDG") && identity.Contains("737"))
            return new("PMDG 737", AirlinerPitch, AirlinerRoll, AirlinerYaw);
        if (identity.Contains("FENIX") || snapshot.Aircraft.IsFenixA320)
            return new("Fenix A320", Damp(AirlinerPitch), Damp(AirlinerRoll), Damp(AirlinerYaw));
        if (identity.Contains("787"))
            return new("ASOBO 787", Slow(AirlinerPitch, 0.88), Slow(AirlinerRoll, 0.9), AirlinerYaw);
        if (snapshot.Class is AircraftClass.WideBody or AircraftClass.HeavyWide)
            return new("Generic Airliner", Slow(AirlinerPitch, 0.88), AirlinerRoll, AirlinerYaw);
        if (snapshot.Class is AircraftClass.NarrowBody or AircraftClass.Regional)
            return new("Generic Airliner", AirlinerPitch, AirlinerRoll, AirlinerYaw);
        return new("Generic GA", LightPitch, LightRoll, LightYaw);
    }

    private static InputAxisProfile Slow(InputAxisProfile axis, double scale) => axis with
    {
        ResponseRate = axis.ResponseRate * scale,
        MaxRate = axis.MaxRate * scale,
        Damping = axis.Damping + 0.25
    };

    private static InputAxisProfile Damp(InputAxisProfile axis) => axis with
    {
        Damping = axis.Damping + 0.35,
        ReturnDelaySeconds = axis.ReturnDelaySeconds + 0.03
    };

    public static InputDynamicsProfile GenericGa => new("Generic GA", LightPitch, LightRoll, LightYaw);
    public static InputDynamicsProfile GenericAirliner => new("Generic Airliner", AirlinerPitch, AirlinerRoll, AirlinerYaw);
}
