using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace HeavyPro.Core.Services;

/// <summary>The weights and details from a SimBrief flight plan (OFP), weights in the OFP's own unit.</summary>
public sealed record SimBriefOfp(
    string Origin, string Destination, string Aircraft, string FlightNumber, string Units,
    double OperatingEmptyWeight, double Payload, int Passengers, double Cargo,
    double ZeroFuelWeight, double TakeoffWeight, double LandingWeight,
    double BlockFuel, double ReserveFuel, double TaxiFuel)
{
    /// <summary>ICAO type designator, e.g. "B77W".</summary>
    public string AircraftType { get; init; } = "";
    public string Registration { get; init; } = "";
    /// <summary>Initial cruise altitude in feet; 0 when the plan has none.</summary>
    public int CruiseAltitudeFeet { get; init; }
    /// <summary>The ATC route string between the airports.</summary>
    public string RouteText { get; init; } = "";
    /// <summary>Planned airborne time; zero when the plan has none.</summary>
    public TimeSpan Duration { get; init; }
    public double MaxTakeoffWeight { get; init; }
    public DateTime? Generated { get; init; }
    /// <summary>SimBrief's own MSFS flight plan file for this OFP, when it offers one.</summary>
    public string MsfsPlanUrl { get; init; } = "";

    public string Route => Origin.Length > 0 && Destination.Length > 0 ? $"{Origin} → {Destination}" : "";

    /// <summary>Planned takeoff weight in pounds, whatever the OFP's unit.</summary>
    public double TakeoffWeightPounds => ToPounds(TakeoffWeight);
    public double MaxTakeoffWeightPounds => ToPounds(MaxTakeoffWeight);

    private double ToPounds(double w) => Units.StartsWith("kg", StringComparison.OrdinalIgnoreCase) ? w * 2.20462 : w;
}

/// <summary>The result of asking SimBrief for the latest plan: an OFP, or a message to show.</summary>
public sealed record SimBriefResult(SimBriefOfp? Ofp, string Message)
{
    public bool Ok => Ofp != null;
}

