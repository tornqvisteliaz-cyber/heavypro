using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// One aircraft family's REAL1 tuning. Times are full-travel seconds at the reference
/// airspeed (small inputs are quicker than full ones); MomentSeconds is how long the
/// control's rate takes to build up, the moment of inertia flight-model mods add.
/// RollDeadzone / RollExpo give the wheel its own centre when it should be calmer than
/// the column; left out, roll shapes like pitch.
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
    bool RealisticRudder,
    double MomentSeconds,
    double? RollDeadzone = null,
    double? RollExpo = null);

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
/// - Like the flight-model mods on flightsim.to: most add-ons have too little moment of
///   inertia, so the rate builds slowly and carries on a touch (MomentSeconds); the
///   centre is softened so small corrections are progressive; full deflection keeps full
///   authority; and damping is not overdone, which only makes an aircraft sluggish.
/// - The Boeing 777 flies like the FlightFactor 777 (FF777): big and settled, with a calm
///   wheel around centre so it never twitches in roll, precise small corrections in pitch,
///   more control mass than the other Boeings and a column that firms up with airspeed.
///   REAL1 picks it by itself whenever a 777 loads; nothing to set up.
/// - The 757 (BlueBird Simulations) sits between the 737 and the 777.
/// - The Normal / Medium / Realistic buttons scale how strong REAL1 is.
///
/// Later versions get higher numbers (REAL2, REAL3, …) so users can tell them apart.
/// </summary>
public static class Real1
{
    public const string RouteName = "Real1";
    public const string DisplayName = "REAL1";

    public static bool IsActive(AppSettings settings) =>
        string.Equals(settings.InputRoute, RouteName, StringComparison.OrdinalIgnoreCase);

    // Family, deadzone, extremity, expo, pitch/roll/yaw seconds, airspeed feel, return delay, return rate, damping, rudder, moment seconds
    private static readonly Real1Tuning Fenix =
        new("Fenix A320 (light touch: Fenix models its own FBW)", 0.03, 0.02, 0.10, 1.60, 1.45, 2.20, 0.0, 0.00, 1.3, 50, true, 0.12);
    private static readonly Real1Tuning AirbusNarrow =
        new("Airbus A320 family fly-by-wire", 0.03, 0.02, 0.12, 2.20, 1.95, 2.70, 0.0, 0.00, 1.2, 50, true, 0.15);
    private static readonly Real1Tuning AirbusWide =
        new("Airbus widebody fly-by-wire (A330/A350/A380)", 0.03, 0.02, 0.15, 2.90, 2.60, 3.40, 0.0, 0.03, 1.1, 55, true, 0.20);
    private static readonly Real1Tuning Boeing777 =
        new("Boeing 777 artificial feel, FF777-style", 0.03, 0.05, 0.22, 3.90, 3.40, 4.10, 1.0, 0.08, 0.9, 60, true, 0.28,
            RollDeadzone: 0.05, RollExpo: 0.20);
    // The 757 sits between the 737 and the 777: a big column on the same kind of feel
    // unit, but famously crisp in roll for its size.
    private static readonly Real1Tuning Boeing757 =
        new("Boeing 757 artificial feel", 0.02, 0.04, 0.20, 3.30, 2.85, 3.60, 1.0, 0.06, 0.95, 58, true, 0.23);
    private static readonly Real1Tuning Boeing737 =
        new("Boeing 737 artificial feel", 0.02, 0.03, 0.18, 3.00, 2.65, 3.20, 1.0, 0.05, 1.0, 55, true, 0.20);
    private static readonly Real1Tuning Boeing787 =
        new("Boeing 787 fly-by-wire column", 0.02, 0.03, 0.18, 3.30, 2.95, 3.50, 0.6, 0.05, 1.0, 55, true, 0.22);
    private static readonly Real1Tuning HeavyAirliner =
        new("Heavy airliner artificial feel", 0.02, 0.05, 0.22, 4.10, 3.60, 4.30, 1.0, 0.08, 0.9, 60, true, 0.28);
    private static readonly Real1Tuning Airliner =
        new("Airliner artificial feel", 0.02, 0.03, 0.18, 3.00, 2.65, 3.20, 1.0, 0.05, 1.0, 55, true, 0.20);
    private static readonly Real1Tuning Regional =
        new("Turboprop / regional / business jet", 0.02, 0.02, 0.15, 2.00, 1.80, 2.50, 0.8, 0.03, 1.1, 50, true, 0.12);
    private static readonly Real1Tuning LightGa =
        new("Light aircraft cables", 0.015, 0.01, 0.12, 1.00, 0.90, 1.35, 1.0, 0.00, 1.2, 45, true, 0.07);

