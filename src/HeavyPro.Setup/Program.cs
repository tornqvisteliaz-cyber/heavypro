using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using HeavyPro.Licensing;
using Rotaidem.Licensing;

namespace HeavyPro.Setup;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Hosting providers refuse old TLS versions, which .NET Framework can still default to.
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // "/update": started by the app's Update button.
        using var setup = new SetupForm(args.Any(a => string.Equals(a, "/update", StringComparison.OrdinalIgnoreCase)));
        System.Windows.Forms.Application.Run(setup);
    }
}

sealed class SetupForm : Form
{
    // Rotaidem brand colours, matching rotaidem.com and the app.
    private static readonly Color Bg = Color.FromArgb(0x11, 0x13, 0x15);
    private static readonly Color Field = Color.FromArgb(0x1B, 0x1F, 0x22);
    private static readonly Color Ink = Color.FromArgb(0xEE, 0xF0, 0xF2);
    private static readonly Color Muted = Color.FromArgb(0x87, 0x8D, 0x94);
    private static readonly Color Accent = Color.FromArgb(0xAA, 0xB6, 0xC2);
    private static readonly Color Danger = Color.FromArgb(0xE5, 0x48, 0x4D);

    private readonly TextBox _key = new() { CharacterCasing = CharacterCasing.Upper };
    private readonly Button _install = new() { Text = "Install HeavyPro" };
    private readonly Label _status = new();
    private readonly bool _updateMode;

    public SetupForm(bool update = false)
    {
        Text = "Install HeavyPro";
        ClientSize = new Size(560, 430);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 10f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

        var logo = new PictureBox { Left = 36, Top = 36, Width = 64, Height = 64, SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
        var title = new Label { Text = "HeavyPro", Left = 116, Top = 36, AutoSize = true, Font = new Font("Segoe UI Semibold", 22f), ForeColor = Ink };
        var by = new Label { Text = "by Rotaidem", Left = 120, Top = 78, AutoSize = true, ForeColor = Muted };
        var intro = new Label
        {
            Text = "Makes your stick feel like flying a real aircraft in Microsoft Flight Simulator 2024: "
                 + "weight, momentum and a smooth return to centre.",
            Left = 36, Top = 128, Width = 488, Height = 46, ForeColor = Muted
        };

        var keyLabel = new Label { Text = "Licence key", Left = 36, Top = 196, AutoSize = true, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 10f) };
        _key.SetBounds(36, 222, 488, 32);
        _key.BackColor = Field;
        _key.ForeColor = Ink;
        _key.BorderStyle = BorderStyle.FixedSingle;
        _key.Font = new Font("Consolas", 13f);
        var keyHint = new Label { Text = "From your purchase email. It looks like HPRO-XXXX-XXXX-XXXX.", Left = 36, Top = 262, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9f) };

        _install.SetBounds(36, 306, 488, 46);
        _install.FlatStyle = FlatStyle.Flat;
        _install.FlatAppearance.BorderSize = 0;
        _install.BackColor = Accent;
        _install.ForeColor = Color.FromArgb(0x0B, 0x0C, 0x0E);
        _install.Font = new Font("Segoe UI Semibold", 11f);
        _install.Cursor = Cursors.Hand;
        _install.Click += async (_, _) => await ActivateThenInstall();
        AcceptButton = _install;

