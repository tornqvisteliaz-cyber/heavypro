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
        Assert.Equal(0.5, Real1.Shape(0.5, a320), 1);                                  // close to linear, like Fenix/iniBuilds recommend
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
    public void Real1_frame_names_the_route_and_family()
    {
        var frame = new InputDynamicsEngine().Step(Real1Route, Snap("A350-1000", "A35K", AircraftClass.WideBody, 500_000, 250, 0.4), 1.0 / 60, 1.4);
        Assert.Equal("REAL1", frame.Profile);
        Assert.StartsWith("Airbus widebody", frame.Feel);
    }
}
