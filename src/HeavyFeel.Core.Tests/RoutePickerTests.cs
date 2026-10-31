using HeavyFeel.Core.Models;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class RoutePickerTests
{
    private static readonly AircraftIdentity Vl3 = new() { Title = "JMB Aviation VL3", AtcModel = "VL3" };
    private static readonly AircraftIdentity Pmdg777 = new() { Title = "777-300ER", AtcModel = "ATCCOM.AC_MODEL B77W.0.text" };
    private static readonly AircraftIdentity BlueBird = new() { Title = "BlueBird 757-200 PW N/W", AtcModel = "ATCCOM.AC_MODEL B752.0.text", AtcType = "BBS752PW" };

    [Fact]
    public void A_stick_bound_in_msfs_flies_every_aircraft_on_the_msfs_bindings_route_like_0_5_3()
    {
        foreach (var aircraft in new[] { Vl3, Pmdg777 })
        {
            var choice = RoutePicker.Choose(stickBoundInMsfs: true, aircraft, ownProfileLinked: false, stickMissingOnMasked: false);
            Assert.Equal("Masked", choice.Route);
            Assert.False(choice.BridgeAnswersBindings);
        }
    }

    [Fact]
    public void The_bluebird_757_gets_the_stick_directly_with_the_bridge_answering_the_binding()
    {
        var choice = RoutePicker.Choose(true, BlueBird, false, false);
        Assert.Equal("Real1", choice.Route);
        Assert.True(choice.IsDirect);
        Assert.True(choice.BridgeAnswersBindings);
        // With the stick unbound there is nothing to answer.
        Assert.False(RoutePicker.Choose(false, BlueBird, false, false).BridgeAnswersBindings);
    }

    [Fact]
    public void An_unbound_stick_gets_real1_or_direct_for_your_own_linked_profile()
    {
        Assert.Equal("Real1", RoutePicker.Choose(false, Vl3, false, false).Route);
        Assert.Equal("Direct", RoutePicker.Choose(false, Pmdg777, true, false).Route);
        Assert.Equal("Direct", RoutePicker.Choose(true, BlueBird, true, false).Route);
    }

    [Fact]
    public void A_stick_that_never_reaches_heavypro_through_msfs_falls_back_to_real1()
    {
        var choice = RoutePicker.Choose(true, Vl3, false, stickMissingOnMasked: true);
        Assert.Equal("Real1", choice.Route);
        Assert.False(choice.BridgeAnswersBindings);
    }
}
