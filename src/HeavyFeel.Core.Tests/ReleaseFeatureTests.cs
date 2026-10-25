using HeavyFeel.Core.Models;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class ReleaseFeatureTests
{
    private const string Log = "# HeavyPro changelog\n\nIntro.\n\n## 0.5.1\n\n- **Profiles per aircraft:** one\n  continued line\n- two\n\n## 0.5.0\n\n- old\n";

    [Fact]
    public void Whats_new_takes_only_that_release()
    {
        Assert.Equal("• Profiles per aircraft: one continued line\n• two", Changelog.WhatsNew(Log, new Version(0, 5, 1)));
        Assert.Equal("• old", Changelog.WhatsNew(Log, new Version(0, 5, 0, 0)));
        Assert.Null(Changelog.WhatsNew(Log, new Version(0, 6, 0)));
        Assert.Null(Changelog.WhatsNew(null, new Version(0, 5, 1)));
    }

    [Fact]
    public void Real_changelog_has_this_release()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "CHANGELOG.md");
        Assert.NotNull(Changelog.WhatsNew(File.ReadAllText(file), new Version(0, 5, 1)));
    }

    [Fact]
    public void Linked_profile_is_found_for_its_aircraft_only()
    {
        var settings = new AppSettings();
        var twin = new CustomProfile { Name = "Boeing 777 - Heavy twin" };
        settings.CustomProfiles.Add(twin);
        var b777 = new AircraftIdentity { Title = "777-300ER" };
        var a350 = new AircraftIdentity { Title = "A350-1000 (No Cabin)" };

        AircraftProfileLinks.Link(settings, b777, twin);
        Assert.Same(twin, AircraftProfileLinks.Find(settings, b777));
        Assert.Same(twin, AircraftProfileLinks.Find(settings, new AircraftIdentity { Title = " 777-300er " }));
        Assert.Null(AircraftProfileLinks.Find(settings, a350));
        Assert.True(AircraftProfileLinks.IsLinked(settings, b777, twin));

        AircraftProfileLinks.Unlink(settings, b777);
        Assert.Null(AircraftProfileLinks.Find(settings, b777));
    }

    [Fact]
    public void Deleting_a_profile_drops_its_links()
    {
        var settings = new AppSettings();
        var p = new CustomProfile { Name = "Mine" };
        settings.CustomProfiles.Add(p);
        AircraftProfileLinks.Link(settings, new AircraftIdentity { Title = "777F" }, p);
        AircraftProfileLinks.Link(settings, new AircraftIdentity { Title = "777-300ER" }, p);
        AircraftProfileLinks.Forget(settings, "mine");
        Assert.Empty(settings.AircraftProfiles);
    }

    [Fact]
    public void No_aircraft_yet_links_nothing()
    {
        var settings = new AppSettings();
        var p = new CustomProfile { Name = "Mine" };
        settings.CustomProfiles.Add(p);
        AircraftProfileLinks.Link(settings, new AircraftIdentity(), p);
        Assert.Empty(settings.AircraftProfiles);
    }
}
