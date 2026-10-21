using HeavyFeel.Core.Models;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Round_trips_slider_values()
    {
        var path = Path.Combine(Path.GetTempPath(), "heavyfeel-test-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings { Inertia = 77, MasterEnable = true, AircraftProfile = "Fenix A320" });
            var loaded = store.Load();
            Assert.Equal(77, loaded.Inertia);
            Assert.True(loaded.MasterEnable);
            Assert.Equal("Fenix A320", loaded.AircraftProfile);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Custom_profiles_round_trip_and_restore_tuning()
    {
        var path = Path.Combine(Path.GetTempPath(), "heavyfeel-test-" + Guid.NewGuid() + ".json");
        try
        {
            var tuned = new AppSettings { Inertia = 81, PitchDamping = 64, InputCurve = "Expo", RudderLimiter = false, StickDevice = "4098:BC27" };
            var settings = new AppSettings();
            settings.CustomProfiles.Add(CustomProfile.Capture("  777 heavy ", tuned));
            var store = new JsonSettingsStore(path);
            store.Save(settings);

            var loaded = store.Load();
            var profile = Assert.Single(loaded.CustomProfiles);
            Assert.Equal("777 heavy", profile.Name);

            var target = new AppSettings { StickDevice = "044F:B679" };
            profile.ApplyTo(target);
            Assert.Equal(81, target.Inertia);
            Assert.Equal(64, target.PitchDamping);
            Assert.Equal("Expo", target.InputCurve);
            Assert.False(target.RudderLimiter);
            Assert.Equal("044F:B679", target.StickDevice);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Old_settings_without_profiles_load_with_an_empty_list()
    {
        var path = Path.Combine(Path.GetTempPath(), "heavyfeel-test-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{ \"Inertia\": 50 }");
            var loaded = new JsonSettingsStore(path).Load();
            Assert.NotNull(loaded.CustomProfiles);
            Assert.Empty(loaded.CustomProfiles);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
