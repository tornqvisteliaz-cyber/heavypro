using HeavyPro.Core.Services;
using Xunit;

namespace HeavyPro.Core.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void A_newer_server_version_is_an_update()
    {
        var info = UpdateCheck.Evaluate(new Version(0, 7, 3), "{\"version\":\"0.8.0\"}");
        Assert.NotNull(info);
        Assert.True(info!.UpdateAvailable);
        Assert.Equal("0.8.0", info.LatestVersion);
    }

    [Fact]
    public void The_same_or_older_server_version_is_not_an_update()
    {
        Assert.False(UpdateCheck.Evaluate(new Version(0, 8, 0), "{\"version\":\"0.8.0\"}")!.UpdateAvailable);
        Assert.False(UpdateCheck.Evaluate(new Version(0, 8, 0), "{\"version\":\"v0.7.9\"}")!.UpdateAvailable);
    }

    [Fact]
    public void A_four_part_build_number_does_not_look_newer_by_its_revision()
    {
        // The running build may be 0.8.0.137; the server says 0.8.0 -> not an update.
        Assert.False(UpdateCheck.Evaluate(new Version(0, 8, 0, 137), "{\"version\":\"0.8.0\"}")!.UpdateAvailable);
    }

    [Fact]
    public void Junk_or_a_missing_field_means_no_reminder()
    {
        Assert.Null(UpdateCheck.Evaluate(new Version(0, 7, 3), "{\"foo\":1}"));
        Assert.Null(UpdateCheck.Evaluate(new Version(0, 7, 3), "not json"));
    }

    [Fact]
    public void A_custom_download_url_is_used_when_given()
    {
        var info = UpdateCheck.Evaluate(new Version(0, 7, 3), "{\"version\":\"0.8.0\",\"url\":\"https://rotaidem.com/get\"}");
        Assert.Equal("https://rotaidem.com/get", info!.DownloadUrl);
    }
}
