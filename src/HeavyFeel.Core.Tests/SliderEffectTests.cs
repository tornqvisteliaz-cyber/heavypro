using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

/// <summary>Every slider must change the feel by an amount a pilot notices.</summary>
public class SliderEffectTests
{
    // Seconds for the shaped pitch to reach 90 % of a full pull.
    private static double PitchTime(AppSettings s, string title = "Boeing 737-800", AircraftClass cls = AircraftClass.NarrowBody, bool ground = false)
        => Time(s, title, cls, ground, f => f.FinalElevator, pitch: 1);

    private static double RollTime(AppSettings s) =>
        Time(s, "Boeing 737-800", AircraftClass.NarrowBody, false, f => f.FinalAileron, roll: 1);

    private static double Time(AppSettings s, string title, AircraftClass cls, bool ground, Func<InputDynamicsFrame, double> axis, double pitch = 0, double roll = 0)
    {
        var engine = new InputDynamicsEngine();
        var snap = new FlightSnapshot
        {
            Aircraft = new AircraftIdentity { Title = title },
            Class = cls,
            AirspeedIndicatedKnots = ground ? 10 : 250,
            OnGround = ground,
            TotalWeightPounds = 150000,
            CapturedPitch = pitch,
            CapturedRoll = roll
        };
        s.InputDeadzone = 0;
        s.TurbulenceResponse = 0;
        for (var t = 0.0; t < 10; t += 1.0 / 60)
            if (axis(engine.Step(s, snap, 1.0 / 60, 1.0)) >= 0.9)
                return t;
        return 10;
    }

    [Fact]
    public void Inertia_spans_light_to_very_heavy()
    {
        var light = PitchTime(new AppSettings { Inertia = 0 });
        var heavy = PitchTime(new AppSettings { Inertia = 100 });
        Assert.True(heavy > light * 4, $"light {light:0.00}s heavy {heavy:0.00}s");
    }

    [Fact]
    public void Response_makes_controls_quicker()
    {
        var slow = PitchTime(new AppSettings { ControlResponse = 0 });
        var quick = PitchTime(new AppSettings { ControlResponse = 100 });
        Assert.True(slow > quick * 2, $"slow {slow:0.00}s quick {quick:0.00}s");
    }

    [Fact]
    public void Pitch_slider_changes_pitch_only()
    {
        var pitchLight = PitchTime(new AppSettings { PitchDamping = 0 });
        var pitchHeavy = PitchTime(new AppSettings { PitchDamping = 100 });
        Assert.True(pitchHeavy > pitchLight * 2, $"pitch {pitchLight:0.00}s vs {pitchHeavy:0.00}s");
        Assert.Equal(RollTime(new AppSettings { PitchDamping = 0 }), RollTime(new AppSettings { PitchDamping = 100 }), 3);
    }

    [Fact]
    public void Ground_slider_works_on_the_ground()
    {
        var light = PitchTime(new AppSettings { GroundInertia = 0 }, ground: true);
        var heavy = PitchTime(new AppSettings { GroundInertia = 100 }, ground: true);
        Assert.True(heavy > light * 1.8, $"light {light:0.00}s heavy {heavy:0.00}s");
    }

    [Fact]
    public void Airbus_is_clearly_shaped_on_realistic()
    {
        var realistic = FeelPresets.Realistic;
        var s = new AppSettings
        {
            Inertia = realistic.Inertia, PitchDamping = realistic.PitchDamping, RollDamping = realistic.RollDamping,
            YawDamping = realistic.YawDamping, ControlResponse = realistic.ControlResponse, GroundInertia = realistic.GroundInertia
        };
        var a350 = PitchTime(s, "Airbus A350-1000", AircraftClass.WideBody);
        Assert.True(a350 > 0.6, $"A350 Realistic only takes {a350:0.00}s");
        Assert.True(a350 < PitchTime(s, "Boeing 777-300ER", AircraftClass.WideBody), "sidestick should stay lighter than a Boeing yoke");
    }
}
