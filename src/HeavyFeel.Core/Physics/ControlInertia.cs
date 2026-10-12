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
    private const double SubStep = 1.0 / 240.0;

    /// <summary>
    /// Critically-damped follower: the output chases the physical stick with a
    /// bounded speed, accelerates and brakes smoothly, and never overshoots or
    /// chatters around the target, independent of the sim frame rate.
    /// </summary>
    public static AxisSample Step(AxisDynamicsState state, double raw, double dt, double fullTravelSeconds, double accelScale, double decelScale, double damping)
    {
        raw = Sanitize(raw);
        dt = Numeric.Clamp(double.IsNaN(dt) ? 0.016 : dt, 0.001, 0.1);
        var travel = Numeric.Clamp(double.IsNaN(fullTravelSeconds) || double.IsInfinity(fullTravelSeconds) ? 0.35 : fullTravelSeconds, 0.05, 6.0);
        var maxRate = 1.0 / travel;
        var lag = Numeric.Clamp(0.12 * travel * Numeric.Clamp(damping, 0.5, 2.5), 0.015, 0.6);
        var accel = maxRate / (0.22 * travel) * Numeric.Clamp(accelScale, 0.25, 2.0);
        var decel = maxRate / (0.18 * travel) * Numeric.Clamp(decelScale, 0.25, 2.0);
        var startVelocity = state.Velocity;
        var steps = Math.Max(1, (int)Math.Ceiling(dt / SubStep));
        var h = dt / steps;

        for (var i = 0; i < steps; i++)
        {
            var error = raw - state.Position;
            var speed = Math.Min(maxRate, Math.Min(Math.Abs(error) / lag, Math.Sqrt(2.0 * decel * Math.Abs(error))));
            var desired = Math.Sign(error) * speed;
            var speedingUp = Math.Sign(desired) == Math.Sign(state.Velocity) && Math.Abs(desired) > Math.Abs(state.Velocity);
            var limit = (speedingUp || state.Velocity == 0 ? accel : decel) * h;
            state.Velocity += Numeric.Clamp(desired - state.Velocity, -limit, limit);
            var next = state.Position + state.Velocity * h;
            if (error != 0 && (raw - next) * error < 0)
            {
                next = raw;
                state.Velocity = 0;
            }
            state.Position = Numeric.Clamp(next, -1, 1);
        }

        if (Math.Abs(raw - state.Position) < 0.0002 && Math.Abs(state.Velocity) < 0.002)
        {
            state.Position = raw;
            state.Velocity = 0;
        }

        if (double.IsNaN(state.Position) || double.IsInfinity(state.Position) || double.IsNaN(state.Velocity))
        {
            state.Reset();
            return new AxisSample(raw, raw, raw, 0, 0);
        }
        return new AxisSample(raw, raw, state.Position, state.Velocity, (state.Velocity - startVelocity) / dt);
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
