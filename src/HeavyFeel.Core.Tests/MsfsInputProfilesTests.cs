using HeavyFeel.Core.Models;
using HeavyFeel.Core.Services;
using Xunit;

namespace HeavyFeel.Core.Tests;

public class MsfsInputProfilesTests
{
    // Shape of a real MSFS 2024 inputprofile file: several top-level elements, and
    // unbound actions written as empty <Action .../> tags next to bound ones.
    private static string Profile(string name, int productId, string category, bool elevator, bool aileron, bool rudder) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Version Num=""-1""/>
<FriendlyName PlatformAvailability=""1"" Locked=""false"">{name}</FriendlyName>
<Device DeviceName=""WINCTRL URSA MINOR 32 Joystick L"" GUID=""{{0}}"" ProductID=""{productId}"" CompositeID=""0"" HWVer=""1.0.0.0"">
	<AircraftInfo CategoryName=""{category}""/>
	<Context ContextName=""SURFACES"">
		{Action("KEY_AXIS_AILERONS_SET", aileron, "Joystick L-Axis X", 1026)}
		{Action("KEY_AXIS_ELEVATOR_SET", elevator, "Joystick L-Axis Y", 1042)}
		{Action("KEY_AXIS_RUDDER_SET", rudder, "Joystick L-Axis Z", 1058)}
	</Context>
</Device>";

    private static string Action(string name, bool bound, string key, int code) => bound
        ? $@"<Action ActionName=""{name}"" Flag=""2""><Primary><KEY Information=""{key} "">{code}</KEY></Primary></Action>"
        : $@"<Action ActionName=""{name}"" ValueEvent=""0.000000"" Delay=""0.000000"" Flag=""2""/>";

    [Fact]
    public void Finds_the_bound_stick_axes_of_every_airplane_profile()
    {
        var files = new[]
        {
            (new DateTime(2026, 5, 4), Profile("clean", 48167, "AIRPLANE", false, false, false)),
            (new DateTime(2026, 10, 10), Profile("throttle'", 48167, "AIRPLANE", true, true, false)),
            (new DateTime(2026, 10, 19), Profile("other", 48167, "OTHER", true, true, true)),
        };
        var binding = Assert.Single(MsfsInputProfiles.FindBindings(files, 48167, new[] { "pitch", "roll" }));
        Assert.Equal("throttle'", binding.ProfileName);
        Assert.Equal(new[] { "pitch", "roll" }, binding.Axes);
    }

    [Fact]
    public void An_older_airplane_profile_still_bound_is_found_after_the_latest_was_cleared()
    {
        // As on this PC: "Fix MSFS bindings" cleared "throttle'", the profile changed last,
        // but MSFS 2024 picks profiles per aircraft and the PMDG 777 uses an older one
        // that still binds the stick, so the 777 got the raw stick and REAL1 at once.
        var files = new[]
        {
            (new DateTime(2026, 9, 6), Profile("My new airplanes profile_1", 48167, "AIRPLANE", true, true, false)),
            (new DateTime(2026, 10, 26), Profile("throttle'", 48167, "AIRPLANE", false, false, false)),
            (new DateTime(2026, 10, 28), Profile("My new gliders profile", 48167, "GLIDER", true, true, false)),
        };
        var binding = Assert.Single(MsfsInputProfiles.FindBindings(files, 48167, new[] { "pitch", "roll" }));
        Assert.Equal("My new airplanes profile_1", binding.ProfileName);
        Assert.Equal(new[] { "pitch", "roll" }, binding.Axes);
    }

    [Fact]
    public void Several_bound_airplane_profiles_are_all_found_newest_first()
    {
        var files = new[]
        {
            (new DateTime(2026, 6, 1), Profile("old", 48167, "AIRPLANE", true, false, false)),
            (new DateTime(2026, 9, 6), Profile("newer", 48167, "AIRPLANE", false, true, false)),
        };
        var bindings = MsfsInputProfiles.FindBindings(files, 48167, new[] { "pitch", "roll" });
        Assert.Equal(new[] { "newer", "old" }, bindings.Select(b => b.ProfileName));
        Assert.Equal(new[] { "roll" }, bindings[0].Axes);
        Assert.Equal(new[] { "pitch" }, bindings[1].Axes);
    }

