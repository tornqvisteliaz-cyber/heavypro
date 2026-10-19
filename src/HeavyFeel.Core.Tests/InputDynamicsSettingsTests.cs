using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class InputDynamicsSettingsTests
{
    [Fact]
    public void Captured_stick_wins_over_reported_yoke()
    {
        // Reported simvars reflect HeavyPro's own lagging output once the axis is captured.
        var snap = new FlightSnapshot { CapturedPitch = 0, YokeY = 0.8, YokeYIndicator = 0.8, CapturedYaw = -0.4, RudderPedal = 0.3 };
        Assert.Equal(0, snap.StickY);
        Assert.Equal(-0.4, snap.RudderInput);
    }

    [Fact]
    public void Uncaptured_axis_falls_back_to_reported_values()
    {
        var snap = new FlightSnapshot { YokeY = 0.5, RudderPedal = 0.3 };
        Assert.Equal(0.5, snap.StickY);
        Assert.Equal(0.3, snap.RudderInput);
    }

    [Fact]
    public void Released_captured_stick_returns_to_centre()
    {
        var engine = new InputDynamicsEngine();
        var settings = new AppSettings();
        for (var i = 0; i < 200; i++)
            engine.Step(settings, Snap(pitch: 1), 0.016, 1);
        var frame = engine.Step(settings, Snap(pitch: 1), 0.016, 1);
        Assert.True(frame.FinalElevator > 0.95);

        for (var i = 0; i < 400; i++)
            frame = engine.Step(settings, Snap(pitch: 0, reported: 0.9), 0.016, 1);
        Assert.Equal(0, frame.FinalElevator, 3);
    }

    [Fact]
    public void Captured_rudder_is_shaped()
    {
        var engine = new InputDynamicsEngine();
        InputDynamicsFrame frame = new();
        for (var i = 0; i < 400; i++)
            frame = engine.Step(new AppSettings { InputDeadzone = 0 }, Snap(yaw: 0.6), 0.016, 1);
        Assert.Equal(0.6, frame.FinalRudder, 2);
    }

    [Fact]
    public void Full_deflection_is_always_reached()
    {
        foreach (var curve in new[] { "Linear", "Expo", "S-Curve" })
        foreach (var sensitivity in new[] { 0.25, 1.0, 2.0 })
        {
            var settings = new AppSettings { InputCurve = curve, InputSensitivity = sensitivity, ExpoStrength = 1, InputDeadzone = 0.1 };
            Assert.Equal(1, Shape(settings, 1), 6);
            Assert.Equal(-1, Shape(settings, -1), 6);
            Assert.True(Shape(settings, 0.999) > 0.98, $"{curve} {sensitivity}");
        }
    }

    [Fact]
    public void Deadzone_curve_and_sensitivity_change_the_target()
    {
        Assert.Equal(0, Shape(new AppSettings { InputDeadzone = 0.1 }, 0.08));
        var linear = Shape(new AppSettings { InputDeadzone = 0 }, 0.5);
        var expo = Shape(new AppSettings { InputDeadzone = 0, InputCurve = "Expo", ExpoStrength = 0.8 }, 0.5);
        var sensitive = Shape(new AppSettings { InputDeadzone = 0, InputSensitivity = 2 }, 0.5);
        var dull = Shape(new AppSettings { InputDeadzone = 0, InputSensitivity = 0.5 }, 0.5);
        Assert.Equal(0.5, linear, 6);
        Assert.True(expo < linear);
        Assert.True(sensitive > linear);
        Assert.True(dull < linear);
    }

    [Fact]
    public void Response_slider_changes_speed()
    {
        Assert.True(TimeTo(new AppSettings { ControlResponse = 90 }) < TimeTo(new AppSettings { ControlResponse = 10 }));
    }

    [Fact]
    public void Rate_limit_changes_speed()
    {
        Assert.True(TimeTo(new AppSettings { PitchRateLimit = 4 }) < TimeTo(new AppSettings { PitchRateLimit = 0.5 }));
    }

    [Fact]
    public void Damping_and_acceleration_change_speed()
    {
        Assert.True(TimeTo(new AppSettings { PitchDamping = 0 }) < TimeTo(new AppSettings { PitchDamping = 100 }));
        Assert.True(TimeTo(new AppSettings { InputAccelerationScale = 2 }) < TimeTo(new AppSettings { InputAccelerationScale = 0.25 }));
    }

    [Fact]
    public void Airspeed_response_slows_controls_at_high_speed()
    {
        var off = TimeTo(new AppSettings { AirspeedResponseEnabled = false }, ias: 300);
        var on = TimeTo(new AppSettings { AirspeedResponseEnabled = true, AirspeedResponse = 1 }, ias: 300);
        Assert.True(on > off);
    }

    [Fact]
    public void Delayed_release_holds_before_returning()
    {
        var delayed = PositionAfterRelease("Delayed", 0.2);
        var immediate = PositionAfterRelease("Immediate", 0.2);
        Assert.True(delayed > immediate);
        Assert.True(delayed > 0.9);
    }

    private static double PositionAfterRelease(string mode, double seconds)
    {
        var engine = new InputDynamicsEngine();
        var settings = new AppSettings { StickReleaseMode = mode, ReturnDelayMs = 400 };
        for (var i = 0; i < 300; i++)
            engine.Step(settings, Snap(pitch: 1), 0.016, 1);
        InputDynamicsFrame frame = new();
        for (var t = 0.0; t < seconds; t += 0.016)
            frame = engine.Step(settings, Snap(pitch: 0), 0.016, 1);
        return frame.FinalElevator;
    }

    private static double Shape(AppSettings s, double raw) =>
        InputDynamicsEngine.Shape(raw, s.InputDeadzone, s.InputSensitivity, s.ExpoStrength,
            s.InputCurve == "S-Curve" ? InputCurve.SCurve : (InputCurve)Enum.Parse(typeof(InputCurve), s.InputCurve));

    private static double TimeTo(AppSettings settings, double ias = 0)
    {
        var engine = new InputDynamicsEngine();
        var t = 0.0;
        while (t < 20)
        {
            var frame = engine.Step(settings, Snap(pitch: 1, ias: ias), 0.016, 1);
            t += 0.016;
            if (frame.FinalElevator >= 0.95)
                return t;
        }
        return t;
    }

    private static FlightSnapshot Snap(double pitch = 0, double yaw = 0, double reported = 0, double ias = 0) => new()
    {
        CapturedPitch = pitch,
        CapturedRoll = 0,
        CapturedYaw = yaw,
        YokeY = reported,
        YokeYIndicator = reported,
        AirspeedIndicatedKnots = ias,
        OnGround = ias < 1,
        TotalWeightPounds = 140000,
        Class = AircraftClass.NarrowBody
    };
}
