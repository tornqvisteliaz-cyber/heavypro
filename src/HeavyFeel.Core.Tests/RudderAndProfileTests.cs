using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class RudderAndProfileTests
{
    private static FlightSnapshot Airliner(double knots, double rudder = 0) => new()
    {
        Aircraft = new AircraftIdentity { Title = "Generic Airliner" },
        Class = AircraftClass.WideBody,
        AirspeedIndicatedKnots = knots,
        RudderPedal = rudder
    };

    [Fact]
    public void RealisticRudderIsOffByDefault()
    {
        var feel = ControlFeel.For(Airliner(250));
        Assert.Equal(1.0, feel.RudderFeelScale(new AppSettings(), Airliner(250)));
    }

    [Fact]
    public void RealisticRudderPedalsGetHeavierWithSpeed()
    {
        var settings = new AppSettings { RealisticRudder = true };
        var feel = ControlFeel.For(Airliner(250));
        var taxi = feel.RudderFeelScale(settings, Airliner(10));
        var cruise = feel.RudderFeelScale(settings, Airliner(250));
        Assert.InRange(taxi, 1.0, 1.1);
        Assert.InRange(cruise, 1.7, 1.9);
    }

    [Fact]
    public void RudderPedalCurveKeepsEndsAndSoftensCentre()
    {
        Assert.Equal(1.0, ControlFeel.RudderPedalCurve(1.0), 6);
        Assert.Equal(-1.0, ControlFeel.RudderPedalCurve(-1.0), 6);
        Assert.Equal(0.0, ControlFeel.RudderPedalCurve(0.0), 6);
        Assert.True(ControlFeel.RudderPedalCurve(0.2) < 0.2);
    }

    [Fact]
    public void RealisticRudderMakesPedalTravelSlowerInFlight()
    {
        static double Travel(bool realistic)
        {
            var engine = new InputDynamicsEngine();
            var settings = new AppSettings { RealisticRudder = realistic, RudderLimiter = false, Inertia = 20, YawDamping = 20, ControlResponse = 70 };
            engine.Step(settings, Airliner(250), 1.0 / 60, 1.0);
            return engine.Step(settings, Airliner(250, 1), 1.0 / 60, 1.0).YawSeconds;
        }
        Assert.True(Travel(true) > Travel(false));
    }

    [Theory]
    [InlineData("777F", "B77L", true)]
    [InlineData("PMDG 737-800", "B738", true)]
    [InlineData("iniBuilds A350-1000", "A35K", true)]
    [InlineData("C172SP G1000", "C172", false)]
    public void StudyLevelAircraftNeedTheDirectRoute(string title, string model, bool expected)
    {
        Assert.Equal(expected, new AircraftIdentity { Title = title, AtcModel = model }.NeedsDirectRoute);
    }

    [Theory]
    [InlineData("A350-1000 (No Cabin)", "A350-1000", "A35K", true)]
    [InlineData("A350-900 (Default Cabin)", "A350-900", "A359", true)]
    [InlineData("777F", "ATCCOM.AC_MODEL B77L.0.text", "BOEING", false)]
    [InlineData("Fenix A320", "A320", "A320", false)]
    public void A350IsRecognisedFromWhatMsfsReports(string title, string model, string type, bool expected)
    {
        Assert.Equal(expected, new AircraftIdentity { Title = title, AtcModel = model, AtcType = type }.IsA350);
    }

    [Theory]
    [InlineData("777-300ER", "ATCCOM.AC_MODEL B77W.0.text")]
    [InlineData("777F", "ATCCOM.AC_MODEL B77L.0.text")]
    public void Msfs2024_pmdg_777_gets_the_777_feel(string title, string model)
    {
        var snap = new FlightSnapshot { Aircraft = new AircraftIdentity { Title = title, AtcModel = model, AtcType = "ATCCOM.ATC_NAME BOEING.0.text" }, Class = AircraftClass.WideBody };
        Assert.Equal("PMDG 777", InputDynamicsProfile.For(snap, "Auto Detect").Name);
    }

    [Fact]
    public void BuiltInProfilesAreAddedOnceAndKeepUserProfiles()
    {
        var mine = new CustomProfile { Name = "Boeing 777 - Heavy twin", Inertia = 5 };
        var list = new List<CustomProfile> { mine };
        var added = BuiltInProfiles.AddMissing(list);
        Assert.Equal(BuiltInProfiles.All.Count - 1, added);
        Assert.Same(mine, list[0]);
        Assert.Equal(0, BuiltInProfiles.AddMissing(list));
        Assert.Equal(list.Count, list.Select(p => p.Name.ToLowerInvariant()).Distinct().Count());
    }
}
