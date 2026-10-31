using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeavyFeel.Core.Services;

/// <summary>An MSFS control profile that still sends stick axes HeavyPro feeds itself.</summary>
public sealed record MsfsAxisBinding(string ProfileName, string DeviceName, IReadOnlyList<string> Axes, string File = "")
{
    public override string ToString() => $"{string.Join(", ", Axes)} on {DeviceName} (MSFS profile \"{ProfileName}\")";
}

/// <summary>
/// Reads MSFS 2024 control profiles (Steam keeps them as inputprofile_* XML files) to
/// find stick axes still bound in MSFS. Add-ons like PMDG and iniBuilds take those
/// hardware events before SimConnect sees them, so this is the only way to warn
/// that a binding fights HeavyPro there: the yoke jumps with the raw stick, then
/// back to HeavyPro's slower value.
/// </summary>
public static class MsfsInputProfiles
{
    private static readonly (string Action, string Axis)[] StickAxes =
    {
        ("KEY_AXIS_ELEVATOR_SET", "pitch"),
        ("KEY_AXIS_AILERONS_SET", "roll"),
        ("KEY_AXIS_RUDDER_SET", "yaw"),
    };

    /// <summary>
    /// For one device (by USB product id), every airplane profile that binds any of the
    /// wanted axes ("pitch", "roll", "yaw"), most recently changed first. MSFS 2024 picks
    /// a profile per aircraft, so the one changed last is not the one every aircraft uses:
    /// the PMDG 777 can sit on an older profile that still binds the stick.
    /// </summary>
    public static IReadOnlyList<MsfsAxisBinding> FindBindings(IEnumerable<(DateTime Modified, string Xml)> profiles, int productId, IReadOnlyCollection<string> axes) =>
        FindBindings(profiles.Select(p => ("", p.Modified, p.Xml)), productId, axes);

