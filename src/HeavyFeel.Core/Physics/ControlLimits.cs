namespace HeavyFeel.Core.Physics;

/// <summary>
/// Runtime enable flag for the shaping output. It is cleared until the app has confirmed its
/// licence; the SimConnect write path only sends shaped values while it is set, so merely
/// skipping the start-up licence screen leaves the stick passing straight through (no feel)
/// instead of a working product. The app sets it in one place after a successful licence check.
/// </summary>
public static class ControlLimits
{
    private static volatile bool _unlocked;

    public static void Unlock() => _unlocked = true;

    public static bool Unlocked => _unlocked;
}
