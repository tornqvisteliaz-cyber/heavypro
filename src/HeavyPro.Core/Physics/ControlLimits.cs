namespace HeavyPro.Core.Physics;

/// <summary>
/// Runtime enable flag for the shaping output, re-armed by a live licence check rather than set
/// once and trusted for the rest of the process. The SimConnect write path only sends shaped
/// values until the lease time passed to <see cref="Unlock"/> runs out, so a licence that stops
/// checking out — the server rejects it, or the app's own recheck loop stops running — leaves the
/// stick passing straight through again on its own, without depending on that rejection also
/// managing to shut the app down.
/// </summary>
public static class ControlLimits
{
    private static long _unlockedUntilTicks;

    /// <summary>Shaping may run until <paramref name="until"/> (the licence's confirmed lease). Call again before it to keep running.</summary>
    public static void Unlock(DateTimeOffset until) => Interlocked.Exchange(ref _unlockedUntilTicks, until.UtcTicks);

    /// <summary>Stops shaping immediately, without waiting for the lease to run out.</summary>
    public static void Lock() => Interlocked.Exchange(ref _unlockedUntilTicks, 0);

    public static bool Unlocked => IsUnlockedAt(DateTimeOffset.UtcNow);

    /// <summary>Testable without waiting on the real clock.</summary>
    public static bool IsUnlockedAt(DateTimeOffset now) => now.UtcTicks < Interlocked.Read(ref _unlockedUntilTicks);
}
