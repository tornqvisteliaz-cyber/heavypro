using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using HeavyPro.Licensing;
using Rotaidem.Licensing;
using HeavyPro.App.Services;

namespace HeavyPro.App;

public partial class App : System.Windows.Application
{
    // Two copies would both mask the stick and fight over it, so only one runs.
    private static Mutex? _singleInstance;
    private HeavyPro.App.Services.BackgroundTray? _tray;

    public App()
    {
        InitializeComponent();
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowError(ex);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\HeavyPro.SingleInstance", out var first);
        if (!first)
        {
            // The copy already running may be hidden in the tray: ask it to show itself.
            if (!BackgroundTray.SignalRunningCopy())
                FocusRunningCopy();
            Shutdown();
            return;
        }

        // HeavyFeel was renamed to HeavyPro: move the old settings/logs folder across once so
        // existing testers keep their setup instead of starting fresh.
        MigrateDataFolder();

        // With MSFS 2024 holding the GPU, WPF's hardware path was seen drawing a
        // completely blank window. This panel is cheap to draw on the CPU, so
        // that is the default; HeavyPro.exe --gpu-render opts back in.
        if (!e.Args.Any(a => string.Equals(a, "--gpu-render", StringComparison.OrdinalIgnoreCase)))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);

        // No window yet, so closing the licence dialog must not end the app by itself.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The beta terms come first, and again whenever they change.
        if (!HeavyPro.App.Services.Terms.Accepted && new TermsWindow(mustAccept: true).ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var leaseUntil = await EnsureLicensedAsync();
        if (leaseUntil is null)
        {
            Shutdown();
            return;
        }
#if !DEBUG
        // Once HeavyPro is code-signed, a modified program file fails this and shaping stays locked.
        // Dormant (allows start-up) on today's unsigned builds; see AssemblyIntegrity / IntegrityPolicy.
        if (AssemblyIntegrity.Check(out var integrityReason) == HeavyPro.Core.Physics.IntegrityVerdict.Tampered)
        {
            LogLicence("Integrity check failed: " + integrityReason);
            MessageBox.Show(integrityReason, "HeavyPro", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }
#endif
        // Licence confirmed: let the shaping output run until the lease ends. Without this the
        // write path stays a pass-through, so skipping the licence screen does not give a working
        // product — and because the unlock expires by itself, disabling the recheck loop below
        // does not keep it working either.
        HeavyPro.Core.Physics.ControlLimits.Unlock(leaseUntil.Value);

        // Closing the window hides HeavyPro in the tray; Exit there (or turning the option off) quits.
        _tray = new BackgroundTray();
        var background = e.Args.Any(a => string.Equals(a, BackgroundTray.BackgroundArg, StringComparison.OrdinalIgnoreCase));
        MainWindow = new MainWindow(_tray, background);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();

        _ = RecheckLoopAsync();
    }

    /// <summary>Checked and re-armed periodically for as long as the app runs; see <see cref="RecheckLoopAsync"/>.</summary>
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(20);

    /// <summary>The confirmed lease to unlock shaping until, or null: not licensed, quit.</summary>
    private static async Task<DateTimeOffset?> EnsureLicensedAsync()
    {
#if DEBUG
        // Developer builds from Visual Studio skip the check. Release builds,
        // which the installer ships, always enforce it.
        await Task.CompletedTask;
        return DateTimeOffset.MaxValue;
#else
        var store = HeavyProLicense.Store();
        var saved = store.Load();

        // A saved answer signed by the licence server, for this PC and still within its
        // lease, starts at once, so a slow or missing internet connection never blocks
        // start-up. A key without one (older versions) is confirmed online first.
        var offline = HeavyProLicense.CheckOffline();
        if (offline.IsValid)
            return offline.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval);

        string? reason = null;
        if (saved != null)
        {
            var result = await HeavyProLicense.CheckAsync();
            if (result.IsValid)
                return result.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval);
            reason = result.Message;
            LogLicence($"Start-up check rejected the saved key: {result.Status} — {result.Message}");
        }

        var dialog = new LicenseWindow(reason, saved?.Key);
        return dialog.ShowDialog() == true ? dialog.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval) : null;
#endif
    }

    /// <summary>
    /// Re-checks the licence for as long as HeavyPro runs, extending the unlock on success. On
    /// anything else it locks shaping at once (not only on the next expiry) and shuts down.
    /// </summary>
    private async Task RecheckLoopAsync()
    {
        while (true)
        {
            await Task.Delay(RecheckInterval);
            var result = await HeavyProLicense.CheckAsync();
            switch (result.Status)
            {
                case LicenseStatus.Valid:
                case LicenseStatus.OfflineGrace:
                    HeavyPro.Core.Physics.ControlLimits.Unlock(result.LeaseUntil ?? DateTimeOffset.UtcNow.Add(RecheckInterval));
                    // The server being asleep or offline is normal; only warn near the end of the lease.
                    if (result.Status == LicenseStatus.OfflineGrace && result.LeaseUntil is { } until && until - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3))
                        MessageBox.Show(MainWindow!, result.Message, "HeavyPro");
                    break;
                default:
                    // Revoked, removed from this PC, not signed by the real server, or the
                    // lease ran out: stop now. Keep the key on disk (never make the customer
                    // dig it out again) but drop the signed answer, so the next start checks
                    // online and shows the activation window pre-filled with the reason.
                    LogLicence($"Recheck rejected the saved key: {result.Status} — {result.Message}");
                    var rejected = HeavyProLicense.Store().Load();
                    if (rejected != null)
                        HeavyProLicense.Store().Save(rejected.Key, DateTimeOffset.FromUnixTimeSeconds(0));
                    HeavyPro.Core.Physics.ControlLimits.Lock();
                    MessageBox.Show(MainWindow!, result.Message + "\n\nHeavyPro will close. Start it again to re-check or enter a licence key.", "HeavyPro",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown();
                    return;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Rename %AppData%\HeavyFeel to %AppData%\HeavyPro once, if it has not been done yet.</summary>
    private static void MigrateDataFolder()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var old = Path.Combine(appData, "HeavyFeel");
            var renamed = Path.Combine(appData, "HeavyPro");
            if (Directory.Exists(old) && !Directory.Exists(renamed))
                Directory.Move(old, renamed);
        }
        catch (Exception)
        {
            // A fresh install has nothing to migrate; a locked folder falls back to a new one.
        }
    }

    private static void FocusRunningCopy()
    {
        var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(self.ProcessName))
        {
            if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero)
                continue;
            ShowWindow(other.MainWindowHandle, SwRestore);
            SetForegroundWindow(other.MainWindowHandle);
            return;
        }
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(e.Exception);
        e.Handled = true;
    }

    /// <summary>Keeps a record of every licence rejection, so support can see what the server said.</summary>
    private static void LogLicence(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeavyPro", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "licence.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never stop the app.
        }
    }

    private static void ShowError(Exception ex)
    {
        // Always keep a copy: a dialog raised during layout can fail to show.
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeavyPro", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing else to do if the log folder is unwritable.
        }

        MessageBox.Show(
            ex.Message + Environment.NewLine + Environment.NewLine + ex,
            "HeavyPro",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
