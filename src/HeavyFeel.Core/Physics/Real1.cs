using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// One aircraft family's REAL1 tuning. Times are full-travel seconds at the reference
/// airspeed; the inertia model makes small inputs quicker than full ones.
/// </summary>
public sealed record Real1Tuning(
    string Family,
    double Deadzone,
    double Extremity,
    double Expo,
    double PitchSeconds,
    double RollSeconds,
    double YawSeconds,
    double AirspeedFeel,
    double ReturnDelaySeconds,
    double ReturnRate,
    double Damping,
    bool RealisticRudder);

/// <summary>
/// REAL1: the input route that sends the stick the Direct way and tunes the feel by
/// itself for each aircraft family, from how the best add-ons do it:
///
/// - Fenix, iniBuilds and PMDG all recommend a LINEAR axis with a small deadzone; their
///   realism comes from modelling stick-to-surface dynamics, not from curves. So REAL1
///   keeps the centre linear for fly-by-wire and only adds the actuator-like lag that
///   makes Fenix feel "planted" (Fenix itself gets a light touch: it models this already).
/// - Boeing columns sit on an artificial-feel unit: force grows with dynamic pressure.
///   REAL1 gives them a slightly soft centre (PMDG 777 flyers use about -15 to -20 %
///   sensitivity), an extremity zone so full deflection arrives just before the stop,
///   and travel that slows as airspeed builds.
/// - Light aircraft have cables: light and direct at taxi, firmer at speed.
/// - Every family: realistic rudder pedals and a spring return to centre.
///
/// Later versions get higher numbers (REAL2, REAL3, …) so users can tell them apart.
/// </summary>
public static class Real1
{
    public const string RouteName = "Real1";
    public const string DisplayName = "REAL1";

    public static bool IsActive(AppSettings settings) =>
        string.Equals(settings.InputRoute, RouteName, StringComparison.OrdinalIgnoreCase);

    private static readonly Real1Tuning Fenix =
        new("Fenix A320 (light touch: Fenix models its own FBW)", 0.03, 0.02, 0.00, 0.20, 0.18, 0.55, 0.0, 0.00, 1.8, 35, true);
    private static readonly Real1Tuning AirbusNarrow =
        new("Airbus A320 family fly-by-wire", 0.03, 0.02, 0.00, 0.32, 0.26, 0.60, 0.0, 0.00, 1.6, 40, true);
    private static readonly Real1Tuning AirbusWide =
        new("Airbus widebody fly-by-wire (A330/A350/A380)", 0.03, 0.02, 0.08, 0.45, 0.38, 0.80, 0.0, 0.00, 1.4, 50, true);
    private static readonly Real1Tuning Boeing777 =
        new("Boeing 777 artificial feel", 0.02, 0.05, 0.20, 1.00, 0.80, 1.10, 1.0, 0.08, 0.8, 65, true);
    private static readonly Real1Tuning Boeing737 =
        new("Boeing 737 artificial feel", 0.02, 0.03, 0.12, 0.75, 0.60, 0.90, 1.0, 0.05, 0.9, 55, true);
    private static readonly Real1Tuning Boeing787 =
        new("Boeing 787 fly-by-wire column", 0.02, 0.03, 0.15, 0.85, 0.70, 1.00, 0.6, 0.05, 0.9, 55, true);
    private static readonly Real1Tuning HeavyAirliner =
        new("Heavy airliner artificial feel", 0.02, 0.05, 0.20, 1.20, 0.95, 1.20, 1.0, 0.08, 0.8, 70, true);
    private static readonly Real1Tuning Airliner =
        new("Airliner artificial feel", 0.02, 0.03, 0.12, 0.80, 0.65, 0.95, 1.0, 0.05, 0.9, 55, true);
    private static readonly Real1Tuning Regional =
        new("Turboprop / regional / business jet", 0.02, 0.02, 0.10, 0.50, 0.40, 0.60, 0.8, 0.03, 1.1, 45, true);
    private static readonly Real1Tuning LightGa =
        new("Light aircraft cables", 0.015, 0.01, 0.08, 0.22, 0.18, 0.35, 1.0, 0.00, 1.2, 35, true);

    /// <summary>The tuning for the aircraft MSFS reports.</summary>
    public static Real1Tuning For(FlightSnapshot snapshot)
    {
        var a = snapshot.Aircraft;
        var id = ($"{a.Title} {a.AtcModel} {a.AtcType}").ToUpperInvariant();
        if (a.IsFenixA320)
            return Fenix;
        if (a.IsPmdg777 || Has(id, "777", "B77W", "B77L", "B772", "B773"))
            return Boeing777;
        if (Has(id, "787", "B788", "B789", "B78X"))
            return Boeing787;
        if (Has(id, "737", "B737", "B738", "B739", "B38M", "B39M", "B736"))
            return Boeing737;
        if (Has(id, "A350", "A359", "A35K", "A330", "A332", "A333", "A339", "A340", "A380", "A388"))
            return AirbusWide;
        if (Has(id, "A318", "A319", "A320", "A321", "A20N", "A21N", "A32N"))
            return AirbusNarrow;
        return snapshot.Class switch
        {
            AircraftClass.HeavyWide or AircraftClass.WideBody => HeavyAirliner,
            AircraftClass.NarrowBody => Airliner,
            AircraftClass.Regional => Regional,
            AircraftClass.LightGa => LightGa,
            _ => snapshot.TotalWeightPounds > 12_000 ? Airliner : LightGa
        };
    }

    /// <summary>Linear centre with deadzone, optional soft centre, full output just before the stop.</summary>
    public static double Shape(double raw, Real1Tuning tuning)
    {
        var x = InputDynamicsEngine.Shape(raw, tuning.Deadzone, 1.0, tuning.Expo, tuning.Expo > 0 ? InputCurve.Expo : InputCurve.Linear);
        var reach = 1.0 - Numeric.Clamp(tuning.Extremity, 0, 0.2);
        return Numeric.Clamp(x / reach, -1, 1);
    }

    /// <summary>Artificial-feel travel multiplier: 1 at taxi, firmer with dynamic pressure.</summary>
    public static double AirspeedScale(Real1Tuning tuning, FlightSnapshot snapshot, ControlFeel feel)
    {
        if (tuning.AirspeedFeel <= 0)
            return 1.0;
        var q = ControlFeel.DynamicPressure(snapshot);
        var curve = Numeric.Clamp(0.85 + 0.35 * Math.Sqrt(q / feel.ReferenceQ), 0.85, 1.35);
        return 1.0 + (curve - 1.0) * tuning.AirspeedFeel;
    }

    private static bool Has(string text, params string[] tokens)
    {
        foreach (var t in tokens)
            if (text.Contains(t))
                return true;
        return false;
    }
}
