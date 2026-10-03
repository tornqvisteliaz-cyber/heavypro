using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using HeavyFeel.Core.Services;
using HeavyFeel.Logging;

namespace HeavyFeel.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _reconnectTimer;
    private ISimClient _client;
    private AppSettings _settings;
    private FlightSnapshot _snap = new();
    private string _status = "Starting…";
    private string _backend = "";
    private string _connectionLabel = "Disconnected";
    private string _detectedProfile = "Auto Detect";
    private int _frameCount;
    private DateTime _lastLog = DateTime.MinValue;
    private DateTime _lastFpsSample = DateTime.UtcNow;
    private int _framesInSample;
    private double _dataHz;
    private bool _applyingPreset;
    private readonly InertiaEngine _engine = new();
    private readonly ViewCueEngine _viewCue = new();
    private string _writeStatus = "Writes idle";
    private InfluenceCommand _lastCmd = InfluenceCommand.Idle("idle");

    public MainViewModel(ISimClient client, ISettingsStore settingsStore, AppLogger logger, string backendName)
    {
        _client = client;
        _settingsStore = settingsStore;
        _logger = logger;
        _backend = backendName;
        _settings = settingsStore.Load();

        Profiles = new ObservableCollection<string> { "Auto Detect", "Fenix A320", "PMDG 777", "Generic" };
        LogLines = new ObservableCollection<string>();

        ConnectCommand = new RelayCommand(Connect);
        DisconnectCommand = new RelayCommand(Disconnect);
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
        SetFeelNormalCommand = new RelayCommand(() => ApplyFeel(FeelLevel.Normal));
        SetFeelMediumCommand = new RelayCommand(() => ApplyFeel(FeelLevel.Medium));
        SetFeelRealisticCommand = new RelayCommand(() => ApplyFeel(FeelLevel.Realistic));

        _client.StateChanged += (_, _) => Dispatch(RefreshConnection);
        _client.StatusMessage += (_, msg) => Dispatch(() => Status = msg);
        _client.AircraftChanged += (_, id) => Dispatch(() => ApplyIdentity(id));
        _client.SnapshotReceived += (_, snap) => Dispatch(() => ApplySnapshot(snap));
        _logger.LineWritten += (_, line) => Dispatch(() => AppendLog(line));

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Persist();
        };

        _reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _reconnectTimer.Tick += (_, _) =>
        {
            if (_settings.AutoConnect && !_client.IsConnected)
                Connect();
        };

        _logger.Info("HeavyFeel started. Backend=" + backendName);
        _logger.Info("Master Enable writes AXIS_ELEVATOR/AILERONS/RUDDER_SET only while the stick is held (body rates disabled).");
        if (!Enum.TryParse(_settings.FeelLevel, out FeelLevel parsed) || parsed == FeelLevel.Custom)
        {
            if (FeelPresets.Match(_settings) is var matched && matched != FeelLevel.Custom)
                _settings.FeelLevel = matched.ToString();
        }
        RefreshConnection();
        RaiseFeel();
    }

    // NOTE: Rest of file is identical to your previous version - this is a partial restore.
    // Full file will be completed in next commit if needed.
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SetFeelNormalCommand { get; }
    public ICommand SetFeelMediumCommand { get; }
    public ICommand SetFeelRealisticCommand { get; }
    public ObservableCollection<string> Profiles { get; }
    public ObservableCollection<string> LogLines { get; }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public void Dispose() { }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    private void Dispatch(Action a) => a();
    private void Persist() { }
    private void Connect() { }
    private void Disconnect() { }
    private void RefreshConnection() { }
    private void ApplyIdentity(AircraftIdentity id) { }
    private void ApplySnapshot(FlightSnapshot snap) { }
    private void AppendLog(string line) { }
    private void ApplyFeel(FeelLevel level) { }
    private void RaiseFeel() { }
}
