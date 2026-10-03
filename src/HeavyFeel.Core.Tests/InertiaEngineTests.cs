using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class InertiaEngineTests
{
    [Fact]
    public void Master_off_does_not_write()
    {
        var engine = new InertiaEngine();
        var cmd = engine.Step(new AppSettings { Inertia = 45 }, FlyingSnap(), masterEnable: false);
        Assert.False(cmd.Active);
        Assert.Contains("Master off", cmd.Reason);
    }

    [Fact]
    public void Autopilot_pauses_writes()
    {
        var engine = new InertiaEngine();
        var snap = FlyingSnap(ap: true);
        var cmd = engine.Step(new AppSettings { MasterEnable = true }, snap, true);
        Assert.False(cmd.Active);
    }

    [Fact]
    public void Taxi_writes_axes_not_rates()
    {
        var engine = new InertiaEngine();
        var snap = FlyingSnap(onGround: true, yokeY: 0.2, ias: 12, agl: 0);
        var cmd = engine.Step(Medium(), snap, true);
        Assert.True(cmd.Active);
        Assert.True(cmd.WriteAxes);
        Assert.False(cmd.WriteRates);
        Assert.Contains("Ground", cmd.Reason);
    }

    [Fact]
    public void Takeoff_roll_writes_axes()
    {
        var engine = new InertiaEngine();
        var snap = FlyingSnap(onGround: true, yokeY: 0.6, ias: 140, agl: 0);
        var cmd = engine.Step(Medium(), snap, true);
        Assert.True(cmd.Active);
        Assert.True(cmd.WriteAxes);
        Assert.False(cmd.WriteRates);
        Assert.NotEqual(0, cmd.ElevatorAxis);
        Assert.Contains("Takeoff", cmd.Reason);
    }

    [Fact]
    public void Low_agl_still_writes()
    {
        var engine = new InertiaEngine();
        var snap = FlyingSnap(onGround: false, yokeY: 0.6, ias: 170, agl: 80);
        var cmd = engine.Step(Medium(), snap, true);
        Assert.True(cmd.Active);
        Assert.True(cmd.WriteAxes);
    }

    [Fact]
    public void Released_stick_does_not_write()
    {
        var engine = new InertiaEngine();
        var snap = FlyingSnap(yokeY: 0, ias: 250, agl: 8000);
        var cmd = engine.Step(Medium(), snap, true);
        Assert.False(cmd.Active);
        Assert.Contains("released", cmd.Reason);
    }

    [Fact]
    public void Cruise_handfly_writes_axes_not_rates()
    {
        var engine = new InertiaEngine();
        var snap = FlyingSnap(yokeY: 0.5, ias: 250, agl: 8000);
        var cmd = engine.Step(Medium(), snap, true);
        Assert.True(cmd.Active);
        Assert.False(cmd.WriteRates);
        Assert.True(cmd.WriteAxes);
        Assert.InRange(cmd.ElevatorAxis, -16384, 16384);
        Assert.NotEqual(0, cmd.ElevatorAxis);
    }

    [Fact]
    public void Heavier_jet_scales_above_one()
    {
        var heavy = FlyingSnap(weight: 170000);
        var light = FlyingSnap(weight: 110000);
        Assert.True(InertiaEngine.LiveMassScale(heavy) > InertiaEngine.LiveMassScale(light));
        Assert.InRange(InertiaEngine.LiveMassScale(heavy), 1.0, 1.35);
    }

    [Fact]
    public void Heavier_setting_reduces_authority()
    {
        var light = InertiaEngine.Authority(0.1, 0.8, 0.5, 0);
        var heavy = InertiaEngine.Authority(0.92, 0.18, 1.3, 0.8);
        Assert.True(light > 0.85);
        Assert.True(heavy < 0.55);
        Assert.True(light > heavy);
    }

    private static AppSettings Medium() => new()
    {
        Inertia = 56,
        PitchDamping = 50,
        RollDamping = 46,
        YawDamping = 42,
        ControlResponse = 40,
        GroundInertia = 50
    };

    private static FlightSnapshot FlyingSnap(
        bool ap = false,
        bool onGround = false,
        double yokeY = 0,
        double ias = 180,
        double agl = 3000,
        double weight = 140000) => new()
    {
        Utc = DateTime.UtcNow,
        OnGround = onGround,
        AirspeedIndicatedKnots = ias,
        AltitudeAglFeet = agl,
        RadioHeightFeet = agl,
        DynamicPressurePsf = onGround ? 8 : 80,
        TotalWeightPounds = weight,
        EmptyWeightPounds = 93000,
        MaxGrossWeightPounds = 174000,
        AutopilotMaster = ap,
        YokeY = yokeY
    };
}
