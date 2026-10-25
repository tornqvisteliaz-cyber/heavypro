using System.Security.Cryptography;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class UpdateManifestTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Url = UpdateManifest.ReleasePrefix + "v1.0.1/HeavyProSetup.exe";

    private static (RSA Key, string Jwk) NewKey()
    {
        var rsa = RSA.Create();
        rsa.KeySize = 2048;
        var p = rsa.ExportParameters(false);
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (rsa, $"{{\"kty\":\"RSA\",\"n\":\"{B64(p.Modulus!)}\",\"e\":\"{B64(p.Exponent!)}\"}}");
    }

    private static string Payload(string product = "HeavyPro", string version = "1.0.1", string url = Url, string sha = Sha) =>
        $"{{\"product\":\"{product}\",\"version\":\"{version}\",\"url\":\"{url}\",\"sha256\":\"{sha}\"}}";

    [Fact]
    public void Accepts_a_manifest_signed_with_the_built_in_key()
    {
        var (key, jwk) = NewKey();
        var info = UpdateManifest.Verify(UpdateManifest.Sign(Payload(), key), jwk, out var problem);
        Assert.NotNull(info);
        Assert.Equal("", problem);
        Assert.Equal(new Version(1, 0, 1), info!.Version);
        Assert.Equal(Url, info.Url);
    }

    [Fact]
    public void Rejects_another_key_and_a_changed_payload()
    {
        var (key, _) = NewKey();
        var (_, otherJwk) = NewKey();
        var jws = UpdateManifest.Sign(Payload(), key);
        Assert.Null(UpdateManifest.Verify(jws, otherJwk, out _));

        var (_, jwk) = NewKey();
        var parts = UpdateManifest.Sign(Payload(), key).Split('.');
        var forged = parts[0] + "." + UpdateManifest.Sign(Payload(version: "9.9.9"), key).Split('.')[1] + "." + parts[2];
        Assert.Null(UpdateManifest.Verify(forged, jwk, out _));
    }

    [Theory]
    [InlineData("OtherApp", "1.0.1", Url, Sha)]
    [InlineData("HeavyPro", "not-a-version", Url, Sha)]
    [InlineData("HeavyPro", "1.0.1", "https://evil.example/HeavyProSetup.exe", Sha)]
    [InlineData("HeavyPro", "1.0.1", Url, "short")]
    public void Rejects_signed_manifests_that_are_not_a_heavypro_installer(string product, string version, string url, string sha)
    {
        var (key, jwk) = NewKey();
        Assert.Null(UpdateManifest.Verify(UpdateManifest.Sign(Payload(product, version, url, sha), key), jwk, out var problem));
        Assert.NotEqual("", problem);
    }

    [Fact]
    public void Rejects_garbage_without_throwing()
    {
        var (_, jwk) = NewKey();
        Assert.Null(UpdateManifest.Verify("not a manifest", jwk, out _));
        Assert.Null(UpdateManifest.Verify("a.b.c", jwk, out _));
        Assert.Null(UpdateManifest.Verify(null, jwk, out _));
        Assert.Null(UpdateManifest.Verify("a.b.c", null, out _));
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("0.4.1", "0.4.0.0", true)]
    public void Newer_compares_major_minor_patch(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateManifest.IsNewer(Version.Parse(candidate), Version.Parse(current)));
    }
}
