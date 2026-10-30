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

    [Theory]
    [InlineData("BlueBird 757-200 PW", "TT:ATCCOM.AC_MODEL B752.0.text", "BBS752PW")]
    [InlineData("BlueBird 757-200 RR N/W 4 Door", "TT:ATCCOM.AC_MODEL B752.0.text", "BBS752RR")]
    public void Detects_the_BlueBird_757_as_a_narrow_body_that_needs_the_direct_route(string title, string model, string type)
    {
        var id = new AircraftIdentity { Title = title, AtcModel = model, AtcType = type };
        Assert.True(id.IsBlueBird757);
        Assert.True(id.NeedsDirectRoute);
        Assert.False(id.IsPmdg777);
        Assert.Equal(AircraftClass.NarrowBody, AircraftCatalog.Classify(id, 0));
    }

    [Fact]
    public void Does_not_flag_generic_airliner()
    {
        var id = new AircraftIdentity { Title = "Airbus A320neo Asobo" };
        Assert.False(id.IsFenixA320);
        Assert.Equal("Narrow-body", id.SuggestedProfile);
    }
}
