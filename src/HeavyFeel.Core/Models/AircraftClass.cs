namespace HeavyFeel.Core.Models;

public enum AircraftClass
{
    Unknown = 0,
    LightGa = 1,
    Regional = 2,
    NarrowBody = 3,
    WideBody = 4,
    HeavyWide = 5
}

public static class AircraftCatalog
{
    public static AircraftClass Classify(AircraftIdentity id, double maxGrossPounds)
    {
        var blob = $"{id.Title} {id.AtcModel} {id.AtcType}".ToUpperInvariant();

        if (ContainsAny(blob, "747", "A380", "AN-225", "AN225", "BELUGA", "A400", "C-5", "C5 GALAXY"))
            return AircraftClass.HeavyWide;

        if (ContainsAny(blob, "777", "787", "A330", "A340", "A350", "A300", "A310", "MD-11", "DC-10", "IL-96", "707"))
            return AircraftClass.WideBody;

        if (ContainsAny(blob, "A318", "A319", "A320", "A321", "A32N", "A20N", "A21N",
                "737", "738", "739", "MAX", "717", "MD-80", "MD-90",
                "E170", "E175", "E190", "E195", "CRJ", "A220", "CS1", "CS3"))
            return AircraftClass.NarrowBody;

        if (ContainsAny(blob,
                "KING AIR", "350I", "B200", "B350",
                "CARAVAN", "C208", "208 B", "208B", "SKYCOURIER", "C408", "TITAN", "C404",
                "TBM", "PC-12", "PC12", "CJ4", "CITATION", "LONGITUDE", "SF50", "VISION",
                "TWIN OTTER", "DHC-6", "DHC6", "BEAVER", "DHC-2", "DHC2",
                "ATR", "DASH", "Q400", "Q300", "SEASTAR", "CL-415", "CL415",
                "KODIAK", "PILATUS"))
            return AircraftClass.Regional;

        if (IsLightGa(blob))
            return AircraftClass.LightGa;

        if (maxGrossPounds > 500_000) return AircraftClass.HeavyWide;
        if (maxGrossPounds > 250_000) return AircraftClass.WideBody;
        if (maxGrossPounds > 80_000) return AircraftClass.NarrowBody;
        if (maxGrossPounds > 12_000) return AircraftClass.Regional;
        if (maxGrossPounds > 500) return AircraftClass.LightGa;
        return AircraftClass.Unknown;
    }

    public static bool IsLightGa(string blob)
    {
        return ContainsAny(blob,
            "C152", "C172", "C182", "C195", "C207", "CESSNA 152", "CESSNA 172", "CESSNA 182",
            "CESSNA 400", "CORVALIS", "SKYHAWK", "AEROBAT", "AGTRUCK", "C188",
            "PIPER", "PA-28", "PA28", "PA-24", "WARRIOR", "ARCHER", "ARROW", "COMANCHE", "CUB",
            "BONANZA", "G36", "V35", "BARON", "G58", "STAGGERWING", "TWIN BEECH",
            "DA40", "DA42", "DA62", "DV20", "DIAMOND",
            "SR20", "SR22", "CIRRUS",
            "PITTS", "S1S", "S2S",
            "XCUB", "NXCUB", "NX CUB", "X CUB", "SAVAGE", "NORDEN",
            "CAP 10", "CAP10", "DR400", "JENNY", "JN-4",
            "OPTICA", "HAWK ARROW", "AT-802", "AT802", "AIR TRACTOR",
            "AN-2", "AN2",
            "DG-1001", "LS8", "GLIDER", "ASK21",
            "EXTRA", "EA-300");
    }

    public static string DisplayName(AircraftClass c) => c switch
    {
        AircraftClass.LightGa => "Light GA",
        AircraftClass.Regional => "Turboprop / light twin",
        AircraftClass.NarrowBody => "Narrow-body",
        AircraftClass.WideBody => "Wide-body",
        AircraftClass.HeavyWide => "Heavy wide-body",
        _ => "Unknown"
    };

    public static double InertiaScale(AircraftClass c) => c switch
    {
        AircraftClass.LightGa => 0.42,
        AircraftClass.Regional => 0.72,
        AircraftClass.NarrowBody => 1.00,
        AircraftClass.WideBody => 1.22,
        AircraftClass.HeavyWide => 1.40,
        _ => 1.00
    };

    private static bool ContainsAny(string blob, params string[] tokens)
    {
        foreach (var t in tokens)
        {
            if (blob.Contains(t))
                return true;
        }
        return false;
    }
}
