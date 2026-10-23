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
    public void Msfs_2024_c172_is_light_ga_not_a_c17()
    {
        // The exact identity MSFS 2024 reported for the C172 on floats.
        var c172 = new AircraftIdentity
        {
            Title = "C172SP G1000 Passengers Floats",
            AtcModel = "ATCCOM.AC_MODEL C172.0.text",
            AtcType = "ATCCOM.ATC_NAME CESSNA.0.text"
        };
        Assert.Equal(AircraftClass.LightGa, AircraftCatalog.Classify(c172, 0));
        Assert.Equal("Light GA", c172.SuggestedProfile);
    }

    [Theory]
    [InlineData("Boeing C-17 Globemaster", AircraftClass.HeavyWide)]
    [InlineData("C17", AircraftClass.HeavyWide)]
    [InlineData("PMDG 737-800", AircraftClass.NarrowBody)]
    [InlineData("Cessna 152", AircraftClass.LightGa)]
    [InlineData("C152 Aerobat", AircraftClass.LightGa)]
    [InlineData("SR22T", AircraftClass.LightGa)]
    [InlineData("DA40NG", AircraftClass.LightGa)]
    [InlineData("A320neo", AircraftClass.NarrowBody)]
    public void Numbers_in_names_match_whole_numbers_only(string title, AircraftClass expected)
    {
        Assert.Equal(expected, AircraftCatalog.Classify(new AircraftIdentity { Title = title }, 0));
    }

    [Fact]
    public void Heavy_has_more_inertia_than_cessna()
    {
        Assert.True(AircraftCatalog.InertiaScale(AircraftClass.HeavyWide) > AircraftCatalog.InertiaScale(AircraftClass.NarrowBody));
        Assert.True(AircraftCatalog.InertiaScale(AircraftClass.NarrowBody) > AircraftCatalog.InertiaScale(AircraftClass.LightGa));
    }
}
