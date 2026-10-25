namespace HeavyFeel.Core.Services;

/// <summary>
/// Reads CHANGELOG.md: one "## x.y.z" heading per release with "- " lines under it.
/// The app shows a release's lines as "What's new"; CI uses the same section as the
/// GitHub release notes.
/// </summary>
public static class Changelog
{
    /// <summary>The lines under "## version" as bullet text, or null if that version has none.</summary>
    public static string? WhatsNew(string? markdown, Version version)
    {
        if (string.IsNullOrEmpty(markdown))
            return null;
        var wanted = Format(version);
        var lines = new List<string>();
        var inSection = false;
        foreach (var raw in markdown!.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection)
                    break;
                var heading = line.Substring(3).Trim();
                var name = heading.Split(' ')[0].TrimStart('v');
                inSection = Version.TryParse(name, out var found) && Format(found) == wanted;
                continue;
            }
            if (!inSection)
                continue;
            if (line.StartsWith("- ", StringComparison.Ordinal))
                lines.Add("• " + line.Substring(2).Replace("**", ""));
            else if (line.Length > 0 && lines.Count > 0)
                lines[lines.Count - 1] += " " + line.Trim().Replace("**", "");
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string Format(Version v) => $"{v.Major}.{Math.Max(0, v.Minor)}.{Math.Max(0, v.Build)}";
}
