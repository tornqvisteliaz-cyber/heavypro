using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class FeelPresetTests
{
    [Fact]
    public void Realistic_is_heavier_than_normal()
    {
        Assert.True(FeelPresets.Realistic.Inertia > FeelPresets.Medium.Inertia);
        Assert.True(FeelPresets.Medium.Inertia > FeelPresets.Normal.Inertia);
        Assert.True(FeelPresets.Realistic.ControlResponse < FeelPresets.Normal.ControlResponse);
    }

    [Fact]
    public void Match_detects_medium_defaults()
    {
        var settings = new AppSettings();
        FeelPresets.For(FeelLevel.Medium);
        settings.Inertia = FeelPresets.Medium.Inertia;
        settings.PitchDamping = FeelPresets.Medium.PitchDamping;
        settings.RollDamping = FeelPresets.Medium.RollDamping;
        settings.YawDamping = FeelPresets.Medium.YawDamping;
        settings.ControlResponse = FeelPresets.Medium.ControlResponse;
        settings.GroundInertia = FeelPresets.Medium.GroundInertia;
        settings.TurbulenceResponse = FeelPresets.Medium.TurbulenceResponse;
        Assert.Equal(FeelLevel.Medium, FeelPresets.Match(settings));
    }
}

public class RealisticResponseTests
{
    // Seconds until the shaped stick reaches 90 % of a 60 % input, light aircraft at 100 kt.
    private static double Answer(FeelPreset p)
    {
        var s = new AppSettings { Inertia = p.Inertia, PitchDamping = p.PitchDamping, RollDamping = p.RollDamping, YawDamping = p.YawDamping, ControlResponse = p.ControlResponse, GroundInertia = p.GroundInertia, TurbulenceResponse = 0 };
        var engine = new InputDynamicsEngine();
        FlightSnapshot Snap(double y) => new() { Aircraft = new AircraftIdentity { Title = "Cessna C152" }, Class = AircraftClass.LightGa, TotalWeightPounds = 1600, AirspeedIndicatedKnots = 100, AltitudeAglFeet = 3000, YokeY = y };
        engine.Step(s, Snap(0), 1.0 / 60, 1.4);
        for (var i = 1; i <= 600; i++)
            if (engine.Step(s, Snap(0.6), 1.0 / 60, 1.4).FinalElevator >= 0.54)
                return i / 60.0;
        return 10;
    }

    [Fact]
    public void Realistic_answers_sooner_than_before_but_stays_heavier_than_medium()
    {
        var realistic = Answer(FeelPresets.Realistic);
        Assert.True(realistic < Answer(FeelPresets.RealisticBefore052) * 0.75, $"Realistic {realistic:0.00}s");
        Assert.True(realistic > Answer(FeelPresets.Medium) * 1.4, $"Realistic {realistic:0.00}s");
    }

    [Fact]
    public void Old_realistic_tuning_is_recognised_for_the_move_to_new_values()
    {
        var old = FeelPresets.RealisticBefore052;
        var s = new AppSettings { Inertia = old.Inertia, PitchDamping = old.PitchDamping, RollDamping = old.RollDamping, YawDamping = old.YawDamping, ControlResponse = old.ControlResponse, GroundInertia = old.GroundInertia, TurbulenceResponse = old.TurbulenceResponse };
        Assert.True(FeelPresets.Matches(s, FeelPresets.RealisticBefore052));
        Assert.False(FeelPresets.Matches(s, FeelPresets.Realistic));
    }
}
