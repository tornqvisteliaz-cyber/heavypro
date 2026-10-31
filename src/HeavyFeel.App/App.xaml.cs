using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using HeavyPro.Licensing;
using Rotaidem.Licensing;
using HeavyFeel.App.Services;

namespace HeavyFeel.App;

public partial class App : System.Windows.Application
{
    // Two copies would both mask the stick and fight over it, so only one runs.
    private static Mutex? _singleInstance;
    private HeavyFeel.App.Services.BackgroundTray? _tray;

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

        // With MSFS 2024 holding the GPU, WPF's hardware path was seen drawing a
        // completely blank window. This panel is cheap to draw on the CPU, so
        // that is the default; HeavyPro.exe --gpu-render opts back in.
        if (!e.Args.Any(a => string.Equals(a, "--gpu-render", StringComparison.OrdinalIgnoreCase)))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);

        // No window yet, so closing the licence dialog must not end the app by itself.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // The beta terms come first, and again whenever they change.
        if (!HeavyFeel.App.Services.Terms.Accepted && new TermsWindow(mustAccept: true).ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        var confirmedRecently = await EnsureLicensedAsync();
        if (confirmedRecently is null)
        {
            Shutdown();
            return;
        }

        // Closing the window hides HeavyPro in the tray; Exit there (or turning the option off) quits.
        _tray = new BackgroundTray();
        var background = e.Args.Any(a => string.Equals(a, BackgroundTray.BackgroundArg, StringComparison.OrdinalIgnoreCase));
        MainWindow = new MainWindow(_tray, background);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();

        if (confirmedRecently == true)
            await RecheckInBackgroundAsync();
    }

    /// <summary>
    /// Null: not licensed, quit. True: started from a key confirmed within the
    /// offline grace period, still to be re-checked online. False: just checked.
    /// </summary>
    private static async Task<bool?> EnsureLicensedAsync()
    {
#if DEBUG
        // Developer builds from Visual Studio skip the check. Release builds,
        // which the installer ships, always enforce it.
        await Task.CompletedTask;
        return false;
#else
        var store = HeavyProLicense.Store();
        var saved = store.Load();

        // A saved answer signed by the licence server, for this PC and still within its
        // lease, starts at once, so a slow or missing internet connection never blocks
        // start-up. A key without one (older versions) is confirmed online first.
        if (HeavyProLicense.CheckOffline().IsValid)
            return true;

        string? reason = null;
        if (saved != null)
        {
            var result = await HeavyProLicense.CheckAsync();
            if (result.IsValid)
                return false;
            reason = result.Message;
            LogLicence($"Start-up check rejected the saved key: {result.Status} — {result.Message}");
        }

        var dialog = new LicenseWindow(reason, saved?.Key);
        return dialog.ShowDialog() == true ? false : null;
#endif
    }

    private async Task RecheckInBackgroundAsync()
    {
        var result = await HeavyProLicense.CheckAsync();
        switch (result.Status)
        {
            case LicenseStatus.Valid:
                return;
            case LicenseStatus.OfflineGrace:
                // The server being asleep or offline is normal; only warn near the end of the lease.
                if (result.LeaseUntil is { } until && until - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3))
                    MessageBox.Show(MainWindow!, result.Message, "HeavyPro");
                return;
            default:
                // Revoked, removed from this PC, not signed by the real server, or the
                // lease ran out: stop now. Keep the key on disk (never make the customer
                // dig it out again) but drop the signed answer, so the next start checks
                // online and shows the activation window pre-filled with the reason.
                LogLicence($"Background check rejected the saved key: {result.Status} — {result.Message}");
                var rejected = HeavyProLicense.Store().Load();
                if (rejected != null)
                    HeavyProLicense.Store().Save(rejected.Key, DateTimeOffset.FromUnixTimeSeconds(0));
                MessageBox.Show(MainWindow!, result.Message + "\n\nHeavyPro will close. Start it again to re-check or enter a licence key.", "HeavyPro",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeavyFeel", "logs");
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeavyFeel", "logs");
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
