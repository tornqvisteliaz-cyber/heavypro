using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// Sends dynamically filtered control inputs to SimConnect. No body-rate writes.
/// </summary>
public sealed class InertiaEngine
{
    private readonly InputDynamicsEngine _input = new();
    private DateTime _last = DateTime.MinValue;

    public InfluenceCommand Step(AppSettings settings, FlightSnapshot snap, bool masterEnable)
    {
        var dt = 0.016;
        if (_last != DateTime.MinValue)
        {
            var raw = (snap.Utc - _last).TotalSeconds;
            if (raw > 0)
                dt = Numeric.Clamp(raw, 1.0 / 240.0, 0.1);
        }
        _last = snap.Utc;

        if (!masterEnable)
        {
            Reset();
            return InfluenceCommand.Idle("Master off — no writes");
        }

        if (snap.AutopilotMaster)
        {
            Reset();
            return InfluenceCommand.Idle("Autopilot on — writes paused");
        }

        var frame = _input.Step(settings, snap, dt);
        var moving = Math.Abs(frame.FilteredElevator) + Math.Abs(frame.FilteredAileron) + Math.Abs(frame.FilteredRudder) > 0.01;
        var settling = Math.Abs(frame.FinalElevator) + Math.Abs(frame.FinalAileron) + Math.Abs(frame.FinalRudder) > 0.0005;
        if (!moving && !settling)
            return InfluenceCommand.Idle("Stick released / centred — no writes");

        var phase = FlightPhaseResolver.Resolve(snap).ToString();

        return new InfluenceCommand
        {
            Active = true,
            WriteRates = false,
            WriteAxes = true,
            WriteYoke = false,
            WriteThrottle = false,
            YokeXOut = frame.FinalAileron,
            YokeYOut = frame.FinalElevator,
            RudderOut = frame.FinalRudder,
            ElevatorAxis = ToAxis(frame.FinalElevator),
            AileronAxis = ToAxis(frame.FinalAileron),
            RudderAxis = ToAxis(frame.FinalRudder),
            Phase = phase,
            ProfileName = frame.Profile,
            Mix = 1,
            RawPitch = frame.RawElevator,
            FilteredPitch = frame.FilteredElevator,
            OutPitch = frame.FinalElevator,
            RawRoll = frame.RawAileron,
            FilteredRoll = frame.FilteredAileron,
            OutRoll = frame.FinalAileron,
            RawYaw = frame.RawRudder,
            FilteredYaw = frame.FilteredRudder,
            OutYaw = frame.FinalRudder,
            AirspeedFactor = frame.AirspeedFactor,
            Reason = $"{phase} · {frame.Profile} · input dynamics · airspeed ×{frame.AirspeedFactor:0.00}"
        };
    }

    public void Reset()
    {
        _input.Reset();
        _last = DateTime.MinValue;
    }

    public static string Phase(FlightSnapshot snap) => FlightPhaseResolver.Resolve(snap).ToString();

    public static double LiveMassScale(FlightSnapshot snap)
    {
        const double a320TypicalTakeoffLb = 154000;
        if (snap.TotalWeightPounds > 20000)
        {
            var reference = a320TypicalTakeoffLb;
            if (snap.Aircraft.IsPmdg777 || snap.Class == AircraftClass.WideBody)
                reference = 500000;
            else if (snap.Class == AircraftClass.HeavyWide)
                reference = 650000;
            else if (snap.Class == AircraftClass.LightGa)
                reference = 2400;
            return Numeric.Clamp(snap.TotalWeightPounds / reference, 0.72, 1.40);
        }

        var fraction = snap.MassFraction;
        return fraction > 0 ? Numeric.Clamp(0.85 + fraction * 0.35, 0.72, 1.35) : 1.0;
    }

    public static double MoiScale(FlightSnapshot snap)
    {
        if (snap.PitchMoi < 1000)
            return 1.0;
        var referenceMoi = snap.PitchMoi < 50000 ? 20000.0 : 1500000.0;
        return Numeric.Clamp(snap.PitchMoi / referenceMoi, 0.80, 1.25);
    }

    // Kept for older UI and settings consumers. The active input dynamics path
    // does not use this authority multiplier.
    public static double Authority(double feel, double response, double size, double ground)
    {
        var heavy = feel * Numeric.Clamp(size, 0.45, 1.6);
        var gain = 1.0 - heavy * 0.42 - ground * 0.12 + response * 0.18;
        return Numeric.Clamp(gain, 0.38, 1.0);
    }

    private static int ToAxis(double unit) => (int)Math.Round(Numeric.Clamp(unit, -1, 1) * 16384.0);
}
