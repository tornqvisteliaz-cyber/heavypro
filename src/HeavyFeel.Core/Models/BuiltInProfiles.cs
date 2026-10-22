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
        Make("Boeing 777 - Heavy twin", "PMDG 777", 88, 84, 72, 90, 20, 85, 18, realisticRudder: true),
        Make("Boeing 737 - Narrow-body", "PMDG 737", 66, 66, 50, 74, 34, 58, 22, realisticRudder: true),
        Make("Boeing 787 - Dreamliner", "ASOBO 787", 72, 70, 58, 80, 30, 66, 18, realisticRudder: true),
        Make("Boeing 747 - Queen", "Generic Airliner", 96, 92, 84, 96, 12, 92, 14, realisticRudder: true),
        Make("Airbus A320 - Sidestick", "Fenix A320", 48, 52, 40, 70, 46, 50, 20, realisticRudder: true),
        Make("Airbus A350 - Heavy sidestick", "Auto Detect", 64, 66, 52, 84, 36, 70, 16, realisticRudder: true),
        Make("Regional - ATR / Dash 8", "Auto Detect", 46, 48, 36, 56, 50, 44, 30, realisticRudder: true),
        Make("Business jet", "Auto Detect", 40, 42, 30, 50, 56, 38, 24, realisticRudder: false),
        Make("Light GA - Cessna 172", "Generic GA", 22, 30, 18, 36, 70, 24, 40, realisticRudder: true),
        Make("Smooth - beginner", "Auto Detect", 40, 50, 40, 50, 55, 40, 10, realisticRudder: false),
        Make("Max realism - Heavy", "Auto Detect", 100, 96, 90, 100, 8, 96, 20, realisticRudder: true),
    };

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

    private static CustomProfile Make(string name, string aircraft, double inertia, double pitch, double roll, double yaw,
        double response, double ground, double turbulence, bool realisticRudder)
    {
        var settings = new AppSettings
        {
            FeelLevel = FeelLevel.Custom.ToString(),
            AircraftProfile = aircraft,
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
