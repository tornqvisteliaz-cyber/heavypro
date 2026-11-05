using HeavyPro.Core.Models;
using HeavyPro.Core.Services;

namespace HeavyPro.Core.Physics;

/// <summary>Multipliers on each axis's travel time and on the moment (rate build-up time). Below 1 is quicker.</summary>
public readonly record struct ResponseFactors(double Pitch, double Roll, double Yaw, double Moment, string Awareness)
{
    public static ResponseFactors Neutral { get; } = new(1, 1, 1, 1, "");
}

/// <summary>
/// The response settings, applied on every route (REAL1, Direct and MSFS bindings) on top of
/// each aircraft family's own tuning:
///
/// - Realistic Response (default 40) scales every axis's travel time: 0 is 1.3x 0.8.0's (heavier),
///   40 is 0.77x (quicker), 100 is 0.35x, and the moment by the square root of that. Exponential, so
///   each step is felt the same. 20 is exactly 0.8.0.
/// - Pitch / roll / yaw responsiveness (default 50, neutral) adjust one axis: 0 is 1.5x, 100 is 1/1.5x.
/// - Input smoothing (default 35) scales the moment, how long the control's rate takes to build:
///   0 is 0.35x 0.8.0's, 35 is about 0.6x, 100 is 1.6x. Too much of it is what makes a stick feel late.
/// - Aircraft awareness: heavier (relative to the aircraft's own maximum) is slower, an aft
///   centre of gravity is a lighter pitch, and nosewheel steering at taxi speed is quicker in yaw.
///   SimBrief weights fill in when the sim gives no weight. Anything unavailable counts as neutral.
///
/// This only changes how the stick input is shaped; the aircraft's flight model is untouched.
/// </summary>
public static class ResponseTuning
{
    public static double TravelScale(double realisticResponse) => InputDynamicsEngine.SliderScale(realisticResponse, 1.3, 0.35);

    public static double AxisScale(double responsiveness) => InputDynamicsEngine.SliderScale(responsiveness, 1.5, 1.0 / 1.5);

    public static double MomentScale(double smoothing) => InputDynamicsEngine.SliderScale(smoothing, 0.35, 1.6);

    /// <summary>
    /// The moment multiplier from both settings. Near the target the output closes in at a pace set by
    /// the moment, not the travel time, so Realistic Response shortens it too (by the square root of its
    /// travel scale, so 20, 0.8.0's travel, still leaves it unchanged); otherwise the slider would barely
    /// change the end of a REAL1 input.
    /// </summary>
    public static double MomentFor(AppSettings settings) =>
        MomentScale(settings.InputSmoothing) * Math.Sqrt(TravelScale(settings.RealisticResponse));

    public static ResponseFactors For(AppSettings settings, FlightSnapshot snapshot, FlightPlanInfo? plan = null)
    {
        var travel = TravelScale(settings.RealisticResponse);
        var aware = settings.AircraftAwareness ? Aware(snapshot, plan) : AircraftResponse.Neutral;
        return new ResponseFactors(
            travel * AxisScale(settings.PitchResponsiveness) * aware.Mass * aware.Pitch,
            travel * AxisScale(settings.RollResponsiveness) * aware.Mass,
            travel * AxisScale(settings.YawResponsiveness) * aware.Mass * aware.Yaw,
            MomentFor(settings),
            aware.Label);
    }

    /// <summary>What the aircraft's own state does to the response.</summary>
    public static AircraftResponse Aware(FlightSnapshot s, FlightPlanInfo? plan = null)
    {
        var notes = new List<string>();

        // Weight relative to the aircraft's own maximum: an empty 777 and a full C172 are both
        // "light for what they are". The family tuning already covers 777 vs C172.
        var mass = 1.0;
        var load = LoadFraction(s, plan, out var source);
        if (load > 0)
        {
            mass = Numeric.Clamp(0.88 + 0.24 * load, 0.9, 1.15);
            notes.Add($"{load:P0} of max weight{source}");
        }

        // Aft CG needs less stick force to pitch; forward CG more.
        var pitch = 1.0;
        if (s.CgPercentMac is > 0.05 and < 0.6)
        {
            pitch = Numeric.Clamp(1.0 - 0.5 * (s.CgPercentMac - 0.25), 0.92, 1.08);
            notes.Add($"CG {s.CgPercentMac:P0} MAC");
        }

        // Nosewheel steering at taxi speed answers the pedals quicker than the rudder in flight.
        var yaw = 1.0;
        if (s.OnGround && s.GroundSpeedKnots < 30)
        {
            yaw = 0.75;
            notes.Add("ground steering");
        }

        return new AircraftResponse(mass, pitch, yaw, string.Join(" · ", notes));
    }

    /// <summary>Weight as a fraction of max: the sim's when it gives both, else the SimBrief plan's for this aircraft type; 0 when unknown.</summary>
    public static double LoadFraction(FlightSnapshot s, FlightPlanInfo? plan, out string source)
    {
        source = "";
        if (IsValid(s.TotalWeightPounds) && IsValid(s.MaxGrossWeightPounds) && s.MaxGrossWeightPounds >= s.TotalWeightPounds * 0.5)
            return Numeric.Clamp(s.TotalWeightPounds / s.MaxGrossWeightPounds, 0.2, 1.1);

        if (plan?.Ofp is { } ofp && ofp.TakeoffWeightPounds > 0 && ofp.MaxTakeoffWeightPounds > 0 && SameType(s.Aircraft, ofp.AircraftType))
        {
            source = " (SimBrief)";
            return Numeric.Clamp(ofp.TakeoffWeightPounds / ofp.MaxTakeoffWeightPounds, 0.2, 1.1);
        }
        return 0;
    }

    /// <summary>The plan was made for the aircraft that is loaded (its ICAO type is in MSFS's names for it).</summary>
    public static bool SameType(AircraftIdentity aircraft, string icaoType)
    {
        if (string.IsNullOrWhiteSpace(icaoType))
            return false;
        var names = $"{aircraft.Title} {aircraft.AtcModel} {aircraft.AtcType}".ToUpperInvariant();
        return names.Contains(icaoType.Trim().ToUpperInvariant());
    }

    private static bool IsValid(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v > 1;
}

/// <summary>Multipliers from the aircraft's state, and a short description of what was used.</summary>
public readonly record struct AircraftResponse(double Mass, double Pitch, double Yaw, string Label)
{
    public static AircraftResponse Neutral { get; } = new(1, 1, 1, "");
}
