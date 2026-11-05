using HeavyPro.Core.Services;
using Xunit;

namespace HeavyPro.Core.Tests;

public class FlightPlanTests
{
    // What MSFS 2020/2024 write when saving a flight plan.
    private const string Pln = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<SimBase.Document Type=""AceXML"" version=""1,0"">
    <Descr>AceXML Document</Descr>
    <FlightPlan.FlightPlan>
        <Title>EGLL to KJFK</Title>
        <FPType>IFR</FPType>
        <RouteType>HighAlt</RouteType>
        <CruisingAlt>35000</CruisingAlt>
        <DepartureID>EGLL</DepartureID>
        <DestinationID>KJFK</DestinationID>
        <ATCWaypoint id=""EGLL""><ATCWaypointType>Airport</ATCWaypointType></ATCWaypoint>
        <ATCWaypoint id=""CPT""><ATCWaypointType>VOR</ATCWaypointType></ATCWaypoint>
        <ATCWaypoint id=""STRUM""><ATCWaypointType>Intersection</ATCWaypointType></ATCWaypoint>
        <ATCWaypoint id=""KJFK""><ATCWaypointType>Airport</ATCWaypointType></ATCWaypoint>
    </FlightPlan.FlightPlan>
</SimBase.Document>";

    // The Airbus route format (Aerosoft, Fenix, iniBuilds).
    private const string Flp = "[CoRte]\r\nArptDep=LFPG\r\nArptArr=EDDM\r\nRwyDep=LFPG27L\r\nDctWpt1=ATREX\r\nDctWpt1Coordinates=49.25,2.38\r\nAirway2=UN869\r\nAirway2FROM=ATREX\r\nAirway2TO=BALSI\r\nDctWpt3=TOSTU\r\n";

    [Fact]
    public void Reads_an_msfs_pln()
    {
        var r = FlightPlans.Parse(Pln, "EGLLKJFK.pln");
        Assert.True(r.Ok, r.Message);
        var p = r.Plan!;
        Assert.Equal("MSFS flight plan", p.Format);
        Assert.Equal("EGLL", p.Departure);
        Assert.Equal("KJFK", p.Arrival);
        Assert.Equal(35000, p.CruiseAltitudeFeet);
        Assert.Equal(new[] { "CPT", "STRUM" }, p.Waypoints);
        Assert.Equal("EGLLKJFK.pln", p.SourceName);
    }

    [Fact]
    public void Reads_an_airbus_flp()
    {
        var r = FlightPlans.Parse(Flp, "LFPGEDDM.flp");
        Assert.True(r.Ok, r.Message);
        var p = r.Plan!;
        Assert.Equal("Airbus FLP", p.Format);
        Assert.Equal("LFPG", p.Departure);
        Assert.Equal("EDDM", p.Arrival);
        Assert.Equal("ATREX UN869 BALSI TOSTU", p.RouteText);
    }

    [Fact]
    public void Reads_a_saved_simbrief_ofp_with_its_weights()
    {
        var r = FlightPlans.Parse(SimBriefTests.FullSample, "ofp.json");
        Assert.True(r.Ok, r.Message);
        Assert.Equal("SimBrief OFP", r.Plan!.Format);
        Assert.Equal("EGLL", r.Plan.Departure);
        Assert.Equal(37000, r.Plan.CruiseAltitudeFeet);
        Assert.NotNull(r.Plan.Ofp);
        Assert.Equal(642000, r.Plan.Ofp!.TakeoffWeight);
    }

    [Theory]
    [InlineData("", "empty.pln", "empty")]
    [InlineData("   \r\n  ", "blank.flp", "empty")]
    [InlineData("hello, not a plan", "notes.txt", "does not recognise")]
    [InlineData("<SimBase.Document><FlightPlan.FlightPlan><Title>x", "broken.pln", "not valid XML")]
    [InlineData("<SimBase.Document><FlightPlan.FlightPlan></FlightPlan.FlightPlan></SimBase.Document>", "nothing.pln", "no airports")]
    [InlineData("ArptDep=EGLL\r\nArptArr=KJFK", "nosection.flp", "[CoRte]")]
    [InlineData("{ \"weights\": 5 ", "cut.json", "")]
    [InlineData("<html><body>login</body></html>", "page.xml", "")]
    [InlineData("{ \"fetch\": { \"status\": \"Error: Unknown UserID\" } }", "error.json", "SimBrief")]
    public void A_bad_file_is_a_message_not_a_crash(string text, string name, string expected)
    {
        var r = FlightPlans.Parse(text, name);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Message));
        Assert.Contains(expected, r.Message);
    }

    [Fact]
    public void A_missing_file_is_a_message_not_a_crash()
    {
        var r = FlightPlans.Load(Path.Combine(Path.GetTempPath(), "no-such-plan-" + Guid.NewGuid() + ".pln"));
        Assert.False(r.Ok);
        Assert.Contains("no longer exists", r.Message);
    }

    [Fact]
    public void Importing_again_replaces_the_kept_plan_and_remove_forgets_it()
    {
        var data = Path.Combine(Path.GetTempPath(), "heavypro-plan-" + Guid.NewGuid());
        var a = Path.Combine(data, "a.pln");
        var b = Path.Combine(data, "b.flp");
        Directory.CreateDirectory(data);
        try
        {
            File.WriteAllText(a, Pln);
            File.WriteAllText(b, Flp);
            var first = FlightPlans.Keep(data, a);
            Assert.True(FlightPlans.Load(first).Ok);
            var second = FlightPlans.Keep(data, b);
            Assert.False(File.Exists(first) && first != second, "the old copy is gone");
            Assert.Equal("LFPG", FlightPlans.Load(second).Plan!.Departure);
            FlightPlans.Forget(data);
            Assert.False(File.Exists(second));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }
}
