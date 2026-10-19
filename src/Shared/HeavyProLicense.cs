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

    public static LicenseClient Client() => new(ServerUrl, Product);
    public static LicenseStore Store() => new(Product);
}
