using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Services;

/// <summary>
/// "Use for this aircraft": a custom profile linked to an aircraft is applied by itself
/// whenever that aircraft loads. Aircraft are told apart by the title MSFS reports
/// ("777-300ER", "A350-1000 (No Cabin)").
/// </summary>
public static class AircraftProfileLinks
{
    /// <summary>The key an aircraft is linked by; empty when MSFS has not said yet.</summary>
    public static string Key(AircraftIdentity id)
    {
        var title = (id.Title ?? "").Trim();
        return title.Length > 0 ? title : (id.AtcModel ?? "").Trim();
    }

    /// <summary>The profile linked to this aircraft, if it still exists.</summary>
    public static CustomProfile? Find(AppSettings settings, AircraftIdentity id)
    {
        var key = Key(id);
        if (key.Length == 0 || !TryGet(settings, key, out var name))
            return null;
        return settings.CustomProfiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsLinked(AppSettings settings, AircraftIdentity id, CustomProfile profile) =>
        Find(settings, id) is { } linked && string.Equals(linked.Name, profile.Name, StringComparison.OrdinalIgnoreCase);

    public static void Link(AppSettings settings, AircraftIdentity id, CustomProfile profile)
    {
        var key = Key(id);
        if (key.Length == 0)
            return;
        Remove(settings, key);
        settings.AircraftProfiles[key] = profile.Name;
    }

    public static void Unlink(AppSettings settings, AircraftIdentity id) => Remove(settings, Key(id));

    /// <summary>Drops every link to a profile that was deleted.</summary>
    public static void Forget(AppSettings settings, string profileName)
    {
        foreach (var key in settings.AircraftProfiles.Where(l => string.Equals(l.Value, profileName, StringComparison.OrdinalIgnoreCase)).Select(l => l.Key).ToList())
            settings.AircraftProfiles.Remove(key);
    }

    private static bool TryGet(AppSettings settings, string key, out string name)
    {
        foreach (var link in settings.AircraftProfiles)
        {
            if (string.Equals(link.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                name = link.Value;
                return true;
            }
        }
        name = "";
        return false;
    }

    private static void Remove(AppSettings settings, string key)
    {
        foreach (var existing in settings.AircraftProfiles.Keys.Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)).ToList())
            settings.AircraftProfiles.Remove(existing);
    }
}
