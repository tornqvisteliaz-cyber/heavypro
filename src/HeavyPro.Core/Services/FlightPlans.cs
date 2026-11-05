using System.Globalization;
using System.Xml.Linq;

namespace HeavyPro.Core.Services;

/// <summary>A flight plan HeavyPro has read: where from, where to, how high, and the route.</summary>
public sealed record FlightPlanInfo
{
    /// <summary>"MSFS flight plan", "Airbus FLP" or "SimBrief OFP".</summary>
    public string Format { get; init; } = "";
    /// <summary>File name the plan came from, for display.</summary>
    public string SourceName { get; init; } = "";
    public string Departure { get; init; } = "";
    public string Arrival { get; init; } = "";
    /// <summary>Cruise altitude in feet; 0 when the file has none.</summary>
    public int CruiseAltitudeFeet { get; init; }
    public IReadOnlyList<string> Waypoints { get; init; } = Array.Empty<string>();
    /// <summary>The route as text: the plan's own route string, or its waypoints.</summary>
    public string RouteText { get; init; } = "";
    /// <summary>Weights and aircraft when the plan is a SimBrief OFP; null otherwise.</summary>
    public SimBriefOfp? Ofp { get; init; }

    public string Route => Departure.Length > 0 && Arrival.Length > 0 ? $"{Departure} → {Arrival}" : Departure + Arrival;
}

public sealed record FlightPlanResult(FlightPlanInfo? Plan, string Message)
{
    public bool Ok => Plan != null;
}

