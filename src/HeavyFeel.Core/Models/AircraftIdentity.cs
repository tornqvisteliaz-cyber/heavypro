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
