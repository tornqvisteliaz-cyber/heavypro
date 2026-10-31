using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HeavyFeel.Core.Services;

/// <summary>One flight_model.cfg value scaled by a factor, found by section and key.</summary>
public sealed record CfgScale(string Section, string Key, double Factor);

/// <summary>A PMDG 777 flight model HeavyPro can override, as found in the Community folder.</summary>
public sealed record FlightModelSource(string SourcePackage, string VirtualPath, string OverridePackage, string Aircraft);

/// <summary>
/// Real 777 feel for the PMDG 777, built on the user's PC: HeavyPro copies the
/// 777's own flight_model.cfg, scales a few lines (more pitch and roll inertia and damping,
/// so the aircraft feels big, settled and planted) and puts the copy in a package of its own
/// that MSFS loads after PMDG's. PMDG's files are never edited and never shipped with HeavyPro;
/// removing the package gives PMDG's flight model back.
/// </summary>
public static class FlightModelOverrides
{
    /// <summary>Bump when <see cref="Changes"/> change, so existing overrides are rebuilt.</summary>
    public const string ChangesVersion = "2";

    public const string StampFile = "heavypro-override.json";
    private const string OverridePrefix = "rotaidem-heavypro-fm-";

    public static readonly IReadOnlyList<CfgScale> Changes = new[]
    {
        new CfgScale("WEIGHT_AND_BALANCE", "empty_weight_pitch_moi", 1.15),
        new CfgScale("WEIGHT_AND_BALANCE", "empty_weight_roll_moi", 1.15),
        new CfgScale("AERODYNAMICS", "pitch_moment_pitch_damping", 1.15),
        new CfgScale("AERODYNAMICS", "roll_moment_roll_damping", 1.15),
    };

    // Latin-1 maps every byte to one char and back, so whatever PMDG wrote in comments survives.
    private static readonly Encoding Bytes = Encoding.GetEncoding(28591);

    private static readonly Regex Section = new(@"^\s*\[(?<name>[^\]]+)\]");
    private static readonly Regex Value = new(@"^(?<lead>\s*(?<key>[A-Za-z0-9_]+)\s*=\s*)(?<num>[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?)(?<rest>.*)$", RegexOptions.Singleline);

    /// <summary>
    /// The file with each change applied, every other byte kept (comments, spacing, line
    /// endings, key case). Null when a key is missing or appears more than once in its
    /// section: then the file is not the one these changes were made for.
    /// </summary>
    public static string? Patch(string cfg, IReadOnlyList<CfgScale> changes)
    {
        var lines = Regex.Split(cfg, @"(?<=\n)");
        var hits = new int[changes.Count];
        var section = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var header = Section.Match(lines[i]);
            if (header.Success)
            {
                section = header.Groups["name"].Value.Trim();
                continue;
            }
            var m = Value.Match(lines[i]);
            if (!m.Success)
                continue;
            for (var c = 0; c < changes.Count; c++)
            {
                if (!string.Equals(changes[c].Section, section, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(changes[c].Key, m.Groups["key"].Value, StringComparison.OrdinalIgnoreCase))
                    continue;
                var text = m.Groups["num"].Value;
                var dot = text.IndexOf('.');
                var decimals = dot < 0 || text.IndexOfAny(new[] { 'e', 'E' }) >= 0 ? 1 : Math.Max(1, text.Length - dot - 1);
                var scaled = double.Parse(text, CultureInfo.InvariantCulture) * changes[c].Factor;
                lines[i] = m.Groups["lead"].Value + scaled.ToString("F" + decimals, CultureInfo.InvariantCulture) + m.Groups["rest"].Value;
                hits[c]++;
            }
        }
        return hits.All(h => h == 1) ? string.Concat(lines) : null;
    }

