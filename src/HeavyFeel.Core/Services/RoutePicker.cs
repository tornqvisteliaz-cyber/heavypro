using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;

namespace HeavyFeel.Core.Services;

/// <summary>
/// The route HeavyPro uses. Route is one of the three that exist ("Real1", "Direct",
/// "Masked"). PassThrough: HeavyPro hands the stick to the aircraft untouched (the aircraft
/// reads the MSFS binding itself and cannot be shaped while it is bound).
/// </summary>
public sealed record RouteChoice(string Route, bool PassThrough, string Reason)
{
    public bool IsDirect => Route != "Masked";
}

/// <summary>
/// Picks the route by itself unless you picked one (the override):
///
/// - Stick bound in MSFS: the MSFS bindings route, how every aircraft flew right on 0.5.3
///   and still does (GA, DC-3, C-46, the PMDG 777, 787, A320neo). Never Direct on top of a
///   bound stick: the aircraft then gets the raw stick and HeavyPro's in turn and jumps
///   (seen on the 777 and the BlueBird 757).
/// - Except aircraft whose own code reads the stick binding itself (BlueBird 757, Aerosoft
///   A330/A340): the MSFS bindings route cannot reach them (the A330 did not move) and
///   shaping them Direct fights the binding (the 757 jumped). HeavyPro hands them the stick
///   untouched so they fly normally; give them an MSFS profile without the stick axes and
///   pick REAL1 to get HeavyPro's weight.
/// - Stick not bound in MSFS: REAL1, tuned per aircraft; or Direct when you linked one of
///   your own profiles to this aircraft, so your sliders are used.
/// </summary>
public static class RoutePicker
{
    public const string Auto = "";

    public static RouteChoice Choose(bool stickBoundInMsfs, AircraftIdentity aircraft, bool ownProfileLinked, string overrideRoute = Auto)
    {
        if (!string.IsNullOrEmpty(overrideRoute))
            return new RouteChoice(overrideRoute, false, "picked by you");
        if (!stickBoundInMsfs)
        {
            return ownProfileLinked
                ? new RouteChoice("Direct", false, "your own profile for this aircraft")
                : new RouteChoice(Real1.RouteName, false, "feel tuned for this aircraft");
        }
        if (ReadsStickItself(aircraft))
        {
            var name = PaywareCatalog.Find(aircraft)?.Name ?? aircraft.DisplayName;
            return new RouteChoice("Direct", true, name + " reads your MSFS stick binding itself, so HeavyPro hands the stick over untouched");
        }
        return new RouteChoice("Masked", false, "your stick is bound in MSFS");
    }

    /// <summary>Aircraft whose own code takes the stick from the MSFS binding, past HeavyPro.</summary>
    public static bool ReadsStickItself(AircraftIdentity aircraft) =>
        aircraft.IsBlueBird757 || PaywareCatalog.Find(aircraft)?.ReadsStickItself == true;

    /// <summary>Short name for the app.</summary>
    public static string Label(RouteChoice choice) => choice.PassThrough ? "Hands-off" : choice.Route switch
    {
        "Masked" => "MSFS bindings",
        "Direct" => "Direct",
        _ => "REAL1"
    };
}
