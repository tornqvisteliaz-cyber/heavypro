using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class FlightStateResolverTests
{
    [Fact]
    public void Airborne_when_agl_is_high_even_if_ground_flag_stuck()
    {
        Assert.False(FlightStateResolver.ResolveOnGround(1, 1, 1200, 1180));
    }

    [Fact]
    public void Ground_when_is_on_ground_and_low_agl()
    {
        Assert.True(FlightStateResolver.ResolveOnGround(1, 0, 3, 2));
    }

    [Fact]
    public void Prefers_is_on_ground_over_sim_on_ground()
    {
        Assert.False(FlightStateResolver.ResolveOnGround(0, 1, 80, 75));
    }

    [Fact]
    public void Fenix_fcu_lights_count_as_autopilot()
    {
        Assert.True(FlightStateResolver.ResolveAutopilot(0, 0, 0, 0, 1, 0, 0, 0));
        Assert.Equal("AP ON (AP1)", FlightStateResolver.AutopilotLabel(true, 1, 0, 0));
    }

    [Fact]
    public void Standard_master_still_works()
    {
        Assert.True(FlightStateResolver.ResolveAutopilot(1, 0, 0, 0, 0, 0, 0, 0));
        Assert.False(FlightStateResolver.ResolveAutopilot(0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void Heading_or_altitude_lock_is_not_autopilot()
    {
        Assert.False(FlightStateResolver.ResolveAutopilot(0, 1, 1, 1, 0, 0, 0, 0));
    }

    [Fact]
    public void Fast_and_off_the_runway_is_air()
    {
        Assert.False(FlightStateResolver.ResolveOnGround(1, 1, 20, 18, indicatedKnots: 155));
    }
}