    [Fact]
    public void Empty_action_tags_are_not_bindings()
    {
        // The rudder action is an empty tag between bound ones: it must not borrow their keys.
        var parsed = MsfsInputProfiles.Parse(Profile("p", 48167, "AIRPLANE", true, true, false));
        Assert.Equal(new[] { "pitch", "roll" }, parsed!.BoundAxes);
    }

    [Fact]
    public void Clean_profile_or_other_device_is_no_conflict()
    {
        var files = new[] { (DateTime.Now, Profile("clean", 48167, "AIRPLANE", false, false, false)) };
        Assert.Empty(MsfsInputProfiles.FindBindings(files, 48167, new[] { "pitch", "roll" }));
        Assert.Empty(MsfsInputProfiles.FindBindings(files, 46713, new[] { "yaw" }));
    }

    [Fact]
    public void Clearing_unbinds_only_the_given_axes_and_keeps_the_rest()
    {
        var xml = Profile("throttle'", 48167, "AIRPLANE", true, true, true);
        var cleared = MsfsInputProfiles.ClearAxes(xml, new[] { "pitch", "roll" });
        Assert.Equal(new[] { "yaw" }, MsfsInputProfiles.Parse(cleared)!.BoundAxes);
        Assert.Contains("<Action ActionName=\"KEY_AXIS_ELEVATOR_SET\" ValueEvent=\"0.000000\" Delay=\"0.000000\" Flag=\"2\"/>", cleared);
        Assert.Contains("Joystick L-Axis Z", cleared);
        Assert.Contains("<FriendlyName PlatformAvailability=\"1\" Locked=\"false\">throttle'</FriendlyName>", cleared);
        // Clearing twice changes nothing more.
        Assert.Equal(cleared, MsfsInputProfiles.ClearAxes(cleared, new[] { "pitch", "roll" }));
    }

    [Fact]
    public void Clearing_the_real_msfs_2024_layout()
    {
        // As MSFS 2024 writes a bound axis (Delay 2, Flag 4, a KEY with a code).
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Version Num=\"-1\"/>\n<FriendlyName>p</FriendlyName>\n"
            + "<Device DeviceName=\"WINCTRL URSA MINOR 32 Joystick L\" ProductID=\"48167\">\n\t<AircraftInfo CategoryName=\"AIRPLANE\"/>\n\t<Context ContextName=\"SURFACES\">\n"
            + "\t\t<Action ActionName=\"KEY_AXIS_AILERONS_SET\" ValueEvent=\"0.000000\" Delay=\"2.000000\" Flag=\"4\">\n\t\t\t<Primary>\n\t\t\t\t<KEY Information=\"Joystick L-Axis X \">1026</KEY>\n\t\t\t</Primary>\n\t\t</Action>\n"
            + "\t\t<Action ActionName=\"KEY_AXIS_ELEVATOR_SET\" ValueEvent=\"0.000000\" Delay=\"2.000000\" Flag=\"4\">\n\t\t\t<Primary>\n\t\t\t\t<KEY Information=\"Joystick L-Axis Y \">1042</KEY>\n\t\t\t</Primary>\n\t\t</Action>\n"
            + "\t</Context>\n</Device>";
        Assert.Equal(new[] { "pitch", "roll" }, MsfsInputProfiles.Parse(xml)!.BoundAxes);
        var cleared = MsfsInputProfiles.ClearAxes(xml, new[] { "pitch", "roll" });
        Assert.Empty(MsfsInputProfiles.Parse(cleared)!.BoundAxes);
        Assert.DoesNotContain("1042", cleared);
    }

    // As MSFS 2024 writes a stick action bound on the stick axis and on buttons, plus a
    // button-only action (localized key names, as on a Swedish install).
    private const string AxisSlot = "\t\t\t<Primary>\r\n\t\t\t\t<KEY Information=\"Joystick V-axel X \">1026</KEY>\r\n\t\t\t</Primary>\r\n";

