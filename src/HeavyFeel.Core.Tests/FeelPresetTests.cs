using HeavyFeel.Core.Models;
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
