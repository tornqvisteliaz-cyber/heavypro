using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeavyFeel.Core.Services;

/// <summary>A newer HeavyPro the Update button can install.</summary>
public sealed record UpdateInfo(Version Version, string Url, string Sha256);

/// <summary>
/// The signed update manifest that each tagged release publishes as update.jws: a compact
/// JWS (RS256) whose payload names the version, the installer URL and its SHA-256. It is
/// signed in CI with the update key, separate from the licence server's key; the app
/// trusts only the public half built into it, so nobody else can push an installer.
/// </summary>
public static class UpdateManifest
{
    public const string Product = "HeavyPro";

    /// <summary>Installers are only ever taken from this repository's releases.</summary>
    public const string ReleasePrefix = "https://github.com/tornqvisteliaz-cyber/heavypro/releases/download/";

    /// <summary>Checks the signature and contents. Null, with the reason, if anything is off.</summary>
    public static UpdateInfo? Verify(string? jws, string? publicKeyJwk, out string problem)
    {
        problem = "";
        try
        {
            if (string.IsNullOrWhiteSpace(publicKeyJwk))
            {
                problem = "This build has no update key.";
                return null;
            }
            var parts = (jws ?? "").Trim().Split('.');
            if (parts.Length != 3)
            {
                problem = "The update file is not a signed manifest.";
                return null;
            }

            using (var header = JsonDocument.Parse(FromBase64Url(parts[0])))
            {
                if (Text(header.RootElement, "alg") != "RS256")
                {
                    problem = "The update file uses an unexpected signature.";
                    return null;
                }
            }

            using (var key = JsonDocument.Parse(publicKeyJwk!))
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = FromBase64Url(Text(key.RootElement, "n")),
                    Exponent = FromBase64Url(Text(key.RootElement, "e"))
                });
                var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
                if (!rsa.VerifyData(signed, FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    problem = "The update file's signature is not Rotaidem's.";
                    return null;
                }
            }

            using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = payload.RootElement;
            var url = Text(root, "url");
            var sha = Text(root, "sha256").ToLowerInvariant();
            if (Text(root, "product") != Product
                || !Version.TryParse(Text(root, "version"), out var version)
                || !url.StartsWith(ReleasePrefix, StringComparison.Ordinal)
                || sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            {
                problem = "The update file does not describe a HeavyPro installer.";
                return null;
            }
            return new UpdateInfo(Normalize(version), url, sha);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException or KeyNotFoundException or InvalidOperationException)
        {
            problem = "The update file could not be read.";
            return null;
        }
    }

    /// <summary>Signs a manifest payload; CI does the same in New-UpdateManifest.ps1.</summary>
    public static string Sign(string payloadJson, RSA privateKey)
    {
        var head = ToBase64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var body = ToBase64Url(Encoding.UTF8.GetBytes(payloadJson));
        var signature = privateKey.SignData(Encoding.ASCII.GetBytes(head + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return head + "." + body + "." + ToBase64Url(signature);
    }

    /// <summary>Compares major.minor.patch only; a missing part counts as 0.</summary>
    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
