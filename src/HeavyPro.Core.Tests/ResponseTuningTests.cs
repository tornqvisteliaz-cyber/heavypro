using HeavyPro.Core.Models;
using HeavyPro.Core.Physics;
using HeavyPro.Core.Services;
using Xunit;

namespace HeavyPro.Core.Tests;

public class ResponseTuningTests
{
    private static FlightSnapshot Snap(string title, string model, AircraftClass cls, double lb, double maxLb, double kt, double stickY,
        bool ground = false, double cg = 0, double gs = 0) => new()
    {
        Aircraft = new AircraftIdentity { Title = title, AtcModel = model },
        Class = cls,
        TotalWeightPounds = lb,
        MaxGrossWeightPounds = maxLb,
        AirspeedIndicatedKnots = kt,
        GroundSpeedKnots = gs,
        AltitudeAglFeet = ground ? 0 : 5000,
        OnGround = ground,
        CgPercentMac = cg,
        YokeY = stickY
    };

    private static FlightSnapshot B777(double stickY) => Snap("777-300ER", "B77W", AircraftClass.WideBody, 520_000, 775_000, 250, stickY);

    /// <summary>
    /// Seconds until the output reaches 90 % of where a 60 % pitch input is shaped to (REAL1's soft
    /// centre puts that a little below 60 %, e.g. about 53 % on the 777); 10 if it never does.
    /// </summary>
    private static double Answer(AppSettings s, Func<double, FlightSnapshot> snap)
    {
        var engine = new InputDynamicsEngine();
        engine.Step(s, snap(0), 1.0 / 60, 1.4);
        for (var i = 1; i <= 600; i++)
        {
            var f = engine.Step(s, snap(0.6), 1.0 / 60, 1.4);
            if (f.FinalElevator >= 0.9 * f.FilteredElevator)
                return i / 60.0;
        }
        return 10;
    }

    private static AppSettings Real1(double response = ResponseDefaults.RealisticResponse) =>
        new() { InputRoute = Physics.Real1.RouteName, RealisticResponse = response };

    // Realistic Response 20 gives 0.8.0's travel times (TravelScale = 1).
    private const double ResponseOf080 = 20;

    [Fact]
    public void Realistic_response_defaults_to_40()
    {
        var s = new AppSettings();
        Assert.Equal(40, s.RealisticResponse);
        Assert.Equal(50, s.PitchResponsiveness);
        Assert.Equal(50, s.RollResponsiveness);
        Assert.Equal(50, s.YawResponsiveness);
        Assert.Equal(35, s.InputSmoothing);
        Assert.True(s.AircraftAwareness);
    }

