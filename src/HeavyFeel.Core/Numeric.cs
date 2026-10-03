namespace HeavyFeel.Core;

public static class Numeric
{
    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
