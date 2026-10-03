using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

public enum FlightPhase
{
    Ground,
    Takeoff,
    Climb,
    Cruise,
    Descent,
    Approach,
    Landing
}

public static class FlightPhaseResolver
{
    public static FlightPhase Resolve(FlightSnapshot snap)
    {
        var ias = snap.AirspeedIndicatedKnots;
        var agl = BestHeight(snap);
        var vs = snap.VerticalSpeedFpm;
        var flaps = Math.Max(snap.FlapsHandlePercent, snap.TrailingEdgeFlapsPercent);

        if (snap.OnGround)
            return ias < 40 ? FlightPhase.Ground : FlightPhase.Takeoff;

        if (agl < 50)
            return FlightPhase.Landing;

        if (agl < 2500 && flaps > 15 && ias < 210)
            return FlightPhase.Approach;

        if (vs > 450)
            return FlightPhase.Climb;

        if (vs < -450)
            return FlightPhase.Descent;

        return FlightPhase.Cruise;
    }

    public static double PhaseFactor(FlightPhase phase, AircraftProfile profile)
    {
        return phase switch
        {
            FlightPhase.Ground => profile.GroundInputFactor,
            FlightPhase.Takeoff => profile.GroundInputFactor * 0.96,
            FlightPhase.Approach => profile.AirInputFactor * 1.04,
            FlightPhase.Landing => profile.AirInputFactor * 1.02,
            FlightPhase.Climb => profile.AirInputFactor,
            FlightPhase.Descent => profile.AirInputFactor,
            _ => profile.AirInputFactor
        };
    }

    private static double BestHeight(FlightSnapshot snap)
    {
        var a = snap.AltitudeAglFeet > 0 ? snap.AltitudeAglFeet : 0;
        var r = snap.RadioHeightFeet > 0 ? snap.RadioHeightFeet : 0;
        if (a > 0 && r > 0)
            return Math.Min(a, r);
        return Math.Max(a, r);
    }
}
