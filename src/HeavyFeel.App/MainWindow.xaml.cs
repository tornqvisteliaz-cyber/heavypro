using System.Windows;
using System.Windows.Interop;
using HeavyFeel.App.Input;
using HeavyFeel.App.ViewModels;
using HeavyFeel.Core.Services;
using HeavyFeel.Logging;
using HeavyFeel.SimConnect;

namespace HeavyFeel.App;

public partial class MainWindow : Window
{
    private readonly AppLogger _logger;
    private MainViewModel? _vm;
    private INativeMessageClient? _native;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        InitializeComponent();
        _logger = new AppLogger();
        Loaded += OnLoaded;
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

        var pump = new HardwareCurvePump(() => _vm.Settings, () => _vm.Snapshot, _logger.Info);
        var curveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        curveTimer.Tick += (_, _) =>
        {
            pump.Tick();
            _vm.HardwareStatus = pump.LastStatus;
        };
        curveTimer.Start();

        var welcome = new WelcomeWindow { Owner = this };
        welcome.ShowDialog();
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
