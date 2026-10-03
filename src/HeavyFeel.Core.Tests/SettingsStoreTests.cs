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
}
