using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class Real1Tests
{
    private static FlightSnapshot Snap(string title, string model, AircraftClass cls, double lb, double kt, double stickY = 0) => new()
    {
        Aircraft = new AircraftIdentity { Title = title, AtcModel = model },
        Class = cls,
        TotalWeightPounds = lb,
        AirspeedIndicatedKnots = kt,
        AltitudeAglFeet = 5000,
        YokeY = stickY
    };

    private static readonly AppSettings Real1Route = new() { InputRoute = Real1.RouteName };

    [Theory]
    [InlineData("777-300ER", "ATCCOM.AC_MODEL B77W.0.text", AircraftClass.WideBody, "Boeing 777")]
    [InlineData("A350-1000 (No Cabin)", "A350-1000", AircraftClass.WideBody, "Airbus widebody")]
    [InlineData("iFly 737-MAX8", "ATCCOM.AC_MODEL B738.0.text", AircraftClass.NarrowBody, "Boeing 737")]
    [InlineData("PMDG 737-800", "B738", AircraftClass.NarrowBody, "Boeing 737")]
    [InlineData("iniBuilds A320neo", "A20N", AircraftClass.NarrowBody, "Airbus A320")]
    [InlineData("Fenix A320 CFM", "A320", AircraftClass.NarrowBody, "Fenix")]
    [InlineData("Cessna C152", "ATCCOM.AC_MODEL C152.0.text", AircraftClass.LightGa, "Light aircraft")]
    [InlineData("C172SP G1000 Cargo", "ATCCOM.AC_MODEL C172.0.text", AircraftClass.LightGa, "Light aircraft")]
    public void Each_aircraft_gets_its_family(string title, string model, AircraftClass cls, string family)
    {
        Assert.StartsWith(family, Real1.For(Snap(title, model, cls, 200_000, 250)).Family);
    }

    [Fact]
    public void Families_are_ordered_from_light_to_heavy()
    {
        double Pitch(string t, string m, AircraftClass c) => Real1.For(Snap(t, m, c, 0, 0)).PitchSeconds;
        var ga = Pitch("Cessna C152", "C152", AircraftClass.LightGa);
        var fenix = Pitch("Fenix A320", "A320", AircraftClass.NarrowBody);
        var a320 = Pitch("iniBuilds A320neo", "A20N", AircraftClass.NarrowBody);
        var a350 = Pitch("A350-1000", "A35K", AircraftClass.WideBody);
        var b737 = Pitch("737-800", "B738", AircraftClass.NarrowBody);
        var b777 = Pitch("777-300ER", "B77W", AircraftClass.WideBody);
        Assert.True(fenix < a320, "Fenix models its own FBW, so REAL1 adds less");
        Assert.True(ga < a320 && a320 < a350 && a350 < b737 && b737 < b777);
    }

    [Fact]
    public void Fly_by_wire_ignores_airspeed_but_boeing_columns_firm_up()
    {
        var feel = ControlFeel.For(Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 280));
        var b777 = Real1.For(Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 280));
        var a350 = Real1.For(Snap("A350-1000", "A35K", AircraftClass.WideBody, 500_000, 280));
        Assert.Equal(1.0, Real1.AirspeedScale(a350, Snap("A350-1000", "A35K", AircraftClass.WideBody, 500_000, 280), feel));
        Assert.True(Real1.AirspeedScale(b777, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 280), feel)
                  > Real1.AirspeedScale(b777, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 20), feel));
    }

    [Fact]
    public void Full_deflection_arrives_just_before_the_stop_and_centre_has_a_deadzone()
    {
        var b777 = Real1.For(Snap("777-300ER", "B77W", AircraftClass.WideBody, 0, 0));
        Assert.Equal(1.0, Real1.Shape(0.98, b777), 6);
        Assert.Equal(0.0, Real1.Shape(0.015, b777), 6);
        Assert.True(Real1.Shape(0.3, b777) < 0.3, "soft centre on the 777");
        var a320 = Real1.For(Snap("iniBuilds A320neo", "A20N", AircraftClass.NarrowBody, 0, 0));
        // A gentler soft centre than the 777, like the A32NX realism mods: progressive small corrections.
        Assert.InRange(Real1.Shape(0.5, a320), Real1.Shape(0.5, b777), 0.5);
    }

    [Fact]
    public void Real1_ignores_the_feel_sliders()
    {
        static double Seconds(AppSettings s)
        {
            var engine = new InputDynamicsEngine();
            engine.Step(s, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 250), 1.0 / 60, 1.4);
            return engine.Step(s, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 250, 0.6), 1.0 / 60, 1.4).PitchSeconds;
        }
        var light = new AppSettings { InputRoute = Real1.RouteName, Inertia = 0, PitchDamping = 0 };
        var heavy = new AppSettings { InputRoute = Real1.RouteName, Inertia = 100, PitchDamping = 100 };
        Assert.Equal(Seconds(light), Seconds(heavy), 6);
        Assert.NotEqual(Seconds(new AppSettings { Inertia = 100, PitchDamping = 100 }), Seconds(heavy), 3);
    }

    [Fact]
    public void Real1_is_clearly_heavy_on_the_777_like_realistic()
    {
        // The 0.6.0 beta gave the 777 about 1 s of travel and felt like nothing changed.
        var engine = new InputDynamicsEngine();
        var settings = new AppSettings { InputRoute = Real1.RouteName };
        engine.Step(settings, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 250), 1.0 / 60, 1.4);
        InputDynamicsFrame frame = null!;
        for (var i = 0; i < 60; i++)
            frame = engine.Step(settings, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 250, 1.0), 1.0 / 60, 1.4);
        Assert.InRange(frame.FinalElevator, 0.05, 0.40);                     // one second in: still well short of full
        for (var i = 0; i < 600; i++)
            frame = engine.Step(settings, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 250, 1.0), 1.0 / 60, 1.4);
        Assert.Equal(1.0, frame.FinalElevator, 2);                          // full authority is kept
    }

    [Fact]
    public void Axis_has_mass_its_rate_builds_up_and_settles_without_a_bounce()
    {
        var state = new AxisDynamicsState();
        var first = Real1.StepAxis(state, 0.5, 1.0 / 60, 3.0, 0.7, 0, 1);
        Assert.True(first.Velocity < 0.05, "rate starts slowly: the axis has inertia");
        double peak = 0;
        for (var i = 0; i < 900; i++)
            peak = Math.Max(peak, Real1.StepAxis(state, 0.5, 1.0 / 60, 3.0, 0.7, 0, 1).Output);
        Assert.InRange(peak, 0.5, 0.52);                                     // a touch of carry, no bounce
        Assert.Equal(0.5, state.Position, 3);
    }

    [Fact]
    public void Feel_level_buttons_scale_real1()
    {
        static double Travel(FeelLevel level)
        {
            var p = FeelPresets.For(level);
            var s = new AppSettings
            {
                InputRoute = Real1.RouteName, Inertia = p.Inertia, PitchDamping = p.PitchDamping, RollDamping = p.RollDamping,
                YawDamping = p.YawDamping, ControlResponse = p.ControlResponse, GroundInertia = p.GroundInertia,
                TurbulenceResponse = p.TurbulenceResponse
            };
            return new InputDynamicsEngine().Step(s, Snap("777-300ER", "B77W", AircraftClass.WideBody, 500_000, 250, 0.5), 1.0 / 60, 1.4).PitchSeconds;
        }
        Assert.True(Travel(FeelLevel.Normal) < Travel(FeelLevel.Medium));
        Assert.True(Travel(FeelLevel.Medium) < Travel(FeelLevel.Realistic));
    }

    [Theory]
    [InlineData("777-300ER", "ATCCOM.AC_MODEL B77W.0.text")]
    [InlineData("777F", "ATCCOM.AC_MODEL B77L.0.text")]
    public void The_777_gets_the_ff777_feel_by_itself(string title, string model)
    {
        var frame = new InputDynamicsEngine().Step(Real1Route, Snap(title, model, AircraftClass.WideBody, 500_000, 250, 0.4), 1.0 / 60, 1.4);
        Assert.Contains("FF777", frame.Feel);
    }

    [Fact]
    public void The_777_wheel_is_calm_around_centre_but_keeps_full_authority()
    {
        var b777 = Real1.For(Snap("777-300ER", "B77W", AircraftClass.WideBody, 0, 0));
        Assert.Equal(0.0, Real1.ShapeRoll(0.05, b777), 6);                 // a resting hand never rolls it
        Assert.True(Real1.ShapeRoll(0.2, b777) < Real1.Shape(0.2, b777), "wheel calmer than column near centre");
        Assert.Equal(1.0, Real1.ShapeRoll(0.98, b777), 6);
        var a320 = Real1.For(Snap("iniBuilds A320neo", "A20N", AircraftClass.NarrowBody, 0, 0));
        Assert.Equal(Real1.Shape(0.3, a320), Real1.ShapeRoll(0.3, a320), 9); // families without their own wheel centre shape roll like pitch
    }

    [Fact]
    public void Real1_frame_names_the_route_and_family()
    {
        var frame = new InputDynamicsEngine().Step(Real1Route, Snap("A350-1000", "A35K", AircraftClass.WideBody, 500_000, 250, 0.4), 1.0 / 60, 1.4);
        Assert.Equal("REAL1", frame.Profile);
        Assert.StartsWith("Airbus widebody", frame.Feel);
    }
}
