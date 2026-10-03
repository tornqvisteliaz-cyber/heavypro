using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace HeavyPro.Setup;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var setup = new SetupForm();
        var detected = CommunityFinder.Find();
        if (!string.IsNullOrEmpty(detected))
            setup.SetPath(detected);
        System.Windows.Forms.Application.Run(setup);
    }
}

sealed class SetupForm : Form
{
    private readonly TextBox _path = new() { Width = 420 };

    public SetupForm()
    {
        Text = "HeavyPro";
        Width = 560;
        Height = 220;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var label = new Label
        {
            Text = "Where is your community folder?",
            Left = 16,
            Top = 16,
            Width = 500
        };
        _path.Left = 16;
        _path.Top = 44;
        var browse = new Button { Text = "Browse", Left = 444, Top = 42, Width = 80 };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Where is your community folder?" };
            if (dialog.ShowDialog() == DialogResult.OK)
                _path.Text = dialog.SelectedPath;
        };
        var install = new Button { Text = "Install", Left = 16, Top = 88, Width = 120 };
        install.Click += (_, _) => Install();
        Controls.Add(label);
        Controls.Add(_path);
        Controls.Add(browse);
        Controls.Add(install);
    }

    public void SetPath(string path) => _path.Text = path;

    private void Install()
    {
        if (!BundleInstaller.HasPayload)
        {
            MessageBox.Show(this, "This setup file was built without the MSFS-enabled app. Download the MSFS-enabled HeavyPro installer.", "HeavyPro");
            return;
        }

        var community = _path.Text.Trim().Trim('"');
        if (community.Length == 0 || !Directory.Exists(community))
        {
            MessageBox.Show(this, "That folder does not exist. Paste the Community folder path.", "HeavyPro");
            return;
        }

        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro");
        Directory.CreateDirectory(appDir);
        if (!CloseRunningHeavyPro())
            return;

        try
        {
            BundleInstaller.Extract(appDir);
            PackageInstaller.Install(community);
            var appExe = Path.Combine(appDir, "HeavyPro.exe");
            File.WriteAllText(Path.Combine(appDir, "community.path"), community);
            SimConnectLocator.CreateShortcut(appExe);

            MessageBox.Show(this,
                "Installed. Opening HeavyPro.\n\nRestart MSFS 2024. The toolbar icon needs InGamePanels/HeavyPro.spb from the MSFS SDK. HTML alone does not add the icon.",
                "HeavyPro");
            DialogResult = DialogResult.OK;
            Close();
            Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"HeavyPro could not be updated. Close HeavyPro and try again.\n\n{ex.Message}",
                "HeavyPro setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool CloseRunningHeavyPro()
    {
        var processes = Process.GetProcessesByName("HeavyPro");
        if (processes.Length == 0)
            return true;

        var answer = MessageBox.Show(this,
            "HeavyPro is open and must close before setup can update it. Save any settings, then choose Yes to close HeavyPro and continue.",
            "Close HeavyPro?", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
        {
            foreach (var process in processes)
                process.Dispose();
            return false;
        }

        try
        {
            foreach (var process in processes)
            {
                if (!process.HasExited)
                    process.CloseMainWindow();
            }

            foreach (var process in processes)
            {
                if (!process.HasExited && !process.WaitForExit(7000))
                {
                    process.Kill();
                    if (!process.WaitForExit(5000))
                    {
                        MessageBox.Show(this,
                            "HeavyPro could not be closed. Restart Windows, then run setup again.",
                            "HeavyPro setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            return true;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}

static class BundleInstaller
{
    public static bool HasPayload => typeof(BundleInstaller).Assembly.GetManifestResourceNames().Contains("HeavyProPayload.zip");

    public static void Extract(string destination)
    {
        var assembly = typeof(BundleInstaller).Assembly;
        using var stream = assembly.GetManifestResourceStream("HeavyProPayload.zip")
            ?? throw new InvalidOperationException("This setup file has no app payload. Download the MSFS-enabled HeavyPro installer.");
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var file = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!file.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid app payload path.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            entry.ExtractToFile(file, true);
        }
    }
}

static class PackageInstaller
{
    public static void Install(string community)
    {
        WritePackage(Path.Combine(community, "heavypro-feel"));
        var sibling = Path.Combine(Path.GetDirectoryName(community) ?? community, "Community2024");
        if (Directory.Exists(sibling))
            WritePackage(Path.Combine(sibling, "heavypro-feel"));
        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "community.path"), community);
    }

    private static void WritePackage(string package)
    {
        Directory.CreateDirectory(Path.Combine(package, "html_ui", "InGamePanels", "HeavyPro"));
        Directory.CreateDirectory(Path.Combine(package, "html_ui", "icons", "toolbar"));
        File.WriteAllText(Path.Combine(package, "manifest.json"), """
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
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "InGamePanels", "HeavyPro", "HeavyPro.html"), """
        <script type="text/html" import-script="/JS/dataStorage.js"></script>
        <script type="text/html" import-script="/JS/simvar.js"></script>
        <script type="text/html" import-script="/Pages/VCockpit/Instruments/Shared/BaseInstrument.js"></script>
        <link rel="stylesheet" href="HeavyPro.css" />
        <script type="text/javascript" src="HeavyPro.js"></script>
        <ingamepanel-custom>
          <ingame-ui id="HeavyPro" panel-id="HeavyPro" title="HeavyPro" class="ingameUiFrame" min-width="320" min-height="180" content-fit="true">
            <div class="panel">HeavyPro</div>
          </ingame-ui>
        </ingamepanel-custom>
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "InGamePanels", "HeavyPro", "HeavyPro.js"), """
        class IngamePanelHeavyPro extends HTMLElement {
          connectedCallback() {
            this.innerHTML = "<div class='panel'>HeavyPro</div>";
          }
        }
        window.customElements.define("ingamepanel-custom", IngamePanelHeavyPro);
        setTimeout(() => {
          const panel = document.getElementById("HeavyPro");
          if (!panel) return;
          panel.classList.remove("hide");
          panel.classList.remove("panelInvisible");
        }, 500);
        if (typeof checkAutoload === "function") checkAutoload();
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "icons", "toolbar", "ICON_TOOLBAR_HEAVYPRO.svg"), """
        <?xml version="1.0" encoding="utf-8"?>
        <svg version="1.1" id="Titre" xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <title>HeavyPro</title>
          <g id="HIGHLIGHT">
            <rect width="64" height="64" rx="8" fill="#2a2a2a"/>
            <text x="32" y="40" text-anchor="middle" font-size="18" fill="#eeeeee">HP</text>
          </g>
        </svg>
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "InGamePanels", "HeavyPro", "HeavyPro.css"), """
        .panel { color: #eee; background: #1c1c1c; padding: 16px; font: 16px Segoe UI, sans-serif; }
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "icons", "toolbar", "ICON_TOOLBAR_HEAVYPRO.svg"), """
        <?xml version="1.0" encoding="utf-8"?>
        <svg version="1.1" id="Titre" xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <title>HeavyPro</title>
          <g id="HIGHLIGHT">
            <rect width="64" height="64" rx="8" fill="#2a2a2a"/>
            <text x="32" y="40" text-anchor="middle" font-size="18" fill="#eeeeee">HP</text>
          </g>
        </svg>
        """);

        var files = Directory.GetFiles(package, "*", SearchOption.AllDirectories);
        var layout = new
        {
            content = files.Select(file => new
            {
                path = Path.GetRelativePath(package, file).Replace('\\', '/'),
                size = new FileInfo(file).Length,
                date = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            }).ToArray()
        };
        File.WriteAllText(Path.Combine(package, "layout.json"), System.Text.Json.JsonSerializer.Serialize(layout));
        Directory.CreateDirectory(Path.Combine(package, "HeavyPro"));
        File.WriteAllText(Path.Combine(package, "HeavyPro", "TOOLBAR.txt"),
            "MSFS 2024 only shows a toolbar icon if InGamePanels/HeavyPro.spb exists. That file is compiled by the MSFS SDK Project Editor. HTML alone does not create the icon.");
    }
}

static class CommunityFinder
{
    public static string? Find()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new[]
        {
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community2024"),
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
}

static class SimConnectLocator
{
    public static void CreateShortcut(string exe)
    {
        var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "HeavyPro.lnk");
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HeavyPro.lnk");
        foreach (var link in new[] { start, desktop })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var script = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{link.Replace("'", "''")}');$s.TargetPath='{exe.Replace("'", "''")}';$s.WorkingDirectory='{Path.GetDirectoryName(exe)!.Replace("'", "''")}';$s.Save()";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"" + script + "\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(5000);
        }
    }
}
