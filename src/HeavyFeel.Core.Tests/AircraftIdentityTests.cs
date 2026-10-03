using HeavyFeel.Core.Models;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class AircraftIdentityTests
{
    [Fact]
    public void Detects_Fenix_A320_from_title()
    {
        var id = new AircraftIdentity { Title = "Fenix A320-111 EasyJet" };
        Assert.True(id.IsFenixA320);
        Assert.Equal("Fenix A320", id.SuggestedProfile);
    }

    [Fact]
    public void Detects_PMDG_777_from_title()
    {
        var id = new AircraftIdentity { Title = "PMDG 777-300ER British Airways" };
        Assert.True(id.IsPmdg777);
        Assert.Equal("PMDG 777", id.SuggestedProfile);
    }

    [Fact]
    public void Does_not_flag_generic_airliner()
    {
        var id = new AircraftIdentity { Title = "Airbus A320neo Asobo" };
        Assert.False(id.IsFenixA320);
        Assert.Equal("Narrow-body", id.SuggestedProfile);
    }
}
