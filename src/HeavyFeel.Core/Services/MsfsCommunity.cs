namespace HeavyFeel.Core.Services;

public static class MsfsCommunity
{
    /// <summary>The MSFS 2024 Community folder, from its own UserCfg.opt first (as the installer finds it).</summary>
    public static string? Find2024()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var cfg in new[]
        {
            Path.Combine(appdata, "Microsoft Flight Simulator 2024", "UserCfg.opt"),
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt")
        })
        {
            if (!File.Exists(cfg))
                continue;
            foreach (var line in File.ReadLines(cfg))
            {
                if (!line.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = line.Split(new[] { ' ' }, 2).Last().Trim().Trim('"');
                var community = Path.Combine(value, "Community");
                if (Directory.Exists(community))
                    return community;
            }
        }
        foreach (var path in new[]
        {
            Path.Combine(appdata, "Microsoft Flight Simulator 2024", "Packages", "Community"),
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community")
        })
        {
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }
}
