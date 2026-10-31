using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class FlightModelOverridesTests
{
    // Shapes of the PMDG 777 flight_model.cfg lines HeavyPro changes: the 300ER and the 777F
    // write them differently (key case, spacing, trailing comments).
    private const string Cfg777W = "[VERSION]\r\nmajor = 1\r\n\r\n[WEIGHT_AND_BALANCE]\r\nempty_weight = 370000.0\t\t// Basic operating weight\r\n"
        + "empty_weight_pitch_moi = 15423466.7\r\nempty_weight_roll_moi = 8360376.2\r\nempty_weight_yaw_moi = 12939955.4\r\n\r\n"
        + "[FLIGHT_TUNING]\r\npitch_stability      = 0.75\r\nroll_stability       = 0.60\t\t\t// WAS 0.30\r\n\r\n"
        + "[AERODYNAMICS]\r\npitch_moment_pitch_damping = -163.860352; The change in pitch moment per change in pitch rate\r\n"
        + "roll_moment_roll_damping = -2.127441 ; \r\nroll_moment_delta_aileron = -0.476563 ; (control)\r\n";

    private const string Cfg777F = "[WEIGHT_AND_BALANCE]\r\nempty_weight_pitch_MOI=  8781448.4\r\nempty_weight_roll_MOI=   7407948.5\r\nempty_weight_yaw_MOI=   10285836.7\r\n"
        + "[AERODYNAMICS]\r\npitch_moment_pitch_damping = -163.860352; deg/s\r\nroll_moment_roll_damping = -2.127441 ; \r\n";

    private const string Path777W = "SimObjects/Airplanes/PMDG 777-300ER/presets/pmdg/PMDG 777-300ER/config/flight_model.cfg";

    [Fact]
    public void Patching_scales_only_the_four_lines_and_keeps_every_other_byte()
    {
        var patched = FlightModelOverrides.Patch(Cfg777W, FlightModelOverrides.Changes);
        Assert.Equal(Cfg777W
            .Replace("empty_weight_pitch_moi = 15423466.7", "empty_weight_pitch_moi = 17736986.7")
            .Replace("empty_weight_roll_moi = 8360376.2", "empty_weight_roll_moi = 9614432.6")
            .Replace("pitch_moment_pitch_damping = -163.860352;", "pitch_moment_pitch_damping = -188.439405;")
            .Replace("roll_moment_roll_damping = -2.127441 ;", "roll_moment_roll_damping = -2.446557 ;"), patched);
    }

    [Fact]
    public void The_777F_layout_keeps_its_own_key_case_and_spacing()
    {
        var patched = FlightModelOverrides.Patch(Cfg777F, FlightModelOverrides.Changes)!;
        Assert.Contains("empty_weight_pitch_MOI=  10098665.7\r\n", patched);
        Assert.Contains("empty_weight_roll_MOI=   8519140.8\r\n", patched);
        Assert.Contains("empty_weight_yaw_MOI=   10285836.7\r\n", patched);
    }

    [Fact]
    public void A_file_without_the_expected_lines_is_left_alone()
    {
        Assert.Null(FlightModelOverrides.Patch(Cfg777W.Replace("roll_moment_roll_damping", "roll_damping_renamed"), FlightModelOverrides.Changes));
        // The right key in the wrong section does not count.
        Assert.Null(FlightModelOverrides.Patch(Cfg777F.Replace("[AERODYNAMICS]", "[OTHER]"), FlightModelOverrides.Changes));
    }

    private static string NewCommunity()
    {
        var community = Path.Combine(Path.GetTempPath(), "heavypro-fm-" + Guid.NewGuid().ToString("N"));
        var pmdg = Path.Combine(community, "pmdg-aircraft-77w");
        var cfg = Path.Combine(pmdg, Path777W.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
        File.WriteAllText(cfg, Cfg777W);
        File.WriteAllText(Path.Combine(pmdg, "manifest.json"), "{ \"creator\": \"PMDG Simulations\", \"package_version\": \"2.4.130\" }");
        // MSFS 2024's converted copy of the package is not an aircraft to override.
        Directory.CreateDirectory(Path.Combine(community, "pmdg-aircraft-77w_CVT_", "SIMOBJECTS"));
        return community;
    }

    [Fact]
    public void Switching_on_builds_a_package_that_overrides_the_777_file()
    {
        var community = NewCommunity();
        try
        {
            var source = Assert.Single(FlightModelOverrides.Find(community));
            Assert.Equal("rotaidem-heavypro-fm-77w", source.OverridePackage);
            Assert.Equal(Path777W, source.VirtualPath);

            var messages = FlightModelOverrides.Sync(community, enabled: true, simRunning: false);
            Assert.Contains("PMDG 777-300ER: Real 777 feel built.", messages);
            var package = Path.Combine(community, "rotaidem-heavypro-fm-77w");
            var cfg = File.ReadAllText(Path.Combine(package, Path777W.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(FlightModelOverrides.Patch(Cfg777W, FlightModelOverrides.Changes), cfg);
            var layout = File.ReadAllText(Path.Combine(package, "layout.json"));
            Assert.Contains("\"path\": \"" + Path777W + "\"", layout);
            Assert.Contains("\"size\": " + cfg.Length, layout);
            var manifest = File.ReadAllText(Path.Combine(package, "manifest.json"));
            Assert.Contains("\"name\": \"pmdg-aircraft-77w\"", manifest);
            Assert.Contains("\"package_order_hint\": \"CUSTOM_SIMOBJECTS\"", manifest);
            // PMDG's own file is untouched.
            Assert.Equal(Cfg777W, File.ReadAllText(Path.Combine(community, "pmdg-aircraft-77w", Path777W.Replace('/', Path.DirectorySeparatorChar))));

            Assert.Contains("PMDG 777-300ER: Real 777 feel on.", FlightModelOverrides.Sync(community, true, false));
        }
        finally
        {
            Directory.Delete(community, true);
        }
    }

    [Fact]
    public void A_pmdg_update_rebuilds_it_and_switching_off_removes_it()
    {
        var community = NewCommunity();
        try
        {
            FlightModelOverrides.Sync(community, true, false);
            var pmdgCfg = Path.Combine(community, "pmdg-aircraft-77w", Path777W.Replace('/', Path.DirectorySeparatorChar));
            var updated = Cfg777W.Replace("pitch_stability      = 0.75", "pitch_stability      = 0.80");
            File.WriteAllText(pmdgCfg, updated);

            // While MSFS runs nothing is written.
            Assert.Contains("PMDG 777-300ER: Real 777 feel is built when MSFS is closed.", FlightModelOverrides.Sync(community, true, true));
            Assert.Contains("PMDG 777-300ER: Real 777 feel built.", FlightModelOverrides.Sync(community, true, false));
            var package = Path.Combine(community, "rotaidem-heavypro-fm-77w");
            Assert.Contains("pitch_stability      = 0.80", File.ReadAllText(Path.Combine(package, Path777W.Replace('/', Path.DirectorySeparatorChar))));

            FlightModelOverrides.Sync(community, false, false);
            Assert.False(Directory.Exists(package));
        }
        finally
        {
            Directory.Delete(community, true);
        }
    }

    [Fact]
    public void A_package_heavypro_did_not_write_is_never_touched()
    {
        var community = NewCommunity();
        try
        {
            var foreign = Path.Combine(community, "rotaidem-heavypro-fm-77w");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "manifest.json"), "{ \"creator\": \"Someone\" }");

            FlightModelOverrides.Sync(community, true, false);
            FlightModelOverrides.Sync(community, false, false);
            Assert.Equal("{ \"creator\": \"Someone\" }", File.ReadAllText(Path.Combine(foreign, "manifest.json")));
        }
        finally
        {
            Directory.Delete(community, true);
        }
    }
}
