using HeavyFeel.Core.Models;

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

    public InputDynamicsFrame Step(AppSettings settings, FlightSnapshot snapshot, double deltaTime)
    {
        if (!IsFinite(deltaTime) || deltaTime <= 0)
            deltaTime = 1.0 / 60.0;
        deltaTime = Numeric.Clamp(deltaTime, 1.0 / 240.0, 0.1);

        var profile = InputDynamicsProfile.For(snapshot, settings.AircraftProfile);
        var airspeedFactor = AirspeedFactor(settings, snapshot.AirspeedIndicatedKnots);
        var curve = ParseCurve(settings.InputCurve);
        var deadzone = Numeric.Clamp(settings.InputDeadzone, 0, 0.25);
        var sensitivity = Numeric.Clamp(settings.InputSensitivity, 0.25, 2.0);
        var expoStrength = Numeric.Clamp(settings.ExpoStrength, 0, 1);
        var accelerationScale = Numeric.Clamp(settings.InputAccelerationScale, 0.25, 2.0);
        var decelerationScale = Numeric.Clamp(settings.InputDecelerationScale, 0.25, 2.0);
        var inertiaScale = 0.65 + Numeric.Clamp(settings.Inertia, 0, 100) / 80.0;
        var responseScale = 0.5 + Numeric.Clamp(settings.ControlResponse, 0, 100) / 100.0;
        if (snapshot.OnGround)
            inertiaScale *= 0.85 + Numeric.Clamp(settings.GroundInertia, 0, 100) / 100.0 * 0.3;

        var rawElevator = Clamp(snapshot.StickY);
        var rawAileron = Clamp(snapshot.StickX);
        var rawRudder = Clamp(snapshot.RudderPedal);

        var elevatorTarget = Shape(rawElevator, deadzone, sensitivity, expoStrength, curve);
        var aileronTarget = Shape(rawAileron, deadzone, sensitivity, expoStrength, curve);
        var rudderTarget = Shape(rawRudder, deadzone, sensitivity, expoStrength, curve);

        var pitchDamping = DampingFromSetting(settings.PitchDamping);
        var rollDamping = DampingFromSetting(settings.RollDamping);
        var yawDamping = DampingFromSetting(settings.YawDamping);
        var releaseDelay = Numeric.Clamp(settings.ReturnDelayMs / 1000.0, 0, 1.5);

        var elevator = StepAxis(_elevator, rawElevator, elevatorTarget, deltaTime,
            Tune(profile.Elevator, settings.PitchRateLimit, pitchDamping, accelerationScale, decelerationScale, responseScale, inertiaScale, airspeedFactor, releaseDelay, settings.StickReleaseMode));
        var aileron = StepAxis(_aileron, rawAileron, aileronTarget, deltaTime,
            Tune(profile.Aileron, settings.RollRateLimit, rollDamping, accelerationScale, decelerationScale, responseScale, inertiaScale, airspeedFactor, releaseDelay, settings.StickReleaseMode));
        var rudder = StepAxis(_rudder, rawRudder, rudderTarget, deltaTime,
            Tune(profile.Rudder, settings.YawRateLimit, yawDamping, accelerationScale, decelerationScale, responseScale, inertiaScale, airspeedFactor, releaseDelay, settings.StickReleaseMode));

        return new InputDynamicsFrame
        {
            Profile = profile.Name,
            AirspeedFactor = airspeedFactor,
            RawElevator = rawElevator,
            FilteredElevator = elevatorTarget,
            FinalElevator = elevator,
            RawAileron = rawAileron,
            FilteredAileron = aileronTarget,
            FinalAileron = aileron,
            RawRudder = rawRudder,
            FilteredRudder = rudderTarget,
            FinalRudder = rudder
        };
    }

    public void Reset()
    {
        _elevator.Reset();
        _aileron.Reset();
        _rudder.Reset();
    }

    private static AxisTune Tune(
        InputAxisProfile profile,
        double configuredRate,
        double damping,
        double accelerationScale,
        double decelerationScale,
        double responseScale,
        double inertiaScale,
        double airspeedFactor,
        double releaseDelay,
        string releaseMode)
    {
        var rate = configuredRate > 0 ? configuredRate : profile.MaxRate;
        var rateScale = responseScale / inertiaScale * airspeedFactor;
        var returnDelay = Math.Max(releaseDelay, profile.ReturnDelaySeconds);
        var returnRate = profile.ReturnRateMultiplier;
        switch (releaseMode)
        {
            case "Immediate":
                returnDelay = 0;
                returnRate = 1;
                break;
            case "Damped":
                returnDelay = 0;
                returnRate = 0.5;
                break;
            case "Delayed":
                returnDelay = Math.Max(0.3, releaseDelay);
                returnRate = 0.5;
                break;
        }
        return new AxisTune(
            Numeric.Clamp(profile.ResponseRate * responseScale * airspeedFactor / inertiaScale, 0.25, 12),
            Numeric.Clamp(profile.AccelerationLimit * accelerationScale * responseScale / inertiaScale, 0.2, 30),
            Numeric.Clamp(profile.DecelerationLimit * decelerationScale * responseScale / inertiaScale, 0.2, 30),
            Numeric.Clamp(rate * rateScale, 0.1, 6),
            Numeric.Clamp(damping + profile.Damping, 0, 8),
            returnDelay,
            returnRate);
    }

    private static double StepAxis(AxisDynamicsState state, double raw, double target, double dt, AxisTune tune)
    {
        if (Math.Abs(raw) <= 0.0001 && Math.Abs(state.Position) > 0.0001)
            state.ReleaseElapsed += dt;
        else
            state.ReleaseElapsed = 0;

        if (target == 0 && raw == 0 && state.ReleaseElapsed < tune.ReturnDelaySeconds)
            target = state.Position;

        var maxStep = 1.0 / 120.0;
        var count = Math.Max(1, (int)Math.Ceiling(dt / maxStep));
        var step = dt / count;
        for (var i = 0; i < count; i++)
        {
            var isReturning = target == 0 && Math.Abs(state.Position) > 0.0001;
            var velocityLimit = tune.MaxRate * (isReturning ? tune.ReturnRateMultiplier : 1.0);
            var error = target - state.Position;
            var desiredVelocity = Numeric.Clamp(error * tune.ResponseRate, -velocityLimit, velocityLimit);
            var velocityError = desiredVelocity - state.Velocity;
            var accelLimit = Math.Abs(desiredVelocity) > Math.Abs(state.Velocity)
                ? tune.AccelerationLimit
                : tune.DecelerationLimit;
            var acceleration = Numeric.Clamp(
                error * tune.ResponseRate * tune.ResponseRate
                    - 2.0 * tune.Damping * tune.ResponseRate * state.Velocity,
                -accelLimit,
                accelLimit);

            state.Velocity = Numeric.Clamp(state.Velocity + acceleration * step, -velocityLimit, velocityLimit);
            var next = state.Position + state.Velocity * step;
            if ((target - state.Position) * (target - next) <= 0)
            {
                state.Position = target;
                state.Velocity = 0;
            }
            else
                state.Position = Clamp(next);

            if (Math.Abs(target - state.Position) < 0.0005 && Math.Abs(state.Velocity) < 0.005)
            {
                state.Position = target;
                state.Velocity = 0;
            }
        }

        return Clamp(state.Position);
    }

    private static double Shape(double raw, double deadzone, double sensitivity, double expoStrength, InputCurve curve)
    {
        var magnitude = Math.Abs(raw);
        if (magnitude <= deadzone)
            return 0;

        if (magnitude >= 1.0)
            return Math.Sign(raw);

        var value = Math.Sign(raw) * (magnitude - deadzone) / (1.0 - deadzone);
        value = Clamp(value * sensitivity);
        var sign = Math.Sign(value);
        var x = Math.Abs(value);
        switch (curve)
        {
            case InputCurve.Expo:
                x = (1.0 - expoStrength) * x + expoStrength * x * x * x;
                break;
            case InputCurve.SCurve:
                x = x * x * (3.0 - 2.0 * x);
                break;
        }
        return sign * x;
    }

    private static double AirspeedFactor(AppSettings settings, double ias)
    {
        if (!settings.AirspeedResponseEnabled || !IsFinite(ias))
            return 1.0;
        var highSpeed = Numeric.Clamp((ias - 60.0) / 240.0, 0, 1);
        var strength = Numeric.Clamp(settings.AirspeedResponse, 0, 1);
        return 1.0 - highSpeed * strength * 0.55;
    }

    private static double DampingFromSetting(double value) => Numeric.Clamp(value, 0, 100) / 100.0 * 1.5;

    private static InputCurve ParseCurve(string? curve) =>
        string.Equals(curve, "S-Curve", StringComparison.OrdinalIgnoreCase)
            ? InputCurve.SCurve
            : Enum.TryParse(curve, true, out InputCurve parsed) ? parsed : InputCurve.Linear;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double Clamp(double value) => Numeric.Clamp(IsFinite(value) ? value : 0, -1, 1);

    private sealed class AxisDynamicsState
    {
        public double Position { get; set; }
        public double Velocity { get; set; }
        public double ReleaseElapsed { get; set; }

        public void Reset()
        {
            Position = 0;
            Velocity = 0;
            ReleaseElapsed = 0;
        }
    }

    private readonly record struct AxisTune(
        double ResponseRate,
        double AccelerationLimit,
        double DecelerationLimit,
        double MaxRate,
        double Damping,
        double ReturnDelaySeconds,
        double ReturnRateMultiplier);
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
    public double RawElevator { get; init; }
    public double FilteredElevator { get; init; }
    public double FinalElevator { get; init; }
    public double RawAileron { get; init; }
    public double FilteredAileron { get; init; }
    public double FinalAileron { get; init; }
    public double RawRudder { get; init; }
    public double FilteredRudder { get; init; }
    public double FinalRudder { get; init; }
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

        if (identity.Contains("PMDG") && (identity.Contains("777") || snapshot.Aircraft.IsPmdg777))
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
