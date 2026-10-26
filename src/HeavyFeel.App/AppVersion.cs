namespace HeavyFeel.App;

/// <summary>
/// The version this build is (CI stamps it from the release tag, e.g. v0.5.2 -> 0.5.2).
/// Updates come from RotaHub, Rotaidem's add-on manager, not from the app itself.
/// </summary>
public static class AppVersion
{
    public static Version Current { get; } = Normalize(typeof(AppVersion).Assembly.GetName().Version ?? new Version(0, 0, 0));

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));
}
