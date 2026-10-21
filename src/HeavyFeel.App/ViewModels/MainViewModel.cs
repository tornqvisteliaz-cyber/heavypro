using HeavyFeel.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using HeavyFeel.App.Input;
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
    private DateTime _lastUiRefresh = DateTime.MinValue;
    private DateTime _lastMeterRefresh = DateTime.MinValue;
    private DateTime _lastFpsSample = DateTime.UtcNow;
    private int _framesInSample;
    private double _dataHz;
    private bool _applyingPreset;
    private readonly InertiaEngine _engine = new();
    private readonly ViewCueEngine _viewCue = new();
    private string _writeStatus = "Writes idle";
    private InfluenceCommand _lastCmd = InfluenceCommand.Idle("idle");
    private readonly DispatcherTimer _stickTimer;
    private DateTime _lastStickUi = DateTime.MinValue;
    private DateTime _lastDeviceScan = DateTime.MinValue;
    private double[] _pitchAxes = new double[6];
    private string _stickStatus = "";
    private string? _detecting;
    private DateTime _detectUntil;
    private Dictionary<string, double[]> _detectBase = new();
    private readonly Dictionary<string, (double Delta, int Axis, double Sign)> _detectBest = new();
    private const string NoRudder = "MSFS keeps the rudder";

    public MainViewModel(ISimClient client, ISettingsStore settingsStore, AppLogger logger, string backendName)
    {
        _client = client;
        _settingsStore = settingsStore;
        _logger = logger;
        _backend = backendName;
        _settings = settingsStore.Load();
        _settings.MasterEnable = false;
        if (_settings.SettingsVersion < 3)
        {
            // 0.3.x shipped a 25 % deadzone slider and let a profile be pinned;
            // both made heavy jets feel dead. Start everyone from sane values once.
            _settings.InputDeadzone = Math.Min(_settings.InputDeadzone, 0.03);
            _settings.AircraftProfile = "Auto Detect";
            _settings.InputRoute = "Direct";
            _settings.SettingsVersion = 3;
            settingsStore.Save(_settings);
        }
        _client.DirectInput = IsDirect;
        StickDevices = new ObservableCollection<JoystickDevice>();
        RudderDevices = new ObservableCollection<JoystickDevice>();
        AxisNames = new ObservableCollection<string>(JoystickReader.AxisNames);
        InputRoutes = new ObservableCollection<string> { RouteDirectLabel, RouteMaskedLabel };
        RefreshDevices();
        DetectPitchCommand = new RelayCommand(() => StartDetect("pitch"));
        DetectRollCommand = new RelayCommand(() => StartDetect("roll"));
        DetectRudderCommand = new RelayCommand(() => StartDetect("rudder"));
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);
        _stickTimer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(8) };
        _stickTimer.Tick += (_, _) => PollStick();
        _stickTimer.Start();

        Profiles = new ObservableCollection<string> { "Auto Detect", "Fenix A320", "PMDG 737", "PMDG 777", "ASOBO 787", "Generic GA", "Generic Airliner" };
        InputCurves = new ObservableCollection<string> { "Linear", "Expo", "S-Curve" };
        ReleaseModes = new ObservableCollection<string> { "Auto", "Immediate", "Damped", "Delayed" };
        LogLines = new ObservableCollection<string>();

        ConnectCommand = new RelayCommand(Connect);
        DisconnectCommand = new RelayCommand(Disconnect);
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
        SetFeelNormalCommand = new RelayCommand(() => ApplyFeel(FeelLevel.Normal));
        SetFeelMediumCommand = new RelayCommand(() => ApplyFeel(FeelLevel.Medium));
        SetFeelRealisticCommand = new RelayCommand(() => ApplyFeel(FeelLevel.Realistic));
        CustomProfiles = new ObservableCollection<CustomProfile>(_settings.CustomProfiles);
        SaveProfileCommand = new RelayCommand(SaveProfile);
        ApplyProfileCommand = new RelayCommand(ApplyProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile);

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SetFeelNormalCommand { get; }
    public ICommand SetFeelMediumCommand { get; }
    public ICommand SetFeelRealisticCommand { get; }

    // ---------------- Custom profiles ----------------
    public ObservableCollection<CustomProfile> CustomProfiles { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }

    private CustomProfile? _selectedCustomProfile;
    public CustomProfile? SelectedCustomProfile
    {
        get => _selectedCustomProfile;
        set
        {
            if (ReferenceEquals(_selectedCustomProfile, value)) return;
            Set(ref _selectedCustomProfile, value);
            if (value != null)
                NewProfileName = value.Name;
        }
    }

    private string _newProfileName = "";
    public string NewProfileName
    {
        get => _newProfileName;
        set => Set(ref _newProfileName, value ?? "");
    }

    /// <summary>Saves the current tuning under the typed name; an existing name is overwritten.</summary>
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0)
            name = "Profile " + (CustomProfiles.Count + 1);
        var profile = CustomProfile.Capture(name, _settings);
        var existing = CustomProfiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var index = CustomProfiles.IndexOf(existing);
            CustomProfiles[index] = profile;
            _logger.Info($"Profile \"{name}\" updated with the current tuning.");
        }
        else
        {
            CustomProfiles.Add(profile);
            _logger.Info($"Profile \"{name}\" saved.");
        }
        SyncProfiles();
        SelectedCustomProfile = profile;
    }

    private void ApplyProfile()
    {
        var profile = SelectedCustomProfile;
        if (profile == null) return;
        profile.ApplyTo(_settings);
        foreach (var name in new[]
        {
            nameof(Inertia), nameof(PitchDamping), nameof(RollDamping), nameof(YawDamping), nameof(ControlResponse),
            nameof(GroundInertia), nameof(TurbulenceResponse), nameof(SelectedInputCurve), nameof(InputDeadzonePercent),
            nameof(InputSensitivityPercent), nameof(ExpoStrengthPercent), nameof(InputAccelerationPercent),
            nameof(InputDecelerationPercent), nameof(PitchRateLimit), nameof(RollRateLimit), nameof(YawRateLimit),
            nameof(AirspeedResponseEnabled), nameof(AirspeedResponsePercent), nameof(RudderLimiter), nameof(StickReleaseMode),
            nameof(ReturnDelayMs), nameof(SelectedProfile), nameof(PreviewTau), nameof(DebugPanel)
        })
            OnChanged(name);
        RaiseFeel();
        _logger.Info($"Profile \"{profile.Name}\" applied — used on the next write frame.");
        ScheduleSave();
    }

    private void DeleteProfile()
    {
        var profile = SelectedCustomProfile;
        if (profile == null) return;
        CustomProfiles.Remove(profile);
        SyncProfiles();
        SelectedCustomProfile = null;
        NewProfileName = "";
        _logger.Info($"Profile \"{profile.Name}\" deleted.");
    }

    private void SyncProfiles()
    {
        _settings.CustomProfiles = CustomProfiles.ToList();
        ScheduleSave();
    }
    public ObservableCollection<string> Profiles { get; }
    public ObservableCollection<string> InputCurves { get; }
    public ObservableCollection<string> ReleaseModes { get; }
    public ObservableCollection<string> LogLines { get; }

    // ---------------- Stick setup (direct route) ----------------
    private const string RouteDirectLabel = "Direct — all aircraft (recommended)";
    private const string RouteMaskedLabel = "MSFS bindings — default aircraft only";
    public ObservableCollection<string> InputRoutes { get; }
    public ObservableCollection<JoystickDevice> StickDevices { get; }
    public ObservableCollection<JoystickDevice> RudderDevices { get; }
    public ObservableCollection<string> AxisNames { get; }
    public ICommand DetectPitchCommand { get; }
    public ICommand DetectRollCommand { get; }
    public ICommand DetectRudderCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

    public bool IsDirect => !string.Equals(_settings.InputRoute, "Masked", StringComparison.OrdinalIgnoreCase);

    public string SelectedInputRoute
    {
        get => IsDirect ? RouteDirectLabel : RouteMaskedLabel;
        set
        {
            var direct = value != RouteMaskedLabel;
            if (direct == IsDirect) return;
            _settings.InputRoute = direct ? "Direct" : "Masked";
            _logger.Info("Input route set to " + _settings.InputRoute + ". Reconnecting.");
            _client.DirectInput = direct;
            if (_client.IsConnected)
            {
                _client.Disconnect();
                _client.Connect();
            }
            OnChanged();
            OnChanged(nameof(IsDirect));
            OnChanged(nameof(SetupNote));
            RaiseMeters();
            ScheduleSave();
        }
    }

    public JoystickDevice? SelectedStick
    {
        get => StickDevices.FirstOrDefault(d => d.Key == _settings.StickDevice);
        set
        {
            if (value == null || value.Key == _settings.StickDevice) return;
            _settings.StickDevice = value.Key;
            OnChanged();
            ScheduleSave();
        }
    }

    public JoystickDevice? SelectedRudderDevice
    {
        get => RudderDevices.FirstOrDefault(d => d.Key == _settings.RudderDevice) ?? RudderDevices.FirstOrDefault();
        set
        {
            if (value == null) return;
            _settings.RudderDevice = value.Key;
            OnChanged();
            OnChanged(nameof(SetupNote));
            ScheduleSave();
        }
    }

    public string PitchAxis { get => _settings.PitchAxis; set { _settings.PitchAxis = value; OnChanged(); ScheduleSave(); } }
    public string RollAxis { get => _settings.RollAxis; set { _settings.RollAxis = value; OnChanged(); ScheduleSave(); } }
    public string RudderAxis { get => _settings.RudderAxis; set { _settings.RudderAxis = value; OnChanged(); ScheduleSave(); } }
    public bool InvertPitch { get => _settings.InvertPitch; set { _settings.InvertPitch = value; OnChanged(); ScheduleSave(); } }
    public bool InvertRoll { get => _settings.InvertRoll; set { _settings.InvertRoll = value; OnChanged(); ScheduleSave(); } }
    public bool InvertRudder { get => _settings.InvertRudder; set { _settings.InvertRudder = value; OnChanged(); ScheduleSave(); } }

    public string StickStatus
    {
        get => _stickStatus;
        private set => Set(ref _stickStatus, value);
    }

    public string SetupNote => IsDirect
        ? "In MSFS Controls, clear Elevator Axis and Aileron Axis"
          + (string.IsNullOrEmpty(_settings.RudderDevice) ? "" : " and Rudder Axis")
          + " for these devices. HeavyPro feeds them itself; a binding left in MSFS fights it and twitches."
        : "Keep the stick bound to Elevator / Aileron / Rudder Axis in MSFS. Works with default aircraft; PMDG and iniBuilds need Direct.";

    private bool StickConfigured => !string.IsNullOrEmpty(_settings.StickDevice);

    private void RefreshDevices()
    {
        var found = JoystickReader.List();
        StickDevices.Clear();
        foreach (var d in found)
            StickDevices.Add(d);
        RudderDevices.Clear();
        RudderDevices.Add(new JoystickDevice(-1, "", NoRudder, 0));
        foreach (var d in found)
            RudderDevices.Add(d);
        _lastDeviceScan = DateTime.UtcNow;
        OnChanged(nameof(SelectedStick));
        OnChanged(nameof(SelectedRudderDevice));
    }

    private void StartDetect(string what)
    {
        _detectBase = new Dictionary<string, double[]>();
        foreach (var d in StickDevices)
            if (JoystickReader.TryRead(d.Id, out var axes))
                _detectBase[d.Key] = axes;
        _detectBest.Clear();
        _detecting = what;
        _detectUntil = DateTime.UtcNow.AddSeconds(4);
        StickStatus = what switch
        {
            "pitch" => "Detecting: pull the stick fully back and hold it…",
            "roll" => "Detecting: roll the stick fully right and hold it…",
            _ => "Detecting: push the right rudder pedal fully and hold it…"
        };
    }

    private void FinishDetect()
    {
        var what = _detecting!;
        _detecting = null;
        var best = _detectBest.OrderByDescending(b => b.Value.Delta).FirstOrDefault();
        if (best.Key == null || best.Value.Delta < 0.4)
        {
            StickStatus = "Nothing moved far enough. Press Detect and move the control to its end.";
            return;
        }
        var device = StickDevices.First(d => d.Key == best.Key);
        var axis = JoystickReader.AxisNames[best.Value.Axis];
        // Detection asks for pull back / roll right / right pedal, which must come
        // out as +1 in HeavyPro's convention; flip the axis when it moved negative.
        var invert = best.Value.Sign < 0;
        switch (what)
        {
            case "pitch":
                _settings.StickDevice = device.Key; _settings.PitchAxis = axis; _settings.InvertPitch = invert;
                OnChanged(nameof(SelectedStick)); OnChanged(nameof(PitchAxis)); OnChanged(nameof(InvertPitch));
                break;
            case "roll":
                _settings.StickDevice = device.Key; _settings.RollAxis = axis; _settings.InvertRoll = invert;
                OnChanged(nameof(SelectedStick)); OnChanged(nameof(RollAxis)); OnChanged(nameof(InvertRoll));
                break;
            default:
                _settings.RudderDevice = device.Key; _settings.RudderAxis = axis; _settings.InvertRudder = invert;
                OnChanged(nameof(SelectedRudderDevice)); OnChanged(nameof(RudderAxis)); OnChanged(nameof(InvertRudder));
                OnChanged(nameof(SetupNote));
                break;
        }
        _logger.Info($"Detected {what}: {device.Name} axis {axis}{(invert ? " inverted" : "")}.");
        StickStatus = $"{what} → {device.Name}, axis {axis}{(invert ? " (inverted)" : "")}";
        ScheduleSave();
    }

    /// <summary>Reads the stick ~120 times a second and, on the direct route, feeds it to the sim.</summary>
    private void PollStick()
    {
        var now = DateTime.UtcNow;
        if (_detecting != null)
        {
            foreach (var d in StickDevices)
            {
                if (!_detectBase.TryGetValue(d.Key, out var baseAxes) || !JoystickReader.TryRead(d.Id, out var axes))
                    continue;
                for (var i = 0; i < 6; i++)
                {
                    var delta = axes[i] - baseAxes[i];
                    if (!_detectBest.TryGetValue(d.Key, out var best) || Math.Abs(delta) > best.Delta)
                        _detectBest[d.Key] = (Math.Abs(delta), i, Math.Sign(delta));
                }
            }
            if (now >= _detectUntil)
                FinishDetect();
            return;
        }

        if (!StickConfigured)
            return;
        var stick = StickDevices.FirstOrDefault(d => d.Key == _settings.StickDevice);
        if (stick == null || !JoystickReader.TryRead(stick.Id, out _pitchAxes))
        {
            // Unplugged or renumbered: rescan now and then, never every tick.
            if ((now - _lastDeviceScan).TotalSeconds > 3)
                RefreshDevices();
            if ((now - _lastStickUi).TotalMilliseconds > 500)
            {
                _lastStickUi = now;
                StickStatus = "Stick not found. Plug it in or pick it again.";
            }
            return;
        }

        var pitch = Read(_pitchAxes, _settings.PitchAxis, _settings.InvertPitch);
        var roll = Read(_pitchAxes, _settings.RollAxis, _settings.InvertRoll);
        double? yaw = null;
        if (!string.IsNullOrEmpty(_settings.RudderDevice))
        {
            var pedals = RudderDevices.FirstOrDefault(d => d.Key == _settings.RudderDevice);
            if (pedals != null && JoystickReader.TryRead(pedals.Id, out var pedalAxes))
                yaw = Read(pedalAxes, _settings.RudderAxis, _settings.InvertRudder);
        }

        if (IsDirect && _client.IsConnected)
            _client.FeedPhysical(ToAxisEvent(pitch), ToAxisEvent(roll), yaw.HasValue ? ToAxisEvent(yaw.Value) : null);

        if ((now - _lastStickUi).TotalMilliseconds > 100)
        {
            _lastStickUi = now;
            StickStatus = F("Stick  pitch {0:+0.00;-0.00;0.00}  roll {1:+0.00;-0.00;0.00}", pitch, roll)
                + (yaw.HasValue ? F("  yaw {0:+0.00;-0.00;0.00}", yaw.Value) : "");
        }
    }

    private static double Read(double[] axes, string axisName, bool invert)
    {
        var i = JoystickReader.AxisIndex(axisName);
        if (i < 0) return 0;
        return invert ? -axes[i] : axes[i];
    }

    // HeavyPro convention: +1 = pull back / roll right / right pedal. MSFS AXIS
    // events use the opposite sign (measured: full pull = -16384, full right = -16384).
    private static int ToAxisEvent(double value) => (int)Math.Round(-Numeric.Clamp(value, -1, 1) * 16384.0);

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string BackendName
    {
        get => _backend;
        private set => Set(ref _backend, value);
    }

    public string ConnectionLabel
    {
        get => _connectionLabel;
        private set => Set(ref _connectionLabel, value);
    }

    public string DetectedProfile
    {
        get => _detectedProfile;
        private set => Set(ref _detectedProfile, value);
    }

    public string AircraftTitle => string.IsNullOrWhiteSpace(_snap.Aircraft.DisplayName) ? "No aircraft" : _snap.Aircraft.DisplayName;
    public string AircraftMeta =>
        $"Model {_snap.Aircraft.AtcModel}   Type {_snap.Aircraft.AtcType}   ID {_snap.Aircraft.AtcId}   {_snap.Aircraft.Category}";

    public bool MasterEnable
    {
        get => _settings.MasterEnable;
        set
        {
            if (_settings.MasterEnable == value) return;
            _settings.MasterEnable = value;
            _logger.Info("Master Enable set to " + value + (value ? " — AXIS writes only while the stick is held." : " — writes stopped."));
            if (!value)
            {
                _engine.Reset();
                _client.ApplyInfluence(InfluenceCommand.Idle("Master off — no writes"));
                WriteStatus = "Writes idle";
            }
            OnChanged(nameof(PhaseNote));
            OnChanged();
            RaiseMeters();
            ScheduleSave();
        }
    }

    public bool AutoConnect
    {
        get => _settings.AutoConnect;
        set
        {
            _settings.AutoConnect = value;
            OnChanged();
            ScheduleSave();
            if (value) _reconnectTimer.Start();
            else _reconnectTimer.Stop();
        }
    }

    public bool LogTelemetry
    {
        get => _settings.LogTelemetry;
        set
        {
            _settings.LogTelemetry = value;
            OnChanged();
            ScheduleSave();
        }
    }

    public AppSettings Settings => _settings;
    public FlightSnapshot Snapshot => _snap;
    public string HardwareStatus
    {
        get => _hardwareStatus;
        set => Set(ref _hardwareStatus, value);
    }
    private string _hardwareStatus = "hardware curve idle";

    public bool UseHardwareCurve
    {
        get => _settings.UseHardwareCurve;
        set
        {
            _settings.UseHardwareCurve = value;
            OnChanged();
            ScheduleSave();
        }
    }

    public bool CompanionMode
    {
        get => _settings.CompanionMode;
        set
        {
            _settings.CompanionMode = value;
            if (value)
            {
                _settings.ViewCue = false;
                _viewCue.Reset();
                _client.ApplyViewCue(HeavyFeel.Core.Physics.ViewCue.Zero);
                OnChanged(nameof(ViewCue));
            }
            OnChanged();
            ScheduleSave();
        }
    }

    public bool ViewCue
    {
        get => _settings.ViewCue;
        set
        {
            _settings.ViewCue = value;
            if (!value)
            {
                _viewCue.Reset();
                _client.ApplyViewCue(HeavyFeel.Core.Physics.ViewCue.Zero);
            }
            OnChanged();
            ScheduleSave();
        }
    }

    public bool DebugMode
    {
        get => _settings.DebugMode;
        set
        {
            _settings.DebugMode = value;
            OnChanged();
            OnChanged(nameof(DebugPanel));
            ScheduleSave();
        }
    }

    public string SelectedProfile
    {
        get => _settings.AircraftProfile;
        set
        {
            _settings.AircraftProfile = value;
            OnChanged();
            OnChanged(nameof(PitchRateLimit));
            OnChanged(nameof(RollRateLimit));
            OnChanged(nameof(YawRateLimit));
            OnChanged(nameof(DebugPanel));
            ScheduleSave();
        }
    }

    public bool IsFeelNormal => CurrentFeel == FeelLevel.Normal;
    public bool IsFeelMedium => CurrentFeel == FeelLevel.Medium;
    public bool IsFeelRealistic => CurrentFeel == FeelLevel.Realistic;
    public bool IsFeelCustom => CurrentFeel == FeelLevel.Custom;

    public FeelLevel CurrentFeel
    {
        get => Enum.TryParse(_settings.FeelLevel, out FeelLevel level) ? level : FeelPresets.Match(_settings);
    }

    public string FeelCaption => CurrentFeel switch
    {
        FeelLevel.Normal => "Normal — low inertia is a fast stick. Every slider still counts.",
        FeelLevel.Medium => "Medium — pitch/roll/yaw sliders only change that axis.",
        FeelLevel.Realistic => "Realistic — high inertia. Raise Response if it feels stuck.",
        _ => "Custom — each slider changes command speed on that axis."
    };

    public double Inertia { get => _settings.Inertia; set => SetSlider(nameof(Inertia), value, v => _settings.Inertia = v); }
    public double PitchDamping { get => _settings.PitchDamping; set => SetSlider(nameof(PitchDamping), value, v => _settings.PitchDamping = v); }
    public double RollDamping { get => _settings.RollDamping; set => SetSlider(nameof(RollDamping), value, v => _settings.RollDamping = v); }
    public double YawDamping { get => _settings.YawDamping; set => SetSlider(nameof(YawDamping), value, v => _settings.YawDamping = v); }
    public double ControlResponse { get => _settings.ControlResponse; set => SetSlider(nameof(ControlResponse), value, v => _settings.ControlResponse = v); }
    public double GroundInertia { get => _settings.GroundInertia; set => SetSlider(nameof(GroundInertia), value, v => _settings.GroundInertia = v); }
    public double TurbulenceResponse { get => _settings.TurbulenceResponse; set => SetSlider(nameof(TurbulenceResponse), value, v => _settings.TurbulenceResponse = v); }

    public string SelectedInputCurve
    {
        get => _settings.InputCurve;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _settings.InputCurve = value;
            OnChanged();
            ScheduleSave();
        }
    }

    public double InputDeadzonePercent
    {
        get => _settings.InputDeadzone * 100;
        set => SetInputSetting(nameof(InputDeadzonePercent), Numeric.Clamp(value, 0, 10) / 100, v => _settings.InputDeadzone = v);
    }

    public double InputSensitivityPercent
    {
        get => _settings.InputSensitivity * 100;
        set => SetInputSetting(nameof(InputSensitivityPercent), Numeric.Clamp(value, 25, 200) / 100, v => _settings.InputSensitivity = v);
    }

    public double ExpoStrengthPercent
    {
        get => _settings.ExpoStrength * 100;
        set => SetInputSetting(nameof(ExpoStrengthPercent), Numeric.Clamp(value, 0, 100) / 100, v => _settings.ExpoStrength = v);
    }

    public double InputAccelerationPercent
    {
        get => _settings.InputAccelerationScale * 100;
        set => SetInputSetting(nameof(InputAccelerationPercent), Numeric.Clamp(value, 25, 200) / 100, v => _settings.InputAccelerationScale = v);
    }

    public double InputDecelerationPercent
    {
        get => _settings.InputDecelerationScale * 100;
        set => SetInputSetting(nameof(InputDecelerationPercent), Numeric.Clamp(value, 25, 200) / 100, v => _settings.InputDecelerationScale = v);
    }

    public double PitchRateLimit
    {
        get => _settings.PitchRateLimit > 0 ? _settings.PitchRateLimit : InputDynamicsProfile.For(_snap, _settings.AircraftProfile).Elevator.MaxRate;
        set => SetInputSetting(nameof(PitchRateLimit), Numeric.Clamp(value, 0.1, 6), v => _settings.PitchRateLimit = v);
    }

    public double RollRateLimit
    {
        get => _settings.RollRateLimit > 0 ? _settings.RollRateLimit : InputDynamicsProfile.For(_snap, _settings.AircraftProfile).Aileron.MaxRate;
        set => SetInputSetting(nameof(RollRateLimit), Numeric.Clamp(value, 0.1, 6), v => _settings.RollRateLimit = v);
    }

    public double YawRateLimit
    {
        get => _settings.YawRateLimit > 0 ? _settings.YawRateLimit : InputDynamicsProfile.For(_snap, _settings.AircraftProfile).Rudder.MaxRate;
        set => SetInputSetting(nameof(YawRateLimit), Numeric.Clamp(value, 0.1, 6), v => _settings.YawRateLimit = v);
    }

    public bool AirspeedResponseEnabled
    {
        get => _settings.AirspeedResponseEnabled;
        set
        {
            _settings.AirspeedResponseEnabled = value;
            OnChanged();
            ScheduleSave();
        }
    }

    public bool RudderLimiter
    {
        get => _settings.RudderLimiter;
        set
        {
            _settings.RudderLimiter = value;
            OnChanged();
            ScheduleSave();
        }
    }

    public string StickReleaseMode
    {
        get => _settings.StickReleaseMode;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _settings.StickReleaseMode = value;
            OnChanged();
            ScheduleSave();
        }
    }

    public double AirspeedResponsePercent
    {
        get => _settings.AirspeedResponse * 100;
        set => SetInputSetting(nameof(AirspeedResponsePercent), Numeric.Clamp(value, 0, 100) / 100, v => _settings.AirspeedResponse = v);
    }

    public double ReturnDelayMs
    {
        get => _settings.ReturnDelayMs;
        set => SetInputSetting(nameof(ReturnDelayMs), Numeric.Clamp(value, 0, 1500), v => _settings.ReturnDelayMs = v);
    }

    public string Ias => F("{0:0.0} kts", _snap.AirspeedIndicatedKnots);
    public string Tas => F("{0:0.0} kts", _snap.AirspeedTrueKnots);
    public string Mach => F("{0:0.000}", _snap.AirspeedMach);
    public string Gs => F("{0:0.0} kts", _snap.GroundSpeedKnots);
    public string Vs => F("{0:+0;-0;0} fpm", _snap.VerticalSpeedFpm);
    public string Pitch => F("{0:+0.00;-0.00;0.00}°", _snap.PitchDegrees);
    public string Bank => F("{0:+0.00;-0.00;0.00}°", _snap.BankDegrees);
    public string Hdg => F("{0:000.0}°", _snap.HeadingTrueDegrees);
    public string Alt => F("{0:0} ft", _snap.AltitudeFeet);
    public string LatLon => F("{0:0.00000}, {1:0.00000}", _snap.LatitudeDegrees, _snap.LongitudeDegrees);
    public string AoA => F("{0:+0.00;-0.00;0.00}°", _snap.AngleOfAttackDegrees);
    public string Beta => F("{0:+0.00;-0.00;0.00}°", _snap.SideslipDegrees);
    public string GLoad => F("{0:0.00} G", _snap.GForce);
    public string RotX => F("{0:+0.000;-0.000;0.000} rad/s", _snap.RotationVelocityBodyX);
    public string RotY => F("{0:+0.000;-0.000;0.000} rad/s", _snap.RotationVelocityBodyY);
    public string RotZ => F("{0:+0.000;-0.000;0.000} rad/s", _snap.RotationVelocityBodyZ);
    public string QBar => F("{0:0.00} psf", _snap.DynamicPressurePsf);
    public string OnGround => _snap.OnGround
        ? $"GROUND  (AGL {_snap.AltitudeAglFeet:0} ft  radio {_snap.RadioHeightFeet:0} ft)"
        : $"AIR  (AGL {_snap.AltitudeAglFeet:0} ft  radio {_snap.RadioHeightFeet:0} ft)  {AircraftCatalog.DisplayName(_snap.Class)}";
    public string Weight
    {
        get
        {
            var kg = _snap.TotalWeightPounds * 0.45359237;
            var emptyKg = _snap.EmptyWeightPounds * 0.45359237;
            var maxKg = _snap.MaxGrossWeightPounds * 0.45359237;
            return F("{0:0} kg  (empty {1:0}, max {2:0})", kg, emptyKg, maxKg);
        }
    }
    public string Fuel => F("{0:0} lb", _snap.FuelWeightPounds);
    public string Moi => F("P {0:0}   R {1:0}   Y {2:0}", _snap.PitchMoi, _snap.RollMoi, _snap.YawMoi);
    public string Config => F("Flaps {0:0}% idx {1:0}   Gear {2:0}%   SpdBrk {3:0}%", _snap.FlapsHandlePercent, _snap.FlapsHandleIndex, _snap.GearTotalExtendedPercent, _snap.SpoilersHandlePercent);
    public string Thrust => F("TLA {0:0}/{1:0}%   N1 {2:0.0}/{3:0.0}%", _snap.Throttle1Percent, _snap.Throttle2Percent, _snap.EngineN1_1, _snap.EngineN1_2);
    public string Controls => F("Yoke X {0:0.00}  Y {1:0.00}  Ped {2:0.00}", _snap.YokeX, _snap.YokeY, _snap.RudderPedal);
    public string Surfaces => F("Elv {0:0.00}  Ail {1:0.00}  Rud {2:0.00}  Trim {3:0.00}", _snap.ElevatorDeflectionPct, _snap.AileronDeflectionPct, _snap.RudderDeflectionPct, _snap.ElevatorTrimPct);
    public string Ap => string.IsNullOrWhiteSpace(_snap.AutopilotLabel)
        ? (_snap.AutopilotMaster ? "AP ON" : "AP OFF")
        : _snap.AutopilotLabel;
    public string DataRate => F("{0:0.0} Hz   frames {1}", _dataHz, _frameCount);
    public string PreviewTau => F("τ {0:0.000} s", InertiaModelStub.PreviewTimeConstant(_settings, _snap));
    public string PhaseNote =>
        MasterEnable
            ? "Master on — writes only while the sidestick is deflected. AP uses Fenix FCU + AUTOPILOT MASTER, not HDG/ALT lock. State uses height + speed, not a stuck ground flag."
            : "Master off — read only. Turn Master Enable on to write to the aircraft.";
    public string WriteStatus
    {
        get => _writeStatus;
        private set => Set(ref _writeStatus, value);
    }

    public string DebugPanel
    {
        get
        {
            var phase = FlightPhaseResolver.Resolve(_snap);
            // AXIS events and the YOKE simvar can use opposite signs, so compare magnitude.
            var follow = Math.Abs(Math.Abs(_snap.YokeY) - Math.Abs(_lastCmd.OutPitch)) < 0.08 ? "reported follows sent" : "reported differs — may be overwritten";
            return
                $"Aircraft  {_snap.Aircraft.DisplayName}\n" +
                $"Category  {AircraftCatalog.DisplayName(_snap.Class)}   profile {_lastCmd.ProfileName}\n" +
                $"Scale     {CommunityProfile.TravelScale():0.00}   conn {ConnectionLabel}\n" +
                $"Route     {_client.RouteStatus}  {_client.RouteDetail}\n" +
                $"State     {(_snap.OnGround ? "GROUND" : "AIR")}   phase {phase}   AP {_snap.AutopilotLabel}\n" +
                $"PHYSICAL  E {Axis(_snap.CapturedPitch)}  A {Axis(_snap.CapturedRoll)}  R {Axis(_snap.CapturedYaw)}   source {_snap.InputSource}\n" +
                $"RECEIVED  E {_lastCmd.RawPitch:+0.00;-0.00;0}  A {_lastCmd.RawRoll:+0.00;-0.00;0}  R {_lastCmd.RawYaw:+0.00;-0.00;0}\n" +
                $"SENT      E {_lastCmd.OutPitch:+0.00;-0.00;0}  A {_lastCmd.OutRoll:+0.00;-0.00;0}  R {_lastCmd.OutYaw:+0.00;-0.00;0}\n" +
                $"REPORTED  yokeY={_snap.YokeY:+0.00;-0.00;0}  {follow}\n" +
                $"{_lastCmd.Reason}";
        }
    }

    // Live meters: where the stick is versus what the sim is being sent.
    public double MeterPitchIn => _snap.CapturedPitch ?? _lastCmd.RawPitch;
    public double MeterPitchOut => _lastCmd.Active ? _lastCmd.OutPitch : MeterPitchIn;
    public double MeterRollIn => _snap.CapturedRoll ?? _lastCmd.RawRoll;
    public double MeterRollOut => _lastCmd.Active ? _lastCmd.OutRoll : MeterRollIn;
    public double MeterYawIn => _snap.CapturedYaw ?? _lastCmd.RawYaw;
    public double MeterYawOut => _lastCmd.Active ? _lastCmd.OutYaw : MeterYawIn;

    /// <summary>One word for the header: is HeavyPro actually shaping right now?</summary>
    public string RouteBadge
    {
        get
        {
            if (!_client.IsConnected) return "OFFLINE";
            if (IsDirect && !StickConfigured) return "SET UP STICK";
            if (_client.RouteStatus == InputRouteStatus.Fault) return "FAULT";
            if (!MasterEnable) return "PASS-THROUGH";
            if (_snap.AutopilotMaster) return "AP ON · PAUSED";
            if (_client.RouteStatus != InputRouteStatus.Active) return "MOVE STICK";
            return "SHAPING";
        }
    }

    public string RouteHint => RouteBadge switch
    {
        "OFFLINE" => "Start MSFS 2024 and load a flight. HeavyPro connects on its own.",
        "SET UP STICK" => "Pick your stick under Stick setup, or press Detect and move it.",
        "FAULT" => "SimConnect refused a stick write. Disconnect and connect again.",
        "PASS-THROUGH" => "Your stick goes to the sim untouched. Turn on Master Enable to add weight.",
        "AP ON · PAUSED" => "Autopilot is flying. HeavyPro hands the stick back until you disconnect it.",
        "MOVE STICK" => IsDirect ? "Waiting for the stick. Check it is plugged in." : "Move the stick once. It must be bound to Elevator, Aileron and Rudder Axis in MSFS.",
        _ => "The gold bar is what the aircraft gets. The white tick is your stick."
    };

    private static string Axis(double? value) => value.HasValue ? value.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) : "n/a";

    private static string Bar(double value)
    {
        var n = (int)Math.Round(Numeric.Clamp(value, -1, 1) * 8);
        return n >= 0 ? new string('+', n).PadRight(8) : new string('-', -n).PadRight(8);
    }

    public void Start()
    {
        if (_settings.AutoConnect)
        {
            _reconnectTimer.Start();
            Connect();
        }
    }

    public void Connect()
    {
        _logger.Info("Connect requested.");
        _client.Connect();
        RefreshConnection();
    }

    public void Disconnect()
    {
        _logger.Info("Disconnect requested.");
        _client.Disconnect();
        RefreshConnection();
    }

    public void PersistWindow(double width, double height)
    {
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        Persist();
    }

    public (double W, double H) SavedSize => (_settings.WindowWidth, _settings.WindowHeight);

    public void Dispose()
    {
        Persist();
        _reconnectTimer.Stop();
        _saveTimer.Stop();
        _stickTimer.Stop();
        _client.Dispose();
        _logger.Dispose();
    }

    private void ApplyIdentity(AircraftIdentity id)
    {
        _snap = new FlightSnapshot { Aircraft = id };
        DetectedProfile = id.SuggestedProfile;
        if (_settings.AircraftProfile == "Auto Detect")
            _logger.Info("Auto profile suggestion: " + DetectedProfile);
        RaiseTelemetry();
    }

    private void ApplySnapshot(FlightSnapshot snap)
    {
        _snap = snap;
        _frameCount++;
        _framesInSample++;
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFpsSample).TotalSeconds;
        var refreshUi = (now - _lastUiRefresh).TotalMilliseconds >= 100;
        if (elapsed >= 1)
        {
            _dataHz = _framesInSample / elapsed;
            _framesInSample = 0;
            _lastFpsSample = now;
        }

        var profile = snap.Aircraft.IsFenixA320
            ? "Fenix A320"
            : snap.Aircraft.IsPmdg777
                ? "PMDG 777"
                : AircraftCatalog.DisplayName(snap.Class);
        if (profile != DetectedProfile)
            DetectedProfile = profile;

        if (_settings.LogTelemetry && (now - _lastLog).TotalMilliseconds >= Math.Max(250, _settings.TelemetryLogIntervalMs))
        {
            _lastLog = now;
            _logger.Telemetry(FormatTelemetryLine(snap));
        }

        var cmd = _engine.Step(_settings, snap, _settings.MasterEnable && _client.IsConnected);
        _lastCmd = cmd;
        _client.SetFeelGain(cmd.Mix > 0 ? cmd.Mix : 0.55);
        _client.ApplyInfluence(cmd);
        if (!_settings.CompanionMode && _settings.ViewCue)
            _client.ApplyViewCue(_viewCue.Step(_settings, snap));
        if (refreshUi)
        {
            WriteStatus = cmd.Reason;
        }
        if (refreshUi)
        {
            _lastUiRefresh = now;
            RaiseTelemetry();
        }
        else if ((now - _lastMeterRefresh).TotalMilliseconds >= 33)
        {
            // Meters refresh at ~30 Hz so the lag is visible; text stays at 10 Hz.
            _lastMeterRefresh = now;
            RaiseMeters();
        }
    }

    private static string FormatTelemetryLine(FlightSnapshot s)
    {
        var b = new StringBuilder();
        b.Append("IAS=").Append(s.AirspeedIndicatedKnots.ToString("0.0", CultureInfo.InvariantCulture));
        b.Append(" ALT=").Append(s.AltitudeFeet.ToString("0", CultureInfo.InvariantCulture));
        b.Append(" P=").Append(s.PitchDegrees.ToString("0.00", CultureInfo.InvariantCulture));
        b.Append(" B=").Append(s.BankDegrees.ToString("0.00", CultureInfo.InvariantCulture));
        b.Append(" G=").Append(s.GForce.ToString("0.00", CultureInfo.InvariantCulture));
        b.Append(" W=").Append(s.TotalWeightPounds.ToString("0", CultureInfo.InvariantCulture));
        b.Append(s.OnGround ? " GND" : " AIR");
        b.Append(" AGL=").Append(s.AltitudeAglFeet.ToString("0", CultureInfo.InvariantCulture));
        b.Append(' ').Append(s.AutopilotLabel);
        return b.ToString();
    }

    private void RefreshConnection()
    {
        ConnectionLabel = _client.State switch
        {
            ConnectionState.Connected => "CONNECTED",
            ConnectionState.SimRunning => "LIVE",
            ConnectionState.Connecting => "CONNECTING",
            ConnectionState.Error => "ERROR",
            _ => "DISCONNECTED"
        };
        Status = _client.StatusText;
        OnChanged(nameof(ConnectionLabel));
        RaiseMeters();
    }

    private void AppendLog(string line)
    {
        LogLines.Insert(0, line);
        while (LogLines.Count > 300)
            LogLines.RemoveAt(LogLines.Count - 1);
    }

    private void ApplyFeel(FeelLevel level)
    {
        var preset = FeelPresets.For(level);
        _applyingPreset = true;
        try
        {
            _settings.FeelLevel = level.ToString();
            Inertia = preset.Inertia;
            PitchDamping = preset.PitchDamping;
            RollDamping = preset.RollDamping;
            YawDamping = preset.YawDamping;
            ControlResponse = preset.ControlResponse;
            GroundInertia = preset.GroundInertia;
            TurbulenceResponse = preset.TurbulenceResponse;
        }
        finally
        {
            _applyingPreset = false;
        }

        _logger.Info("Feel level set to " + level + " — used on the next write frame.");
        RaiseFeel();
        ScheduleSave();
    }

    private void SetSlider(string name, double value, Action<double> assign)
    {
        assign(Numeric.Clamp(value, SliderLimits.Min, SliderLimits.Max));
        OnChanged(name);
        OnChanged(nameof(PreviewTau));
        if (!_applyingPreset)
        {
            _settings.FeelLevel = FeelLevel.Custom.ToString();
            RaiseFeel();
        }
        ScheduleSave();
    }

    private void SetInputSetting(string name, double value, Action<double> assign)
    {
        assign(value);
        OnChanged(name);
        OnChanged(nameof(DebugPanel));
        ScheduleSave();
    }

    private void RaiseFeel()
    {
        OnChanged(nameof(CurrentFeel));
        OnChanged(nameof(IsFeelNormal));
        OnChanged(nameof(IsFeelMedium));
        OnChanged(nameof(IsFeelRealistic));
        OnChanged(nameof(IsFeelCustom));
        OnChanged(nameof(FeelCaption));
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Persist()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save settings", ex);
        }
    }

    private void RaiseTelemetry()
    {
        OnChanged(nameof(AircraftTitle));
        OnChanged(nameof(AircraftMeta));
        OnChanged(nameof(Ias));
        OnChanged(nameof(Tas));
        OnChanged(nameof(Mach));
        OnChanged(nameof(Gs));
        OnChanged(nameof(Vs));
        OnChanged(nameof(Pitch));
        OnChanged(nameof(Bank));
        OnChanged(nameof(Hdg));
        OnChanged(nameof(Alt));
        OnChanged(nameof(LatLon));
        OnChanged(nameof(AoA));
        OnChanged(nameof(Beta));
        OnChanged(nameof(GLoad));
        OnChanged(nameof(RotX));
        OnChanged(nameof(RotY));
        OnChanged(nameof(RotZ));
        OnChanged(nameof(QBar));
        OnChanged(nameof(OnGround));
        OnChanged(nameof(Weight));
        OnChanged(nameof(Fuel));
        OnChanged(nameof(Moi));
        OnChanged(nameof(Config));
        OnChanged(nameof(Thrust));
        OnChanged(nameof(Controls));
        OnChanged(nameof(Surfaces));
        OnChanged(nameof(Ap));
        OnChanged(nameof(DataRate));
        OnChanged(nameof(PreviewTau));
        OnChanged(nameof(WriteStatus));
        OnChanged(nameof(DebugPanel));
        OnChanged(nameof(PitchRateLimit));
        OnChanged(nameof(RollRateLimit));
        OnChanged(nameof(YawRateLimit));
        OnChanged(nameof(PhaseNote));
        RaiseMeters();
    }

    private void RaiseMeters()
    {
        OnChanged(nameof(MeterPitchIn));
        OnChanged(nameof(MeterPitchOut));
        OnChanged(nameof(MeterRollIn));
        OnChanged(nameof(MeterRollOut));
        OnChanged(nameof(MeterYawIn));
        OnChanged(nameof(MeterYawOut));
        OnChanged(nameof(RouteBadge));
        OnChanged(nameof(RouteHint));
    }

    private static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnChanged(name);
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static void Dispatch(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess())
            action();
        else
            d.BeginInvoke(action);
    }
}