    private const string AxisAndButtons = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Version Num=\"-1\"/>\r\n<FriendlyName>p</FriendlyName>\r\n"
        + "<Device DeviceName=\"WINCTRL URSA MINOR 32 Joystick L\" ProductID=\"48167\">\r\n\t<AircraftInfo CategoryName=\"AIRPLANE\"/>\r\n\t<Context ContextName=\"SURFACES\">\r\n"
        + "\t\t<Action ActionName=\"KEY_AXIS_AILERONS_SET\" ValueEvent=\"0.000000\" Delay=\"2.000000\" Flag=\"4\">\r\n" + AxisSlot
        + "\t\t\t<Secondary>\r\n\t\t\t\t<KEY Information=\"Joystick Knapp 11\">10</KEY>\r\n\t\t\t</Secondary>\r\n\t\t</Action>\r\n"
        + "\t\t<Action ActionName=\"KEY_AXIS_ELEVATOR_SET\" ValueEvent=\"0.000000\" Delay=\"2.000000\" Flag=\"4\">\r\n"
        + "\t\t\t<Primary>\r\n\t\t\t\t<KEY Information=\"Joystick Knapp 12\">11</KEY>\r\n\t\t\t</Primary>\r\n\t\t</Action>\r\n"
        + "\t\t<Action ActionName=\"KEY_BRAKES\" ValueEvent=\"0.000000\" Delay=\"2.000000\" Flag=\"2\">\r\n"
        + "\t\t\t<Primary>\r\n\t\t\t\t<KEY Information=\"Joystick Knapp 1\">0</KEY>\r\n\t\t\t</Primary>\r\n\t\t</Action>\r\n"
        + "\t</Context>\r\n</Device>";

    [Fact]
    public void A_button_on_a_stick_action_is_not_a_stick_binding()
    {
        // Elevator is bound to a button only: that is the user's button, not the stick.
        Assert.Equal(new[] { "roll" }, MsfsInputProfiles.Parse(AxisAndButtons)!.BoundAxes);
    }

    [Fact]
    public void Clearing_removes_only_the_axis_and_keeps_every_button()
    {
        var cleared = MsfsInputProfiles.ClearAxes(AxisAndButtons, new[] { "pitch", "roll" });
        Assert.Empty(MsfsInputProfiles.Parse(cleared)!.BoundAxes);
        Assert.DoesNotContain("V-axel X", cleared);
        // The aileron action keeps its button slot, the elevator and brake actions are untouched.
        Assert.Equal(AxisAndButtons.Replace(AxisSlot, ""), cleared);
        Assert.Equal(cleared, MsfsInputProfiles.ClearAxes(cleared, new[] { "pitch", "roll" }));
    }

    [Fact]
    public void Binding_found_on_disk_names_its_file()
    {
        var files = new[] { (@"C:\x\inputprofile_1", DateTime.Now, Profile("p", 48167, "AIRPLANE", true, false, false)) };
        Assert.Equal(@"C:\x\inputprofile_1", Assert.Single(MsfsInputProfiles.FindBindings(files, 48167, new[] { "pitch" })).File);
    }

    [Fact]
    public void Product_id_comes_from_the_device_key()
    {
        Assert.Equal(48167, MsfsInputProfiles.ProductIdFromKey("4098:BC27"));
        Assert.Null(MsfsInputProfiles.ProductIdFromKey(""));
    }

    [Fact]
    public void Built_in_profiles_never_pin_an_aircraft_and_old_pins_are_cleared()
    {
        Assert.All(BuiltInProfiles.All, p => Assert.Equal("Auto Detect", p.AircraftProfile));
        var old = new List<CustomProfile>
        {
            new() { Name = "Light GA - Cessna 172", AircraftProfile = "Generic GA" },
            new() { Name = "My own", AircraftProfile = "PMDG 777" }
        };
        Assert.Equal(1, BuiltInProfiles.Unpin(old));
        Assert.Equal("Auto Detect", old[0].AircraftProfile);
        Assert.Equal("PMDG 777", old[1].AircraftProfile);
    }
}
