using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace HeavyPro.Core.Services;

/// <summary>The weights and a few details from a SimBrief flight plan (OFP), in the OFP's own unit.</summary>
public sealed record SimBriefOfp(
    string Origin, string Destination, string Aircraft, string FlightNumber, string Units,
    double OperatingEmptyWeight, double Payload, int Passengers, double Cargo,
    double ZeroFuelWeight, double TakeoffWeight, double LandingWeight,
    double BlockFuel, double ReserveFuel, double TaxiFuel)
{
    public string Route => Origin.Length > 0 && Destination.Length > 0 ? $"{Origin} → {Destination}" : "";
}

/// <summary>The result of asking SimBrief for the latest plan: an OFP, or a message to show.</summary>
public sealed record SimBriefResult(SimBriefOfp? Ofp, string Message)
{
    public bool Ok => Ofp != null;
}

/// <summary>
/// Reads the latest SimBrief flight plan for a pilot from SimBrief's public fetcher
/// (simbrief.com/api/xml.fetcher.php), so HeavyPro can show the planned weights. It needs
/// the pilot's SimBrief username or numeric pilot id; it sends nothing else and stores nothing.
/// </summary>
public static class SimBrief
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string FetchUrl(string usernameOrId)
    {
        var id = (usernameOrId ?? "").Trim();
        var field = id.Length > 0 && id.All(char.IsDigit) ? "userid" : "username";
        return $"https://www.simbrief.com/api/xml.fetcher.php?{field}={Uri.EscapeDataString(id)}&json=1";
    }

    public static async Task<SimBriefResult> FetchAsync(string usernameOrId)
    {
        if (string.IsNullOrWhiteSpace(usernameOrId))
            return new SimBriefResult(null, "Enter your SimBrief username (Account Settings on simbrief.com) or pilot id.");
        try
        {
            var json = await Http.GetStringAsync(FetchUrl(usernameOrId)).ConfigureAwait(false);
            return Parse(json);
        }
        catch (HttpRequestException ex)
        {
            return new SimBriefResult(null, "Could not reach SimBrief: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            return new SimBriefResult(null, "SimBrief did not answer in time. Try again.");
        }
    }

    /// <summary>Parses the SimBrief fetcher JSON. Kept separate from the network call so it can be tested.</summary>
    public static SimBriefResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // SimBrief reports a bad username/id as {"fetch":{"status":"Error: ..."}}.
            if (root.TryGetProperty("fetch", out var fetch) && fetch.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String && status.GetString() is { } st && st.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                return new SimBriefResult(null, st.Replace("Error:", "SimBrief:").Trim());
            if (!root.TryGetProperty("weights", out var w))
                return new SimBriefResult(null, "No SimBrief plan found. Generate one on simbrief.com, then refresh.");

            var units = Str(root, "params", "units");
            var ofp = new SimBriefOfp(
                Str(root, "origin", "icao_code"), Str(root, "destination", "icao_code"),
                Str(root, "aircraft", "name") is { Length: > 0 } name ? name : Str(root, "aircraft", "icaocode"),
                (Str(root, "general", "icao_airline") + Str(root, "general", "flight_number")).Trim(),
                units.Length > 0 ? units : "lbs",
                Num(w, "oew"), Num(w, "payload"), (int)Num(w, "pax_count"), Num(w, "cargo"),
                Num(w, "est_zfw"), Num(w, "est_tow"), Num(w, "est_ldw"),
                Num(root.TryGetProperty("fuel", out var f) ? f : default, "plan_ramp"),
                Num(root.TryGetProperty("fuel", out var f2) ? f2 : default, "reserve"),
                Num(root.TryGetProperty("fuel", out var f3) ? f3 : default, "taxi"));
            return new SimBriefResult(ofp, ofp.Route.Length > 0 ? ofp.Route : "SimBrief plan loaded.");
        }
        catch (JsonException)
        {
            return new SimBriefResult(null, "SimBrief sent something HeavyPro could not read.");
        }
    }

    private static string Str(JsonElement parent, string obj, string field)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(obj, out var o)
            && o.ValueKind == JsonValueKind.Object && o.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    // SimBrief weights/fuel come as strings in the OFP's unit; a few can be numbers.
    private static double Num(JsonElement parent, string field)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(field, out var v))
            return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n))
            return n;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s))
            return s;
        return 0;
    }
}
