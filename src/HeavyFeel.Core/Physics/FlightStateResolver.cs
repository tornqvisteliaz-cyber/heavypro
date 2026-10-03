namespace HeavyFeel.Core.Physics;

/// <summary>
/// GROUND/AIR and AP ON/OFF from documented SimVars.
/// Heading/altitude lock is NOT autopilot — Fenix can have HDG/ALT selected with AP off.
/// </summary>
public static class FlightStateResolver
{
    public static bool FlagOn(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return false;
        if (Math.Abs(value) < 1e-6)
            return false;
        if (value > 0.5 && value < 1.5)
            return true;
        if (Math.Abs(value - 1) < 0.01)
            return true;
        return value >= 0.5 && value <= 100;
    }

    public static bool IntFlagOn(int value) => value != 0;

    /// <summary>
    /// Airborne wins when height or speed says the wheels are off the pavement.
    /// Ground flags alone are not trusted — they stick on Fenix.
    /// </summary>
    public static bool ResolveOnGround(
        double isOnGround,
        double simOnGround,
        double altitudeAglFeet,
        double radioHeightFeet,
        double indicatedKnots = 0,
        int isOnGroundInt = 0,
        int simOnGroundInt = 0)
    {
        var agl = BestAgl(altitudeAglFeet, radioHeightFeet);
        var flag = IntFlagOn(isOnGroundInt) || FlagOn(isOnGround)
            || IntFlagOn(simOnGroundInt) || FlagOn(simOnGround);

        if (agl > 30)
            return false;
        if (indicatedKnots > 120 && agl > 12)
            return false;
        if (agl < 4 && flag)
            return true;
        if (agl < 8 && indicatedKnots < 40)
            return true;
        if (flag && agl < 18 && indicatedKnots < 90)
            return true;
        return agl < 5;
    }

    public static bool ResolveAutopilot(
        double autopilotMaster,
        double flightDirector,
        double headingLock,
        double altitudeLock,
        double fenixAp1,
        double fenixAp2,
        double fenixAp1Light,
        double fenixAp2Light,
        int autopilotMasterInt = 0)
    {
        // Flight director / HDG / ALT selected mode must not count as AP.
        _ = flightDirector;
        _ = headingLock;
        _ = altitudeLock;

        return IntFlagOn(autopilotMasterInt)
            || FlagOn(autopilotMaster)
            || FlagOn(fenixAp1)
            || FlagOn(fenixAp2)
            || FlagOn(fenixAp1Light)
            || FlagOn(fenixAp2Light);
    }

    public static string AutopilotLabel(bool engaged, double fenixAp1, double fenixAp2, double master)
    {
        if (!engaged)
            return "AP OFF";
        if (FlagOn(fenixAp1) && FlagOn(fenixAp2))
            return "AP ON (AP1+AP2)";
        if (FlagOn(fenixAp1))
            return "AP ON (AP1)";
        if (FlagOn(fenixAp2))
            return "AP ON (AP2)";
        if (FlagOn(master))
            return "AP ON";
        return "AP ON";
    }

    private static double BestAgl(double altAgl, double radio)
    {
        var a = San(altAgl);
        var r = San(radio);
        if (a > 0 && r > 0)
            return Math.Min(a, r);
        return Math.Max(a, r);
    }

    private static double San(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
            return 0;
        return v;
    }
}
