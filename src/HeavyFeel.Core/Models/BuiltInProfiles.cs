namespace HeavyFeel.Core.Models;

/// <summary>
/// Ready-made profiles added to everyone's profile list once. They are ordinary
/// custom profiles: the user can apply, change, share or delete them.
/// Starting points tuned by feel, not manufacturer control-system data.
/// </summary>
public static class BuiltInProfiles
{
    public static IReadOnlyList<CustomProfile> All { get; } = new[]
    {
        Make("Boeing 777 - Heavy twin", 88, 84, 72, 90, 20, 85, 18, realisticRudder: true),
        Make("Boeing 737 - Narrow-body", 66, 66, 50, 74, 34, 58, 22, realisticRudder: true),
        Make("Boeing 787 - Dreamliner", 72, 70, 58, 80, 30, 66, 18, realisticRudder: true),
        Make("Boeing 747 - Queen", 96, 92, 84, 96, 12, 92, 14, realisticRudder: true),
        Make("Airbus A320 - Sidestick", 48, 52, 40, 70, 46, 50, 20, realisticRudder: true),
        Make("Airbus A350 - Heavy sidestick", 64, 66, 52, 84, 36, 70, 16, realisticRudder: true),
        Make("Regional - ATR / Dash 8", 46, 48, 36, 56, 50, 44, 30, realisticRudder: true),
        Make("Business jet", 40, 42, 30, 50, 56, 38, 24, realisticRudder: false),
        Make("Light GA - Cessna 172", 22, 30, 18, 36, 70, 24, 40, realisticRudder: true),
        Make("Smooth - beginner", 40, 50, 40, 50, 55, 40, 10, realisticRudder: false),
        Make("Max realism - Heavy", 100, 96, 90, 100, 8, 96, 20, realisticRudder: true),
    };

    /// <summary>
    /// 0.5.0 shipped built-ins pinned to an aircraft ("Generic GA" for the C172 one), which
    /// then shaped an A350 as a light aircraft. Unpins every profile with a built-in name.
    /// </summary>
    public static int Unpin(List<CustomProfile> profiles)
    {
        var count = 0;
        foreach (var profile in profiles)
        {
            if (profile.AircraftProfile == "Auto Detect" || !All.Any(b => string.Equals(b.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            profile.AircraftProfile = "Auto Detect";
            count++;
        }
        return count;
    }

    /// <summary>Adds every built-in profile the list does not already have by name.</summary>
    public static int AddMissing(List<CustomProfile> profiles)
    {
        var added = 0;
        foreach (var builtIn in All)
        {
            if (profiles.Any(p => string.Equals(p.Name, builtIn.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var settings = new AppSettings();
            builtIn.ApplyTo(settings);
            profiles.Add(CustomProfile.Capture(builtIn.Name, settings));
            added++;
        }
        return added;
    }

    private static CustomProfile Make(string name, double inertia, double pitch, double roll, double yaw,
        double response, double ground, double turbulence, bool realisticRudder)
    {
        var settings = new AppSettings
        {
            FeelLevel = FeelLevel.Custom.ToString(),
            // Never pin an aircraft: a profile applied in one aircraft must not
            // carry GA or airliner dynamics into the next one.
            AircraftProfile = "Auto Detect",
            Inertia = inertia,
            PitchDamping = pitch,
            RollDamping = roll,
            YawDamping = yaw,
            ControlResponse = response,
            GroundInertia = ground,
            TurbulenceResponse = turbulence,
            RealisticRudder = realisticRudder
        };
        return CustomProfile.Capture(name, settings);
    }
}