/// <summary>
/// Reads the latest SimBrief flight plan for a pilot from SimBrief's documented fetcher
/// (https://www.simbrief.com/api/xml.fetcher.php, userid= for a pilot id, username= for a
/// username, json=1 for JSON). It sends only the id; the last good answer is kept on disk
/// so the plan is still there after a restart or without internet.
/// </summary>
public static class SimBrief
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HeavyPro");
        return http;
    }

    public static string FetchUrl(string usernameOrId)
    {
        var id = (usernameOrId ?? "").Trim();
        var field = IsPilotId(id) ? "userid" : "username";
        return $"https://www.simbrief.com/api/xml.fetcher.php?{field}={Uri.EscapeDataString(id)}&json=1";
    }

    /// <summary>A numeric SimBrief pilot id (Account Settings on simbrief.com), as opposed to a username.</summary>
    public static bool IsPilotId(string id) => id.Length > 0 && id.All(char.IsDigit);

    /// <summary>Why an entered id can't be sent, or null when it looks usable.</summary>
    public static string? Validate(string? usernameOrId)
    {
        var id = (usernameOrId ?? "").Trim();
        if (id.Length == 0)
            return "Enter your SimBrief pilot id (Account Settings on simbrief.com) or username.";
        if (id.Length > 64 || id.Any(char.IsWhiteSpace))
            return "That doesn't look like a SimBrief pilot id or username.";
        return null;
    }

    public static async Task<SimBriefResult> FetchAsync(string usernameOrId)
    {
        var (text, error) = await FetchTextAsync(usernameOrId).ConfigureAwait(false);
        return error ?? Parse(text!);
    }

    /// <summary>Fetches the latest plan and, when it is good, keeps it in the cache.</summary>
    public static async Task<SimBriefResult> FetchAndCacheAsync(string usernameOrId, string dataFolder)
    {
        var (text, error) = await FetchTextAsync(usernameOrId).ConfigureAwait(false);
        if (error != null)
            return error;
        var result = Parse(text!);
        if (result.Ok)
            SaveCache(dataFolder, text!);
        return result;
    }

    private static async Task<(string? Text, SimBriefResult? Error)> FetchTextAsync(string usernameOrId)
    {
        if (Validate(usernameOrId) is { } invalid)
            return (null, new SimBriefResult(null, invalid));
        try
        {
            // SimBrief answers an unknown id with HTTP 400 and a JSON error, so read the
            // body whatever the status instead of throwing on it.
            using var response = await Http.GetAsync(FetchUrl(usernameOrId)).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && !text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                return (null, new SimBriefResult(null, $"SimBrief answered {(int)response.StatusCode} {response.ReasonPhrase}. Try again later."));
            return (text, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, new SimBriefResult(null, "Could not reach SimBrief: " + ex.Message));
        }
        catch (TaskCanceledException)
        {
            return (null, new SimBriefResult(null, "SimBrief did not answer in time. Try again."));
        }
    }

    /// <summary>Downloads SimBrief's MSFS flight plan file (.pln) for an OFP.</summary>
    public static async Task<string?> DownloadMsfsPlanAsync(SimBriefOfp ofp)
    {
        if (ofp.MsfsPlanUrl.Length == 0)
            return null;
        try
        {
            using var response = await Http.GetAsync(ofp.MsfsPlanUrl).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Parses an OFP, JSON (json=1) or XML (the fetcher's default), so a saved OFP file can be imported too.</summary>
    public static SimBriefResult Parse(string text)
    {
        var trimmed = (text ?? "").TrimStart();
        return trimmed.StartsWith("<", StringComparison.Ordinal) ? ParseXml(trimmed) : ParseJson(trimmed);
    }

    private static SimBriefResult ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FromFields(new JsonFields(doc.RootElement));
        }
        catch (JsonException)
        {
            return new SimBriefResult(null, "SimBrief sent something HeavyPro could not read.");
        }
    }

    private static SimBriefResult ParseXml(string xml)
    {
        try
        {
            return FromFields(new XmlFields(XDocument.Parse(xml).Root));
        }
        catch (System.Xml.XmlException)
        {
            return new SimBriefResult(null, "That SimBrief file could not be read.");
        }
    }

    // JSON and XML OFPs have the same section/field names, so one reader handles both.
    private interface IFields
    {
        string Str(string section, string field);
        string Str(string section, string child, string field);
    }

    private sealed class JsonFields : IFields
    {
        private readonly JsonElement _root;
        public JsonFields(JsonElement root) => _root = root;

        public string Str(string section, string field) => Read(Get(_root, section), field);
        public string Str(string section, string child, string field) => Read(Get(Get(_root, section), child), field);

        private static JsonElement Get(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) ? v : default;

        private static string Read(JsonElement parent, string field)
        {
            var v = Get(parent, field);
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => ""
            };
        }
    }

    private sealed class XmlFields : IFields
    {
        private readonly XElement? _root;
        public XmlFields(XElement? root) => _root = root;
        public string Str(string section, string field) => _root?.Element(section)?.Element(field)?.Value.Trim() ?? "";
        public string Str(string section, string child, string field) =>
            _root?.Element(section)?.Element(child)?.Element(field)?.Value.Trim() ?? "";
    }

    private static SimBriefResult FromFields(IFields f)
    {
        // SimBrief reports a bad username/id as fetch.status "Error: ...".
        var status = f.Str("fetch", "status");
        if (status.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            return new SimBriefResult(null, status.Replace("Error:", "SimBrief:").Trim());
        var origin = f.Str("origin", "icao_code");
        var hasWeights = f.Str("weights", "est_tow").Length > 0 || f.Str("weights", "est_zfw").Length > 0;
        if (!hasWeights && origin.Length == 0)
            return new SimBriefResult(null, "No SimBrief plan found. Generate one on simbrief.com, then refresh.");

        var units = f.Str("params", "units");
        var name = f.Str("aircraft", "name");
        var type = f.Str("aircraft", "icaocode");
        var directory = f.Str("fms_downloads", "directory");
        var mfs = f.Str("fms_downloads", "mfs", "link");
        var generated = Num(f.Str("params", "time_generated"));
        var ofp = new SimBriefOfp(
            origin, f.Str("destination", "icao_code"),
            name.Length > 0 ? name : type,
            (f.Str("general", "icao_airline") + f.Str("general", "flight_number")).Trim(),
            units.Length > 0 ? units : "lbs",
            Num(f.Str("weights", "oew")), Num(f.Str("weights", "payload")), (int)Num(f.Str("weights", "pax_count")), Num(f.Str("weights", "cargo")),
            Num(f.Str("weights", "est_zfw")), Num(f.Str("weights", "est_tow")), Num(f.Str("weights", "est_ldw")),
            Num(f.Str("fuel", "plan_ramp")), Num(f.Str("fuel", "reserve")), Num(f.Str("fuel", "taxi")))
        {
            AircraftType = type,
            Registration = f.Str("aircraft", "reg"),
            CruiseAltitudeFeet = (int)Num(f.Str("general", "initial_altitude")),
            RouteText = f.Str("general", "route"),
            Duration = TimeSpan.FromSeconds(Num(f.Str("times", "est_time_enroute"))),
            MaxTakeoffWeight = Num(f.Str("weights", "max_tow")),
            Generated = generated > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)generated).UtcDateTime : null,
            MsfsPlanUrl = directory.Length > 0 && mfs.Length > 0 ? directory + mfs : ""
        };
        return new SimBriefResult(ofp, ofp.Route.Length > 0 ? ofp.Route : "SimBrief plan loaded.");
    }

    // SimBrief weights/fuel come as strings in the OFP's unit; a few can be numbers.
    private static double Num(string text) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : 0;

    // ---------------- cache ----------------

    /// <summary>Where the last good OFP is kept (the HeavyPro data folder).</summary>
    public static string CachePath(string dataFolder) => Path.Combine(dataFolder, "simbrief-latest.json");

    /// <summary>Keeps the raw OFP text of a successful fetch.</summary>
    public static void SaveCache(string dataFolder, string ofpText)
    {
        try
        {
            Directory.CreateDirectory(dataFolder);
            File.WriteAllText(CachePath(dataFolder), ofpText);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static SimBriefResult? LoadCache(string dataFolder)
    {
        try
        {
            var path = CachePath(dataFolder);
            if (!File.Exists(path))
                return null;
            var result = Parse(File.ReadAllText(path));
            return result.Ok ? result : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static void ClearCache(string dataFolder)
    {
        try { File.Delete(CachePath(dataFolder)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
