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

    public static FeelPreset Realistic { get; } = new(
        Inertia: 94,
        PitchDamping: 88,
        RollDamping: 76,
        YawDamping: 92,
        ControlResponse: 16,
        GroundInertia: 88,
        TurbulenceResponse: 16);

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

    private static bool Matches(AppSettings settings, FeelPreset preset)
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
