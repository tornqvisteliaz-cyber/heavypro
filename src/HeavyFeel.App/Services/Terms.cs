using System.IO;
using System.Text.RegularExpressions;

namespace HeavyFeel.App.Services;

/// <summary>
/// The HeavyPro Terms of Service built into the app (TERMS.md at the repo root) and
/// whether this Windows user accepted the current version.
/// </summary>
public static class Terms
{
    public static string Text { get; } = Load();

    /// <summary>The "Last updated" date in TERMS.md: a new date asks everyone again.</summary>
    public static string Version { get; } = Regex.Match(Text, @"Last updated:\s*(.+)").Groups[1].Value.Trim();

    private static string AcceptedFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rotaidem", "heavypro", "terms-accepted.txt");

    public static bool Accepted
    {
        get
        {
            try
            {
                return File.Exists(AcceptedFile) && File.ReadAllText(AcceptedFile).Trim() == Version;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    public static void Accept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AcceptedFile)!);
        File.WriteAllText(AcceptedFile, Version);
    }

    private static string Load()
    {
        using var stream = typeof(Terms).Assembly.GetManifestResourceStream("HeavyPro.Terms.md");
        if (stream == null)
            return "# HeavyPro Terms of Service\n\nLast updated: unknown\n\nThe terms could not be loaded. See rotaidem.com.";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
