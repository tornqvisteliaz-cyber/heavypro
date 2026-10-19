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

    // Render's free tier sleeps after ~15 idle minutes and takes 30-60 s to wake.
    // The library's own 15 s timeout reported that as "could not reach the server".
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static LicenseClient Client() => new(ServerUrl, Product, Http);
    public static LicenseStore Store() => new(Product);

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
