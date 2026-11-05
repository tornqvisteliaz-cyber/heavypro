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
    }
}