        _status.SetBounds(36, 362, 488, 40);
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 9f);
        _status.Text = "Installs into your user folder and adds Start menu and desktop shortcuts.";

        // Reinstalling on a computer that is already activated should not ask again.
        var saved = HeavyProLicense.Store().Load();
        if (saved != null)
            _key.Text = saved.Key;
        // From the Update button on an activated PC: install straight away, nothing to answer.
        _updateMode = update && saved != null;
        if (_updateMode)
        {
            Text = "Updating HeavyPro";
            _install.Text = "Updating…";
            _install.Enabled = false;
            _status.Text = "Updating HeavyPro. It opens again when done.";
            Shown += (_, _) => BeginInvoke(new Action(Install));
        }
        HeavyProLicense.WakeServer();
        _key.HandleCreated += (_, _) => SetPlaceholder(_key, "HPRO-XXXX-XXXX-XXXX");
        // Open with the cursor after a pre-filled key rather than the whole key selected.
        Shown += (_, _) => { _key.SelectionStart = _key.Text.Length; _key.SelectionLength = 0; };
        Controls.AddRange(new Control[] { logo, title, by, intro, keyLabel, _key, keyHint, _install, _status });
    }

    private static Image? LoadLogo()
    {
        using var stream = typeof(SetupForm).Assembly.GetManifestResourceStream("HeavyPro.Setup.Logo.png");
        return stream == null ? null : Image.FromStream(new MemoryStream(ReadAll(stream)));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private void ShowStatus(string text, bool problem = false)
    {
        _status.ForeColor = problem ? Danger : Muted;
        _status.Text = text;
    }

    private async Task ActivateThenInstall()
    {
        if (!LicenseKey.IsWellFormed(_key.Text, LicenseKey.HeavyProPrefix))
        {
            ShowStatus("That licence key has a typo. It should look like HPRO-XXXX-XXXX-XXXX.", problem: true);
            return;
        }

        _install.Enabled = false;
        _install.Text = "Checking your key…";
        ShowStatus("Contacting the licence server. This can take up to a minute.");
        try
        {
            var result = await HeavyProLicense.ActivateAsync(_key.Text);
            if (!result.IsValid)
            {
                ShowStatus(result.Message, problem: true);
                return;
            }
            HeavyProLicense.Save(_key.Text, result);
        }
        finally
        {
            _install.Enabled = true;
            _install.Text = "Install HeavyPro";
        }

        Install();
    }

    // TextBox.PlaceholderText does not exist on .NET Framework; Windows' cue banner does the same.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private static void SetPlaceholder(TextBox box, string text) => SendMessage(box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, text);

    private void Install()
    {
        if (!BundleInstaller.HasPayload)
        {
            ShowStatus("This setup file was built without the app. Download the HeavyPro installer again.", problem: true);
            return;
        }

        var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro");
        Directory.CreateDirectory(appDir);
        if (!CloseRunningHeavyPro())
            return;

        try
        {
            _install.Enabled = false;
            ShowStatus("Installing…");
            BundleInstaller.Extract(appDir);
            FeelSettings.Install(appDir);
            var panel = PanelInstaller.Install();
            var appExe = Path.Combine(appDir, "HeavyPro.exe");
            SimConnectLocator.CreateShortcut(appExe);

            if (!_updateMode)
                MessageBox.Show(this, panel != null
                    ? "HeavyPro is installed and will open now.\n\nIn MSFS 2024, the HeavyPro panel is in the toolbar."
                    : "HeavyPro is installed and will open now.\n\nThe MSFS 2024 Community folder was not found, so the in-game panel was not added. The app works without it.",
                    "HeavyPro");
            DialogResult = DialogResult.OK;
            Close();
            Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _install.Enabled = true;
            _install.Text = "Install HeavyPro";
            ShowStatus("HeavyPro could not be installed. Close HeavyPro and try again. " + ex.Message, problem: true);
        }
    }

    private bool CloseRunningHeavyPro()
    {
        var processes = Process.GetProcessesByName("HeavyPro");
        if (processes.Length == 0)
            return true;
        if (_updateMode)
        {
            // The app closes itself after starting the update; give it time before asking.
            foreach (var process in processes)
            {
                process.WaitForExit(15000);
                process.Dispose();
            }
            processes = Process.GetProcessesByName("HeavyPro");
            if (processes.Length == 0)
                return true;
        }

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

/// <summary>
/// Writes the app's feel settings (travel scale) into HeavyPro's own folder and removes the
/// in-game toolbar package that earlier versions put in the MSFS Community folder.
/// The app finds the file through community.path, so it works unchanged.
/// </summary>
static class FeelSettings
{
    private const string Package = "heavypro-feel";

    public static void Install(string appDir)
    {
        var marker = Path.Combine(appDir, "community.path");
        var oldCommunity = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        var feelRoot = Path.Combine(appDir, "feel");

        // Keep a travel scale the user already tuned in the old package.
        var settings = Path.Combine(feelRoot, Package, "HeavyPro", "profiles.json");
        var existing = new[] { settings, oldCommunity == null ? null : Path.Combine(oldCommunity, Package, "HeavyPro", "profiles.json") }
            .FirstOrDefault(f => f != null && File.Exists(f));
        var text = existing != null ? File.ReadAllText(existing) : """
        {
          "travelScale": 1.4,
          "note": "HeavyPro reads this file. 1.0 is normal. 1.4 is heavier. 2.0 is slow. Restart the app after changing it."
        }
        """;
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, text);
        File.WriteAllText(marker, feelRoot);

        // No toolbar any more: remove the package earlier versions installed.
        foreach (var community in new[] { oldCommunity, CommunityFinder.Find() }.Where(c => c != null).Distinct())
        {
            RemoveOldPackage(community!);
            var sibling = Path.Combine(Path.GetDirectoryName(community!) ?? community!, "Community2024");
            RemoveOldPackage(sibling);
        }
    }

    private static void RemoveOldPackage(string community)
    {
        try
        {
            var package = Path.Combine(community, Package);
            var manifest = Path.Combine(package, "manifest.json");
            // Only ever delete our own package.
            if (File.Exists(manifest) && File.ReadAllText(manifest).Contains("\"creator\": \"HeavyPro\""))
                Directory.Delete(package, recursive: true);
        }
        catch (Exception)
        {
            // A leftover toolbar package is harmless; never fail the install over it.
        }
    }
}

/// <summary>
/// Puts the in-game HeavyPro panel (toolbar panel plus WASM bridge) in the MSFS 2024
/// Community folder. Never fails the install: the app works without it.
/// </summary>
static class PanelInstaller
{
    private const string Package = "rotaidem-heavypro-panel";

    public static string? Install()
    {
        try
        {
            var community = CommunityFinder.Find2024();
            using var stream = typeof(PanelInstaller).Assembly.GetManifestResourceStream("HeavyProPanel.zip");
            if (community == null || stream == null)
                return null;

            var target = Path.Combine(community, Package);
            var manifest = Path.Combine(target, "manifest.json");
            // While MSFS runs it holds the package's files: overwrite them in place instead
            // of deleting the folder, so a locked file can never leave half a package.
            var simRunning = Process.GetProcessesByName("FlightSimulator2024").Length > 0;
            if (Directory.Exists(target))
            {
                // Only ever replace our own package.
                if (!File.Exists(manifest) || !File.ReadAllText(manifest).Contains("\"creator\": \"Rotaidem\""))
                    return null;
                if (!simRunning)
                    Directory.Delete(target, recursive: true);
            }

            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            var root = Path.GetFullPath(community) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith("/"))
                    continue;
                var file = Path.GetFullPath(Path.Combine(community, entry.FullName));
                if (!file.StartsWith(root + Package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid panel package path.");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                try
                {
                    entry.ExtractToFile(file, true);
                }
                catch (IOException) when (simRunning)
                {
                    // In use by the sim: the copy already there stays until the next install.
                }
            }
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

static class CommunityFinder
{
    /// <summary>The MSFS 2024 Community folder, from its own UserCfg.opt first.</summary>
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
                var value = line.Split(new[] { ' ' }, 2).Last().Trim().Trim('"');
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