    [Fact]
    public void Older_settings_files_get_the_new_defaults_and_values_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "heavypro-test-" + Guid.NewGuid() + ".json");
        try
        {
            // A 0.8.0 settings file has none of the new keys.
            File.WriteAllText(path, "{ \"Inertia\": 70, \"SimBriefUsername\": \"123456\" }");
            var store = new JsonSettingsStore(path);
            var loaded = store.Load();
            Assert.Equal(40, loaded.RealisticResponse);
            Assert.Equal(35, loaded.InputSmoothing);
            Assert.Equal("123456", loaded.SimBriefUsername);

            loaded.RealisticResponse = 65;
            loaded.PitchResponsiveness = 70;
            loaded.InputSmoothing = 10;
            loaded.AircraftAwareness = false;
            loaded.FlightPlanName = "EGLLKJFK.pln";
            store.Save(loaded);
            var again = new JsonSettingsStore(path).Load();
            Assert.Equal(65, again.RealisticResponse);
            Assert.Equal(70, again.PitchResponsiveness);
            Assert.Equal(10, again.InputSmoothing);
            Assert.False(again.AircraftAwareness);
            Assert.Equal("EGLLKJFK.pln", again.FlightPlanName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reset_puts_every_response_setting_back()
    {
        var s = new AppSettings { RealisticResponse = 90, PitchResponsiveness = 5, RollResponsiveness = 99, YawResponsiveness = 0, InputSmoothing = 100, AircraftAwareness = false };
        ResponseDefaults.Apply(s);
        Assert.Equal(40, s.RealisticResponse);
        Assert.Equal(50, s.PitchResponsiveness);
        Assert.Equal(50, s.RollResponsiveness);
        Assert.Equal(50, s.YawResponsiveness);
        Assert.Equal(35, s.InputSmoothing);
        Assert.True(s.AircraftAwareness);
    }

    [Fact]
    public void Realistic_response_has_a_measurable_effect_on_real1()
    {
        var slow = Answer(Real1(0), B777);
        var standard = Answer(Real1(40), B777);
        var quick = Answer(Real1(100), B777);
        // At 0 the 777 sits on REAL1's 5 s travel ceiling, so the gap is smaller than the scale alone.
        Assert.True(standard < slow * 0.9, $"40: {standard:0.00}s vs 0: {slow:0.00}s");
        Assert.True(quick < standard * 0.6, $"100: {quick:0.00}s vs 40: {standard:0.00}s");
    }

    [Fact]
    public void The_default_answers_sooner_than_080()
    {
        var before = Answer(new AppSettings { InputRoute = Physics.Real1.RouteName, RealisticResponse = ResponseOf080, InputSmoothing = 69 }, B777);
        var now = Answer(Real1(), B777);
        Assert.True(now < before * 0.85, $"0.9.0 default {now:0.00}s vs 0.8.0 {before:0.00}s");
        Assert.Equal(1.0, ResponseTuning.TravelScale(ResponseOf080), 2);
    }

    [Fact]
    public void Realistic_response_also_shapes_the_direct_route()
    {
        FlightSnapshot C172(double y) => Snap("C172SP", "C172", AircraftClass.LightGa, 2200, 2550, 100, y);
        var slow = Answer(new AppSettings { InputRoute = "Direct", RealisticResponse = 0 }, C172);
        var quick = Answer(new AppSettings { InputRoute = "Direct", RealisticResponse = 100 }, C172);
        Assert.True(quick < slow * 0.7, $"100: {quick:0.00}s vs 0: {slow:0.00}s");
    }

    [Fact]
    public void Each_axis_slider_moves_only_its_axis()
    {
        var neutral = ResponseTuning.For(Real1(), B777(0));
        var pitchy = ResponseTuning.For(new AppSettings { InputRoute = Physics.Real1.RouteName, PitchResponsiveness = 100 }, B777(0));
        Assert.True(pitchy.Pitch < neutral.Pitch * 0.7);
        Assert.Equal(neutral.Roll, pitchy.Roll, 6);
        Assert.Equal(neutral.Yaw, pitchy.Yaw, 6);
        Assert.Equal(1.0, ResponseTuning.AxisScale(50), 6);
    }

    [Fact]
    public void Less_smoothing_gets_a_small_input_moving_sooner()
    {
        double FirstMove(double smoothing)
        {
            var s = new AppSettings { InputRoute = Physics.Real1.RouteName, InputSmoothing = smoothing };
            var engine = new InputDynamicsEngine();
            engine.Step(s, B777(0), 1.0 / 60, 1.4);
            for (var i = 1; i <= 600; i++)
                if (engine.Step(s, B777(0.1), 1.0 / 60, 1.4).FinalElevator >= 0.05)
                    return i / 60.0;
            return 10;
        }
        Assert.True(FirstMove(0) < FirstMove(100), $"smoothing 0: {FirstMove(0):0.00}s, 100: {FirstMove(100):0.00}s");
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(40, 35)]
    [InlineData(0, 100)]
    public void Never_overshoots_or_oscillates(double response, double smoothing)
    {
        // On the ground: REAL1 adds a deliberate light buffet in the air, which is not oscillation.
        FlightSnapshot Ground(double y) => Snap("777-300ER", "B77W", AircraftClass.WideBody, 520_000, 775_000, 100, y, ground: true, gs: 100);
        foreach (var route in new[] { Physics.Real1.RouteName, "Direct" })
        {
            var s = new AppSettings { InputRoute = route, RealisticResponse = response, InputSmoothing = smoothing };
            var engine = new InputDynamicsEngine();
            engine.Step(s, Ground(0), 1.0 / 60, 1.4);
            // REAL1 lets a control "carry on a touch" (0.8.0 behaviour, well under 0.5 %), then settles.
            // Real trouble would be a big overshoot or the output swinging back and forth.
            var reversals = 0;
            var lastSign = 0;
            var output = 0.0;
            var target = 0.0;
            for (var i = 0; i < 600; i++)
            {
                var f = engine.Step(s, Ground(0.8), 1.0 / 60, 1.4);
                (output, target) = (f.FinalElevator, f.FilteredElevator);
                Assert.True(output <= target * 1.005, $"{route} r{response} s{smoothing}: {output} overshoots {target}");
                var error = target - output;
                var sign = Math.Abs(error) < 1e-6 ? 0 : Math.Sign(error);
                if (sign != 0 && lastSign != 0 && sign != lastSign)
                    reversals++;
                if (sign != 0)
                    lastSign = sign;
            }
            Assert.True(reversals <= 1, $"{route} r{response} s{smoothing}: swung across the target {reversals} times");
            Assert.True(Math.Abs(target - output) < 0.01, $"{route}: settled at {output}, target {target}");
        }
    }

    [Theory]
    [InlineData("C172SP G1000", "C172", AircraftClass.LightGa, 2200, 2550)]
    [InlineData("Fenix A320 CFM", "A320", AircraftClass.NarrowBody, 140_000, 172_000)]
    [InlineData("iniBuilds A350-1000", "A35K", AircraftClass.WideBody, 550_000, 700_000)]
    [InlineData("PMDG 777-300ER", "B77W", AircraftClass.WideBody, 520_000, 775_000)]
    [InlineData("Boeing 787-10", "B78X", AircraftClass.WideBody, 420_000, 560_000)]
    [InlineData("Saab 340B", "SF34", AircraftClass.Regional, 26_000, 29_000)]
    public void Every_listed_aircraft_answers_sooner_than_080_with_sane_times(string title, string model, AircraftClass cls, double lb, double maxLb)
    {
        FlightSnapshot S(double y) => Snap(title, model, cls, lb, maxLb, 200, y);
        var now = Answer(Real1(), S);
        var before = Answer(new AppSettings { InputRoute = Physics.Real1.RouteName, RealisticResponse = ResponseOf080, InputSmoothing = 69, AircraftAwareness = false }, S);
        Assert.InRange(now, 0.05, 6);
        Assert.True(now < before, $"{title}: {now:0.00}s vs 0.8.0 {before:0.00}s");
    }

    [Fact]
    public void Heavier_for_its_type_is_slower()
    {
        var light = ResponseTuning.Aware(Snap("777-300ER", "B77W", AircraftClass.WideBody, 380_000, 775_000, 250, 0));
        var heavy = ResponseTuning.Aware(Snap("777-300ER", "B77W", AircraftClass.WideBody, 760_000, 775_000, 250, 0));
        Assert.True(heavy.Mass > light.Mass);
        Assert.InRange(light.Mass, 0.9, 1.15);
        Assert.InRange(heavy.Mass, 0.9, 1.15);
        Assert.Contains("max weight", heavy.Label);
    }

    [Fact]
    public void An_aft_centre_of_gravity_lightens_pitch()
    {
        var forward = ResponseTuning.Aware(Snap("A320", "A320", AircraftClass.NarrowBody, 140_000, 172_000, 250, 0, cg: 0.15));
        var aft = ResponseTuning.Aware(Snap("A320", "A320", AircraftClass.NarrowBody, 140_000, 172_000, 250, 0, cg: 0.38));
        Assert.True(aft.Pitch < forward.Pitch);
        Assert.Contains("CG", aft.Label);
    }

    [Fact]
    public void Nosewheel_steering_at_taxi_speed_is_quicker_in_yaw_only()
    {
        var taxi = ResponseTuning.Aware(Snap("A320", "A320", AircraftClass.NarrowBody, 140_000, 172_000, 15, 0, ground: true, gs: 15));
        var takeoffRoll = ResponseTuning.Aware(Snap("A320", "A320", AircraftClass.NarrowBody, 140_000, 172_000, 120, 0, ground: true, gs: 120));
        Assert.True(taxi.Yaw < 1);
        Assert.Equal(1, takeoffRoll.Yaw);
        Assert.Equal(1, taxi.Pitch);
    }

    [Fact]
    public void Missing_data_is_neutral_not_a_guess()
    {
        var unknown = ResponseTuning.Aware(Snap("Mystery", "", AircraftClass.Unknown, 0, 0, 100, 0));
        Assert.Equal(1, unknown.Mass);
        Assert.Equal(1, unknown.Pitch);
        Assert.Equal("", unknown.Label);
        // A nonsense CG is ignored.
        Assert.Equal(1, ResponseTuning.Aware(Snap("A320", "A320", AircraftClass.NarrowBody, 0, 0, 100, 0, cg: 3.5)).Pitch);
    }

    [Fact]
    public void Simbrief_weights_stand_in_only_for_the_same_aircraft_type()
    {
        var ofp = new SimBriefOfp("EGLL", "KJFK", "Boeing 777-300ER", "BAW178", "lbs", 370_000, 60_000, 264, 18_000,
            430_000, 640_000, 470_000, 210_000, 15_000, 2_000) { AircraftType = "B77W", MaxTakeoffWeight = 775_000 };
        var plan = FlightPlans.FromOfp(ofp);
        var noWeight777 = Snap("777-300ER", "B77W", AircraftClass.WideBody, 0, 0, 250, 0);
        var noWeightA320 = Snap("A320", "A320", AircraftClass.NarrowBody, 0, 0, 250, 0);
        Assert.Equal(640_000.0 / 775_000, ResponseTuning.LoadFraction(noWeight777, plan, out var source), 3);
        Assert.Contains("SimBrief", source);
        Assert.Equal(0, ResponseTuning.LoadFraction(noWeightA320, plan, out _));
        // The sim's own weight wins when it has one.
        var withWeight = Snap("777-300ER", "B77W", AircraftClass.WideBody, 400_000, 775_000, 250, 0);
        Assert.Equal(400_000.0 / 775_000, ResponseTuning.LoadFraction(withWeight, plan, out _), 3);
    }

    [Fact]
    public void Turning_awareness_off_ignores_the_aircraft_state()
    {
        var heavy = Snap("777-300ER", "B77W", AircraftClass.WideBody, 760_000, 775_000, 250, 0, cg: 0.4);
        var on = ResponseTuning.For(new AppSettings(), heavy);
        var off = ResponseTuning.For(new AppSettings { AircraftAwareness = false }, heavy);
        Assert.NotEqual(on.Pitch, off.Pitch);
        Assert.Equal(ResponseTuning.TravelScale(40), off.Pitch, 6);
        Assert.Equal("", off.Awareness);
    }
}
