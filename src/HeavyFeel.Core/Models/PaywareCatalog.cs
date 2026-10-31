namespace HeavyFeel.Core.Models;

/// <summary>Which REAL1 feel an aircraft gets (see Real1): the kind of controls it has.</summary>
public enum FeelFamily
{
    Fenix,
    AirbusNarrow,
    AirbusWide,
    Boeing777,
    Boeing757,
    Boeing737,
    Boeing787,
    HeavyAirliner,
    Airliner,
    Regional,
    LightGa
}

/// <summary>A payware aircraft HeavyPro knows by name.</summary>
/// <param name="Name">What the app shows, e.g. "PMDG 777-300ER".</param>
/// <param name="ModelsOwnRudderLimit">A study-level add-on that models its own rudder limiting, so HeavyPro's stays out (only where HeavyPro already treated it so).</param>
public sealed record PaywareAircraft(string Name, FeelFamily Family, bool ModelsOwnRudderLimit);

/// <summary>
/// The major payware aircraft for MSFS 2024, recognised from the title and ATC codes MSFS
/// reports (taken from the add-ons' own aircraft.cfg where installed here), so HeavyPro
/// can name them and give each the feel of its real controls:
///
/// - Boeing columns on an artificial-feel unit (737, 757, 777, 727, MD-80/90, DC-9, MD-11,
///   TriStar, A300/A310, Concorde) get the column feel that firms up with airspeed.
/// - Airbus fly-by-wire sidesticks (A320 family, A330, A340, A350) stay linear and planted.
/// - Regionals, turboprops and bizjets get the lighter regional feel; piston GA the cables.
/// </summary>
public static class PaywareCatalog
{
    private sealed record Entry(Func<string, AircraftIdentity, bool> Matches, Func<string, string> Name, FeelFamily Family, bool OwnRudder);

    private static Entry E(Func<string, bool> matches, string name, FeelFamily family, bool ownRudder = false) =>
        new((blob, _) => matches(blob), _ => name, family, ownRudder);

    private static bool Has(string blob, params string[] any) => any.Any(blob.Contains);

    // First match wins: specific add-ons before the generic types they share codes with.
    private static readonly Entry[] Entries =
    {
        // ---- Boeing ----
        new((b, id) => id.IsPmdg777, Pmdg777Name, FeelFamily.Boeing777, true),
        E(b => Has(b, "PMDG 737", "PMDG737"), "PMDG 737", FeelFamily.Boeing737, true),
        E(b => Has(b, "IFLY") && Has(b, "737"), "iFly 737 MAX 8", FeelFamily.Boeing737, true),
        E(b => Has(b, "CSS B737", "CSS 737"), "CSS 737 Classic", FeelFamily.Boeing737),
        new((b, id) => id.IsBlueBird757, _ => "BlueBird 757-200", FeelFamily.Boeing757, true),
        E(b => Has(b, "BOEING 727", "B721", "B722", "B727"), "FSS Boeing 727", FeelFamily.Airliner),
        E(b => Has(b, "B789") && Has(b, "787-9") && !Has(b, "ASOBO"), "Horizon Simulations 787-9", FeelFamily.Boeing787),
        E(b => Has(b, "PMDG DC-6", "PMDG DC6"), "PMDG DC-6", FeelFamily.Airliner),

        // ---- Airbus ----
        new((b, id) => id.IsFenixA320, FenixName, FeelFamily.Fenix, true),
        E(b => Has(b, "FSLABS", "FSL ("), "FSLabs A321", FeelFamily.AirbusNarrow, true),
        E(b => Has(b, "INIBUILDS A32", "INIBUILDS A20N", "INIBUILDS A21N"), "iniBuilds A320neo", FeelFamily.AirbusNarrow, true),
        E(b => Has(b, "A346 PRO", "AEROSOFT A346", "AEROSOFT A340"), "Aerosoft A340-600 Pro", FeelFamily.AirbusWide),
        E(b => Has(b, "AEROSOFT A330", "A339RR", "AEROSOFT A339"), "Aerosoft A330-900neo", FeelFamily.AirbusWide),
        new((b, _) => Has(b, "A350", "A359", "A35K"), b => Has(b, "A35K", "A350-1000") ? "iniBuilds A350-1000" : "iniBuilds A350-900", FeelFamily.AirbusWide, true),
        E(b => Has(b, "A300-600", "A306", "A300"), "iniBuilds A300-600", FeelFamily.HeavyAirliner),
        E(b => Has(b, "A310"), "iniBuilds A310", FeelFamily.HeavyAirliner),

        // ---- Other airliners ----
        E(b => Has(b, "TRISTAR", "L1011", "L-1011", "L101"), "iniBuilds L-1011 TriStar", FeelFamily.HeavyAirliner, true),
        E(b => Has(b, "MADDOG", "MD-82", "MD82"), "Leonardo MaddogX MD-82", FeelFamily.Airliner, true),
        E(b => Has(b, "TFDI", "MD-11", "MD11"), "TFDi MD-11", FeelFamily.HeavyAirliner),
        E(b => Has(b, "DC-9", "DC91", "DC93"), "SkySimulations DC-9", FeelFamily.Airliner),
        E(b => Has(b, "FSS EMBRAER", "E190F", "E195", "E175", "E170"), "FSS Embraer E-Jet", FeelFamily.Airliner),
        E(b => Has(b, "CRJ"), "Aerosoft CRJ", FeelFamily.Airliner),
        E(b => Has(b, "CONCORDE"), "DC Designs Concorde", FeelFamily.HeavyAirliner),
        E(b => Has(b, "146") && Has(b, "JUST FLIGHT", "JUSTFLIGHT", "BAE", "AVRO"), "Just Flight 146", FeelFamily.Airliner),

        // ---- Bizjets, turboprops, GA ----
        E(b => Has(b, "HJET", "HA420", "HONDAJET"), "FlightFX HondaJet", FeelFamily.Regional, false),
        E(b => Has(b, "C680"), "Skyward Citation Latitude", FeelFamily.Regional, false),
        E(b => Has(b, "F406"), "iniBuilds F406 Caravan II", FeelFamily.Regional, false),
        E(b => Has(b, "ISLANDER", "BN2"), "BlackBox Islander", FeelFamily.Regional, false),
        E(b => Has(b, "KODIAK"), "SimWorks Kodiak", FeelFamily.Regional, false),
        E(b => Has(b, "BLACK SQUARE", "BKSQ"), "Black Square", FeelFamily.Regional, false),
        E(b => Has(b, "A2A") && Has(b, "COMANCHE"), "A2A Comanche", FeelFamily.LightGa, false),
        E(b => Has(b, "JUST FLIGHT", "JUSTFLIGHT") && Has(b, "PA28", "PA-28", "WARRIOR", "ARROW", "ARCHER"), "Just Flight PA-28", FeelFamily.LightGa, false),
        E(b => Has(b, "CARENADO"), "Carenado", FeelFamily.LightGa, false),
    };

