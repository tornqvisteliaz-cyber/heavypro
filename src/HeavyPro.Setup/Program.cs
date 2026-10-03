using System.Runtime.InteropServices;
using System.Text.Json;

const string Manifest = """
{
  "dependencies": [],
  "content_type": "MISC",
  "title": "HeavyPro",
  "manufacturer": "HeavyPro",
  "creator": "HeavyPro",
  "package_version": "1.0.0",
  "minimum_game_version": "1.0.0",
  "release_notes": { "neutral": { "LastUpdate": "", "OlderHistory": "" } }
}
""";

const string Readme = """
HeavyPro community package.
Installed by HeavyProSetup.exe.
Stock aircraft overrides belong in this package. Fenix and PMDG are locked.
""";

string message;
try
{
    var community = FindCommunity();
    if (community == null)
    {
        message = "HeavyPro did not install. No MSFS Community folder was found. Start MSFS 2024 once, then run this again.";
        Show(message);
        return 1;
    }

    var target = Path.Combine(community, "heavypro-feel");
    Directory.CreateDirectory(Path.Combine(target, "HeavyPro"));
    File.WriteAllText(Path.Combine(target, "manifest.json"), Manifest);
    File.WriteAllText(Path.Combine(target, "HeavyPro", "README.txt"), Readme);

    var files = new[]
    {
        Path.Combine(target, "manifest.json"),
        Path.Combine(target, "HeavyPro", "README.txt")
    };
    var layout = new
    {
        content = files.Select(file => new
        {
            path = Path.GetRelativePath(target, file).Replace('\\', '/'),
            size = new FileInfo(file).Length,
            date = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        }).ToArray()
    };
    File.WriteAllText(Path.Combine(target, "layout.json"), JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
    message = "Installed. Restart MSFS 2024.\n\n" + target;
    Show(message);
    return 0;
}
catch (Exception ex)
{
    Show("HeavyPro install failed.\n\n" + ex.Message);
    return 1;
}

static void Show(string text)
{
    var log = Path.Combine(AppContext.BaseDirectory, "HeavyProSetup.log");
    File.WriteAllText(log, text);
    MessageBox(IntPtr.Zero, text, "HeavyPro", 0);
}

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

static string? FindCommunity()
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var candidates = new[]
    {
        Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community2024"),
        Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
        Path.Combine(appdata, "Microsoft Flight Simulator 2024", "Packages", "Community"),
        Path.Combine(appdata, "Microsoft Flight Simulator", "Packages", "Community")
    };
    foreach (var path in candidates)
    {
        if (Directory.Exists(path))
            return path;
    }

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
            var value = line.Split(' ', 2).Last().Trim().Trim('"');
            var community = Path.Combine(value, "Community");
            if (Directory.Exists(community))
                return community;
        }
    }

    return null;
}

