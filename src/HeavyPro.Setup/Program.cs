using System.Diagnostics;
using System.IO;

namespace HeavyPro.Setup;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro", "community.path");
        if (!File.Exists(marker))
        {
            using var setup = new SetupForm();
            System.Windows.Forms.Application.Run(setup);
            if (!File.Exists(marker))
                return;
        }

        var app = new HeavyFeel.App.App();
        app.Run(new HeavyFeel.App.MainWindow());
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

    private void Install()
    {
        var community = _path.Text.Trim().Trim('"');
        if (community.Length == 0 || !Directory.Exists(community))
        {
            MessageBox.Show(this, "That folder does not exist. Paste the Community folder path.", "HeavyPro");
            return;
        }

        var package = Path.Combine(community, "heavypro-feel");
        WritePackage(package);

        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro");
        Directory.CreateDirectory(appDir);
        var appExe = Path.Combine(appDir, "HeavyPro.exe");
        if (!string.IsNullOrEmpty(Environment.ProcessPath))
            File.Copy(Environment.ProcessPath, appExe, true);
        File.WriteAllText(Path.Combine(appDir, "community.path"), community);
        CreateShortcut(appExe);

        MessageBox.Show(this,
            "Installed. Opening HeavyPro.\n\nRestart MSFS 2024 so the toolbar package loads.",
            "HeavyPro");
        DialogResult = DialogResult.OK;
        Close();
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
        <!DOCTYPE html>
        <html>
        <head>
          <link rel="stylesheet" href="HeavyPro.css" />
          <script type="text/javascript" src="HeavyPro.js"></script>
        </head>
        <body>
          <ingame-ui id="HeavyPro" panel-id="HeavyPro" title="HeavyPro" class="ingameUiFrame" min-width="320" min-height="180">
            <div class="panel">HeavyPro</div>
          </ingame-ui>
        </body>
        </html>
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "InGamePanels", "HeavyPro", "HeavyPro.css"), """
        .panel { color: #eee; background: #1c1c1c; padding: 16px; font: 16px Segoe UI, sans-serif; }
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "InGamePanels", "HeavyPro", "HeavyPro.js"), """
        class IngamePanelHeavyPro extends HTMLElement {
          connectedCallback() {
            this.innerHTML = "<div class='panel'>HeavyPro is installed.</div>";
          }
        }
        if (!window.customElements.get("ingamepanel-heavypro"))
          window.customElements.define("ingamepanel-heavypro", IngamePanelHeavyPro);
        if (typeof checkAutoload === "function") checkAutoload();
        """);
        File.WriteAllText(Path.Combine(package, "html_ui", "icons", "toolbar", "ICON_TOOLBAR_HEAVYPRO.svg"), """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48"><g id="HIGHLIGHT"><rect width="48" height="48" rx="6" fill="#2a2a2a"/><text x="24" y="30" text-anchor="middle" font-size="14" fill="#eee">HP</text></g></svg>
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
    }

    private static void CreateShortcut(string exe)
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