    /// <summary>Every PMDG 777 flight model in the Community folder (300ER, 777F, 200LR...).</summary>
    public static IReadOnlyList<FlightModelSource> Find(string community)
    {
        var result = new List<FlightModelSource>();
        foreach (var package in Directory.GetDirectories(community, "pmdg-aircraft-77*"))
        {
            var name = Path.GetFileName(package);
            if (name.Contains("_CVT_"))
                continue;
            var airplanes = Path.Combine(package, "SimObjects", "Airplanes");
            if (!Directory.Exists(airplanes))
                continue;
            foreach (var aircraft in Directory.GetDirectories(airplanes))
            {
                var presets = Path.Combine(aircraft, "presets", "pmdg");
                if (!Directory.Exists(presets))
                    continue;
                foreach (var preset in Directory.GetDirectories(presets))
                {
                    var cfg = Path.Combine(preset, "config", "flight_model.cfg");
                    if (!File.Exists(cfg))
                        continue;
                    var virtualPath = cfg.Substring(package.Length + 1).Replace('\\', '/');
                    var suffix = name.Substring("pmdg-aircraft-".Length).ToLowerInvariant();
                    result.Add(new FlightModelSource(name, virtualPath, OverridePrefix + suffix, Path.GetFileName(preset)));
                }
            }
        }
        // One override package per PMDG package: a package with several presets keeps the first.
        return result.GroupBy(s => s.OverridePackage).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// Brings the override packages in line with the switch: built (or rebuilt after a PMDG
    /// update or a HeavyPro change) when on, removed when off or when the 777 is gone.
    /// Never touches a package HeavyPro did not write, and nothing while MSFS runs.
    /// Returns one line per aircraft for the log and the status text.
    /// </summary>
    public static IReadOnlyList<string> Sync(string community, bool enabled, bool simRunning)
    {
        var messages = new List<string>();
        var sources = Find(community);
        var wanted = new HashSet<string>(sources.Select(s => s.OverridePackage), StringComparer.OrdinalIgnoreCase);

        // Ours but no longer wanted: switched off, or the 777 it was built from is gone.
        foreach (var dir in Directory.GetDirectories(community, OverridePrefix + "*"))
        {
            if (!IsOurs(dir) || (enabled && wanted.Contains(Path.GetFileName(dir))))
                continue;
            if (simRunning)
            {
                messages.Add(Path.GetFileName(dir) + ": close MSFS to remove it.");
                continue;
            }
            Directory.Delete(dir, recursive: true);
            messages.Add("Removed " + Path.GetFileName(dir) + "; PMDG's own flight model is back.");
        }
        if (!enabled)
            return messages;
        if (sources.Count == 0)
        {
            messages.Add("No PMDG 777 found in the Community folder.");
            return messages;
        }

        foreach (var source in sources)
        {
            var target = Path.Combine(community, source.OverridePackage);
            if (Directory.Exists(target) && !IsOurs(target))
            {
                messages.Add(source.Aircraft + ": " + source.OverridePackage + " is not HeavyPro's; left alone.");
                continue;
            }
            var bytes = File.ReadAllBytes(Path.Combine(community, source.SourcePackage, source.VirtualPath.Replace('/', Path.DirectorySeparatorChar)));
            var stamp = Stamp(bytes);
            var stampPath = Path.Combine(target, StampFile);
            if (File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
            {
                messages.Add(source.Aircraft + ": Real 777 feel on.");
                continue;
            }
            if (simRunning)
            {
                messages.Add(source.Aircraft + ": Real 777 feel is built when MSFS is closed.");
                continue;
            }
            var patched = Patch(Bytes.GetString(bytes), Changes);
            if (patched == null)
            {
                if (Directory.Exists(target))
                    Directory.Delete(target, recursive: true);
                messages.Add(source.Aircraft + ": this PMDG version's flight model is not one HeavyPro knows; left as PMDG made it.");
                continue;
            }
            var building = target + ".building";
            if (Directory.Exists(building))
                Directory.Delete(building, recursive: true);
            WritePackage(building, source, Bytes.GetBytes(patched), PmdgVersion(Path.Combine(community, source.SourcePackage)));
            File.WriteAllText(Path.Combine(building, StampFile), stamp);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            Directory.Move(building, target);
            messages.Add(source.Aircraft + ": Real 777 feel built.");
        }
        return messages;
    }

    /// <summary>What an override was built from: the changes and PMDG's file, by hash.</summary>
    public static string Stamp(byte[] source)
    {
        using var sha = SHA256.Create();
        var hash = string.Concat(sha.ComputeHash(source).Select(b => b.ToString("x2")));
        return "{ \"changes\": \"" + ChangesVersion + "\", \"source\": \"" + hash + "\" }";
    }

    /// <summary>Written by HeavyPro: a real folder (not a RotaHub link) with Rotaidem's manifest and a stamp.</summary>
    private static bool IsOurs(string dir)
    {
        if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint))
            return false;
        var manifest = Path.Combine(dir, "manifest.json");
        return File.Exists(Path.Combine(dir, StampFile)) && File.Exists(manifest)
            && File.ReadAllText(manifest).Contains("\"creator\": \"Rotaidem\"");
    }

    private static string PmdgVersion(string package)
    {
        var manifest = Path.Combine(package, "manifest.json");
        var m = File.Exists(manifest) ? Regex.Match(File.ReadAllText(manifest), "\"package_version\"\\s*:\\s*\"([^\"]+)\"") : Match.Empty;
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// An MSFS package holding the one file. It depends on PMDG's package and shares its
    /// load-order hint, so MSFS mounts it after PMDG's and this copy of the file wins.
    /// </summary>
    public static void WritePackage(string dir, FlightModelSource source, byte[] cfg, string pmdgVersion)
    {
        var file = Path.Combine(dir, source.VirtualPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, cfg);
        var date = File.GetLastWriteTimeUtc(file).ToFileTimeUtc();
        File.WriteAllText(Path.Combine(dir, "layout.json"),
            "{\r\n  \"content\": [\r\n    {\r\n      \"path\": \"" + source.VirtualPath + "\",\r\n      \"size\": " + cfg.Length
            + ",\r\n      \"date\": " + date + "\r\n    }\r\n  ]\r\n}");
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            "{\r\n  \"dependencies\": [\r\n    {\r\n      \"name\": \"" + source.SourcePackage + "\",\r\n      \"package_version\": \"" + (pmdgVersion.Length > 0 ? pmdgVersion : "0.1.0") + "\"\r\n    }\r\n  ],\r\n"
            + "  \"content_type\": \"MISC\",\r\n"
            + "  \"title\": \"HeavyPro Real 777 feel for " + source.Aircraft + "\",\r\n"
            + "  \"manufacturer\": \"Rotaidem\",\r\n"
            + "  \"creator\": \"Rotaidem\",\r\n"
            + "  \"package_version\": \"1.0." + ChangesVersion + "\",\r\n"
            + "  \"minimum_game_version\": \"1.9.18\",\r\n"
            + "  \"minimum_compatibility_version\": \"9.18.0.250\",\r\n"
            + "  \"export_type\": \"Community\",\r\n"
            + "  \"builder\": \"Microsoft Flight Simulator 2024\",\r\n"
            + "  \"package_order_hint\": \"CUSTOM_SIMOBJECTS\",\r\n"
            + "  \"release_notes\": {\r\n    \"neutral\": {\r\n      \"LastUpdate\": \"\",\r\n      \"OlderHistory\": \"\"\r\n    }\r\n  },\r\n"
            + "  \"total_package_size\": \"" + cfg.Length + "\"\r\n}");
    }
}
