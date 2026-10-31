using HeavyFeel.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using HeavyFeel.App.Input;
using HeavyFeel.App.Services;
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
        // A fresh install has never saved settings: it starts on REAL1, the best route.
        var freshInstall = _settings.SettingsVersion == 0;
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
        if (_settings.SettingsVersion < 4)
        {
            // 0.5: ready-made profiles for common aircraft, added once.
            BuiltInProfiles.AddMissing(_settings.CustomProfiles);
            _settings.SettingsVersion = 4;
            settingsStore.Save(_settings);
        }
        if (_settings.SettingsVersion < 6)
        {
            // 0.5.2: Realistic answers sooner. Move saved Realistic tuning to the new values.
            if (FeelPresets.Matches(_settings, FeelPresets.RealisticBefore052))
            {
                var r = FeelPresets.Realistic;
                (_settings.Inertia, _settings.PitchDamping, _settings.RollDamping, _settings.YawDamping) = (r.Inertia, r.PitchDamping, r.RollDamping, r.YawDamping);
                (_settings.ControlResponse, _settings.GroundInertia, _settings.TurbulenceResponse) = (r.ControlResponse, r.GroundInertia, r.TurbulenceResponse);
            }
            _settings.SettingsVersion = 6;
            settingsStore.Save(_settings);
        }
        if (_settings.SettingsVersion < 5)
        {
            // 0.5.0 built-ins pinned an aircraft; applying the C172 one made an A350 fly as GA.
            BuiltInProfiles.Unpin(_settings.CustomProfiles);
            _settings.AircraftProfile = "Auto Detect";
            _settings.SettingsVersion = 5;
            settingsStore.Save(_settings);
        }
        if (freshInstall)
        {
            _settings.InputRoute = Real1.RouteName;
            settingsStore.Save(_settings);
        }
        ApplyTransport();
        StickDevices = new ObservableCollection<JoystickDevice>();
        RudderDevices = new ObservableCollection<JoystickDevice>();
        AxisNames = new ObservableCollection<string>(JoystickReader.AxisNames);
        InputRoutes = new ObservableCollection<string> { RouteReal1Label, RouteDirectLabel, RouteMaskedLabel };
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
        ToggleAircraftLinkCommand = new RelayCommand(ToggleAircraftLink);
        FixBindingsCommand = new RelayCommand(FixMsfsBindings);
        RestoreBindingsCommand = new RelayCommand(RestoreMsfsBindings);
        ShowTermsCommand = new RelayCommand(() => new TermsWindow(mustAccept: false) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog());
        CopyBetaReportCommand = new RelayCommand(CopyBetaReport);
        DeleteProfileCommand = new RelayCommand(DeleteProfile);
        ShareProfileCommand = new RelayCommand(() => _ = ShareProfileAsync());
        GetSharedProfileCommand = new RelayCommand(() => _ = GetSharedProfileAsync());
        CommunityProfiles = new ObservableCollection<SharedProfileListing>();
        RefreshCommunityCommand = new RelayCommand(() => _ = RefreshCommunityAsync());
        GetCommunityProfileCommand = new RelayCommand(() => _ = GetCommunityProfileAsync());

        _client.StateChanged += (_, _) => Dispatch(RefreshConnection);
        _client.StatusMessage += (_, msg) => Dispatch(() => Status = msg);
        _client.AircraftChanged += (_, id) => Dispatch(() => ApplyIdentity(id));
        _client.SnapshotReceived += (_, snap) => Dispatch(() => ApplySnapshot(snap));
        _client.PanelRequested += (_, request) => Dispatch(() => ApplyPanelRequest(request));
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
            if (DateTime.UtcNow - _lastBindingScan > TimeSpan.FromSeconds(20))
                ScanMsfsBindings();
            // Picks up a PMDG update, or finishes a build that waited for MSFS to close.
            if (DateTime.UtcNow - _lastFlightModelSync > TimeSpan.FromSeconds(20))
                SyncFlightModel();
        };
        ScanMsfsBindings();
        PickRoute();
        SyncFlightModel();

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
    public ICommand ToggleAircraftLinkCommand { get; }
    public ICommand FixBindingsCommand { get; }
    public ICommand RestoreBindingsCommand { get; }
    public ICommand ShowTermsCommand { get; }
    public ICommand CopyBetaReportCommand { get; }

    public ICommand ApplyProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand ShareProfileCommand { get; }
    public ICommand GetSharedProfileCommand { get; }

    private string _profileStatus = "";
    public string ProfileStatus
    {
        get => _profileStatus;
        private set => Set(ref _profileStatus, value);
    }

    /// <summary>Publishes the selected profile online under its name.</summary>
    private async Task ShareProfileAsync()
    {
        var profile = SelectedCustomProfile;
        if (profile == null)
        {
            ProfileStatus = "Pick a saved profile to share, or save the current tuning first.";
            return;
        }
        ProfileStatus = "Sharing…";
        ProfileStatus = await SharedProfiles.PublishAsync(profile);
        _logger.Info("Share profile \"" + profile.Name + "\": " + ProfileStatus);
    }

    // ---------------- Community list ----------------
    public ObservableCollection<SharedProfileListing> CommunityProfiles { get; }
    public ICommand RefreshCommunityCommand { get; }
    public ICommand GetCommunityProfileCommand { get; }

    private SharedProfileListing? _selectedCommunityProfile;
    public SharedProfileListing? SelectedCommunityProfile
    {
        get => _selectedCommunityProfile;
        set => Set(ref _selectedCommunityProfile, value);
    }

    private string _communitySearch = "";
    public string CommunitySearch
    {
        get => _communitySearch;
        set => Set(ref _communitySearch, value ?? "");
    }

    private string _communityStatus = "Press Refresh to load everyone's shared profiles.";
    public string CommunityStatus
    {
        get => _communityStatus;
        private set => Set(ref _communityStatus, value);
    }

    private async Task RefreshCommunityAsync()
    {
        CommunityStatus = "Loading…";
        var (profiles, message) = await SharedProfiles.ListAsync(CommunitySearch.Trim());
        CommunityProfiles.Clear();
        foreach (var p in profiles)
            CommunityProfiles.Add(p);
        CommunityStatus = message;
    }

    private async Task GetCommunityProfileAsync()
    {
        var row = SelectedCommunityProfile;
        if (row == null)
        {
            CommunityStatus = "Pick a profile in the list first.";
            return;
        }
        await GetSharedProfileAsync(row.Name);
        CommunityStatus = ProfileStatus;
    }

    /// <summary>Fetches a shared profile by the typed name and adds it to My Profiles.</summary>
    private Task GetSharedProfileAsync() => GetSharedProfileAsync(NewProfileName.Trim());

    private async Task GetSharedProfileAsync(string name)
    {
        if (name.Length == 0)
        {
            ProfileStatus = "Type the name of the shared profile, then press Get by name.";
            return;
        }
        ProfileStatus = "Getting…";
        var (profile, message) = await SharedProfiles.GetAsync(name);
        ProfileStatus = message;
        if (profile == null)
            return;
        var existing = CustomProfiles.FirstOrDefault(p => string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            CustomProfiles[CustomProfiles.IndexOf(existing)] = profile;
        else
            CustomProfiles.Add(profile);
        SyncProfiles();
        SelectedCustomProfile = profile;
        _logger.Info("Got shared profile \"" + profile.Name + "\".");
    }

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
            OnChanged(nameof(AircraftLinkText));
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
        if (SelectedCustomProfile is { } profile)
            ApplyProfile(profile);
    }

    private void ApplyProfile(CustomProfile profile)
    {
        profile.ApplyTo(_settings);
        foreach (var name in new[]
        {
            nameof(Inertia), nameof(PitchDamping), nameof(RollDamping), nameof(YawDamping), nameof(ControlResponse),
            nameof(GroundInertia), nameof(TurbulenceResponse), nameof(SelectedInputCurve), nameof(InputDeadzonePercent),
            nameof(InputSensitivityPercent), nameof(ExpoStrengthPercent), nameof(InputAccelerationPercent),
            nameof(InputDecelerationPercent), nameof(PitchRateLimit), nameof(RollRateLimit), nameof(YawRateLimit),
            nameof(AirspeedResponseEnabled), nameof(AirspeedResponsePercent), nameof(RudderLimiter), nameof(RealisticRudder),
            nameof(StickReleaseMode), nameof(ReturnDelayMs), nameof(SelectedProfile), nameof(PreviewTau), nameof(DebugPanel)
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
        AircraftProfileLinks.Forget(_settings, profile.Name);
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
    private const string RouteReal1Label = "REAL1 — realistic feel tuned per aircraft (best)";
    private const string RouteDirectLabel = "Direct — your own feel settings";
    private const string RouteMaskedLabel = "MSFS bindings — default aircraft only";
    public ObservableCollection<string> InputRoutes { get; }
    public ObservableCollection<JoystickDevice> StickDevices { get; }
    public ObservableCollection<JoystickDevice> RudderDevices { get; }
    public ObservableCollection<string> AxisNames { get; }
    public ICommand DetectPitchCommand { get; }
    public ICommand DetectRollCommand { get; }
    public ICommand DetectRudderCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

    /// <summary>Direct and REAL1 both send the stick where hardware enters; only MSFS bindings masks it.</summary>
    public bool IsDirect => !string.Equals(_settings.InputRoute, "Masked", StringComparison.OrdinalIgnoreCase);

    public bool IsReal1 => Real1.IsActive(_settings);

    public string SelectedInputRoute
    {
        get => IsReal1 ? RouteReal1Label : IsDirect ? RouteDirectLabel : RouteMaskedLabel;
        set
        {
            var route = value == RouteReal1Label ? Real1.RouteName : value == RouteMaskedLabel ? "Masked" : "Direct";
            if (string.Equals(route, _settings.InputRoute, StringComparison.OrdinalIgnoreCase)) return;
            var reconnect = (route == "Masked") != !IsDirect;
            _settings.InputRoute = route;
            _logger.Info("Input route set to " + _settings.InputRoute + (reconnect ? ". Reconnecting." : "."));
            if (!reconnect)
            {
                // REAL1 and Direct send the stick the same way: no reconnect, the feel switches next frame.
                OnChanged();
                OnChanged(nameof(IsReal1));
                OnChanged(nameof(SetupNote));
                ScheduleSave();
                return;
            }
            _logger.Info("Input route set to " + _settings.InputRoute + ". Reconnecting.");
            ApplyTransport();
            if (_client.IsConnected)
            {
                _client.Disconnect();
                _client.Connect();
            }
            OnChanged();
            OnChanged(nameof(IsDirect));
            OnChanged(nameof(IsReal1));
            OnChanged(nameof(SetupNote));
            OnChanged(nameof(CanFixBindings));
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
            _lastBindingScan = DateTime.MinValue;
            if (!value.HasAxis(_settings.PitchAxis)) { _settings.PitchAxis = "Y"; OnChanged(nameof(PitchAxis)); }
            if (!value.HasAxis(_settings.RollAxis)) { _settings.RollAxis = "X"; OnChanged(nameof(RollAxis)); }
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
            _lastBindingScan = DateTime.MinValue;
            // Pedals usually put the rudder on Z (X/Y are often the toe brakes).
            if (value.Id >= 0 && !value.HasAxis(_settings.RudderAxis))
            {
                _settings.RudderAxis = value.HasAxis("Z") ? "Z" : "X";
                OnChanged(nameof(RudderAxis));
            }
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

    public string SetupNote =>
        "HeavyPro picks the route by itself for every aircraft. With your stick bound in MSFS it shapes the stick through that binding (the BlueBird 757 gets it directly, with HeavyPro's in-sim bridge answering the binding). "
        + "With the stick unbound it uses REAL1, the feel tuned for each aircraft, or your own profile if you linked one to the aircraft. Normal / Medium / Realistic set how strong it is.";

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

        // Settings can name an axis the device does not have (it reads as full
        // deflection); move it to one that exists.
        var stick = found.FirstOrDefault(d => d.Key == _settings.StickDevice);
        if (stick != null && !stick.HasAxis(_settings.PitchAxis)) { _settings.PitchAxis = "Y"; OnChanged(nameof(PitchAxis)); }
        if (stick != null && !stick.HasAxis(_settings.RollAxis)) { _settings.RollAxis = "X"; OnChanged(nameof(RollAxis)); }
        var pedals = found.FirstOrDefault(d => d.Key == _settings.RudderDevice);
        if (pedals != null && !pedals.HasAxis(_settings.RudderAxis))
        {
            _logger.Info($"Rudder axis {_settings.RudderAxis} does not exist on {pedals.Name}; using {(pedals.HasAxis("Z") ? "Z" : "X")}.");
            _settings.RudderAxis = pedals.HasAxis("Z") ? "Z" : "X";
            OnChanged(nameof(RudderAxis));
            if (_saveTimer != null) // RefreshDevices also runs from the constructor, before the timer exists
                ScheduleSave();
        }
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

        // An axis the device does not have reads as fully deflected: never send it.
        var missing = new List<string>();
        double? pitch = stick.HasAxis(_settings.PitchAxis) ? Read(_pitchAxes, _settings.PitchAxis, _settings.InvertPitch) : null;
        double? roll = stick.HasAxis(_settings.RollAxis) ? Read(_pitchAxes, _settings.RollAxis, _settings.InvertRoll) : null;
        if (pitch == null) missing.Add("pitch " + _settings.PitchAxis);
        if (roll == null) missing.Add("roll " + _settings.RollAxis);
        double? yaw = null;
        if (!string.IsNullOrEmpty(_settings.RudderDevice))
        {
            var pedals = RudderDevices.FirstOrDefault(d => d.Key == _settings.RudderDevice);
            if (pedals != null && !pedals.HasAxis(_settings.RudderAxis))
                missing.Add("yaw " + _settings.RudderAxis);
            else if (pedals != null && JoystickReader.TryRead(pedals.Id, out var pedalAxes))
                yaw = Read(pedalAxes, _settings.RudderAxis, _settings.InvertRudder);
        }

        // MSFS bindings route: the app shows the stick from the device itself, and notices
        // when it moves but no stick event reaches HeavyPro (the aircraft took it first).
        _stickPitch = pitch;
        _stickRoll = roll;
        var offCentre = Math.Max(Math.Abs(pitch ?? 0), Math.Abs(roll ?? 0)) > 0.25;
        var pitchRollCaptured = _snap.CapturedPitch.HasValue || _snap.CapturedRoll.HasValue;
        if (!_client.DirectInput && _client.IsConnected && offCentre && !pitchRollCaptured)
        {
            _stickHiddenSince ??= now;
            if (!_stickHidden && now - _stickHiddenSince.Value > TimeSpan.FromSeconds(1.5))
            {
                _stickHidden = true;
                _logger.Warn("The stick moves but no stick event reaches HeavyPro through MSFS for this aircraft: switching it to REAL1.");
                _stickMissingFor = AircraftProfileLinks.Key(_snap.Aircraft);
                PickRoute();
            }
        }
        else if (pitchRollCaptured || !_client.IsConnected)
        {
            _stickHiddenSince = null;
            _stickHidden = false;
        }

        if (_client.DirectInput && _client.IsConnected)
            _client.FeedPhysical(pitch.HasValue ? ToAxisEvent(pitch.Value) : null, roll.HasValue ? ToAxisEvent(roll.Value) : null,
                yaw.HasValue ? ToAxisEvent(yaw.Value) : null);

        if ((now - _lastStickUi).TotalMilliseconds > 100)
        {
            _lastStickUi = now;
            StickStatus = missing.Count > 0
                ? "That device has no axis for " + string.Join(", ", missing) + ". Press Detect and move that control."
                : F("Stick  pitch {0:+0.00;-0.00;0.00}  roll {1:+0.00;-0.00;0.00}", pitch ?? 0, roll ?? 0)
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

    /// <summary>
    /// Real 777 feel for the PMDG 777: an override package HeavyPro builds in the
    /// Community folder from the user's own PMDG file (see <see cref="FlightModelOverrides"/>).
    /// </summary>
    public bool FlightModelOverride
    {
        get => _settings.FlightModelOverride;
        set
        {
            _settings.FlightModelOverride = value;
            _logger.Info("Real 777 feel for the PMDG 777 " + (value ? "on" : "off") + ".");
            OnChanged();
            ScheduleSave();
            SyncFlightModel();
        }
    }

    /// <summary>Closing the window keeps HeavyPro running in the tray.</summary>
    public bool RunInBackground
    {
        get => _settings.RunInBackground;
        set
        {
            _settings.RunInBackground = value;
            OnChanged();
            ScheduleSave();
        }
    }

    /// <summary>Starts HeavyPro in the tray when Windows starts (the Run key, for this Windows user only).</summary>
    public bool StartWithWindows
    {
        get
        {
            try { return BackgroundTray.StartsWithWindows; }
            catch (Exception) { return false; }
        }
        set
        {
            try
            {
                BackgroundTray.StartsWithWindows = value;
                _logger.Info("Start with Windows " + (value ? "on" : "off") + ".");
            }
            catch (Exception ex)
            {
                _logger.Error("Could not change Start with Windows", ex);
            }
            OnChanged();
        }
    }

    private string _flightModelStatus = "";
    public string FlightModelStatus
    {
        get => _flightModelStatus;
        private set => Set(ref _flightModelStatus, value);
    }

    private DateTime _lastFlightModelSync = DateTime.MinValue;

    /// <summary>Builds, rebuilds after a PMDG update, or removes the 777 override packages.</summary>
    private void SyncFlightModel()
    {
        _lastFlightModelSync = DateTime.UtcNow;
        try
        {
            var community = MsfsCommunity.Find2024();
            if (community == null)
            {
                FlightModelStatus = _settings.FlightModelOverride ? "MSFS 2024 Community folder not found." : "";
                return;
            }
            var simRunning = System.Diagnostics.Process.GetProcessesByName("FlightSimulator2024").Length > 0;
            var messages = FlightModelOverrides.Sync(community, _settings.FlightModelOverride, simRunning);
            foreach (var message in messages.Where(m => !m.EndsWith(" on.") && !m.EndsWith("when MSFS is closed.")))
                _logger.Info("Flight model: " + message);
            FlightModelStatus = string.Join("\n", messages);
        }
        catch (Exception ex)
        {
            _logger.Error("Updating the PMDG 777 flight model override failed", ex);
            FlightModelStatus = "Could not update the 777 flight model: " + ex.Message;
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

    public bool RealisticRudder
    {
        get => _settings.RealisticRudder;
        set
        {
            _settings.RealisticRudder = value;
            _logger.Info("Realistic rudder " + (value ? "on" : "off") + ".");
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
    private double? _stickPitch;
    private double? _stickRoll;
    private DateTime? _stickHiddenSince;
    private bool _stickHidden;

    // When no stick event reaches HeavyPro, show the stick from the device itself.
    public double MeterPitchIn => _snap.CapturedPitch ?? (!_client.DirectInput && _stickPitch.HasValue ? _stickPitch.Value : _lastCmd.RawPitch);
    public double MeterPitchOut => _lastCmd.Active ? _lastCmd.OutPitch : MeterPitchIn;
    public double MeterRollIn => _snap.CapturedRoll ?? (!_client.DirectInput && _stickRoll.HasValue ? _stickRoll.Value : _lastCmd.RawRoll);
    public double MeterRollOut => _lastCmd.Active ? _lastCmd.OutRoll : MeterRollIn;
    public double MeterYawIn => _snap.CapturedYaw ?? _lastCmd.RawYaw;
    public double MeterYawOut => _lastCmd.Active ? _lastCmd.OutYaw : MeterYawIn;

    /// <summary>One word for the header: is HeavyPro actually shaping right now?</summary>
    public string RouteBadge
    {
        get
        {
            if (!_client.IsConnected) return "OFFLINE";
            if (_client.DirectInput && !StickConfigured) return "SET UP STICK";
            if (IsDirect && !_client.CounterMsfsBindings && (_client.InputConflict != null || (_profileConflict != null && _client.KnownBindingConflict))) return "UNBIND IN MSFS";
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
        "UNBIND IN MSFS" => _client.InputConflict != null
            ? "MSFS still sends your " + _client.InputConflict + " axis. HeavyPro passes your stick through untouched so it cannot twitch, which also means no weight. In MSFS Controls, clear Elevator / Aileron / Rudder Axis for that device, then reconnect."
            : "MSFS still binds " + _profileConflict + ". On Direct, HeavyPro passes your stick through untouched so it cannot twitch, which also means no weight. Press \"Fix MSFS bindings for me\" under Stick setup (with MSFS closed).",
        "FAULT" => "SimConnect refused a stick write. Disconnect and connect again.",
        "PASS-THROUGH" => "Your stick goes to the sim untouched. Turn on Master Enable to add weight.",
        "AP ON · PAUSED" => "Autopilot is flying. HeavyPro hands the stick back until you disconnect it.",
        "MOVE STICK" => IsDirect ? "Waiting for the stick. Check it is plugged in."
            : _stickHidden ? "Your stick moves, but the aircraft takes it before HeavyPro can (the A350 does this when it loads first). Disconnect, go to the MSFS main menu, connect HeavyPro, then load the flight."
            : "Move the stick once. It must be bound to Elevator, Aileron and Rudder Axis in MSFS.",
        _ => "The gold bar is what the aircraft gets. The white tick is your stick."
    };

    // ---------------- Input transport ----------------
    private void ApplyTransport()
    {
        _client.DirectInput = IsDirect;
        // The BlueBird 757 with the stick bound in MSFS: the in-sim bridge answers the binding.
        _client.CounterMsfsBindings = IsDirect && _route.BridgeAnswersBindings;
    }

    // ---------------- MSFS bindings that fight HeavyPro ----------------
    private string? _profileConflict;
    private DateTime _lastBindingScan = DateTime.MinValue;

    /// <summary>
    /// Reads the MSFS 2024 control profiles for the selected stick and pedals. PMDG and
    /// iniBuilds take hardware events before SimConnect sees them, so a live check
    /// cannot catch a binding there; the profile files can.
    /// </summary>
    private void ScanMsfsBindings()
    {
        _lastBindingScan = DateTime.UtcNow;
        string? found = null;
        var bindings = new List<MsfsAxisBinding>();
        try
        {
            var files = MsfsProfileFiles();
            if (MsfsInputProfiles.ProductIdFromKey(_settings.StickDevice) is int stick)
                bindings.AddRange(MsfsInputProfiles.FindBindings(files, stick, new[] { "pitch", "roll" }));
            if (MsfsInputProfiles.ProductIdFromKey(_settings.RudderDevice) is int pedals)
                bindings.AddRange(MsfsInputProfiles.FindBindings(files, pedals, new[] { "yaw" }));
            found = bindings.Count == 0 ? null : string.Join("; ", bindings);
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not read the MSFS control profiles: " + ex.Message);
        }
        _profileBindings = bindings;
        var stickPid = MsfsInputProfiles.ProductIdFromKey(_settings.StickDevice);
        var stickBound = bindings.Any(b => b.Axes.Contains("pitch") || b.Axes.Contains("roll"));
        if (found == _profileConflict && stickBound == _stickBoundInMsfs)
            return;
        _profileConflict = found;
        _stickBoundInMsfs = stickBound;
        OnChanged(nameof(CanFixBindings));
        _logger.Info(found == null
            ? "MSFS control profiles: no stick axes bound for HeavyPro's devices."
            : "MSFS control profile binds " + found + ".");
        PickRoute();
    }

    // ---------------- One route, picked by itself ----------------
    private bool _stickBoundInMsfs;
    private string _stickMissingFor = "";
    private RouteChoice _route = new(Real1.RouteName, false, "starting");

    /// <summary>Which route HeavyPro picked and why, for the Setup card and the header tooltip.</summary>
    public string RouteText => RoutePicker.Label(_route) + " — " + _route.Reason;

    /// <summary>
    /// Picks the route from what HeavyPro can see (see <see cref="RoutePicker"/>): whether the
    /// stick is bound in MSFS, the aircraft, and a profile you linked to it. Runs at start,
    /// on every aircraft, and whenever the MSFS bindings change.
    /// </summary>
    private void PickRoute()
    {
        var aircraft = _snap.Aircraft;
        var key = AircraftProfileLinks.Key(aircraft);
        var choice = RoutePicker.Choose(_stickBoundInMsfs, aircraft, AircraftProfileLinks.Find(_settings, aircraft) != null,
            key.Length > 0 && key == _stickMissingFor);
        // A binding in another MSFS profile is no conflict for an aircraft whose profile does not have it.
        _client.KnownBindingConflict = _profileConflict != null && !choice.StickMissingFallback;
        if (choice == _route)
            return;
        var bridgeChanged = choice.BridgeAnswersBindings != _route.BridgeAnswersBindings;
        _route = choice;
        _logger.Info("Route picked: " + RouteText + ".");
        var label = choice.Route == "Masked" ? RouteMaskedLabel : choice.Route == "Direct" ? RouteDirectLabel : RouteReal1Label;
        if (label != SelectedInputRoute)
            SelectedInputRoute = label;
        if (bridgeChanged)
            ApplyTransport();
        OnChanged(nameof(RouteText));
        OnChanged(nameof(RouteBadge));
        OnChanged(nameof(RouteHint));
        OnChanged(nameof(CanFixBindings));
    }

    /// <summary>MSFS 2024 (Steam) control profiles: Steam\userdata\*\2537590\remote\inputprofile_*.</summary>
    private static List<(string Path, DateTime Modified, string Xml)> MsfsProfileFiles()
    {
        var result = new List<(string, DateTime, string)>();
        var steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam))
            return result;
        var userdata = System.IO.Path.Combine(steam!.Replace('/', '\\'), "userdata");
        if (!System.IO.Directory.Exists(userdata))
            return result;
        foreach (var user in System.IO.Directory.GetDirectories(userdata))
        {
            var remote = System.IO.Path.Combine(user, "2537590", "remote");
            if (!System.IO.Directory.Exists(remote))
                continue;
            foreach (var file in System.IO.Directory.GetFiles(remote, "inputprofile_*"))
                result.Add((file, System.IO.File.GetLastWriteTimeUtc(file), System.IO.File.ReadAllText(file)));
        }
        return result;
    }

    private List<MsfsAxisBinding> _profileBindings = new();

    /// <summary>MSFS still binds the stick and HeavyPro knows which profile files to fix.</summary>
    /// <remarks>
    /// Only on REAL1 and Direct. The MSFS bindings route needs those bindings: offered there,
    /// Fix cleared the stick from every airplane profile and the GA planes lost their stick.
    /// </remarks>
    public bool CanFixBindings => IsDirect && !_route.BridgeAnswersBindings && _profileConflict != null;

    /// <summary>
    /// "Fix MSFS bindings for me": clears exactly the stick axes HeavyPro feeds (never a button) in the MSFS
    /// control profiles that bind them, after a backup, then switches to the Direct route
    /// (measured smooth on the PMDG 777 once nothing else sends the stick).
    /// </summary>
    private void FixMsfsBindings()
    {
        ScanMsfsBindings();
        var bindings = _profileBindings.Where(b => b.File.Length > 0).ToList();
        if (bindings.Count == 0)
        {
            System.Windows.MessageBox.Show("MSFS does not bind your stick or pedals any more. Nothing to fix.", "HeavyPro");
            return;
        }
        if (System.Diagnostics.Process.GetProcessesByName("FlightSimulator2024").Length > 0)
        {
            System.Windows.MessageBox.Show("Close MSFS 2024 first. It saves its control profiles when it exits, which would put these bindings back.", "HeavyPro");
            return;
        }
        var list = string.Join("\n", bindings.Select(b => "• " + string.Join(", ", b.Axes) + " on " + b.DeviceName + " (profile \"" + b.ProfileName + "\")"));
        var answer = System.Windows.MessageBox.Show(
            "HeavyPro will clear these axes in your MSFS 2024 controls, so your stick reaches the aircraft only through HeavyPro:\n\n"
            + list + "\n\nOnly the stick axes are removed: every button, including buttons on these same controls, and everything else in those profiles stays as it is. A backup of each is kept. Continue?",
            "Fix MSFS bindings", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
            return;

        var backupDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeavyPro", "msfs-profile-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            System.IO.Directory.CreateDirectory(backupDir);
            foreach (var binding in bindings)
            {
                var bytes = System.IO.File.ReadAllBytes(binding.File);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(backupDir, System.IO.Path.GetFileName(binding.File)), bytes);
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var text = System.IO.File.ReadAllText(binding.File);
                var cleared = MsfsInputProfiles.ClearAxes(text, binding.Axes);
                // Never write a file the edit did not fix, or one that no longer parses.
                var check = MsfsInputProfiles.Parse(cleared);
                if (check == null || check.BoundAxes.Intersect(binding.Axes).Any())
                    throw new InvalidOperationException("the edit of " + System.IO.Path.GetFileName(binding.File) + " did not check out; it was left unchanged");
                System.IO.File.WriteAllText(binding.File, cleared, new System.Text.UTF8Encoding(bom));
                _logger.Info($"Cleared {string.Join(", ", binding.Axes)} in MSFS profile \"{binding.ProfileName}\" ({binding.File}); backup in {backupDir}.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Fixing the MSFS bindings failed", ex);
            System.Windows.MessageBox.Show("HeavyPro could not change the MSFS profiles: " + ex.Message + "\n\nBackups are in " + backupDir, "HeavyPro");
            ScanMsfsBindings();
            return;
        }

        if (!IsDirect)
            SelectedInputRoute = RouteReal1Label;
        _lastBindingScan = DateTime.MinValue;
        ScanMsfsBindings();
        OnChanged(nameof(CanRestoreBindings));
        System.Windows.MessageBox.Show(
            "Done. MSFS no longer sends your stick on its own, and HeavyPro is on the Direct route.\n\n"
            + "Start MSFS 2024 and fly. If Steam asks about cloud saves for MSFS, choose the files on this PC.\n\nBackups: " + backupDir,
            "HeavyPro");
    }

    private static string BindingBackupRoot => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeavyPro", "msfs-profile-backups");

    /// <summary>The newest "Fix MSFS bindings" backup not yet restored, or null.</summary>
    private static string? LatestBindingBackup()
    {
        if (!System.IO.Directory.Exists(BindingBackupRoot))
            return null;
        return System.IO.Directory.GetDirectories(BindingBackupRoot)
            .Where(d => !d.EndsWith("-restored", StringComparison.OrdinalIgnoreCase)
                && System.IO.Directory.GetFiles(d, "inputprofile_*").Length > 0)
            .OrderByDescending(d => System.IO.Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>A "Fix MSFS bindings" backup is there to put back.</summary>
    public bool CanRestoreBindings => LatestBindingBackup() != null;

    /// <summary>
    /// "Restore MSFS bindings": puts the stick axes from the newest Fix backup back, for
    /// beta testers flying without HeavyPro or removing it. Only the elevator, aileron and
    /// rudder actions change; buttons and anything set since stay. What is there now is
    /// kept too, in msfs-profile-replaced, so the restore itself can be undone by hand.
    /// </summary>
    private void RestoreMsfsBindings()
    {
        var backup = LatestBindingBackup();
        if (backup == null)
        {
            System.Windows.MessageBox.Show("There is no \"Fix MSFS bindings\" backup to restore.", "HeavyPro");
            return;
        }
        if (System.Diagnostics.Process.GetProcessesByName("FlightSimulator2024").Length > 0)
        {
            System.Windows.MessageBox.Show("Close MSFS 2024 first. It saves its control profiles when it exits, which would undo the restore.", "HeavyPro");
            return;
        }
        var current = MsfsProfileFiles().ToDictionary(f => System.IO.Path.GetFileName(f.Path), f => f.Path, StringComparer.OrdinalIgnoreCase);
        var files = System.IO.Directory.GetFiles(backup, "inputprofile_*")
            .Where(f => current.ContainsKey(System.IO.Path.GetFileName(f))).ToList();
        if (files.Count == 0)
        {
            System.Windows.MessageBox.Show("The profiles in the newest backup are no longer in MSFS, so there is nothing to restore.\n\nBackup: " + backup, "HeavyPro");
            return;
        }
        var names = files.Select(f => "• " + (MsfsInputProfiles.Parse(System.IO.File.ReadAllText(f))?.Name ?? System.IO.Path.GetFileName(f)));
        var answer = System.Windows.MessageBox.Show(
            "HeavyPro will put the stick axes in these MSFS 2024 control profiles back as they were before \"Fix MSFS bindings\" (from " + System.IO.Path.GetFileName(backup) + "):\n\n"
            + string.Join("\n", names) + "\n\nOnly the stick axes change; your buttons and anything you set since stay as they are. MSFS then sends your stick by itself again, and HeavyPro passes it through untouched until you fix the bindings again. Continue?",
            "Restore MSFS bindings", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
            return;

        var replaced = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeavyPro", "msfs-profile-replaced", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            System.IO.Directory.CreateDirectory(replaced);
            foreach (var file in files)
            {
                var target = current[System.IO.Path.GetFileName(file)];
                var bytes = System.IO.File.ReadAllBytes(target);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(replaced, System.IO.Path.GetFileName(target)), bytes);
                var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var restored = MsfsInputProfiles.RestoreAxes(System.IO.File.ReadAllText(target), System.IO.File.ReadAllText(file));
                if (MsfsInputProfiles.Parse(restored) == null)
                    throw new InvalidOperationException("the restore of " + System.IO.Path.GetFileName(target) + " did not check out; it was left unchanged");
                System.IO.File.WriteAllText(target, restored, new System.Text.UTF8Encoding(bom));
            }
            System.IO.Directory.Move(backup, backup + "-restored");
            _logger.Info($"Restored {files.Count} MSFS profile(s) from {backup}; the replaced ones are in {replaced}.");
        }
        catch (Exception ex)
        {
            _logger.Error("Restoring the MSFS bindings failed", ex);
            System.Windows.MessageBox.Show("HeavyPro could not restore the MSFS profiles: " + ex.Message + "\n\nThe files it replaced are in " + replaced, "HeavyPro");
            return;
        }
        _lastBindingScan = DateTime.MinValue;
        ScanMsfsBindings();
        OnChanged(nameof(CanRestoreBindings));
        System.Windows.MessageBox.Show("Done. Your stick axes are bound in MSFS again, as they were.\n\nStart MSFS 2024 and fly. If Steam asks about cloud saves for MSFS, choose the files on this PC.", "HeavyPro");
    }

    // ---------------- Beta ----------------
    public string FooterText => $"HeavyPro {AppVersion.Current.ToString(3)} beta  ·  © 2026 Rotaidem. All rights reserved.";

    /// <summary>
    /// "Copy beta report": what a tester's bug report needs, on the clipboard. The Windows
    /// user folder and any licence key are taken out, so it is safe to post.
    /// </summary>
    private void CopyBetaReport()
    {
        var a = _snap.Aircraft;
        var lines = new List<string>
        {
            $"HeavyPro {AppVersion.Current.ToString(3)} beta report, {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Windows {Environment.OSVersion.Version}, MSFS {(System.Diagnostics.Process.GetProcessesByName("FlightSimulator2024").Length > 0 ? "running" : "not running")}, {ConnectionLabel}",
            $"Aircraft: {a.DisplayName} | {a.AtcModel} | {a.AtcType}",
            $"Route: {_settings.InputRoute}, feel {_settings.FeelLevel}, master {(MasterEnable ? "on" : "off")}, badge {RouteBadge}",
            IsReal1 ? $"REAL1 family: {Real1.For(_snap).Family}" : "REAL1 off",
            $"Stick: {_settings.StickDevice} ({_settings.PitchAxis}/{_settings.RollAxis}), pedals: {_settings.RudderDevice} ({_settings.RudderAxis})",
            $"MSFS bindings: {_profileConflict ?? "none fighting HeavyPro"}",
            $"777 flight model: {(_settings.FlightModelOverride ? "on" : "off")} {FlightModelStatus.Replace("\n", " / ")}",
            "",
            "Recent log (newest first):"
        };
        lines.AddRange(LogLines.Take(80));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = System.Text.RegularExpressions.Regex.Replace(string.Join(Environment.NewLine, lines).Replace(profile, "%USERPROFILE%"),
            @"HPRO(-[A-Z0-9]{4}){3}", "HPRO-****-****-****", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        try
        {
            System.Windows.Clipboard.SetText(text);
            Status = "Beta report copied. Paste it in your report to Eliaz@rotaidem.com.";
        }
        catch (Exception ex)
        {
            _logger.Error("Copying the beta report failed", ex);
            Status = "Could not copy the beta report: " + ex.Message;
        }
    }

    // ---------------- In-game panel ----------------
    private PanelState CurrentPanelState()
    {
        var state = RouteBadge switch
        {
            "OFFLINE" => 0,
            "PASS-THROUGH" => 1,
            "SHAPING" => 2,
            "AP ON · PAUSED" => 3,
            "SET UP STICK" => 4,
            "UNBIND IN MSFS" => 5,
            "MOVE STICK" => 6,
            _ => 7
        };
        var preset = CurrentFeel switch { FeelLevel.Normal => 1, FeelLevel.Medium => 2, FeelLevel.Realistic => 3, _ => 0 };
        return new PanelState(MasterEnable, state, preset, _settings.Inertia, MeterPitchIn, MeterPitchOut, MeterRollIn, MeterRollOut);
    }

    private void ApplyPanelRequest(PanelRequest request)
    {
        if (request.Master == 1) MasterEnable = false;
        else if (request.Master == 2) MasterEnable = true;
        if (request.Preset is >= 1 and <= 3)
            ApplyFeel(request.Preset == 1 ? FeelLevel.Normal : request.Preset == 2 ? FeelLevel.Medium : FeelLevel.Realistic);
        if (request.Inertia is >= 1 and <= 101)
            Inertia = request.Inertia - 1;
        _client.PublishPanelState(CurrentPanelState());
    }

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

        // A profile linked to this aircraft switches on once when it loads.
        var key = AircraftProfileLinks.Key(id);
        if (key.Length > 0 && key != _linkedAircraft)
        {
            _linkedAircraft = key;
            if (AircraftProfileLinks.Find(_settings, id) is { } linked)
            {
                ApplyProfile(linked);
                ProfileStatus = $"\"{linked.Name}\" switched on for {key}.";
                _logger.Info($"Profile \"{linked.Name}\" is linked to {key}: applied.");
            }
        }
        OnChanged(nameof(AircraftLinkText));
        PickRoute();
        RaiseTelemetry();
    }

    // ---------------- Profile per aircraft ----------------
    private string _linkedAircraft = "";

    public string AircraftLinkText
    {
        get
        {
            var key = AircraftProfileLinks.Key(_snap.Aircraft);
            if (key.Length == 0)
                return "Use for this aircraft (load one first)";
            return SelectedCustomProfile is { } p && AircraftProfileLinks.IsLinked(_settings, _snap.Aircraft, p)
                ? "Stop using for " + key
                : "Use for " + key;
        }
    }

    private void ToggleAircraftLink()
    {
        var key = AircraftProfileLinks.Key(_snap.Aircraft);
        if (key.Length == 0)
        {
            ProfileStatus = "Load an aircraft in MSFS first, then link a profile to it.";
            return;
        }
        if (SelectedCustomProfile is not { } profile)
        {
            ProfileStatus = "Pick a profile above first.";
            return;
        }
        if (AircraftProfileLinks.IsLinked(_settings, _snap.Aircraft, profile))
        {
            AircraftProfileLinks.Unlink(_settings, _snap.Aircraft);
            ProfileStatus = $"{key} no longer switches a profile on.";
            _logger.Info($"Profile link removed for {key}.");
        }
        else
        {
            AircraftProfileLinks.Link(_settings, _snap.Aircraft, profile);
            ApplyProfile(profile);
            ProfileStatus = $"\"{profile.Name}\" now switches on whenever {key} loads.";
            _logger.Info($"Profile \"{profile.Name}\" linked to {key}.");
        }
        _linkedAircraft = key;
        OnChanged(nameof(AircraftLinkText));
        PickRoute();
        ScheduleSave();
    }

    // ---------------- Version and What's new ----------------
    /// <summary>
    /// What's new for this version, once: the first start after installing or updating.
    /// Null when it was already shown or the changelog has nothing for this version.
    /// </summary>
    public string? TakeWhatsNew()
    {
        var current = AppVersion.Current.ToString(3);
        if (_settings.LastSeenVersion == current)
            return null;
        _settings.LastSeenVersion = current;
        ScheduleSave();
        using var stream = typeof(MainViewModel).Assembly.GetManifestResourceStream("HeavyPro.Changelog.md");
        if (stream == null)
            return null;
        using var reader = new System.IO.StreamReader(stream);
        return Changelog.WhatsNew(reader.ReadToEnd(), AppVersion.Current);
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
            _client.PublishPanelState(CurrentPanelState());
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
