using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class ControlFeelTests
{
    private static FlightSnapshot Snap(string title, AircraftClass cls, double ias, double q = 0) => new()
    {
        Aircraft = new AircraftIdentity { Title = title },
        Class = cls,
        AirspeedIndicatedKnots = ias,
        DynamicPressurePsf = q
    };

    [Fact]
    public void Airbus_sidestick_has_no_airspeed_feel_and_short_travel()
    {
        var feel = ControlFeel.For(Snap("Airbus A320neo", AircraftClass.NarrowBody, 280));
        Assert.Equal(FeelSystem.Sidestick, feel.System);
        Assert.Equal(1.0, feel.SpeedScale(new AppSettings(), Snap("Airbus A320neo", AircraftClass.NarrowBody, 280)));
        Assert.True(feel.BaseScale < 1.0);
    }

    [Fact]
    public void A300_has_a_conventional_yoke()
    {
        Assert.Equal(FeelSystem.ArtificialFeel, ControlFeel.For(Snap("iniBuilds A300-600R", AircraftClass.WideBody, 200)).System);
    }

    [Fact]
    public void Boeing_yoke_gets_heavier_with_speed()
    {
        var settings = new AppSettings();
        var feel = ControlFeel.For(Snap("Boeing 787-10", AircraftClass.WideBody, 0));
        Assert.Equal(FeelSystem.ArtificialFeel, feel.System);
        var taxi = feel.SpeedScale(settings, Snap("Boeing 787-10", AircraftClass.WideBody, 20));
        var cruise = feel.SpeedScale(settings, Snap("Boeing 787-10", AircraftClass.WideBody, 290));
        Assert.True(taxi < 1.0, $"taxi {taxi}");
        Assert.True(cruise > 1.15, $"cruise {cruise}");
        Assert.True(feel.MinTravelSeconds >= 0.25);
    }

    [Fact]
    public void Dynamic_pressure_from_the_sim_wins_over_airspeed()
    {
        Assert.Equal(150, ControlFeel.DynamicPressure(Snap("x", AircraftClass.LightGa, 50, q: 150)));
        Assert.InRange(ControlFeel.DynamicPressure(Snap("x", AircraftClass.LightGa, 100)), 33, 35);
    }

    [Fact]
    public void Rudder_throw_shrinks_with_speed_on_default_jets_only()
    {
        var settings = new AppSettings();
        var generic = ControlFeel.For(Snap("Boeing 747-8", AircraftClass.HeavyWide, 300));
        Assert.Equal(1.0, generic.RudderAuthority(settings, 140));
        Assert.InRange(generic.RudderAuthority(settings, 300), 0.2, 0.35);

        var pmdg = ControlFeel.For(Snap("PMDG 777-300ER", AircraftClass.WideBody, 300));
        Assert.Equal(1.0, pmdg.RudderAuthority(settings, 300));

        // The BlueBird 757 models its own rudder ratio changer.
        var b757 = ControlFeel.For(Snap("BlueBird 757-200 PW", AircraftClass.NarrowBody, 300));
        Assert.Equal(1.0, b757.RudderAuthority(settings, 300));

        var ga = ControlFeel.For(Snap("Cessna 172", AircraftClass.LightGa, 140));
        Assert.Equal(1.0, ga.RudderAuthority(settings, 140));

        Assert.Equal(1.0, generic.RudderAuthority(new AppSettings { RudderLimiter = false }, 300));
    }

    [Fact]
    public void Engine_applies_the_rudder_limit_to_its_target()
    {
        var engine = new InputDynamicsEngine();
        var snap = new FlightSnapshot
        {
            Aircraft = new AircraftIdentity { Title = "Boeing 747-8" },
            Class = AircraftClass.HeavyWide,
            AirspeedIndicatedKnots = 320,
            CapturedYaw = 1.0
        };
        var frame = engine.Step(new AppSettings { InputDeadzone = 0 }, snap, 1.0 / 60);
        Assert.InRange(frame.FilteredRudder, 0.19, 0.21);
    }
}