/// <summary>
/// Reads flight plan files: MSFS .pln (2020 and 2024 write the same XML), the Airbus .flp
/// route format ([CoRte], used by Aerosoft, Fenix and iniBuilds), and SimBrief OFP files
/// (.json from json=1, or .xml). A file it can't read gives a message, never an exception.
/// </summary>
public static class FlightPlans
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>For the file picker.</summary>
    public const string DialogFilter =
        "Flight plans (*.pln;*.flp;*.json;*.xml)|*.pln;*.flp;*.json;*.xml|MSFS flight plan (*.pln)|*.pln|Airbus route (*.flp)|*.flp|SimBrief OFP (*.json;*.xml)|*.json;*.xml|All files (*.*)|*.*";

    public static FlightPlanResult Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new FlightPlanResult(null, "That file no longer exists.");
            if (new FileInfo(path).Length > MaxFileBytes)
                return new FlightPlanResult(null, "That file is too big to be a flight plan.");
            return Parse(File.ReadAllText(path), Path.GetFileName(path));
        }
        catch (IOException ex)
        {
            return new FlightPlanResult(null, "Could not read the file: " + ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return new FlightPlanResult(null, "HeavyPro is not allowed to read that file.");
        }
    }

    /// <summary>Reads a plan from its text. The file name picks the format; the content decides when it doesn't.</summary>
    public static FlightPlanResult Parse(string text, string fileName)
    {
        text ??= "";
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (trimmed.Length == 0)
            return new FlightPlanResult(null, "The file is empty.");

        FlightPlanResult result;
        if (ext == ".flp" || trimmed.StartsWith("[CoRte]", StringComparison.OrdinalIgnoreCase))
            result = ParseFlp(trimmed);
        else if (trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.IndexOf("FlightPlan.FlightPlan", StringComparison.Ordinal) >= 0)
            result = ParsePln(trimmed);
        else if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("<", StringComparison.Ordinal))
            result = ParseOfp(trimmed);
        else
            return new FlightPlanResult(null, "HeavyPro does not recognise that file as a flight plan (.pln, .flp or a SimBrief OFP).");

        return result.Plan is { } plan
            ? new FlightPlanResult(plan with { SourceName = fileName ?? "" }, result.Message)
            : result;
    }

    private static FlightPlanResult ParsePln(string xml)
    {
        XElement? plan;
        try
        {
            plan = XDocument.Parse(xml).Descendants("FlightPlan.FlightPlan").FirstOrDefault();
        }
        catch (System.Xml.XmlException)
        {
            return new FlightPlanResult(null, "The .pln file is not valid XML.");
        }
        if (plan == null)
            return new FlightPlanResult(null, "The .pln file has no flight plan in it.");

        var dep = Value(plan, "DepartureID");
        var arr = Value(plan, "DestinationID");
        var waypoints = plan.Elements("ATCWaypoint")
            .Select(w => (string?)w.Attribute("id") ?? "")
            .Where(id => id.Length > 0)
            .ToList();
        // The first and last waypoints are the airports themselves.
        var enroute = waypoints.Where(w => !w.Equals(dep, StringComparison.OrdinalIgnoreCase) && !w.Equals(arr, StringComparison.OrdinalIgnoreCase)).ToList();
        if (dep.Length == 0 && arr.Length == 0 && waypoints.Count == 0)
            return new FlightPlanResult(null, "The .pln file has no airports or waypoints.");

        return Ok(new FlightPlanInfo
        {
            Format = "MSFS flight plan",
            Departure = dep.ToUpperInvariant(),
            Arrival = arr.ToUpperInvariant(),
            CruiseAltitudeFeet = (int)Number(Value(plan, "CruisingAlt")),
            Waypoints = enroute,
            RouteText = string.Join(" ", enroute)
        });
    }

    // [CoRte] ArptDep/ArptArr, then numbered DctWptN or AirwayN (+ AirwayNTO) legs.
    private static FlightPlanResult ParseFlp(string text)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sawSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                continue;
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                sawSection |= line.Equals("[CoRte]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq > 0)
                keys[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        if (!sawSection)
            return new FlightPlanResult(null, "The .flp file has no [CoRte] route section.");

        keys.TryGetValue("ArptDep", out var dep);
        keys.TryGetValue("ArptArr", out var arr);
        var legs = new SortedDictionary<int, string>();
        foreach (var pair in keys)
        {
            if (TryIndex(pair.Key, "DctWpt", out var i) && pair.Value.Length > 0)
                legs[i] = pair.Value;
            else if (TryIndex(pair.Key, "Airway", out var j) && pair.Value.Length > 0)
                legs[j] = keys.TryGetValue($"Airway{j}TO", out var to) && to.Length > 0 ? $"{pair.Value} {to}" : pair.Value;
        }
        dep ??= "";
        arr ??= "";
        if (dep.Length == 0 && arr.Length == 0 && legs.Count == 0)
            return new FlightPlanResult(null, "The .flp file has no airports or waypoints.");

        var waypoints = legs.Values.ToList();
        return Ok(new FlightPlanInfo
        {
            Format = "Airbus FLP",
            Departure = dep.ToUpperInvariant(),
            Arrival = arr.ToUpperInvariant(),
            Waypoints = waypoints,
            RouteText = string.Join(" ", waypoints)
        });
    }

    private static FlightPlanResult ParseOfp(string text)
    {
        var ofp = SimBrief.Parse(text);
        if (!ofp.Ok)
            return new FlightPlanResult(null, ofp.Message.StartsWith("SimBrief", StringComparison.Ordinal)
                ? ofp.Message
                : "That file is not a flight plan HeavyPro can read.");
        return Ok(FromOfp(ofp.Ofp!));
    }

    /// <summary>A SimBrief OFP as a flight plan, weights and aircraft included.</summary>
    public static FlightPlanInfo FromOfp(SimBriefOfp ofp) => new()
    {
        Format = "SimBrief OFP",
        Departure = ofp.Origin,
        Arrival = ofp.Destination,
        CruiseAltitudeFeet = ofp.CruiseAltitudeFeet,
        Waypoints = ofp.RouteText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
        RouteText = ofp.RouteText,
        Ofp = ofp
    };

    private static FlightPlanResult Ok(FlightPlanInfo plan) =>
        new(plan, plan.Route.Length > 0 ? $"{plan.Format}: {plan.Route}" : plan.Format + " loaded.");

    private static bool TryIndex(string key, string prefix, out int index)
    {
        index = 0;
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var digits = key.Substring(prefix.Length);
        return digits.Length > 0 && digits.All(char.IsDigit) && int.TryParse(digits, out index);
    }

    private static string Value(XElement parent, string name) => parent.Element(name)?.Value.Trim() ?? "";

    private static double Number(string text) =>
        double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    // ---------------- the imported plan, kept in the data folder ----------------

    /// <summary>Copies an imported plan into the data folder so it survives the original moving. Returns the copy's path.</summary>
    public static string Keep(string dataFolder, string sourcePath)
    {
        var folder = Path.Combine(dataFolder, "flightplan");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "current.*"))
            File.Delete(old);
        var copy = Path.Combine(folder, "current" + Path.GetExtension(sourcePath).ToLowerInvariant());
        File.Copy(sourcePath, copy, overwrite: true);
        return copy;
    }

    /// <summary>Keeps a plan that came as text (SimBrief's MSFS plan download).</summary>
    public static string KeepText(string dataFolder, string text, string extension)
    {
        var folder = Path.Combine(dataFolder, "flightplan");
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "current.*"))
            File.Delete(old);
        var copy = Path.Combine(folder, "current" + extension);
        File.WriteAllText(copy, text);
        return copy;
    }

    public static void Forget(string dataFolder)
    {
        var folder = Path.Combine(dataFolder, "flightplan");
        if (!Directory.Exists(folder))
            return;
        foreach (var old in Directory.GetFiles(folder, "current.*"))
            File.Delete(old);
    }
}
