using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class ControlInertiaTests
{
    [Fact]
    public void Small_correction_moves_faster_than_a_full_throw()
    {
        var small = Run(0.1);
        var large = Run(1.0);
        Assert.True(small / 0.1 > large / 1.0);
    }

    [Fact]
    public void Invalid_moi_falls_back_to_weight()
    {
        var moi = ControlInertia.TravelSeconds(double.NaN, 2450, 0.35, AircraftClass.LightGa);
        var weight = ControlInertia.TravelSeconds(0, 2450, 0.35, AircraftClass.LightGa);
        Assert.Equal(weight, moi, 2);
    }

    [Fact]
    public void Output_cannot_stick_outside_range()
    {
        var state = new AxisDynamicsState();
        var sample = ControlInertia.Step(state, double.PositiveInfinity, 0.016, 0.35, 1, 1, 1);
        Assert.InRange(sample.Output, -1, 1);
    }

    [Fact]
    public void Release_returns_toward_center()
    {
        var state = new AxisDynamicsState();
        for (var i = 0; i < 40; i++)
            ControlInertia.Step(state, 1, 0.016, 0.4, 1, 1, 1);
        var held = state.Position;
        for (var i = 0; i < 20; i++)
            ControlInertia.Step(state, 0, 0.016, 0.4, 1, 1.4, 1);
        Assert.True(Math.Abs(state.Position) < Math.Abs(held));
    }

    private static double Run(double input)
    {
        var state = new AxisDynamicsState();
        var sample = new AxisSample();
        for (var i = 0; i < 6; i++)
            sample = ControlInertia.Step(state, input, 0.016, 0.8, 1, 1, 1);
        return Math.Abs(sample.Output);
    }
}
