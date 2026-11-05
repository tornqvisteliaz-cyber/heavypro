using System.Net.Http;
using System.Text.Json;

namespace HeavyPro.Core.Services;

/// <summary>What the update check found.</summary>
public sealed record UpdateInfo(bool UpdateAvailable, string LatestVersion, string DownloadUrl);

/// <summary>
/// Asks rotaidem.com for the current HeavyPro version so the app can remind the user when a
/// newer build is out. It only reads a small version file; updating itself is still done
/// through RotaHub. A missing or unreachable endpoint just means "no reminder", never an error.
/// </summary>
public static class UpdateCheck
{
    public const string VersionUrl = "https://rotaidem.com/api/heavypro/version";
    public const string DownloadUrl = "https://rotaidem.com/downloads";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<UpdateInfo?> CheckAsync(Version current)
    {
        try
        {
            var json = await Http.GetStringAsync(VersionUrl).ConfigureAwait(false);
            return Evaluate(current, json);
        }
        catch (Exception)
        {
            return null; // Offline, or no endpoint yet: no reminder.
        }
    }

    /// <summary>Compares the running version against the server's reply. Kept separate so it can be tested.</summary>
    public static UpdateInfo? Evaluate(Version current, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String)
                return null;
            var text = (v.GetString() ?? "").TrimStart('v', 'V').Trim();
            if (!Version.TryParse(Normalize(text), out var latest))
                return null;
            var url = doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } s ? s : DownloadUrl;
            return new UpdateInfo(latest > Normalize(current), FormatVersion(latest), url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Compare on major.minor.build only, so a 4-part build number never looks "newer" by its revision.
    private static Version Normalize(Version v) => new(v.Major, v.Minor < 0 ? 0 : v.Minor, v.Build < 0 ? 0 : v.Build);

    private static string Normalize(string text)
    {
        var parts = text.Split('.');
        return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : text;
    }

    private static string FormatVersion(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";
}
