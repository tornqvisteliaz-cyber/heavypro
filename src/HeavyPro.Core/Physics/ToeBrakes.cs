using HeavyPro.Core.Models;

namespace HeavyPro.Core.Physics;

/// <summary>Brake pressure per side (0 = off, 1 = full) and whether HeavyPro should be writing it.</summary>
public readonly record struct BrakeOutput(double Left, double Right, bool Active);

/// <summary>
/// Heavy toe brakes. A real airliner's brakes are hydraulic: pressure builds over a moment after
/// the pedal moves and the first part of the pedal does little, so a heavy jet slows smoothly
/// instead of the instant lock-up a toe-brake axis gives in the sim. This shapes each pedal:
/// - a small deadzone, so a resting foot never drags the brakes;
/// - a progressive curve: gentle first half of the travel, full pressure at the end;
/// - pressure that builds up over time, slower on heavier aircraft, and lets go quicker than it builds.
/// When both pedals are released and the pressure has bled off it goes idle, so the parking
/// brake and autobrake are never fought.
/// </summary>
public sealed class ToeBrakes
{
    private const double Deadzone = 0.04;
    private const double Curve = 1.6;
    private double _left, _right;
    private bool _wasActive;

    public BrakeOutput Step(double leftPedal, double rightPedal, FlightSnapshot snapshot, double dt)
    {
        dt = Numeric.Clamp(double.IsNaN(dt) ? 0 : dt, 0, 0.1);
        var rise = RiseSeconds(snapshot);
        var release = rise * 0.4;
        _left = Toward(_left, Target(leftPedal), dt, rise, release);
        _right = Toward(_right, Target(rightPedal), dt, rise, release);

        var pressing = _left > 0.0005 || _right > 0.0005;
        if (!pressing)
        {
            _left = _right = 0;
            // One last write of zero after the pedals were let go, then stay out of the way.
            var last = _wasActive;
            _wasActive = false;
            return new BrakeOutput(0, 0, last);
        }
        _wasActive = true;
        return new BrakeOutput(_left, _right, true);
    }

    public void Reset()
    {
        _left = _right = 0;
        _wasActive = false;
    }

    /// <summary>Pedal travel (0..1) to the pressure it asks for: deadzone, then a progressive curve.</summary>
    public static double Target(double pedal)
    {
        pedal = double.IsNaN(pedal) ? 0 : Numeric.Clamp(pedal, 0, 1);
        if (pedal <= Deadzone)
            return 0;
        return Math.Pow((pedal - Deadzone) / (1 - Deadzone), Curve);
    }

    /// <summary>Seconds for the pressure to go from none to full: light aircraft are quick, heavies take longer.</summary>
    public static double RiseSeconds(FlightSnapshot snapshot)
    {
        var baseSeconds = snapshot.Class switch
        {
            AircraftClass.LightGa => 0.25,
            AircraftClass.Regional => 0.5,
            AircraftClass.NarrowBody => 0.7,
            AircraftClass.WideBody => 0.9,
            AircraftClass.HeavyWide => 1.0,
            _ => 0.6
        };
        // Loaded close to its maximum the same pedal needs a little longer to bite.
        var load = snapshot.MaxGrossWeightPounds > 1 && snapshot.TotalWeightPounds > 1
            ? Numeric.Clamp(snapshot.TotalWeightPounds / snapshot.MaxGrossWeightPounds, 0.3, 1.1)
            : 0.7;
        return baseSeconds * (0.85 + 0.3 * load);
    }

    private static double Toward(double current, double target, double dt, double rise, double release)
    {
        var rate = target > current ? 1.0 / rise : 1.0 / release;
        var step = rate * dt;
        return Math.Abs(target - current) <= step ? target : current + Math.Sign(target - current) * step;
    }
}
