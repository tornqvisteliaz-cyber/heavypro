using HeavyFeel.Core.Models;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class RoutePickerTests
{
    private static readonly AircraftIdentity Vl3 = new() { Title = "JMB Aviation VL3", AtcModel = "VL3" };
    private static readonly AircraftIdentity Pmdg777 = new() { Title = "777-300ER", AtcModel = "ATCCOM.AC_MODEL B77W.0.text" };
    private static readonly AircraftIdentity A320neo = new() { Title = "A320neo V2", AtcModel = "ATCCOM.AC_MODEL_A20N.0.text" };
    private static readonly AircraftIdentity BlueBird = new() { Title = "BlueBird 757-200 PW N/W", AtcModel = "ATCCOM.AC_MODEL B752.0.text", AtcType = "BBS752PW" };
    private static readonly AircraftIdentity AerosoftA330 = new() { Title = "Aerosoft A330-900 Delta", AtcModel = "A339RR", AtcType = "AIRBUS" };

    [Fact]
    public void A_stick_bound_in_msfs_flies_these_aircraft_on_the_msfs_bindings_route_like_0_5_3()
    {
        foreach (var aircraft in new[] { Vl3, Pmdg777, A320neo, new AircraftIdentity() })
        {
            var choice = RoutePicker.Choose(stickBoundInMsfs: true, aircraft, ownProfileLinked: false);
            Assert.Equal("Masked", choice.Route);
            Assert.False(choice.PassThrough);
        }
    }

    [Fact]
    public void Aircraft_that_read_the_binding_themselves_get_the_stick_untouched()
    {
        // The 757 jumped between the raw and the shaped stick on Direct; the A330 did not move on MSFS bindings.
        foreach (var aircraft in new[] { BlueBird, AerosoftA330 })
        {
            var choice = RoutePicker.Choose(true, aircraft, false);
            Assert.True(choice.IsDirect);
            Assert.True(choice.PassThrough);
        }
        // With the stick unbound (an MSFS profile without the axes) they get REAL1's weight.
        Assert.Equal("Real1", RoutePicker.Choose(false, BlueBird, false).Route);
        Assert.False(RoutePicker.Choose(false, AerosoftA330, false).PassThrough);
    }

    [Fact]
    public void An_unbound_stick_gets_real1_or_direct_for_your_own_linked_profile()
    {
        Assert.Equal("Real1", RoutePicker.Choose(false, Vl3, false).Route);
        Assert.Equal("Direct", RoutePicker.Choose(false, Pmdg777, true).Route);
    }

    [Fact]
    public void Your_own_pick_wins()
    {
        Assert.Equal("Real1", RoutePicker.Choose(true, Pmdg777, false, "Real1").Route);
        Assert.Equal("Masked", RoutePicker.Choose(false, Vl3, false, "Masked").Route);
        Assert.False(RoutePicker.Choose(true, BlueBird, false, "Direct").PassThrough);
    }
}
