namespace HeavyFeel.Core.Models;

public sealed class AircraftIdentity
{
    public string Title { get; init; } = string.Empty;
    public string AtcModel { get; init; } = string.Empty;
    public string AtcType { get; init; } = string.Empty;
    public string AtcId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    public bool IsFenixA320
    {
        get
        {
            var blob = $"{Title} {AtcModel} {AtcType}".ToUpperInvariant();
            return blob.Contains("FENIX") && (blob.Contains("A320") || blob.Contains("A32N") || blob.Contains("A319") || blob.Contains("A321"));
        }
    }

    public bool IsPmdg777
    {
        get
        {
            var blob = $"{Title} {AtcModel} {AtcType}".ToUpperInvariant();
            var seven = blob.Contains("777") || blob.Contains("77W") || blob.Contains("77L") || blob.Contains("77F");
            return seven && (blob.Contains("PMDG") || blob.Contains("777-200") || blob.Contains("777-300") || blob.Contains("777F"));
        }
    }

    /// <summary>
    /// PMDG and iniBuilds run their own control model, which only the Direct route
    /// reaches; on the Masked route the yoke does not move at all.
    /// </summary>
    public bool NeedsDirectRoute
    {
        get
        {
            var blob = $"{Title} {AtcModel} {AtcType}".ToUpperInvariant();
            return IsPmdg777 || blob.Contains("PMDG") || blob.Contains("INIBUILDS")
                || blob.Contains("A350") || blob.Contains("A359") || blob.Contains("A35K");
        }
    }

    /// <summary>
    /// The iniBuilds A350 claims the stick events in its own masking group at the same
    /// SimConnect priority as HeavyPro's (10,000,000, the highest maskable one).
    /// </summary>
    public bool IsA350
    {
        get
        {
            var blob = $"{Title} {AtcModel} {AtcType}".ToUpperInvariant();
            return blob.Contains("A350") || blob.Contains("A359") || blob.Contains("A35K");
        }
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;
            if (!string.IsNullOrWhiteSpace(AtcModel))
                return AtcModel;
            return "Unknown aircraft";
        }
    }

    public string SuggestedProfile
    {
        get
        {
            if (IsFenixA320)
                return "Fenix A320";
            if (IsPmdg777)
                return "PMDG 777";
            var cls = AircraftCatalog.Classify(this, 0);
            if (cls != AircraftClass.Unknown)
                return AircraftCatalog.DisplayName(cls);
            if (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(AtcModel))
                return "Auto Detect";
            return "Generic";
        }
    }
}
