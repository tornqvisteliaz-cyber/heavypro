using HeavyPro.Core.Services;
using Xunit;

namespace HeavyPro.Core.Tests;

public class SimBriefTests
{
    // The shape SimBrief's fetcher returns (json=1): weights and fuel as strings in the OFP unit.
    private const string Sample = @"{
      ""params"": { ""units"": ""lbs"" },
      ""general"": { ""icao_airline"": ""BAW"", ""flight_number"": ""178"" },
      ""origin"": { ""icao_code"": ""EGLL"" },
      ""destination"": { ""icao_code"": ""KJFK"" },
      ""aircraft"": { ""icaocode"": ""B77W"", ""name"": ""Boeing 777-300ER"" },
      ""weights"": { ""oew"": ""370100"", ""pax_count"": ""264"", ""cargo"": ""18000"",
                     ""payload"": ""62000"", ""est_zfw"": ""432100"", ""est_tow"": ""642000"", ""est_ldw"": ""470000"" },
      ""fuel"": { ""plan_ramp"": ""212000"", ""reserve"": ""15000"", ""taxi"": ""2000"" }
    }";

    [Fact]
    public void Parses_the_weights_from_a_simbrief_plan()
    {
        var r = SimBrief.Parse(Sample);
        Assert.True(r.Ok);
        var o = r.Ofp!;
        Assert.Equal("EGLL", o.Origin);
        Assert.Equal("KJFK", o.Destination);
        Assert.Equal("EGLL → KJFK", o.Route);
        Assert.Equal("Boeing 777-300ER", o.Aircraft);
        Assert.Equal("BAW178", o.FlightNumber);
        Assert.Equal("lbs", o.Units);
        Assert.Equal(432100, o.ZeroFuelWeight);
        Assert.Equal(642000, o.TakeoffWeight);
        Assert.Equal(264, o.Passengers);
        Assert.Equal(212000, o.BlockFuel);
        Assert.Equal(15000, o.ReserveFuel);
    }

    [Fact]
    public void A_bad_username_comes_back_as_a_message_not_a_crash()
    {
        var r = SimBrief.Parse(@"{ ""fetch"": { ""status"": ""Error: Unknown UserID"" } }");
        Assert.False(r.Ok);
        Assert.Contains("SimBrief", r.Message);
    }

    [Fact]
    public void The_fetch_url_uses_userid_for_numbers_and_username_for_names()
    {
        Assert.Contains("userid=123456", SimBrief.FetchUrl("123456"));
        Assert.Contains("username=eliaz", SimBrief.FetchUrl("eliaz"));
        Assert.StartsWith("https://www.simbrief.com/api/xml.fetcher.php?", SimBrief.FetchUrl("eliaz"));
        Assert.EndsWith("&json=1", SimBrief.FetchUrl("eliaz"));
    }

    /// <summary>A fuller OFP: registration, cruise, route, times, max weights and the MSFS plan download.</summary>
    public const string FullSample = @"{
      ""fetch"": { ""userid"": ""123456"", ""status"": ""Success"" },
      ""params"": { ""units"": ""lbs"", ""time_generated"": ""1793000000"" },
      ""general"": { ""icao_airline"": ""BAW"", ""flight_number"": ""178"", ""initial_altitude"": ""37000"",
                     ""route"": ""CPT3F CPT UL9 STU DCT NIBOG"" },
      ""origin"": { ""icao_code"": ""EGLL"" },
      ""destination"": { ""icao_code"": ""KJFK"" },
      ""aircraft"": { ""icaocode"": ""B77W"", ""name"": ""Boeing 777-300ER"", ""reg"": ""G-STBA"" },
      ""weights"": { ""oew"": ""370100"", ""pax_count"": ""264"", ""cargo"": ""18000"", ""payload"": ""62000"",
                     ""est_zfw"": ""432100"", ""est_tow"": ""642000"", ""est_ldw"": ""470000"", ""max_tow"": ""775000"" },
      ""fuel"": { ""plan_ramp"": ""212000"", ""reserve"": ""15000"", ""taxi"": ""2000"" },
      ""times"": { ""est_time_enroute"": ""27180"" },
      ""fms_downloads"": { ""directory"": ""https://www.simbrief.com/ofp/flightplans/"",
                           ""mfs"": { ""name"": ""Microsoft Flight Simulator"", ""link"": ""EGLLKJFK_MFS_1.pln"" } }
    }";

    [Fact]
    public void Parses_every_detail_heavypro_shows()
    {
        var r = SimBrief.Parse(FullSample);
        Assert.True(r.Ok, r.Message);
        var o = r.Ofp!;
        Assert.Equal("B77W", o.AircraftType);
        Assert.Equal("G-STBA", o.Registration);
        Assert.Equal(37000, o.CruiseAltitudeFeet);
        Assert.Equal("CPT3F CPT UL9 STU DCT NIBOG", o.RouteText);
        Assert.Equal(TimeSpan.FromSeconds(27180), o.Duration);
        Assert.Equal(775000, o.MaxTakeoffWeight);
        Assert.Equal(62000, o.Payload);
        Assert.Equal(212000, o.BlockFuel);
        Assert.Equal("BAW178", o.FlightNumber);
        Assert.Equal("https://www.simbrief.com/ofp/flightplans/EGLLKJFK_MFS_1.pln", o.MsfsPlanUrl);
        Assert.Equal(new DateTime(2026, 10, 26, 7, 33, 20, DateTimeKind.Utc), o.Generated);
    }

    [Fact]
    public void Kilogram_plans_convert_to_pounds_for_the_response()
    {
        var kg = SimBrief.Parse(FullSample.Replace(@"""units"": ""lbs""", @"""units"": ""kgs""")).Ofp!;
        Assert.Equal(642000 * 2.20462, kg.TakeoffWeightPounds, 0);
        var lb = SimBrief.Parse(FullSample).Ofp!;
        Assert.Equal(642000, lb.TakeoffWeightPounds);
    }

    [Fact]
    public void Reads_the_xml_ofp_the_same_way()
    {
        const string xml = @"<?xml version=""1.0""?><OFP>
          <params><units>kgs</units></params>
          <general><icao_airline>SAS</icao_airline><flight_number>1415</flight_number><initial_altitude>36000</initial_altitude><route>DCT TINUR</route></general>
          <origin><icao_code>ESSA</icao_code></origin><destination><icao_code>EKCH</icao_code></destination>
          <aircraft><icaocode>A20N</icaocode><name>A320neo</name><reg>SE-ROA</reg></aircraft>
          <weights><est_zfw>58000</est_zfw><est_tow>63000</est_tow><max_tow>79000</max_tow><pax_count>150</pax_count></weights>
          <fuel><plan_ramp>5200</plan_ramp></fuel><times><est_time_enroute>3300</est_time_enroute></times>
        </OFP>";
        var r = SimBrief.Parse(xml);
        Assert.True(r.Ok, r.Message);
        Assert.Equal("ESSA → EKCH", r.Ofp!.Route);
        Assert.Equal("SE-ROA", r.Ofp.Registration);
        Assert.Equal(36000, r.Ofp.CruiseAltitudeFeet);
        Assert.Equal(150, r.Ofp.Passengers);
        Assert.Equal("kgs", r.Ofp.Units);
    }

    [Fact]
    public void The_error_simbrief_sends_with_http_400_is_shown_as_its_message()
    {
        // Measured: an unknown id gets HTTP 400 with this body.
        var r = SimBrief.Parse(@"{""fetch"":{""userid"":"""",""static_id"":"""",""status"":""Error: Unknown UserID"",""time"":""0""}}");
        Assert.False(r.Ok);
        Assert.Equal("SimBrief: Unknown UserID", r.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    public void An_unusable_id_is_never_sent(string id)
    {
        Assert.NotNull(SimBrief.Validate(id));
        var r = SimBrief.FetchAsync(id).GetAwaiter().GetResult();
        Assert.False(r.Ok);
    }

    [Fact]
    public void A_plan_with_nothing_in_it_says_so()
    {
        Assert.Contains("No SimBrief plan", SimBrief.Parse("{}").Message);
    }

    [Fact]
    public void The_last_good_plan_is_cached_and_can_be_cleared()
    {
        var data = Path.Combine(Path.GetTempPath(), "heavypro-simbrief-" + Guid.NewGuid());
        try
        {
            Assert.Null(SimBrief.LoadCache(data));
            SimBrief.SaveCache(data, FullSample);
            var cached = SimBrief.LoadCache(data);
            Assert.NotNull(cached);
            Assert.Equal("G-STBA", cached!.Ofp!.Registration);
            SimBrief.ClearCache(data);
            Assert.Null(SimBrief.LoadCache(data));
            // A broken cache file is ignored, not trusted.
            SimBrief.SaveCache(data, "{ broken");
            Assert.Null(SimBrief.LoadCache(data));
        }
        finally
        {
            if (Directory.Exists(data))
                Directory.Delete(data, recursive: true);
        }
    }
}
