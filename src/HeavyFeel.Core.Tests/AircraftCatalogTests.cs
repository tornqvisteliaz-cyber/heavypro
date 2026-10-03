using HeavyFeel.Core.Models;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class AircraftCatalogTests
{
    [Fact]
    public void Classifies_airliner_families()
    {
        Assert.Equal(AircraftClass.HeavyWide, AircraftCatalog.Classify(new AircraftIdentity { Title = "Boeing 747-400" }, 0));
        Assert.Equal(AircraftClass.WideBody, AircraftCatalog.Classify(new AircraftIdentity { Title = "Boeing 777-300ER" }, 0));
        Assert.Equal(AircraftClass.NarrowBody, AircraftCatalog.Classify(new AircraftIdentity { Title = "Fenix A320" }, 0));
        Assert.Equal(AircraftClass.LightGa, AircraftCatalog.Classify(new AircraftIdentity { Title = "Cessna 172" }, 0));
    }

    [Fact]
    public void Heavy_has_more_inertia_than_cessna()
    {
        Assert.True(AircraftCatalog.InertiaScale(AircraftClass.HeavyWide) > AircraftCatalog.InertiaScale(AircraftClass.NarrowBody));
        Assert.True(AircraftCatalog.InertiaScale(AircraftClass.NarrowBody) > AircraftCatalog.InertiaScale(AircraftClass.LightGa));
    }
}
