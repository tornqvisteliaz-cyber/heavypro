using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class ControlEngineTests
{
    [Fact]
    public void Fast_stick_does_not_reach_target_immediately()
    {
        var engine = new ControlEngine();
        var snap = new FlightSnapshot
        {
            YokeX = 1,
            TotalWeightPounds = 2450,
            AirspeedIndicatedKnots = 90,
            OnGround = false,
            Class = AircraftClass.LightGa
        };
        var settings = new AppSettings { FeelLevel = "RealisticPlus", MasterEnable = true };
        var frame = engine.Step(settings, snap, true, 0.016);
        Assert.True(frame.Active);
        Assert.True(Math.Abs(frame.RollOut) < 0.35);
    }

    [Fact]
    public void Heavier_profile_is_slower_than_light_ga()
    {
        var light = Run(AircraftClass.LightGa, 2450, "Normal");
        var heavy = Run(AircraftClass.NarrowBody, 145000, "RealisticPlus");
        Assert.True(Math.Abs(light) > Math.Abs(heavy));
    }

    private static double Run(AircraftClass cls, double weight, string feel)
    {
        var engine = new ControlEngine();
        var snap = new FlightSnapshot
        {
            YokeX = 1,
            TotalWeightPounds = weight,
            AirspeedIndicatedKnots = 140,
            Class = cls
        };
        var settings = new AppSettings { FeelLevel = feel };
        double output = 0;
        for (var i = 0; i < 8; i++)
            output = engine.Step(settings, snap, true, 0.016).RollOut;
        return output;
    }
}
