using System.Net.Http;
using System.Reflection;
using Rotaidem.Licensing;

namespace HeavyPro.Licensing;

/// <summary>
/// Licence settings shared by the app and the installer (linked into both projects),
/// so the server address lives in one place.
/// </summary>
internal static class HeavyProLicense
{
    // The Rotaidem website on STRATO serves the licence and profile endpoints.
    public const string ServerUrl = LicenseClient.DefaultBaseUrl; // https://rotaidem.com
    public const string Product = "heavypro";
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(14);

    // The server compares product ids exactly. The website's product is
    // "rotaidem-heavypro" (slug of "Rotaidem HeavyPro", used by the admin panel's
    // licence form); the licence generator defaults to "heavypro". Accept both.
    // A wrong product is rejected before an activation is recorded, so trying the
    // next id costs nothing.
    private static readonly string[] ProductIds = { "rotaidem-heavypro", Product };

    // Generous upper limit for slow connections; each licence call also has its own
    // per-attempt timeout in LicenseClient.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static LicenseStore Store() => new(Product);

    /// <summary>
    /// The licence server's public key, built into the program (packaging/licence-public-key.json,
    /// fetched from https://rotaidem.com/api/licenses/jwks.json by the build). HeavyPro trusts only
    /// answers signed with the matching private key, which never leaves the server. Null if missing.
    /// </summary>
    public static readonly string? PublicKeyJwk = ReadPublicKey();

    private static string? ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HeavyPro.LicensePublicKey.json");
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Trim();
        return text.Length > 0 ? text : null;
    }

    private static LicenseClient Client(string product) => new(ServerUrl, product, Http, PublicKeyJwk);

    /// <summary>Without the network: is the saved signed answer still good here? Used for an instant start.</summary>
    public static LicenseResult CheckOffline() => Client(Product).CheckOffline(Store(), OfflineGrace);

    /// <summary>Saves the key with the server's signed answer, so the next start can begin offline.</summary>
    public static void Save(string key, LicenseResult result) => Store().Save(key, DateTimeOffset.UtcNow, result.Token);

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
            result = await call(Client(id));
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
