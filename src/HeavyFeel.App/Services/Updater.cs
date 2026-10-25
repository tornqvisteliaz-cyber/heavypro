using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using HeavyFeel.Core.Services;

namespace HeavyFeel.App.Services;

/// <summary>
/// The Update button. Only tagged releases publish update.jws, and GitHub's "latest"
/// link skips the beta pre-release, so this only ever offers released versions.
/// Nothing runs unless the manifest carries Rotaidem's update signature and the
/// downloaded installer matches its SHA-256.
/// </summary>
public static class Updater
{
    private const string ManifestUrl = "https://github.com/tornqvisteliaz-cyber/heavypro/releases/latest/download/update.jws";

    private static readonly HttpClient Http = CreateClient();

    public static Version Current { get; } = UpdateManifest.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static readonly string? PublicKey = ReadPublicKey();

    /// <summary>The newer release, or null with the reason (none newer, offline, bad signature).</summary>
    public static async Task<(UpdateInfo? Update, string Message)> CheckAsync()
    {
        try
        {
            using var response = await Http.GetAsync(ManifestUrl).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, "No HeavyPro release has been published yet.");
            if (!response.IsSuccessStatusCode)
                return (null, $"The update check failed ({(int)response.StatusCode}).");
            var jws = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var info = UpdateManifest.Verify(jws, PublicKey, out var problem);
            if (info == null)
                return (null, problem);
            return UpdateManifest.IsNewer(info.Version, Current)
                ? (info, $"HeavyPro {info.Version} is available.")
                : (null, $"HeavyPro {Current} is the newest version.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "Could not reach GitHub to check for updates.");
        }
    }

    /// <summary>
    /// Downloads the installer, checks its SHA-256 against the signed manifest and starts it
    /// in update mode. The caller then closes HeavyPro; setup reopens it when done.
    /// </summary>
    public static async Task<string?> DownloadAndStartAsync(UpdateInfo update, IProgress<double>? progress = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "HeavyProUpdate");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"HeavyProSetup-{update.Version}.exe");
        try
        {
            using (var response = await Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return $"The download failed ({(int)response.StatusCode}).";
                var total = response.Content.Headers.ContentLength ?? 0;
                using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var target = File.Create(file);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                        progress?.Report((double)done / total);
                }
            }

            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
            {
                var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (hash != update.Sha256)
                {
                    File.Delete(file);
                    return "The download does not match the signed release. Nothing was installed.";
                }
            }

            Process.Start(new ProcessStartInfo(file, "/update") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return "The update could not be downloaded: " + ex.Message;
        }
    }

    private static HttpClient CreateClient()
    {
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HeavyPro-Updater");
        return client;
    }

    private static string? ReadPublicKey()
    {
        using var stream = typeof(Updater).Assembly.GetManifestResourceStream("HeavyPro.UpdatePublicKey.json");
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd().Trim();
        return text.Length > 0 ? text : null;
    }
}
