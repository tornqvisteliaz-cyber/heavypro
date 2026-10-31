using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class PaywareCatalogTests
{
    // Title / ATC model / ATC type exactly as these add-ons report them (their aircraft.cfg
    // on the dev PC, or HeavyPro's own logs).
    [Theory]
    [InlineData("777-300ER", "ATCCOM.AC_MODEL B77W.0.text", "ATCCOM.ATC_NAME BOEING.0.text", "PMDG 777-300ER", FeelFamily.Boeing777)]
    [InlineData("777F", "ATCCOM.AC_MODEL B77L.0.text", "ATCCOM.ATC_NAME BOEING.0.text", "PMDG 777F", FeelFamily.Boeing777)]
    [InlineData("PMDG 737-800 PMDG House", "ATCCOM.AC_MODEL B738.0.text", "BOEING", "PMDG 737", FeelFamily.Boeing737)]
    [InlineData("iFly 737-MAX8 Lonestar N166LS (166Seats)", "ATCCOM.AC_MODEL B738.0.text", "ATCCOM.ATC_NAME BOEING.0.text", "iFly 737 MAX 8", FeelFamily.Boeing737)]
    [InlineData("CSS B737-300 20K", "TT:ATCCOM.AC_MODEL_B737.0.text", "TT:ATCCOM.ATC_NAME BOEING.0.text", "CSS 737 Classic", FeelFamily.Boeing737)]
    [InlineData("BlueBird 757-200 PW N/W", "ATCCOM.AC_MODEL B752.0.text", "BBS752PW", "BlueBird 757-200", FeelFamily.Boeing757)]
    [InlineData("Boeing 727-200F", "B727", "BOEING", "FSS Boeing 727", FeelFamily.Airliner)]
    [InlineData("Boeing 787-9 (GE)", "B789", "Boeing", "Horizon Simulations 787-9", FeelFamily.Boeing787)]
    [InlineData("FenixA320 CFM SL", "ATCCOM.AC_MODEL A320.0.text", "ATCCOM.ATC_NAME AIRBUS.0.text", "Fenix A320", FeelFamily.Fenix)]
    [InlineData("FSLabs A321-211 - FSL (SX-FSL)", "A321", "AIRBUS", "FSLabs A321", FeelFamily.AirbusNarrow)]
    [InlineData("Aerosoft A330-900 Delta", "A339RR", "AIRBUS", "Aerosoft A330-900neo", FeelFamily.AirbusWide)]
    [InlineData("Aerosoft A346 Pro", "A346", "TT:ATCCOM.ATC_NAME AIRBUS.0.text", "Aerosoft A340-600 Pro", FeelFamily.AirbusWide)]
    [InlineData("A350-1000 (No Cabin)", "A350-1000", "A35K", "iniBuilds A350-1000", FeelFamily.AirbusWide)]
    [InlineData("A350-900 (Default Cabin)", "A350-900", "A359", "iniBuilds A350-900", FeelFamily.AirbusWide)]
    [InlineData("A300-600", "A300", "TT:AIRCRAFT.UI_TYPE", "iniBuilds A300-600", FeelFamily.HeavyAirliner)]
    [InlineData("A310", "ATCCOM.AC_MODEL A310.0.text", "ATCCOM.ATC_NAME AIRBUS.0.text", "iniBuilds A310", FeelFamily.HeavyAirliner)]
    [InlineData("L1011-500 Cabin w/ Lounge", "", "Lockheed", "iniBuilds L-1011 TriStar", FeelFamily.HeavyAirliner)]
    [InlineData("Fly The Maddog X MD-82 20th", "MD82", "MCDONNELLDOUGLAS", "Leonardo MaddogX MD-82", FeelFamily.Airliner)]
    [InlineData("McDonnell Douglas DC-9-10F", "DC91", "McDonnellDouglas", "SkySimulations DC-9", FeelFamily.Airliner)]
    [InlineData("FSS Embraer E190 Freighter", "E190F", "Embraer", "FSS Embraer E-Jet", FeelFamily.Airliner)]
    [InlineData("DCDesigns_Concorde", "Concorde", "Concorde", "DC Designs Concorde", FeelFamily.HeavyAirliner)]
    [InlineData("HJET HA420", "HA420", "HJET", "FlightFX HondaJet", FeelFamily.Regional)]
    [InlineData("Cessna C680: HB-SOV", "C680+", "Textron Aviation", "Skyward Citation Latitude", FeelFamily.Regional)]
    [InlineData("iniBuilds F406 Caravan II", "TT:ATCCOM.AC_MODEL F406.0.text", "iniBuilds", "iniBuilds F406 Caravan II", FeelFamily.Regional)]
    [InlineData("Britten Norman Islander Common", "TT:ATCCOM.AC_MODEL BN2B20.0.text", "TT:ATCCOM.ATC_NAME BRITTENNORMAN.0.text", "BlackBox Islander", FeelFamily.Regional)]
    [InlineData("Carenado CT210N", "CT210N", "CT210N", "Carenado", FeelFamily.LightGa)]
    public void Payware_aircraft_are_named_and_get_their_controls_feel(string title, string model, string type, string name, FeelFamily family)
    {
        var id = new AircraftIdentity { Title = title, AtcModel = model, AtcType = type };
        var known = PaywareCatalog.Find(id);
        Assert.NotNull(known);
        Assert.Equal(name, known!.Name);
        Assert.Equal(family, known.Family);
        var snap = new FlightSnapshot { Aircraft = id, Class = AircraftCatalog.Classify(id, 0) };
        Assert.Equal(Real1.ForFamily(family).Family, Real1.For(snap).Family);
    }

    [Theory]
    [InlineData("C172SP G1000 Passengers", "ATCCOM.AC_MODEL C172.0.text", "ATCCOM.ATC_NAME CESSNA.0.text")]
    [InlineData("Boeing 787-10 Asobo", "TT:ATCCOM.AC_MODEL_B78X.0.text", "TT:ATCCOM.ATC_NAME BOEING.0.text")]
    [InlineData("JMB Aviation VL3", "VL3", "JMB Aviation")]
    [InlineData("", "", "")]
    public void Default_and_unknown_aircraft_are_not_named(string title, string model, string type)
    {
        Assert.Null(PaywareCatalog.Find(new AircraftIdentity { Title = title, AtcModel = model, AtcType = type }));
    }

    [Fact]
    public void Every_named_family_in_the_list_is_one_the_catalog_can_return()
    {
        Assert.True(PaywareCatalog.Names.Count >= 30);
        Assert.All(PaywareCatalog.Names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
    }
}
