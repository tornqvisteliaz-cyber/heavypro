using System.Text.Json;

namespace HeavyFeel.Core.Services;

public static class CommunityProfile
{
    public static double TravelScale()
    {
        try
        {
            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro");
            var marker = Path.Combine(appDir, "community.path");
            if (!File.Exists(marker))
                return 1;
            var community = File.ReadAllText(marker).Trim();
            var file = Path.Combine(community, "heavypro-feel", "HeavyPro", "profiles.json");
            if (!File.Exists(file))
                return 1;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("travelScale", out var scale) && scale.TryGetDouble(out var value))
                return Math.Max(0.5, Math.Min(3, value));
        }
        catch
        {
        }
        return 1;
    }
}
