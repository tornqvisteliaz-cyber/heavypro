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
    public void Higher_live_weight_slows_controls_even_when_moi_is_available()
    {
        var light = ControlInertia.TravelSeconds(1_200_000, 110_000, 1.0, AircraftClass.NarrowBody);
        var heavy = ControlInertia.TravelSeconds(1_200_000, 170_000, 1.0, AircraftClass.NarrowBody);

        Assert.True(heavy > light);
        Assert.True(heavy / light > 1.15);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.4)]
    [InlineData(2.0)]
    public void Scale_changes_time_to_target(double scale)
    {
        var slower = TimeTo(0.95, scale);
        var baseline = TimeTo(0.95, 1.0);
        if (scale > 1)
            Assert.True(slower > baseline);
        else
            Assert.Equal(baseline, slower, 3);
    }

    [Fact]
    public void Scales_are_ordered()
    {
        var a = TimeTo(0.95, 1.0);
        var b = TimeTo(0.95, 1.4);
        var c = TimeTo(0.95, 2.0);
        Assert.True(a < b && b < c);
    }

    [Fact]
    public void Reversal_does_not_overshoot()
    {
        var engine = new InputDynamicsEngine();
        var settings = new AppSettings { Inertia = 50 };
        var snap = new FlightSnapshot { YokeY = 1, TotalWeightPounds = 2450, Class = AircraftClass.LightGa };
        for (var i = 0; i < 40; i++)
            engine.Step(settings, snap, 0.016, 1.4);
        var reversed = new FlightSnapshot { YokeY = -1, TotalWeightPounds = 2450, Class = AircraftClass.LightGa };
        var frame = engine.Step(settings, reversed, 0.016, 1.4);
        Assert.InRange(frame.FinalElevator, -1, 1);
        Assert.True(frame.FinalElevator > -1);
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
        for (var i = 0; i < 8; i++)
            sample = ControlInertia.Step(state, input, 0.016, 0.8, 1, 1, 1);
        return Math.Abs(sample.Output);
    }

    private static double TimeTo(double target, double scale)
    {
        var engine = new InputDynamicsEngine();
        var settings = new AppSettings { Inertia = 50 };
        var snap = new FlightSnapshot { YokeY = 1, TotalWeightPounds = 2450, Class = AircraftClass.LightGa };
        var t = 0.0;
        while (t < 12)
        {
            var frame = engine.Step(settings, snap, 0.016, scale);
            t += 0.016;
            if (frame.FinalElevator >= target)
                return t;
        }
        return t;
    }

    [Fact]
    public void Follows_slow_moving_stick_without_twitching()
    {
        var state = new AxisDynamicsState();
        var lastVelocity = 0.0;
        for (var frame = 0; frame < 120; frame++)
        {
            var stick = Math.Min(0.6, frame * 0.6 / 90.0);
            var sample = ControlInertia.Step(state, stick, 1.0 / 30.0, 0.8, 1, 1, 1);
            Assert.True(sample.Velocity >= -1e-9, $"velocity reversed at frame {frame}");
            Assert.True(sample.Output <= stick + 1e-9, $"overshoot at frame {frame}");
            lastVelocity = sample.Velocity;
        }
        Assert.Equal(0.6, state.Position, 3);
        Assert.Equal(0, lastVelocity, 3);
    }

    [Fact]
    public void Response_is_frame_rate_independent()
    {
        double Run(double fps)
        {
            var state = new AxisDynamicsState();
            var frames = (int)Math.Round(0.4 * fps);
            for (var i = 0; i < frames; i++)
                ControlInertia.Step(state, 0.8, 1.0 / fps, 0.8, 1, 1, 1);
            return state.Position;
        }
        Assert.True(Math.Abs(Run(30) - Run(144)) < 0.01);
    }
}
