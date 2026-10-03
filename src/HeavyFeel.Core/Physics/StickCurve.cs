namespace HeavyFeel.Core.Physics;

/// <summary>
/// Shapes a physical stick before the simulator sees it.
/// gain 1 = unchanged. Lower gain = heavier, more progressive throw.
/// </summary>
public static class StickCurve
{
    public static double Shape(double raw, double gain)
    {
        var v = Numeric.Clamp(raw, -1.0, 1.0);
        var mag = Math.Abs(v);
        if (mag < 0.02)
            return 0;
        var exp = 1.0 + (1.0 - Numeric.Clamp(gain, 0.35, 1.0)) * 1.8;
        return (v < 0 ? -1 : 1) * Math.Pow(mag, exp);
    }

    public static int ToVJoy(double unit)
    {
        var c = Numeric.Clamp(unit, -1.0, 1.0);
        return (int)Math.Round((c + 1.0) * 0.5 * 32768.0);
    }
}
