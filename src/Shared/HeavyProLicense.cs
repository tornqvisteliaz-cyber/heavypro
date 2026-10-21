using System.Net.Http;
using Rotaidem.Licensing;

namespace HeavyPro.Licensing;

/// <summary>
/// Licence settings shared by the app and the installer (linked into both projects),
/// so the server address lives in one place.
/// </summary>
internal static class HeavyProLicense
{
    // rotaidem.com still shows the registrar's parking page. Switch to
    // LicenseClient.DefaultBaseUrl once the domain points at the Render service.
    public const string ServerUrl = "https://rotaidem.onrender.com";
    public const string Product = "heavypro";
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(14);

    // The server compares product ids exactly. The website's product is
    // "rotaidem-heavypro" (slug of "Rotaidem HeavyPro", used by the admin panel's
    // licence form); the licence generator defaults to "heavypro". Accept both.
    // A wrong product is rejected before an activation is recorded, so trying the
    // next id costs nothing.
    private static readonly string[] ProductIds = { "rotaidem-heavypro", Product };

    // Render's free tier sleeps after ~15 idle minutes and takes 30-60 s to wake.
    // The library's own 15 s timeout reported that as "could not reach the server".
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static LicenseStore Store() => new(Product);

    /// <summary>POSTs JSON to the licence server and returns the status and reply body; network failures come back as status 0.</summary>
    public static async Task<(int Status, string Body)> PostJsonAsync(string path, string json)
    {
        try
        {
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(ServerUrl + path, content).ConfigureAwait(false);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (0, "");
        }
    }

    public static Task<LicenseResult> ActivateAsync(string key) =>
        FirstMatchingProduct(client => client.ActivateAsync(key));

    public static Task<LicenseResult> CheckAsync() =>
        FirstMatchingProduct(client => client.CheckAsync(Store(), OfflineGrace));

    private static async Task<LicenseResult> FirstMatchingProduct(Func<LicenseClient, Task<LicenseResult>> call)
    {
        LicenseResult? result = null;
        foreach (var id in ProductIds)
        {
            result = await call(new LicenseClient(ServerUrl, id, Http));
            if (result.Status != LicenseStatus.WrongProduct)
                return result;
        }
        return result!;
    }

    /// <summary>
    /// Start waking the server while the user is still typing their key.
    /// Fire and forget: failures are ignored, the real call reports errors.
    /// </summary>
    public static void WakeServer()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var response = await Http.GetAsync(ServerUrl + "/health").ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The activation call that follows gives the user a proper message.
            }
        });
    }
}
