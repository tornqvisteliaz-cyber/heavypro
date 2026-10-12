using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Services;

/// <summary>A click in the MSFS toolbar panel, delivered through L:HEAVYPRO_REQ_* variables.</summary>
public sealed record ToolbarRequest(bool MasterEnable, FeelLevel? Feel);

/// <summary>App state mirrored to L:HEAVYPRO_* variables for the MSFS toolbar panel.</summary>
public sealed record ToolbarStatus(bool MasterEnable, FeelLevel Feel, int Mode, double RawX, double RawY, double OutX, double OutY)
{
    public const int ModePassThrough = 0;
    public const int ModeShaping = 1;
    public const int ModeNoStick = 2;

    public static int Encode(FeelLevel level) => level switch
    {
        FeelLevel.Normal => 1,
        FeelLevel.Medium => 2,
        FeelLevel.Realistic => 3,
        _ => 0
    };

    public static FeelLevel? Decode(double value) => (int)Math.Round(value) switch
    {
        1 => FeelLevel.Normal,
        2 => FeelLevel.Medium,
        3 => FeelLevel.Realistic,
        _ => null
    };
}
