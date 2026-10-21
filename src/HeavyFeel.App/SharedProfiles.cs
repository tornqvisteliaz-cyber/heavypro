using System.Text.Json;
using HeavyFeel.Core.Models;
using HeavyPro.Licensing;
using Rotaidem.Licensing;

namespace HeavyFeel.App;

/// <summary>One row of the community list.</summary>
public sealed class SharedProfileListing
{
    public SharedProfileListing(string name, int downloads)
    {
        Name = name;
        Downloads = downloads;
    }

    public string Name { get; }
    public int Downloads { get; }
    public string DownloadsText => Downloads == 1 ? "1 download" : $"{Downloads} downloads";
}

/// <summary>
/// Shares feel profiles through the Rotaidem server: publish one under a name,
/// fetch someone else's by its name. Both need this computer's activated licence.
/// </summary>
public static class SharedProfiles
{
    public static async Task<string> PublishAsync(CustomProfile profile)
    {
        if (!TryLicence(out var key, out var problem))
            return problem;
        var payload = new Dictionary<string, object>
        {
            ["key"] = key,
            ["machine_id"] = MachineId.Get(),
            ["name"] = profile.Name,
            ["profile"] = profile
        };
        var (status, body) = await HeavyProLicense.PostJsonAsync("/api/profiles/publish", JsonSerializer.Serialize(payload));
        if (status == 200)
            return $"Shared as \"{Read(body, "name") ?? profile.Name}\". Anyone with HeavyPro can get it by that name.";
        return Failure(status, body);
    }

    public static async Task<(CustomProfile? Profile, string Message)> GetAsync(string name)
    {
        if (!TryLicence(out var key, out var problem))
            return (null, problem);
        var payload = new Dictionary<string, object> { ["key"] = key, ["machine_id"] = MachineId.Get(), ["name"] = name };
        var (status, body) = await HeavyProLicense.PostJsonAsync("/api/profiles/get", JsonSerializer.Serialize(payload));
        if (status != 200)
            return (null, Failure(status, body));
        try
        {
            using var doc = JsonDocument.Parse(body);
            var profile = JsonSerializer.Deserialize<CustomProfile>(doc.RootElement.GetProperty("profile").GetRawText()) ?? new CustomProfile();
            profile.Name = doc.RootElement.GetProperty("name").GetString() ?? name;
            return (profile, $"Got \"{profile.Name}\". Press Apply to use it.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, "The server sent a profile HeavyPro could not read.");
        }
    }

    /// <summary>Every shared profile, most downloaded first, optionally filtered by name.</summary>
    public static async Task<(IReadOnlyList<SharedProfileListing> Profiles, string Message)> ListAsync(string search)
    {
        if (!TryLicence(out var key, out var problem))
            return (Array.Empty<SharedProfileListing>(), problem);
        var payload = new Dictionary<string, object> { ["key"] = key, ["machine_id"] = MachineId.Get(), ["search"] = search ?? "" };
        var (status, body) = await HeavyProLicense.PostJsonAsync("/api/profiles/list", JsonSerializer.Serialize(payload));
        if (status != 200)
            return (Array.Empty<SharedProfileListing>(), Failure(status, body));
        try
        {
            using var doc = JsonDocument.Parse(body);
            var list = new List<SharedProfileListing>();
            foreach (var item in doc.RootElement.GetProperty("profiles").EnumerateArray())
            {
                list.Add(new SharedProfileListing(
                    item.GetProperty("name").GetString() ?? "",
                    item.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 0));
            }
            return (list, list.Count == 0 ? "No shared profiles match." : $"{list.Count} shared profile{(list.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (Array.Empty<SharedProfileListing>(), "The server sent a list HeavyPro could not read.");
        }
    }

    private static bool TryLicence(out string key, out string problem)
    {
        key = HeavyProLicense.Store().Load()?.Key ?? "";
        problem = key.Length == 0 ? "Sharing profiles needs this computer's licence to be activated." : "";
        return key.Length > 0;
    }

    private static string Failure(int status, string body) =>
        status == 0
            ? "Could not reach the server. Check your internet connection and try again."
            : Read(body, "error") ?? $"The server answered {status}.";

    private static string? Read(string body, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
