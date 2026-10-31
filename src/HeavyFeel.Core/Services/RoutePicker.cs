using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;

namespace HeavyFeel.Core.Services;

/// <summary>
/// The route HeavyPro uses, picked by itself. Route is one of the three that exist
/// ("Real1", "Direct", "Masked"); BridgeAnswersBindings turns on the WASM bridge that
/// answers an MSFS stick binding with HeavyPro's shaped value.
/// </summary>
public sealed record RouteChoice(string Route, bool BridgeAnswersBindings, string Reason, bool StickMissingFallback = false)
{
    public bool IsDirect => Route != "Masked";
}

/// <summary>
/// One route for every aircraft, chosen from what HeavyPro can see instead of a setting:
///
/// - Stick still bound in MSFS: the MSFS bindings route, which is how every aircraft flew
///   right on 0.5.3 (GA, DC-3, C-46, the PMDG 777). Sending the stick Direct as well would
///   give the aircraft both sticks and it twitches.
/// - Except the BlueBird 757: its own flight controls read the stick events, so the MSFS
///   bindings route cannot shape it. HeavyPro sends it the stick directly and the WASM
///   bridge answers the MSFS binding with the shaped value, so the 757 ends every frame
///   on HeavyPro's stick.
/// - Stick not bound in MSFS: REAL1, tuned per aircraft; or Direct when you linked one of
///   your own profiles to this aircraft, so your sliders are used.
/// - The MSFS bindings route chosen but the stick never reaches HeavyPro there (this
///   aircraft uses an MSFS profile without the binding, or takes the stick first): REAL1.
/// </summary>
public static class RoutePicker
{
    public static RouteChoice Choose(bool stickBoundInMsfs, AircraftIdentity aircraft, bool ownProfileLinked, bool stickMissingOnMasked)
    {
        var direct = ownProfileLinked
            ? new RouteChoice("Direct", false, "your own profile for this aircraft")
            : new RouteChoice(Real1.RouteName, false, "feel tuned for this aircraft");
        if (!stickBoundInMsfs)
            return direct;
        if (aircraft.IsBlueBird757)
            return direct with { BridgeAnswersBindings = true, Reason = "the BlueBird 757 reads the stick itself; HeavyPro's in-sim bridge answers your MSFS binding" };
        if (stickMissingOnMasked)
            return direct with { Reason = "your stick never reached HeavyPro through MSFS for this aircraft", StickMissingFallback = true };
        return new RouteChoice("Masked", false, "your stick is bound in MSFS");
    }

    /// <summary>Short name for the app.</summary>
    public static string Label(RouteChoice choice) => choice.Route switch
    {
        "Masked" => "MSFS bindings",
        "Direct" => "Direct",
        _ => "REAL1"
    };
}
