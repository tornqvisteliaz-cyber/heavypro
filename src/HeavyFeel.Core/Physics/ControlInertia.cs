using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

public sealed class AxisDynamicsState
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

/// <summary>
/// Perceptual control inertia. It does not change aircraft mass or aerodynamics.
/// </summary>
public static class ControlInertia
{
    public static AxisSample Step(AxisDynamicsState state, double raw, double dt, double fullTravelSeconds, double accelScale, double decelScale, double damping)
    {
        raw = Sanitize(raw);
        dt = Numeric.Clamp(dt, 0.001, 0.05);
        var time = Numeric.Clamp(fullTravelSeconds, 0.2, 4.5);
        var target = raw;
        var error = target - state.Position;
        var size = Math.Abs(error);
        var smallBoost = 1.0 + (1.0 - Numeric.Clamp(size, 0, 1)) * 0.8;
        var accelLimit = (1.1 / time) * smallBoost * accelScale;
        var decelLimit = accelLimit * 1.8 * decelScale;
        var maxVelocity = Math.Max(0.22, 0.7 / time);
        var stiffness = 4.5 / time * smallBoost;
        var accel = error * stiffness - damping * state.Velocity;
        var limit = Math.Abs(target) < Math.Abs(state.Position) ? decelLimit : accelLimit;
        accel = Numeric.Clamp(accel, -limit, limit);
        state.Velocity = Numeric.Clamp(state.Velocity + accel * dt, -maxVelocity, maxVelocity);
        state.Position = Numeric.Clamp(state.Position + state.Velocity * dt, -1, 1);
        if (double.IsNaN(state.Position) || double.IsInfinity(state.Position) || double.IsNaN(state.Velocity) || double.IsInfinity(state.Velocity))
        {
            state.Reset();
            return new AxisSample(raw, raw, raw, 0, 0);
        }
        return new AxisSample(raw, target, state.Position, state.Velocity, accel);
    }

    public static double TravelSeconds(double moi, double weightPounds, double profileSeconds, AircraftClass aircraftClass)
    {
        var baseSeconds = profileSeconds > 0.05 ? profileSeconds : ClassSeconds(aircraftClass);
        if (IsValid(moi))
        {
            var reference = moi < 50000 ? 1800.0 : 1200000.0;
            return Numeric.Clamp(baseSeconds * Math.Sqrt(moi / reference), 0.12, 3.5);
        }
        if (IsValid(weightPounds) && weightPounds > 500)
            return Numeric.Clamp(baseSeconds * Math.Sqrt(weightPounds / 2450.0), 0.12, 3.5);
        return Numeric.Clamp(ClassSeconds(aircraftClass), 0.12, 3.5);
    }

    public static double PhaseScale(FlightPhase phase) => phase switch
    {
        FlightPhase.Ground => 0.62,
        FlightPhase.Takeoff => 0.8,
        FlightPhase.Approach or FlightPhase.Landing => 0.78,
        FlightPhase.Climb or FlightPhase.Descent => 0.9,
        _ => 1.0
    };

    public static double SpeedScale(double iasKnots)
    {
        if (double.IsNaN(iasKnots) || double.IsInfinity(iasKnots) || iasKnots < 30)
            return 0.85;
        var t = Numeric.Clamp((iasKnots - 80) / 240.0, 0, 1);
        return 0.9 + t * 0.25;
    }

    public static string Compatibility(FlightSnapshot snapshot)
    {
        if (snapshot.Aircraft.IsFenixA320 || snapshot.Aircraft.IsPmdg777)
            return "PARTIAL";
        var name = snapshot.Aircraft.DisplayName.ToUpperInvariant();
        if (name.Contains("PMDG") || name.Contains("FENIX"))
            return "PARTIAL";
        if (snapshot.Class == AircraftClass.Unknown && string.IsNullOrWhiteSpace(snapshot.Aircraft.Title))
            return "UNKNOWN";
        return "SUPPORTED";
    }

    private static double ClassSeconds(AircraftClass aircraftClass) => aircraftClass switch
    {
        AircraftClass.LightGa => 0.35,
        AircraftClass.Regional => 0.8,
        AircraftClass.NarrowBody => 1.5,
        AircraftClass.WideBody or AircraftClass.HeavyWide => 2.2,
        _ => 0.7
    };

    private static bool IsValid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    private static double Sanitize(double value) => !double.IsNaN(value) && !double.IsInfinity(value) ? Numeric.Clamp(value, -1, 1) : 0;
}

public readonly record struct AxisSample(double Raw, double Target, double Output, double Velocity, double Acceleration);
