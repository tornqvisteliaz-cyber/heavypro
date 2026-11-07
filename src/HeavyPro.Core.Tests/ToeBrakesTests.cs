using HeavyPro.Core.Models;
using HeavyPro.Core.Physics;
using Xunit;

namespace HeavyPro.Core.Tests;

public class ToeBrakesTests
{
    private static FlightSnapshot Plane(AircraftClass cls, double lb = 0, double max = 0) =>
        new() { Class = cls, TotalWeightPounds = lb, MaxGrossWeightPounds = max, OnGround = true };

    private static double SecondsToReach(ToeBrakes brakes, FlightSnapshot snap, double pedal, double level)
    {
        for (var i = 1; i <= 600; i++)
            if (brakes.Step(pedal, pedal, snap, 1.0 / 60).Left >= level)
                return i / 60.0;
        return double.PositiveInfinity;
    }

    [Fact]
    public void A_resting_foot_does_not_drag_the_brakes()
    {
        var brakes = new ToeBrakes();
        var o = brakes.Step(0.03, 0.02, Plane(AircraftClass.WideBody), 1.0 / 60);
        Assert.Equal(0, o.Left);
        Assert.Equal(0, o.Right);
        Assert.False(o.Active);
    }

    [Fact]
    public void First_half_of_the_pedal_is_gentle_and_full_pedal_is_full_pressure()
    {
        Assert.True(ToeBrakes.Target(0.5) < 0.4);
        Assert.Equal(1.0, ToeBrakes.Target(1.0), 6);
    }

    [Fact]
    public void Pressure_builds_up_and_a_heavy_jet_takes_longer_than_a_cessna()
    {
        var c172 = SecondsToReach(new ToeBrakes(), Plane(AircraftClass.LightGa), 1.0, 0.99);
        var b777 = SecondsToReach(new ToeBrakes(), Plane(AircraftClass.WideBody, 650_000, 775_000), 1.0, 0.99);
        Assert.InRange(c172, 0.15, 0.4);
        Assert.InRange(b777, 0.8, 1.3);
        var first = new ToeBrakes().Step(1.0, 1.0, Plane(AircraftClass.WideBody), 1.0 / 60);
        Assert.True(first.Left < 0.05, "no instant lock-up on a stamp");
    }

    [Fact]
    public void Lets_go_quicker_than_it_builds_then_goes_idle_after_one_zero()
    {
        var brakes = new ToeBrakes();
        var snap = Plane(AircraftClass.NarrowBody);
        for (var i = 0; i < 120; i++) brakes.Step(1, 1, snap, 1.0 / 60);
        var frames = 0;
        BrakeOutput o;
        do { o = brakes.Step(0, 0, snap, 1.0 / 60); frames++; } while (o.Left > 0 && frames < 600);
        Assert.InRange(frames / 60.0, 0.1, 0.5);
        Assert.True(o.Active, "one last write of zero");
        Assert.False(brakes.Step(0, 0, snap, 1.0 / 60).Active, "then out of the way of parking brake and autobrake");
    }

    [Fact]
    public void Each_side_is_its_own_for_differential_braking()
    {
        var brakes = new ToeBrakes();
        BrakeOutput o = default;
        for (var i = 0; i < 120; i++) o = brakes.Step(1, 0, Plane(AircraftClass.NarrowBody), 1.0 / 60);
        Assert.Equal(1.0, o.Left, 3);
        Assert.Equal(0, o.Right);
    }
}
