using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeavyFeel.Core.Services;

/// <summary>An MSFS control profile that still sends stick axes HeavyPro feeds itself.</summary>
public sealed record MsfsAxisBinding(string ProfileName, string DeviceName, IReadOnlyList<string> Axes)
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
    /// For one device (by USB product id), the airplane profile MSFS changed last, and
    /// which of the wanted axes ("pitch", "roll", "yaw") it binds. Null when none.
    /// </summary>
    public static MsfsAxisBinding? FindBinding(IEnumerable<(DateTime Modified, string Xml)> profiles, int productId, IReadOnlyCollection<string> axes)
    {
        var latest = profiles
            .Select(p => (p.Modified, Profile: Parse(p.Xml)))
            .Where(p => p.Profile != null && p.Profile.ProductId == productId
                && string.Equals(p.Profile.Category, "AIRPLANE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Modified)
            .Select(p => p.Profile!)
            .FirstOrDefault();
        if (latest == null)
            return null;
        var bound = latest.BoundAxes.Where(axes.Contains).ToList();
        return bound.Count == 0 ? null : new MsfsAxisBinding(latest.Name, latest.DeviceName, bound);
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
                var hasKey = device.Descendants("Action")
                    .Where(a => (string?)a.Attribute("ActionName") == action)
                    .Any(a => a.Descendants().Any(k => !string.IsNullOrWhiteSpace((string?)k.Attribute("Information"))));
                if (hasKey)
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