    /// <summary>The payware aircraft MSFS reports, or null for default and unknown ones.</summary>
    public static PaywareAircraft? Find(AircraftIdentity id)
    {
        var blob = $"{id.Title} {id.AtcModel} {id.AtcType}".ToUpperInvariant();
        if (blob.Trim().Length == 0)
            return null;
        foreach (var e in Entries)
        {
            if (e.Matches(blob, id))
                return new PaywareAircraft(e.Name(blob), e.Family, e.OwnRudder);
        }
        return null;
    }

    /// <summary>Every name HeavyPro knows, for the app and the README.</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "PMDG 777-300ER / 777F / 777-200", "PMDG 737", "iFly 737 MAX 8", "CSS 737 Classic", "BlueBird 757-200",
        "FSS Boeing 727", "Horizon Simulations 787-9", "PMDG DC-6",
        "Fenix A319 / A320 / A321", "FSLabs A321", "iniBuilds A320neo", "iniBuilds A350-900 / -1000",
        "iniBuilds A300-600", "iniBuilds A310", "Aerosoft A330-900neo", "Aerosoft A340-600 Pro",
        "iniBuilds L-1011 TriStar", "Leonardo MaddogX MD-82", "TFDi MD-11", "SkySimulations DC-9",
        "FSS Embraer E-Jet", "Aerosoft CRJ", "DC Designs Concorde", "Just Flight 146",
        "FlightFX HondaJet", "Skyward Citation Latitude", "iniBuilds F406 Caravan II", "BlackBox Islander",
        "SimWorks Kodiak", "Black Square", "A2A Comanche", "Just Flight PA-28", "Carenado"
    };

    private static string Pmdg777Name(string b) =>
        Has(b, "777F", "77F") ? "PMDG 777F"
        : Has(b, "200LR", "B77L") ? "PMDG 777-200LR"
        : Has(b, "200ER", "B772") ? "PMDG 777-200ER"
        : "PMDG 777-300ER";

    private static string FenixName(string b) =>
        Has(b, "A319") ? "Fenix A319" : Has(b, "A321") ? "Fenix A321" : "Fenix A320";
}
