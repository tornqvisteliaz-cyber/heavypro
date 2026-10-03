using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class FlightPhaseResolverTests
{
    [Fact]
    public void Taxi_is_ground()
    {
        var snap = new FlightSnapshot { OnGround = true, AirspeedIndicatedKnots = 12 };
        Assert.Equal(FlightPhase.Ground, FlightPhaseResolver.Resolve(snap));
    }

    [Fact]
    public void Fast_roll_is_takeoff()
    {
        var snap = new FlightSnapshot { OnGround = true, AirspeedIndicatedKnots = 130 };
        Assert.Equal(FlightPhase.Takeoff, FlightPhaseResolver.Resolve(snap));
    }

    [Fact]
    public void High_and_level_is_cruise()
    {
        var snap = new FlightSnapshot
        {
            OnGround = false,
            AirspeedIndicatedKnots = 280,
            AltitudeAglFeet = 8000,
            RadioHeightFeet = 8000,
            VerticalSpeedFpm = 50
        };
        Assert.Equal(FlightPhase.Cruise, FlightPhaseResolver.Resolve(snap));
    }
}
