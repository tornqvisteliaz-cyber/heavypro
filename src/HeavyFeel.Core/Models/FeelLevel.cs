namespace HeavyFeel.Core.Models;

public enum FeelLevel
{
    Normal = 0,
    Medium = 1,
    Realistic = 2,
    Custom = 3
}

public readonly record struct FeelPreset(
    double Inertia,
    double PitchDamping,
    double RollDamping,
    double YawDamping,
    double ControlResponse,
    double GroundInertia,
    double TurbulenceResponse);

public static class FeelPresets
{
    public static FeelPreset Normal { get; } = new(
        Inertia: 28,
        PitchDamping: 36,
        RollDamping: 20,
        YawDamping: 40,
        ControlResponse: 66,
        GroundInertia: 28,
        TurbulenceResponse: 40);

    public static FeelPreset Medium { get; } = new(
        Inertia: 62,
        PitchDamping: 68,
        RollDamping: 46,
        YawDamping: 76,
        ControlResponse: 38,
        GroundInertia: 52,
        TurbulenceResponse: 24);

    // 0.5.1 had 94/88/76/92/16/88: every axis sat on the 4 s travel ceiling and a C152
    // took 1.25 s to answer a 60 % input, which felt like no response. Now 0.77 s there,
    // still well above Medium (0.45 s); heavy jets stay at the ceiling as before.
    public static FeelPreset Realistic { get; } = new(
        Inertia: 84,
        PitchDamping: 72,
        RollDamping: 58,
        YawDamping: 80,
        ControlResponse: 26,
        GroundInertia: 78,
        TurbulenceResponse: 16);

    /// <summary>Realistic as 0.5.1 shipped it, to move saved settings to the new values.</summary>
    public static FeelPreset RealisticBefore052 { get; } = new(94, 88, 76, 92, 16, 88, 16);

    public static FeelPreset For(FeelLevel level) => level switch
    {
        FeelLevel.Normal => Normal,
        FeelLevel.Realistic => Realistic,
        _ => Medium
    };

    public static FeelLevel Match(AppSettings settings)
    {
        if (Matches(settings, Normal)) return FeelLevel.Normal;
        if (Matches(settings, Medium)) return FeelLevel.Medium;
        if (Matches(settings, Realistic)) return FeelLevel.Realistic;
        return FeelLevel.Custom;
    }

    public static bool Matches(AppSettings settings, FeelPreset preset)
    {
        return Nearly(settings.Inertia, preset.Inertia)
            && Nearly(settings.PitchDamping, preset.PitchDamping)
            && Nearly(settings.RollDamping, preset.RollDamping)
            && Nearly(settings.YawDamping, preset.YawDamping)
            && Nearly(settings.ControlResponse, preset.ControlResponse)
            && Nearly(settings.GroundInertia, preset.GroundInertia)
            && Nearly(settings.TurbulenceResponse, preset.TurbulenceResponse);
    }

    private static bool Nearly(double a, double b) => Math.Abs(a - b) < 0.51;
}
