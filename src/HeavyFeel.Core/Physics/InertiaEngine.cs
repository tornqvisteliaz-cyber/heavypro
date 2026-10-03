using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// Sends the control engine output to SimConnect. No body-rate writes.
/// </summary>
public sealed class InertiaEngine
{
    private const double A320TypicalTakeoffLb = 154000;
    private readonly ControlEngine _control = new();
    private DateTime _last = DateTime.MinValue;

    public InfluenceCommand Step(AppSettings settings, FlightSnapshot snap, bool masterEnable)
    {
        var dt = 0.016;
        if (_last != DateTime.MinValue)
        {
            var raw = (snap.Utc - _last).TotalSeconds;
            if (raw > 0.001 && raw < 0.12)
                dt = raw;
        }
        _last = snap.Utc;

        if (!masterEnable)
        {
            Reset();
            return InfluenceCommand.Idle("Master off — no writes");
        }

        var frame = _control.Step(settings, snap, true, dt);
        if (!frame.Active)
        {
            Reset();
            return InfluenceCommand.Idle(frame.Reason);
        }

        return new InfluenceCommand
        {
            Active = true,
            WriteRates = false,
            WriteAxes = true,
            WriteYoke = true,
            WriteThrottle = true,
            YokeXOut = frame.RollOut,
            YokeYOut = frame.PitchOut,
            RudderOut = frame.YawOut,
            ElevatorAxis = ToAxis(frame.PitchOut),
            AileronAxis = ToAxis(frame.RollOut),
            RudderAxis = ToAxis(frame.YawOut),
            ThrottleAxis = (int)Math.Round(Numeric.Clamp(frame.ThrottleOut, 0, 1) * 16383.0),
            Phase = FlightPhaseResolver.Resolve(snap).ToString(),
            ProfileName = frame.Profile,
            Mix = frame.WeightFactor,
            RawPitch = frame.PitchIn,
            OutPitch = frame.PitchOut,
            RawRoll = frame.RollIn,
            OutRoll = frame.RollOut,
            Reason =
                $"INPUT {frame.PitchIn:0.00} OUTPUT {frame.PitchOut:0.00} VELOCITY {frame.PitchVelocity:0.00} " +
                $"WEIGHT {frame.WeightFactor:0.00} SPEED {frame.SpeedFactor:0.00} PROFILE {frame.Profile} {frame.Reason}"
        };
    }

    public void Reset()
    {
        _control.Reset();
        _last = DateTime.MinValue;
    }

    public static string Phase(FlightSnapshot snap) => FlightPhaseResolver.Resolve(snap).ToString();

    public static double LiveMassScale(FlightSnapshot snap)
    {
        if (snap.TotalWeightPounds > 20000)
        {
            var reference = A320TypicalTakeoffLb;
            if (snap.Aircraft.IsPmdg777 || snap.Class == AircraftClass.WideBody)
                reference = 500000;
            else if (snap.Class == AircraftClass.HeavyWide)
                reference = 650000;
            else if (snap.Class == AircraftClass.LightGa)
                reference = 2400;
            return Numeric.Clamp(snap.TotalWeightPounds / reference, 0.72, 1.40);
        }

        var frac = snap.MassFraction;
        if (frac > 0)
            return Numeric.Clamp(0.85 + frac * 0.35, 0.72, 1.35);
        return 1.0;
    }

    public static double MoiScale(FlightSnapshot snap)
    {
        if (snap.PitchMoi < 1000)
            return 1.0;
        var refMoi = snap.PitchMoi < 50_000 ? 20_000.0 : 1_500_000.0;
        return Numeric.Clamp(snap.PitchMoi / refMoi, 0.80, 1.25);
    }

    public static double Authority(double feel, double response, double size, double ground)
    {
        var heavy = feel * Numeric.Clamp(size, 0.45, 1.6);
        var gain = 1.0 - heavy * 0.42 - ground * 0.12 + response * 0.18;
        return Numeric.Clamp(gain, 0.38, 1.0);
    }

    private static int ToAxis(double unit) => (int)Math.Round(Numeric.Clamp(unit, -1, 1) * 16384.0);
}