    /// <summary>The tuning for the aircraft MSFS reports.</summary>
    public static Real1Tuning For(FlightSnapshot snapshot)
    {
        var a = snapshot.Aircraft;
        var id = ($"{a.Title} {a.AtcModel} {a.AtcType}").ToUpperInvariant();
        if (a.IsFenixA320)
            return Fenix;
        if (a.IsPmdg777 || Has(id, "777", "B77W", "B77L", "B772", "B773"))
            return Boeing777;
        if (a.IsBlueBird757 || Has(id, "757", "B752", "B753"))
            return Boeing757;
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

    /// <summary>REAL1 strength from the feel level: Normal is light, Realistic (or your own sliders) is full.</summary>
    public static double Strength(AppSettings settings) => FeelPresets.Match(settings) switch
    {
        FeelLevel.Normal => 0.45,
        FeelLevel.Medium => 0.75,
        _ => 1.0
    };

    /// <summary>
    /// One control axis with mass: its rate builds up over MomentSeconds (moment of
    /// inertia) toward a rate capped by the travel time, and eases into the target so it
    /// carries on only a touch instead of stopping dead.
    /// </summary>
    public static AxisSample StepAxis(AxisDynamicsState state, double raw, double dt, double fullTravelSeconds,
        double momentSeconds, double returnDelaySeconds, double returnRate)
    {
        raw = double.IsNaN(raw) || double.IsInfinity(raw) ? 0 : Numeric.Clamp(raw, -1, 1);
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

        var error = target - state.Position;
        var time = Numeric.Clamp(fullTravelSeconds * (0.35 + 0.65 * Numeric.Clamp(Math.Abs(error), 0, 1)), 0.05, 6.0);
        var maxRate = 1.0 / time * (returning ? Numeric.Clamp(returnRate, 0.1, 2.0) : 1.0);
        var moment = Numeric.Clamp(momentSeconds, 0.02, 1.5);
        var desired = Numeric.Clamp(error / (3.0 * moment), -maxRate, maxRate);
        var before = state.Velocity;
        state.Velocity += (desired - state.Velocity) * (1.0 - Math.Exp(-dt / moment));
        state.Position = Numeric.Clamp(state.Position + state.Velocity * dt, -1, 1);
        if (Math.Abs(target - state.Position) < 0.0005 && Math.Abs(state.Velocity) < 0.002)
        {
            state.Position = target;
            state.Velocity = 0;
        }
        if (double.IsNaN(state.Position) || double.IsInfinity(state.Position))
        {
            state.Reset();
            return new AxisSample(raw, raw, raw, 0, 0);
        }
        return new AxisSample(raw, target, state.Position, state.Velocity, state.Velocity - before);
    }

    /// <summary>Linear centre with deadzone, optional soft centre, full output just before the stop.</summary>
    public static double Shape(double raw, Real1Tuning tuning)
    {
        var x = InputDynamicsEngine.Shape(raw, tuning.Deadzone, 1.0, tuning.Expo, tuning.Expo > 0 ? InputCurve.Expo : InputCurve.Linear);
        var reach = 1.0 - Numeric.Clamp(tuning.Extremity, 0, 0.2);
        return Numeric.Clamp(x / reach, -1, 1);
    }

    /// <summary>The wheel: its own calmer centre where the family has one, else like pitch.</summary>
    public static double ShapeRoll(double raw, Real1Tuning tuning) =>
        Shape(raw, tuning with { Deadzone = tuning.RollDeadzone ?? tuning.Deadzone, Expo = tuning.RollExpo ?? tuning.Expo });

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