    /// <summary>As above, from files on disk: each binding names the file it is in.</summary>
    public static IReadOnlyList<MsfsAxisBinding> FindBindings(IEnumerable<(string Path, DateTime Modified, string Xml)> profiles, int productId, IReadOnlyCollection<string> axes) =>
        profiles
            .Select(p => (p.Path, p.Modified, Profile: Parse(p.Xml)))
            .Where(p => p.Profile != null && p.Profile.ProductId == productId
                && string.Equals(p.Profile.Category, "AIRPLANE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Modified)
            .Select(p => new MsfsAxisBinding(p.Profile!.Name, p.Profile.DeviceName, p.Profile.BoundAxes.Where(axes.Contains).ToList(), p.Path))
            .Where(b => b.Axes.Count > 0)
            .ToList();

    // MSFS key codes: buttons below 256, hat switches from 256, axes from 512
    // (L-Axis X 1026, Y 1042, Z 1058, sliders 514...). The names are in the user's
    // language ("Joystick Knapp 11", "V-axel Y"), so only the code says what a key is.
    private const int FirstAxisCode = 512;

    private static readonly Regex Slot = new(@"[ \t]*<(Primary|Secondary)>.*?</\1>(\r?\n)?", RegexOptions.Singleline);
    private static readonly Regex KeyCode = new(@"<KEY\b[^>]*>\s*(\d+)\s*</KEY>");

    /// <summary>
    /// A Primary or Secondary slot made only of axes, so moving the stick drives it.
    /// A slot with a button or hat in it is the user's button binding, never ours to clear.
    /// </summary>
    private static bool IsAxisSlot(IEnumerable<int> codes)
    {
        var list = codes.ToList();
        return list.Count > 0 && list.All(c => c >= FirstAxisCode);
    }

    /// <summary>
    /// The device has airplane profiles but none binds these axes, so MSFS itself sends
    /// nothing for them (after "Fix MSFS bindings"). The MSFS bindings route then gets no
    /// stick at all. False when there is no airplane profile to tell from.
    /// </summary>
    public static bool Unbound(IEnumerable<(string Path, DateTime Modified, string Xml)> profiles, int productId, IReadOnlyCollection<string> axes)
    {
        var airplane = profiles.Select(p => Parse(p.Xml))
            .Where(p => p != null && p.ProductId == productId && string.Equals(p.Category, "AIRPLANE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return airplane.Count > 0 && !airplane.Any(p => p!.BoundAxes.Any(axes.Contains));
    }

    /// <summary>
    /// The profile with those axes unbound: only the slots that hold a stick axis are
    /// removed from the stick actions, and an action left with no slot becomes the empty
    /// <c>&lt;Action … Flag="2"/&gt;</c> MSFS itself writes for an unbound one. Buttons,
    /// even ones bound to those same actions, and everything else in the file stay byte
    /// for byte the same.
    /// </summary>
    public static string ClearAxes(string xml, IReadOnlyCollection<string> axes)
    {
        foreach (var (action, axis) in StickAxes)
        {
            if (!axes.Contains(axis))
                continue;
            var bound = new Regex("<Action ActionName=\"" + action + "\"[^>]*(?<!/)>.*?</Action>", RegexOptions.Singleline);
            xml = bound.Replace(xml, m =>
            {
                var kept = Slot.Replace(m.Value, s =>
                    IsAxisSlot(KeyCode.Matches(s.Value).Cast<Match>().Select(k => int.Parse(k.Groups[1].Value))) ? "" : s.Value);
                if (kept == m.Value)
                    return m.Value;
                return Slot.IsMatch(kept)
                    ? kept
                    : "<Action ActionName=\"" + action + "\" ValueEvent=\"0.000000\" Delay=\"0.000000\" Flag=\"2\"/>";
            });
        }
        return xml;
    }

    /// <summary>
    /// The current profile with its elevator, aileron and rudder actions put back as they
    /// are in the backup ("Restore MSFS bindings"). Buttons and anything else changed since
    /// the backup stay as they are now. An action is only restored when both files have it
    /// the same number of times, so a profile MSFS has reshaped is left alone.
    /// </summary>
    public static string RestoreAxes(string current, string backup)
    {
        foreach (var (action, _) in StickAxes)
        {
            var any = new Regex("<Action ActionName=\"" + action + "\"(?:[^>]*/>|[^>]*(?<!/)>.*?</Action>)", RegexOptions.Singleline);
            var saved = any.Matches(backup);
            if (saved.Count == 0 || saved.Count != any.Matches(current).Count)
                continue;
            var i = 0;
            current = any.Replace(current, _ => saved[i++].Value);
        }
        return current;
    }

    public sealed record Profile(string Name, string DeviceName, int ProductId, string Category, IReadOnlyList<string> BoundAxes);

    /// <summary>Parses one inputprofile file. Null if it is not a device profile.</summary>
    public static Profile? Parse(string xml)
    {
        try
        {
            // The file has several top-level elements; give them one root.
            var body = Regex.Replace(xml, @"<\?xml[^>]*\?>", "");
            var root = XElement.Parse("<r>" + body + "</r>");
            var device = root.Element("Device");
            if (device == null || !int.TryParse((string?)device.Attribute("ProductID"), out var productId))
                return null;
            var category = (string?)device.Element("AircraftInfo")?.Attribute("CategoryName") ?? "";
            var bound = new List<string>();
            foreach (var (action, axis) in StickAxes)
            {
                var hasAxis = device.Descendants("Action")
                    .Where(a => (string?)a.Attribute("ActionName") == action)
                    .Any(a => a.Elements().Where(s => s.Name == "Primary" || s.Name == "Secondary")
                        .Any(s => IsAxisSlot(s.Elements("KEY").Select(k => int.TryParse(k.Value.Trim(), out var c) ? c : -1))));
                if (hasAxis)
                    bound.Add(axis);
            }
            return new Profile((string?)root.Element("FriendlyName") ?? "", (string?)device.Attribute("DeviceName") ?? "",
                productId, category, bound);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The USB product id from a HeavyPro device key ("VID:PID" in hex).</summary>
    public static int? ProductIdFromKey(string? key)
    {
        var parts = (key ?? "").Split(':');
        return parts.Length == 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var pid) ? pid : null;
    }
}
