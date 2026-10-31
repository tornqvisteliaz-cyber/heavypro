using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using HeavyFeel.App.Services;
using HeavyFeel.App.ViewModels;
using HeavyFeel.Core.Services;
using HeavyFeel.Logging;
using HeavyFeel.SimConnect;

namespace HeavyFeel.App;

public partial class MainWindow : Window
{
    private readonly AppLogger _logger;
    private readonly BackgroundTray _tray;
    private readonly bool _startHidden;
    private bool _exiting;
    private MainViewModel? _vm;
    private INativeMessageClient? _native;
    private HwndSource? _hwndSource;

    /// <param name="startHidden">Started with Windows: go straight to the tray.</param>
    public MainWindow(BackgroundTray tray, bool startHidden)
    {
        InitializeComponent();
        _logger = new AppLogger();
        _tray = tray;
        _startHidden = startHidden;
        tray.OpenRequested += ShowFromTray;
        tray.ExitRequested += ExitFromTray;
        if (startHidden)
        {
            // Shown once so the app starts up (Loaded), then hidden in OnLoaded.
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        var factory = new SimClientFactory(_logger, helper.Handle);
        var client = factory.Create();
        _native = client as INativeMessageClient;

        var settings = new JsonSettingsStore();
        _vm = new MainViewModel(client, settings, _logger, factory.BackendName);
        DataContext = _vm;

        if (_vm.SavedSize.W >= MinWidth && _vm.SavedSize.H >= MinHeight)
        {
            Width = _vm.SavedSize.W;
            Height = _vm.SavedSize.H;
        }

        _vm.Start();

        if (_startHidden)
        {
            Hide();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            _logger.Info("Started with Windows: running in the background.");
            return;
        }

        var welcome = new WelcomeWindow(AppVersion.Current.ToString(3), _vm.TakeWhatsNew()) { Owner = this };
        welcome.Show();
    }

    /// <summary>Closing hides HeavyPro in the tray (it keeps shaping) unless that is turned off or Exit was picked.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm == null)
            return;
        if (!_vm.RunInBackground || _exiting)
        {
            // Quitting for real: with HeavyPro as the only path the stick stops moving aircraft.
            if (_vm.IsOnlyPath && MessageBox.Show(this,
                    "Your stick reaches MSFS only through HeavyPro. Without HeavyPro running it will not move any aircraft.\n\nQuit HeavyPro anyway?",
                    "HeavyPro", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                _exiting = false;
            }
            return;
        }
        e.Cancel = true;
        _vm.PersistWindow(Width, Height);
        Hide();
        _tray.TellStillRunning();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _exiting = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.PersistWindow(Width, Height);
            _vm.Dispose();
        }

        if (_hwndSource != null)
            _hwndSource.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_native != null && msg == _native.MessageId)
        {
            _native.Receive();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
