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
    /// <param name="accelScale">Multiplier on how quickly the axis speeds up (1 = default).</param>
    /// <param name="decelScale">Multiplier on how quickly the axis slows down (1 = default).</param>
    /// <param name="damping">1 = default; higher values take longer to build up speed.</param>
    /// <param name="returnDelaySeconds">Hold time after the stick is released before returning.</param>
    /// <param name="returnRateMultiplier">Speed multiplier while returning to centre.</param>
    public static AxisSample Step(
        AxisDynamicsState state,
        double raw,
        double dt,
        double fullTravelSeconds,
        double accelScale,
        double decelScale,
        double damping,
        double returnDelaySeconds = 0,
        double returnRateMultiplier = 1)
    {
        raw = Sanitize(raw);
        dt = Numeric.Clamp(dt, 0.001, 0.05);
        var target = raw;
        var returning = target == 0 && Math.Abs(state.Position) > 0.0001;
        if (returning)
        {
            state.ReleaseElapsed += dt;
            if (state.ReleaseElapsed < returnDelaySeconds)
                target = state.Position;
        }
        else
            state.ReleaseElapsed = 0;

        var size = Math.Abs(target - state.Position);
        var scaled = fullTravelSeconds * (0.35 + 0.65 * Numeric.Clamp(size, 0, 1));
        var time = Numeric.Clamp(scaled, 0.08, 4.0);
        var error = target - state.Position;
        var rate = 1.0 / time;
        if (returning)
            rate *= Numeric.Clamp(returnRateMultiplier, 0.1, 2.0);
        var desired = Math.Abs(error) < 0.0001 ? 0 : Math.Sign(error) * rate;
        var speedingUp = Math.Abs(desired) > Math.Abs(state.Velocity) && desired * state.Velocity >= 0;
        var accel = rate / (0.18 * Numeric.Clamp(damping, 0.5, 2.0))
            * Numeric.Clamp(speedingUp ? accelScale : decelScale, 0.25, 2.0);
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
        var weightScale = WeightScale(weightPounds, aircraftClass);
        if (IsValid(moi))
        {
            var reference = moi < 50000 ? 1800.0 : 1200000.0;
            return Numeric.Clamp(baseSeconds * Math.Sqrt(moi / reference) * 1.2 * weightScale, 0.3, 3.2);
        }
        if (IsValid(weightPounds) && weightPounds > 500)
            return Numeric.Clamp(baseSeconds * Math.Sqrt(weightPounds / WeightReference(aircraftClass)) * 1.2, 0.3, 3.2);
        return Numeric.Clamp(ClassSeconds(aircraftClass), 0.3, 3.2);
    }

    private static double WeightScale(double weightPounds, AircraftClass aircraftClass)
    {
        if (!IsValid(weightPounds) || weightPounds < 500)
            return 1.0;
        return Numeric.Clamp(Math.Sqrt(weightPounds / WeightReference(aircraftClass)), 0.75, 1.5);
    }

    private static double WeightReference(AircraftClass aircraftClass) => aircraftClass switch
    {
        AircraftClass.LightGa => 2450.0,
        AircraftClass.Regional => 70000.0,
        AircraftClass.NarrowBody => 145000.0,
        AircraftClass.WideBody => 500000.0,
        AircraftClass.HeavyWide => 650000.0,
        _ => 145000.0
    };

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
