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
        var size = Math.Abs(target - state.Position);
        var scaled = fullTravelSeconds * (0.35 + 0.65 * Numeric.Clamp(size, 0, 1));
        var time = Numeric.Clamp(scaled, 0.2, 4.0);
        var target = raw;
        var error = target - state.Position;
        var rate = 1.0 / time;
        var desired = Math.Abs(error) < 0.0001 ? 0 : Math.Sign(error) * rate;
        var accel = rate / 0.18;
        var deltaV = Numeric.Clamp(desired - state.Velocity, -accel * dt, accel * dt);
        state.Velocity += deltaV;
        var step = state.Velocity * dt;
        if (Math.Abs(step) > Math.Abs(error))
        {
            step = error;
            state.Velocity = 0;
        }
        state.Position = Numeric.Clamp(state.Position + step, -1, 1);
        if (double.IsNaN(state.Position) || double.IsInfinity(state.Position))
        {
            state.Reset();
            return new AxisSample(raw, raw, raw, 0, 0);
        }
        return new AxisSample(raw, target, state.Position, state.Velocity, deltaV);
    }

    public static double TravelSeconds(double moi, double weightPounds, double profileSeconds, AircraftClass aircraftClass)
    {
        var baseSeconds = profileSeconds > 0.05 ? profileSeconds : ClassSeconds(aircraftClass);
        if (IsValid(moi))
        {
            var reference = moi < 50000 ? 1800.0 : 1200000.0;
            return Numeric.Clamp(baseSeconds * Math.Sqrt(moi / reference) * 1.2, 0.3, 3.2);
        }
        if (IsValid(weightPounds) && weightPounds > 500)
            return Numeric.Clamp(baseSeconds * Math.Sqrt(weightPounds / 2450.0) * 1.2, 0.3, 3.2);
        return Numeric.Clamp(ClassSeconds(aircraftClass), 0.3, 3.2);
    }

    public static double PhaseScale(FlightPhase phase) => phase switch
    {
        FlightPhase.Ground => 0.9,
        FlightPhase.Takeoff => 1.0,
        FlightPhase.Approach or FlightPhase.Landing => 0.95,
        FlightPhase.Climb or FlightPhase.Descent => 1.0,
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
        AircraftClass.LightGa => 1.6,
        AircraftClass.Regional => 2.4,
        AircraftClass.NarrowBody => 3.2,
        AircraftClass.WideBody or AircraftClass.HeavyWide => 4.5,
        _ => 2.0
    };

    private static bool IsValid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    private static double Sanitize(double value) => !double.IsNaN(value) && !double.IsInfinity(value) ? Numeric.Clamp(value, -1, 1) : 0;
}

public readonly record struct AxisSample(double Raw, double Target, double Output, double Velocity, double Acceleration);
