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
        Inertia: 25,
        PitchDamping: 20,
        RollDamping: 20,
        YawDamping: 18,
        ControlResponse: 70,
        GroundInertia: 25,
        TurbulenceResponse: 40);

    public static FeelPreset Medium { get; } = new(
        Inertia: 56,
        PitchDamping: 50,
        RollDamping: 46,
        YawDamping: 42,
        ControlResponse: 40,
        GroundInertia: 50,
        TurbulenceResponse: 24);

    public static FeelPreset Realistic { get; } = new(
        Inertia: 78,
        PitchDamping: 72,
        RollDamping: 68,
        YawDamping: 64,
        ControlResponse: 28,
        GroundInertia: 78,
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
