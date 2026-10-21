using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>The kind of flight-control feel the real aircraft gives its pilot.</summary>
public enum FeelSystem
{
    /// <summary>Cables to the surfaces (light aircraft, most turboprops and bizjets): force grows with dynamic pressure.</summary>
    Mechanical,

    /// <summary>Hydraulic surfaces with an artificial feel unit (Boeing-style yokes, A300/A310): column force grows with airspeed.</summary>
    ArtificialFeel,

    /// <summary>Fly-by-wire sidestick (Airbus A320 onwards, A220): constant spring, stick commands g and roll rate.</summary>
    Sidestick
}

/// <summary>
/// Per-family feel rules layered on top of the weight/inertia model.
/// A spring-centred desktop stick cannot change its force, so "heavier" is
/// rendered as slower control travel:
/// - Mechanical and artificial feel stiffen with dynamic pressure, so the same
///   stick feels light taxiing and heavy at cruise speed.
/// - Hydraulic surfaces cannot move faster than their actuators, so airliners
///   never go below a quarter second to full travel.
/// - An Airbus-style sidestick has the same spring at every speed and commands a
///   rate, so it starts from half the shaping and no airspeed scaling; the
///   sliders still take it as heavy as the user wants.
/// - Jets limit rudder throw as speed rises (747 ratio changer, 737 blowdown /
///   pressure reducer); study-level add-ons model that themselves, so it is only
///   applied to the others.
/// </summary>
public sealed record ControlFeel(
    FeelSystem System,
    string Label,
    double ReferenceQ,
    double MinTravelSeconds,
    double MaxTravelSeconds,
    bool LimitsRudder,
    double BaseScale = 1.0)
{
    // Dynamic pressure, psf, at a typical working speed: ~110 kt for light
    // aircraft, ~250 kt for airliners. At this q the stick feels "normal heavy".
    private const double LightAircraftQ = 41;
    private const double AirlinerQ = 212;

    public static ControlFeel For(FlightSnapshot snapshot)
    {
        var id = (snapshot.Aircraft.Title + " " + snapshot.Aircraft.AtcModel + " " + snapshot.Aircraft.AtcType).ToUpperInvariant();
        var studyLevel = snapshot.Aircraft.IsFenixA320 || snapshot.Aircraft.IsPmdg777
            || ContainsAny(id, "PMDG", "FENIX", "INIBUILDS", "FLYBYWIRE", "A32NX", "A380X", "IFLY", "FSLABS", "MADDOG", "HEADWIND", "LEONARDO");

        if (IsSidestickAirbus(id))
            return new(FeelSystem.Sidestick, "FBW sidestick", AirlinerQ, 0.08, 3.0, !studyLevel, BaseScale: 0.5);
        if (snapshot.Class is AircraftClass.NarrowBody or AircraftClass.WideBody or AircraftClass.HeavyWide)
            return new(FeelSystem.ArtificialFeel, "Artificial feel (hydraulic)", AirlinerQ, 0.25, 4.0, !studyLevel);
        return new(FeelSystem.Mechanical, "Mechanical", LightAircraftQ, 0.08, 4.0, false);
    }

    /// <summary>
    /// Travel-time multiplier from dynamic pressure. 1 = unchanged. Sidesticks
    /// ignore airspeed. AirspeedResponse 65 % (the default) gives the base curve.
    /// </summary>
    public double SpeedScale(AppSettings settings, FlightSnapshot snapshot)
    {
        if (System == FeelSystem.Sidestick || !settings.AirspeedResponseEnabled)
            return 1.0;
        var q = DynamicPressure(snapshot);
        // Light at taxi speed, ~1.2 at the reference speed, levelling off above.
        var curve = Numeric.Clamp(0.85 + 0.35 * Math.Sqrt(q / ReferenceQ), 0.85, 1.35);
        var strength = Numeric.Clamp(settings.AirspeedResponse, 0, 1) / 0.65;
        return Math.Max(0.5, 1.0 + (curve - 1.0) * strength);
    }

    /// <summary>Fraction of full rudder throw available at this airspeed (1 = all).</summary>
    public double RudderAuthority(AppSettings settings, double indicatedKnots)
    {
        if (!LimitsRudder || !settings.RudderLimiter || double.IsNaN(indicatedKnots) || indicatedKnots <= 160)
            return 1.0;
        // 747-style schedule: full throw to 160 kt, down to a fifth by ~320 kt.
        return Numeric.Clamp(1.0 - (indicatedKnots - 160) / 160.0 * 0.8, 0.2, 1.0);
    }

    /// <summary>Dynamic pressure in psf; falls back to indicated airspeed if the sim gives none.</summary>
    public static double DynamicPressure(FlightSnapshot snapshot)
    {
        var q = snapshot.DynamicPressurePsf;
        if (!double.IsNaN(q) && !double.IsInfinity(q) && q > 0.5)
            return q;
        var kts = Math.Max(0, double.IsNaN(snapshot.AirspeedIndicatedKnots) ? 0 : snapshot.AirspeedIndicatedKnots);
        // q = 1/2 * rho0 * V^2 with V in ft/s (1 kt = 1.68781 ft/s, rho0 = 0.0023769 slug/ft^3).
        return 0.5 * 0.0023769 * Math.Pow(kts * 1.68781, 2);
    }

    private static bool IsSidestickAirbus(string id) =>
        ContainsAny(id, "A318", "A319", "A320", "A321", "A32N", "A20N", "A21N", "A330", "A332", "A333", "A338", "A339",
            "A340", "A350", "A359", "A35K", "A380", "A388", "A220", "BCS1", "BCS3", "CS100", "CS300");

    private static bool ContainsAny(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            if (text.Contains(token))
                return true;
        return false;
    }
}
