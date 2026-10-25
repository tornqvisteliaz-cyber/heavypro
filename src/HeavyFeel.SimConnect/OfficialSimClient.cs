#if HAS_SIMCONNECT
using HeavyFeel.Core;
using System.Runtime.InteropServices;
using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using HeavyFeel.Core.Services;
using HeavyFeel.Logging;
using Microsoft.FlightSimulator.SimConnect;

namespace HeavyFeel.SimConnect;

/// <summary>
/// Official SimConnect client. Reads telemetry every frame.
/// Stick and rudder AXIS_*_SET events are captured through a maskable
/// notification group. While shaping is armed the captured value goes to the
/// input dynamics engine and only the shaped value is sent on; otherwise the
/// physical value is passed straight through.
/// </summary>
public sealed class OfficialSimClient : ISimClient, INativeMessageClient
{
    public const int WmUserSimconnect = 0x0402;

    private enum Definitions : uint
    {
        Identity = 1,
        Flight = 2,
        FenixFcu = 3,
        RateWrite = 4,
        Flags = 5,
        YokeSense = 6,
        YokeWrite = 7,
        PanelState = 8,
        PanelRequest = 9,
        BridgeWrite = 10,
        BridgeStatus = 11
    }

    private enum Requests : uint
    {
        Identity = 1,
        Flight = 2,
        FenixFcu = 3,
        Flags = 4,
        YokeSense = 5,
        PanelRequest = 6,
        BridgeStatus = 7
    }

    private enum Events : uint
    {
        AircraftLoaded = 1,
        SimStart = 2,
        SimStop = 3,
        Pause = 4,
        AxisElevator = 20,
        AxisAileron = 21,
        AxisRudder = 22,
        AxisThrottle = 23
    }

    private enum Groups : uint
    {
        Stick = 1
    }

    /// <summary>
    /// Used as the GroupID argument when GROUPID_IS_PRIORITY is set.
    /// Some SimConnect managed wrappers do not ship SIMCONNECT_GROUP_PRIORITY.
    /// 1 == HIGHEST.
    /// </summary>
    private enum EventPriority : uint
    {
        Highest = 1,
        HighestMaskable = 10000000,
        // Just after HeavyPro's masking group (10,000,000), where the hardware stick
        // would carry on: PMDG's own handler sits below this and drives its yoke.
        AfterMask = 10000001,
        Standard = 1900000000
    }

    private readonly AppLogger _logger;
    private readonly InputOwnershipManager _route = new();
    public InputRouteStatus RouteStatus => _route.Status;
    public string RouteDetail => _route.Detail;
    private readonly IntPtr _hwnd;
    private readonly Dictionary<Events, (int Value, long Tick)> _recentAxisWrites = new();
    private readonly Dictionary<Events, int> _physicalAxis = new();
    private readonly Dictionary<uint, string> _sentNames = new();
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _sim;
    private ConnectionState _state = ConnectionState.Disconnected;
    private AircraftIdentity _identity = new();
    private FenixFcuPacket _fenixFcu;
    private FlagsPacket _flags;
    private YokeSensePacket _yokeSense;
    private bool _rateWriteReady;
    private bool _axisWriteReady;
    private bool _yokeWriteReady;
    private bool _shapingArmed;
    private bool _flightLayoutBroken;
    private bool _wasWriting;
    private bool _writesDisabled;
    private bool _cameraDisabled;
    private int _writeLogSkip;
    private double _feelGain = 0.55;
    private DateTime _lastInfluence = DateTime.MinValue;
    private DateTime _writesPausedUntil = DateTime.MinValue;

    // If the engine stops stepping (no flight frames), hand the stick back.
    // Long enough to ride out a sim stutter: dropping to the raw stick for one
    // frame and back to the shaped value reads as a twitch.
    private static readonly TimeSpan ArmedTimeout = TimeSpan.FromMilliseconds(1000);
    // Our own transmissions should not come back through the group, but if they
    // do, ignore an identical value seen this soon after sending it.
    private static readonly TimeSpan EchoWindow = TimeSpan.FromMilliseconds(200);

    public OfficialSimClient(AppLogger logger, IntPtr hwnd)
    {
        _logger = logger;
        _hwnd = hwnd;
    }

    public ConnectionState State => _state;
    public string StatusText { get; private set; } = "Disconnected";
    public bool IsConnected => _sim != null && _state is ConnectionState.Connected or ConnectionState.SimRunning;
    public int MessageId => WmUserSimconnect;

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<FlightSnapshot>? SnapshotReceived;
    public event EventHandler<AircraftIdentity>? AircraftChanged;
    public event EventHandler<string>? StatusMessage;
    public event EventHandler<PanelRequest>? PanelRequested;

    // ---- In-game panel bridge (L:HEAVYPRO_* variables) ----
    private bool _panelReady;
    private double _panelAlive;

    private void RegisterPanel()
    {
        try
        {
            foreach (var name in new[] { "ALIVE", "MASTER", "STATE", "PRESET", "INERTIA", "IN_P", "OUT_P", "IN_R", "OUT_R" })
                Add(Definitions.PanelState, "L:HEAVYPRO_" + name, "number");
            _sim!.RegisterDataDefineStruct<PanelStatePacket>(Definitions.PanelState);
            foreach (var name in new[] { "REQ_MASTER", "REQ_PRESET", "REQ_INERTIA" })
                Add(Definitions.PanelRequest, "L:HEAVYPRO_" + name, "number");
            _sim.RegisterDataDefineStruct<PanelRequestPacket>(Definitions.PanelRequest);
            _sim.RequestDataOnSimObject(Requests.PanelRequest, Definitions.PanelRequest,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _panelReady = true;
        }
        catch (Exception ex)
        {
            _panelReady = false;
            _logger.Warn("In-game panel bridge unavailable: " + ex.Message);
        }
    }

    // ---- WASM bridge (HeavyProBridge.wasm in the panel package) ----
    // With the module loaded, HeavyPro writes each value it sends to L:HEAVYPRO_SHAPED_*
    // first, and the module answers any other stick event (an MSFS binding) with that
    // value, so the stick can stay bound in MSFS.
    private bool _bridgeReady;
    private bool _bridgeLoaded;
    private DateTime _bridgeLastLog = DateTime.MinValue;
    public bool CounterMsfsBindings { get; set; }
    public bool BridgeLoaded => _bridgeLoaded;
    private double _bridgeOverrides;
    private DateTime _bridgeLastOverride = DateTime.MinValue;
    private readonly Dictionary<Events, double> _bridgeShaped = new();

    private void RegisterBridge()
    {
        try
        {
            foreach (var name in new[] { "SHAPED_ELEV", "SHAPED_AIL", "SHAPED_RUD", "OVERRIDE" })
                Add(Definitions.BridgeWrite, "L:HEAVYPRO_" + name, "number");
            _sim!.RegisterDataDefineStruct<BridgeWritePacket>(Definitions.BridgeWrite);
            Add(Definitions.BridgeStatus, "L:HEAVYPRO_WASM_VERSION", "number");
            Add(Definitions.BridgeStatus, "L:HEAVYPRO_WASM_OVERRIDES", "number");
            _sim.RegisterDataDefineStruct<BridgeStatusPacket>(Definitions.BridgeStatus);
            _sim.RequestDataOnSimObject(Requests.BridgeStatus, Definitions.BridgeStatus,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _bridgeReady = true;
        }
        catch (Exception ex)
        {
            _bridgeReady = false;
            _logger.Warn("WASM bridge variables unavailable: " + ex.Message);
        }
    }

    private void OnBridgeStatus(BridgeStatusPacket packet)
    {
        // A module that is answering stick events is loaded, whatever its version L:var says.
        var loaded = packet.Version >= 1 || packet.Overrides > 0;
        if (loaded != _bridgeLoaded)
            _logger.Info(loaded ? "HeavyPro WASM bridge is loaded in the sim." : "HeavyPro WASM bridge is not loaded.");
        _bridgeLoaded = loaded;
        if (packet.Overrides != _bridgeOverrides)
        {
            if (_bridgeOverrides > 0 || packet.Overrides > 0)
                _bridgeLastOverride = DateTime.UtcNow;
            if (packet.Overrides > 0 && DateTime.UtcNow - _bridgeLastLog > TimeSpan.FromSeconds(10))
            {
                _bridgeLastLog = DateTime.UtcNow;
                _logger.Info($"WASM bridge has answered {packet.Overrides:0} MSFS stick events with HeavyPro's value.");
            }
            _bridgeOverrides = packet.Overrides;
        }
    }

    /// <summary>True while the module is answering MSFS stick bindings for HeavyPro.</summary>
    private bool BridgeCovering => _bridgeLoaded && DateTime.UtcNow - _bridgeLastOverride < ConflictHold;

    private void WriteBridge(Events ev, int value)
    {
        // Not gated on the module saying it is loaded: loading a flight clears that L:var,
        // and these writes are harmless when no module listens.
        if (!_bridgeReady || !_directActive)
            return;
        _bridgeShaped[ev] = value;
        // Only while an MSFS binding is really sending: with the axes unbound the
        // module has nothing to answer and stays out of the way.
        SetBridge(CounterMsfsBindings || ForeignInputSeen);
    }

    private bool ForeignInputSeen
    {
        get
        {
            var now = DateTime.UtcNow;
            return _foreign.Any(f => f.Value.Count >= 3 && now - f.Value.Last < ConflictHold);
        }
    }

    private void SetBridge(bool active)
    {
        if (_sim == null || !_bridgeReady)
            return;
        _sim.SetDataOnSimObject(Definitions.BridgeWrite,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_DATA_SET_FLAG.DEFAULT,
            new BridgeWritePacket
            {
                Elevator = _bridgeShaped.TryGetValue(Events.AxisElevator, out var e) ? e : 0,
                Aileron = _bridgeShaped.TryGetValue(Events.AxisAileron, out var a) ? a : 0,
                Rudder = _bridgeShaped.TryGetValue(Events.AxisRudder, out var r) ? r : 0,
                Override = active ? 1 : 0
            });
    }

    public void PublishPanelState(PanelState state)
    {
        if (_sim == null || !_panelReady)
            return;
        try
        {
            _panelAlive = _panelAlive >= 1_000_000 ? 1 : _panelAlive + 1;
            _sim.SetDataOnSimObject(Definitions.PanelState,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                new PanelStatePacket
                {
                    Alive = _panelAlive, Master = state.Master ? 1 : 0, State = state.State, Preset = state.Preset,
                    Inertia = state.Inertia, InP = state.InPitch, OutP = state.OutPitch, InR = state.InRoll, OutR = state.OutRoll
                });
        }
        catch (Exception ex)
        {
            _panelReady = false;
            _logger.Warn("In-game panel bridge stopped: " + ex.Message);
        }
    }

    private void OnPanelRequest(PanelRequestPacket packet)
    {
        var request = new PanelRequest((int)Math.Round(packet.Master), (int)Math.Round(packet.Preset), (int)Math.Round(packet.Inertia));
        if (!request.Any)
            return;
        // Clear first so a request is applied once, even if handling it is slow.
        _sim?.SetDataOnSimObject(Definitions.PanelRequest,
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_DATA_SET_FLAG.DEFAULT, new PanelRequestPacket());
        _logger.Info($"In-game panel request: master={request.Master} preset={request.Preset} inertia={request.Inertia}");
        PanelRequested?.Invoke(this, request);
    }

    public void Connect()
    {
        if (_sim != null)
            return;

        SetState(ConnectionState.Connecting, "Connecting to MSFS 2024…");
        try
        {
            _sim = new Microsoft.FlightSimulator.SimConnect.SimConnect(
                "HeavyFeel",
                _hwnd,
                (uint)WmUserSimconnect,
                null,
                0);

            _sim.OnRecvOpen += OnOpen;
            _sim.OnRecvQuit += OnQuit;
            _sim.OnRecvException += OnException;
            _sim.OnRecvSimobjectData += OnSimObjectData;
            _sim.OnRecvEvent += OnEvent;

            _logger.Info("SimConnect object created. Waiting for RecvOpen.");
        }
        catch (COMException ex)
        {
            _sim = null;
            SetState(ConnectionState.Error, "MSFS 2024 is not running, or SimConnect refused the connection.");
            _logger.Error("SimConnect.Open failed", ex);
        }
        catch (Exception ex)
        {
            _sim = null;
            SetState(ConnectionState.Error, "Failed to create SimConnect: " + ex.Message);
            _logger.Error("SimConnect.Open failed", ex);
        }
    }

    public void Disconnect()
    {
        CloseSim("Disconnected.");
    }

    public void Receive()
    {
        try
        {
            _sim?.ReceiveMessage();
            try
            {
                _monitor?.ReceiveMessage();
            }
            catch (Exception ex)
            {
                _logger.Warn("Binding monitor stopped: " + ex.Message);
                _monitor = null;
            }
            try
            {
                _sender?.ReceiveMessage();
            }
            catch (Exception ex)
            {
                _logger.Warn("Stick sender stopped: " + ex.Message);
                _sender = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("ReceiveMessage failed", ex);
            CloseSim("SimConnect receive failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        CloseSim("Disposed.");
    }

    private void OnOpen(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        _logger.Info($"SimConnect open. App={data.szApplicationName} Ver={data.dwApplicationVersionMajor}.{data.dwApplicationVersionMinor}");
        try
        {
            RegisterIdentity();
            RegisterFlight();
            RegisterFenixFcu();
            RegisterFlags();
            RegisterYokeSense();
            RegisterYokeWrite();
            RegisterRateWrite();
            RegisterPanel();
            RegisterBridge();
            MapAxisEvents();
            sender.SubscribeToSystemEvent(Events.AircraftLoaded, "AircraftLoaded");
            sender.SubscribeToSystemEvent(Events.SimStart, "SimStart");
            sender.SubscribeToSystemEvent(Events.SimStop, "SimStop");
            sender.SubscribeToSystemEvent(Events.Pause, "Pause");

            sender.RequestDataOnSimObject(
                Requests.Identity,
                Definitions.Identity,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.CHANGED,
                0, 0, 0);

            sender.RequestDataOnSimObject(
                Requests.Flight,
                Definitions.Flight,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SIM_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);

            try
            {
                sender.RequestDataOnSimObject(
                    Requests.FenixFcu,
                    Definitions.FenixFcu,
                    Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.CHANGED,
                    0, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.Warn("Fenix FCU LVar request was not accepted: " + ex.Message);
            }

            try
            {
                sender.RequestDataOnSimObject(
                    Requests.Flags,
                    Definitions.Flags,
                    Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.CHANGED,
                    0, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.Warn("INT32 flag request was not accepted: " + ex.Message);
            }

            try
            {
                sender.RequestDataOnSimObject(
                    Requests.YokeSense,
                    Definitions.YokeSense,
                    Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.SIM_FRAME,
                    SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                    0, 0, 0);
            }
            catch (Exception ex)
            {
                _logger.Warn("Yoke indicator request was not accepted: " + ex.Message);
            }

            SetState(ConnectionState.Connected, "Connected to MSFS 2024. Waiting for aircraft data…");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to register data definitions", ex);
            SetState(ConnectionState.Error, "Connected but data registration failed: " + ex.Message);
        }
    }

    private void OnQuit(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV data)
    {
        _logger.Warn("Simulator quit.");
        CloseSim("MSFS closed.");
    }

    private void OnException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        _sentNames.TryGetValue(data.dwSendID, out var what);
        _logger.Error($"SimConnect exception dwException={data.dwException} sendId={data.dwSendID} index={data.dwIndex} {what}");
        // A rejected field is dropped from its definition, which shifts every
        // later field. Never trust a packet whose layout no longer matches.
        if (what != null && what.StartsWith(nameof(Definitions.Flight) + " ", StringComparison.Ordinal))
        {
            _flightLayoutBroken = true;
            SetState(ConnectionState.Error, "MSFS rejected a flight variable (" + what + "). Telemetry disabled — see log.");
            return;
        }
        StatusMessage?.Invoke(this, $"SimConnect exception {data.dwException} (index {data.dwIndex}). See log.");
    }

    private void OnEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        switch ((Events)data.uEventID)
        {
            case Events.AircraftLoaded:
                _writesPausedUntil = DateTime.UtcNow.AddSeconds(8);
                _logger.Info("AircraftLoaded event. Writes paused while the flight loads.");
                StatusMessage?.Invoke(this, "Aircraft loaded. Writes paused for 8 seconds.");
                break;
            case Events.SimStart:
                _writesPausedUntil = DateTime.UtcNow.AddSeconds(8);
                SetState(ConnectionState.SimRunning, "Simulator running. Writes paused while the flight loads.");
                break;
            case Events.SimStop:
                SetState(ConnectionState.Connected, "Simulator stopped.");
                break;
            case Events.Pause:
                _logger.Info($"Pause event data={data.dwData}");
                break;
            case Events.AxisElevator:
            case Events.AxisAileron:
            case Events.AxisRudder:
                NoteMaskedAxis((Events)data.uEventID, data.dwData);
                break;
        }
    }

    public void SetFeelGain(double gain) => _feelGain = Numeric.Clamp(gain, 0.35, 1.0);

    public bool DirectInput { get; set; } = true;
    private bool _directActive;

    // ---- Binding-conflict monitor (direct route) ----
    // A second connection with a plain, non-masking group just above the sim
    // sees every stick event that reaches the aircraft. Anything HeavyPro did not
    // send comes from an MSFS binding that should have been cleared.
    private enum MonitorEvents : uint { Elevator = 1, Aileron = 2, Rudder = 3 }
    private enum MonitorGroups : uint { Watch = 1 }
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _monitor;
    private readonly Dictionary<Events, Queue<(int Value, long Tick)>> _sentHistory = new();
    private readonly Dictionary<string, (int Count, DateTime First, DateTime Last)> _foreign = new();
    private static readonly TimeSpan SentMemory = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ConflictHold = TimeSpan.FromSeconds(10);

    public string? InputConflict
    {
        get
        {
            var now = DateTime.UtcNow;
            if (BridgeCovering)
                return null;
            var axes = _foreign.Where(f => f.Value.Count >= 3 && now - f.Value.Last < ConflictHold).Select(f => f.Key).ToList();
            return axes.Count == 0 ? null : string.Join(", ", axes);
        }
    }

    private void OpenMonitor()
    {
        try
        {
            _monitor = new Microsoft.FlightSimulator.SimConnect.SimConnect("HeavyPro binding monitor", IntPtr.Zero, 0, null, 0);
            _monitor.OnRecvEvent += OnMonitorEvent;
            _monitor.MapClientEventToSimEvent(MonitorEvents.Elevator, "AXIS_ELEVATOR_SET");
            _monitor.MapClientEventToSimEvent(MonitorEvents.Aileron, "AXIS_AILERONS_SET");
            _monitor.MapClientEventToSimEvent(MonitorEvents.Rudder, "AXIS_RUDDER_SET");
            _monitor.AddClientEventToNotificationGroup(MonitorGroups.Watch, MonitorEvents.Elevator, false);
            _monitor.AddClientEventToNotificationGroup(MonitorGroups.Watch, MonitorEvents.Aileron, false);
            _monitor.AddClientEventToNotificationGroup(MonitorGroups.Watch, MonitorEvents.Rudder, false);
            _monitor.SetNotificationGroupPriority(MonitorGroups.Watch, 1950000000u);
        }
        catch (Exception ex)
        {
            _monitor = null;
            _logger.Warn("Binding monitor unavailable: " + ex.Message);
        }
    }

    // ---- Stick sender (masked route) ----
    // Sends from the masking connection at Standard priority reach the flight model but
    // skip the handler PMDG and iniBuilds put between 10,000,001 and 1e8, so their yoke
    // never moves. Measured: a client that holds these events in its own maskable group
    // cannot get sends through to that handler, but one without groups can. This second
    // connection has no groups and sends where the hardware stick would carry on.
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _sender;

    private void OpenSender()
    {
        try
        {
            _sender = new Microsoft.FlightSimulator.SimConnect.SimConnect("HeavyPro stick sender", IntPtr.Zero, 0, null, 0);
            _sender.MapClientEventToSimEvent(Events.AxisElevator, "AXIS_ELEVATOR_SET");
            _sender.MapClientEventToSimEvent(Events.AxisAileron, "AXIS_AILERONS_SET");
            _sender.MapClientEventToSimEvent(Events.AxisRudder, "AXIS_RUDDER_SET");
        }
        catch (Exception ex)
        {
            _sender = null;
            _logger.Warn("Stick sender unavailable; PMDG and iniBuilds yokes may not move: " + ex.Message);
        }
    }

    /// <summary>Aircraft whose own control handler moves the yoke (PMDG, iniBuilds).</summary>
    private bool SendThroughAddonHandler => !DirectInput && _sender != null && _identity.NeedsDirectRoute && !_identity.IsA350;

    /// <summary>
    /// The A350's own masking group sits at the same priority as HeavyPro's. Sent at
    /// 10,000,001 the stick lands after it, so its fly-by-wire never gets the stick and
    /// fights HeavyPro's elevator. Passed on from HeavyPro's own group instead (the
    /// standard way to re-send a masked event), it goes to the groups after HeavyPro's,
    /// the A350's included, and never back to HeavyPro.
    /// </summary>
    private bool SendFromOwnGroup => !DirectInput && _identity.IsA350;

    private void OnMonitorEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        var (ev, axis) = (MonitorEvents)data.uEventID switch
        {
            MonitorEvents.Elevator => (Events.AxisElevator, "pitch"),
            MonitorEvents.Aileron => (Events.AxisAileron, "roll"),
            _ => (Events.AxisRudder, "yaw")
        };
        // Only axes HeavyPro feeds can conflict; an axis left to MSFS on purpose cannot.
        if (!_physicalAxis.ContainsKey(ev))
            return;
        var value = unchecked((int)data.dwData);
        if (WeSent(ev, value))
            return;
        var now = DateTime.UtcNow;
        var seen = _foreign.TryGetValue(axis, out var f) && now - f.Last < TimeSpan.FromSeconds(3) ? f : (0, now, now);
        _foreign[axis] = (seen.Item1 + 1, seen.Item2, now);
        if (seen.Item1 + 1 == 3)
            _logger.Warn($"MSFS still has a binding sending {axis}: it fights HeavyPro. Clear it in MSFS Controls.");
    }

    private bool WeSent(Events ev, int value)
    {
        if (!_sentHistory.TryGetValue(ev, out var history))
            return false;
        var cutoff = DateTime.UtcNow.Ticks - SentMemory.Ticks;
        while (history.Count > 0 && history.Peek().Tick < cutoff)
            history.Dequeue();
        foreach (var sent in history)
            if (Math.Abs(sent.Value - value) <= 2)
                return true;
        return false;
    }

    public void FeedPhysical(int? elevator, int? aileron, int? rudder)
    {
        if (_sim == null || !_directActive)
            return;
        try
        {
            if (elevator.HasValue) NotePhysical(Events.AxisElevator, elevator.Value);
            if (aileron.HasValue) NotePhysical(Events.AxisAileron, aileron.Value);
            if (rudder.HasValue) NotePhysical(Events.AxisRudder, rudder.Value);
            _route.NoteDirectInput();
        }
        catch (Exception ex)
        {
            _route.Fault(ex.Message);
            _logger.Error("Direct stick send failed", ex);
        }
    }

    private bool ShapingOwnsInput =>
        _shapingArmed
        && DateTime.UtcNow - _lastInfluence < ArmedTimeout
        && DateTime.UtcNow >= _writesPausedUntil;

    private void NoteMaskedAxis(Events ev, uint data)
    {
        var raw = unchecked((int)data);
        if (raw > 16384)
            raw -= 65536;
        raw = (int)Numeric.Clamp(raw, -16383, 16383);
        if (_recentAxisWrites.TryGetValue(ev, out var sent)
            && sent.Value == raw
            && DateTime.UtcNow.Ticks - sent.Tick < EchoWindow.Ticks)
            return;

        _route.NoteMaskedAxis();
        try
        {
            NotePhysical(ev, raw);
        }
        catch (Exception ex)
        {
            _route.Fault(ex.Message);
            _logger.Error("Axis pass-through failed", ex);
        }
    }

    /// <summary>Record the stick and, when not shaping, hand it straight to the sim.</summary>
    private void NotePhysical(Events ev, int raw)
    {
        raw = (int)Numeric.Clamp(raw, -16383, 16383);
        _physicalAxis[ev] = raw;
        if (!ShapingOwnsInput)
            TransmitAxis(ev, raw);
    }

    /// <summary>Send the last physical stick values so the sim matches the hardware again.</summary>
    private void ReleaseToPhysical()
    {
        try
        {
            foreach (var axis in _physicalAxis.ToList())
                TransmitAxis(axis.Key, axis.Value);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not restore physical stick position", ex);
        }
    }

    private double? Captured(Events ev) =>
        _physicalAxis.TryGetValue(ev, out var raw) ? raw / 16384.0 : null;

    private void OnSimObjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        try
        {
            switch ((Requests)data.dwRequestID)
            {
                case Requests.Identity:
                    if (data.dwData[0] is IdentityPacket id)
                    {
                        var next = new AircraftIdentity
                        {
                            Title = Clean(id.Title),
                            AtcModel = Clean(id.AtcModel),
                            AtcType = Clean(id.AtcType),
                            AtcId = Clean(id.AtcId),
                            Category = Clean(id.Category)
                        };
                        _identity = next;
                        _logger.Info($"Aircraft: {next.DisplayName} model={next.AtcModel} type={next.AtcType} id={next.AtcId}");
                        AircraftChanged?.Invoke(this, next);
                        SetState(ConnectionState.SimRunning, $"Connected — {next.DisplayName}");
                    }
                    break;

                case Requests.Flight:
                    if (data.dwData[0] is FlightPacket f && !_flightLayoutBroken)
                    {
                        var snap = MapFlight(f, _identity, _fenixFcu, _flags, _yokeSense);
                        SnapshotReceived?.Invoke(this, snap);
                    }
                    break;

                case Requests.FenixFcu:
                    if (data.dwData[0] is FenixFcuPacket fenix)
                        _fenixFcu = fenix;
                    break;

                case Requests.Flags:
                    if (data.dwData[0] is FlagsPacket flags)
                        _flags = flags;
                    break;

                case Requests.YokeSense:
                    if (data.dwData[0] is YokeSensePacket ys)
                        _yokeSense = ys;
                    break;

                case Requests.PanelRequest:
                    if (data.dwData[0] is PanelRequestPacket panel)
                        OnPanelRequest(panel);
                    break;

                case Requests.BridgeStatus:
                    if (data.dwData[0] is BridgeStatusPacket bridge)
                        OnBridgeStatus(bridge);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to decode SimObject data", ex);
        }
    }

    private void RegisterIdentity()
    {
        AddString(Definitions.Identity, "TITLE");
        AddString(Definitions.Identity, "ATC MODEL");
        AddString(Definitions.Identity, "ATC TYPE");
        AddString(Definitions.Identity, "ATC ID");
        AddString(Definitions.Identity, "CATEGORY");
        _sim!.RegisterDataDefineStruct<IdentityPacket>(Definitions.Identity);
    }

    private void RegisterFlight()
    {
        Add(Definitions.Flight, "AIRSPEED INDICATED", "knots");
        Add(Definitions.Flight, "AIRSPEED TRUE", "knots");
        Add(Definitions.Flight, "AIRSPEED MACH", "mach");
        Add(Definitions.Flight, "GROUND VELOCITY", "knots");
        Add(Definitions.Flight, "VERTICAL SPEED", "feet per minute");
        Add(Definitions.Flight, "PLANE PITCH DEGREES", "degrees");
        Add(Definitions.Flight, "PLANE BANK DEGREES", "degrees");
        Add(Definitions.Flight, "PLANE HEADING DEGREES TRUE", "degrees");
        Add(Definitions.Flight, "PLANE LATITUDE", "degrees");
        Add(Definitions.Flight, "PLANE LONGITUDE", "degrees");
        Add(Definitions.Flight, "PLANE ALTITUDE", "feet");
        Add(Definitions.Flight, "INCIDENCE ALPHA", "degrees");
        Add(Definitions.Flight, "INCIDENCE BETA", "degrees");
        Add(Definitions.Flight, "G FORCE", "gforce");
        Add(Definitions.Flight, "ROTATION VELOCITY BODY X", "radians per second");
        Add(Definitions.Flight, "ROTATION VELOCITY BODY Y", "radians per second");
        Add(Definitions.Flight, "ROTATION VELOCITY BODY Z", "radians per second");
        Add(Definitions.Flight, "ACCELERATION BODY X", "feet per second squared");
        Add(Definitions.Flight, "ACCELERATION BODY Y", "feet per second squared");
        Add(Definitions.Flight, "ACCELERATION BODY Z", "feet per second squared");
        Add(Definitions.Flight, "VELOCITY BODY X", "feet per second");
        Add(Definitions.Flight, "VELOCITY BODY Y", "feet per second");
        Add(Definitions.Flight, "VELOCITY BODY Z", "feet per second");
        Add(Definitions.Flight, "DYNAMIC PRESSURE", "psf");
        Add(Definitions.Flight, "SIM ON GROUND", "number");
        Add(Definitions.Flight, "TOTAL WEIGHT", "pounds");
        Add(Definitions.Flight, "EMPTY WEIGHT", "pounds");
        Add(Definitions.Flight, "MAX GROSS WEIGHT", "pounds");
        Add(Definitions.Flight, "TOTAL WEIGHT PITCH MOI", "slug feet squared");
        Add(Definitions.Flight, "TOTAL WEIGHT ROLL MOI", "slug feet squared");
        Add(Definitions.Flight, "TOTAL WEIGHT YAW MOI", "slug feet squared");
        Add(Definitions.Flight, "FUEL TOTAL QUANTITY WEIGHT", "pounds");
        Add(Definitions.Flight, "FLAPS HANDLE PERCENT", "percent");
        Add(Definitions.Flight, "FLAPS HANDLE INDEX", "number");
        Add(Definitions.Flight, "TRAILING EDGE FLAPS LEFT PERCENT", "percent");
        Add(Definitions.Flight, "GEAR HANDLE POSITION", "percent");
        Add(Definitions.Flight, "GEAR TOTAL PCT EXTENDED", "percent");
        Add(Definitions.Flight, "SPOILERS HANDLE POSITION", "percent");
        Add(Definitions.Flight, "GENERAL ENG THROTTLE LEVER POSITION:1", "percent");
        Add(Definitions.Flight, "GENERAL ENG THROTTLE LEVER POSITION:2", "percent");
        Add(Definitions.Flight, "TURB ENG N1:1", "percent");
        Add(Definitions.Flight, "TURB ENG N1:2", "percent");
        Add(Definitions.Flight, "YOKE X POSITION", "position");
        Add(Definitions.Flight, "YOKE Y POSITION", "position");
        Add(Definitions.Flight, "YOKE X POSITION WITH AP", "position");
        Add(Definitions.Flight, "YOKE Y POSITION WITH AP", "position");
        Add(Definitions.Flight, "RUDDER PEDAL POSITION", "position");
        Add(Definitions.Flight, "ELEVATOR POSITION", "position");
        Add(Definitions.Flight, "AILERON POSITION", "position");
        Add(Definitions.Flight, "RUDDER POSITION", "position");
        Add(Definitions.Flight, "ELEVATOR DEFLECTION PCT", "percent over 100");
        Add(Definitions.Flight, "AILERON LEFT DEFLECTION PCT", "percent over 100");
        Add(Definitions.Flight, "RUDDER DEFLECTION PCT", "percent over 100");
        Add(Definitions.Flight, "ELEVATOR TRIM PCT", "percent over 100");
        Add(Definitions.Flight, "AUTOPILOT MASTER", "number");
        Add(Definitions.Flight, "PLANE ALT ABOVE GROUND", "feet");
        Add(Definitions.Flight, "RADIO HEIGHT", "feet");
        Add(Definitions.Flight, "AUTOPILOT FLIGHT DIRECTOR ACTIVE", "number");
        Add(Definitions.Flight, "AUTOPILOT HEADING LOCK", "number");
        Add(Definitions.Flight, "AUTOPILOT ALTITUDE LOCK", "number");
        _sim!.RegisterDataDefineStruct<FlightPacket>(Definitions.Flight);
    }

    private void RegisterFlags()
    {
        try
        {
            AddInt(Definitions.Flags, "SIM ON GROUND");
            AddInt(Definitions.Flags, "AUTOPILOT MASTER");
            _sim!.RegisterDataDefineStruct<FlagsPacket>(Definitions.Flags);
            _logger.Info("Registered INT32 flag definition (ground + AP master).");
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not register INT32 flags: " + ex.Message);
        }
    }

    private void RegisterFenixFcu()
    {
        try
        {
            // Community-documented Fenix FCU locals. Isolated so a missing name
            // cannot mis-align the main flight packet.
            Add(Definitions.FenixFcu, "L:S_FCU_AP1", "number");
            Add(Definitions.FenixFcu, "L:S_FCU_AP2", "number");
            Add(Definitions.FenixFcu, "L:I_FCU_AP1", "number");
            Add(Definitions.FenixFcu, "L:I_FCU_AP2", "number");
            _sim!.RegisterDataDefineStruct<FenixFcuPacket>(Definitions.FenixFcu);
            _logger.Info("Registered Fenix FCU LVar definition (AP1/AP2).");
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not register Fenix FCU LVars: " + ex.Message);
        }
    }

    private void RegisterYokeSense()
    {
        try
        {
            Add(Definitions.YokeSense, "YOKE X INDICATOR", "position");
            Add(Definitions.YokeSense, "YOKE Y INDICATOR", "position");
            _sim!.RegisterDataDefineStruct<YokeSensePacket>(Definitions.YokeSense);
            _logger.Info("Registered YOKE X/Y INDICATOR read.");
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not register yoke indicators: " + ex.Message);
        }
    }

    private void RegisterYokeWrite()
    {
        try
        {
            Add(Definitions.YokeWrite, "YOKE X POSITION", "position");
            Add(Definitions.YokeWrite, "YOKE Y POSITION", "position");
            Add(Definitions.YokeWrite, "RUDDER PEDAL POSITION", "position");
            _sim!.RegisterDataDefineStruct<YokeWritePacket>(Definitions.YokeWrite);
            _yokeWriteReady = true;
            _logger.Info("Registered YOKE POSITION write definition.");
        }
        catch (Exception ex)
        {
            _yokeWriteReady = false;
            _logger.Warn("Could not register yoke writes: " + ex.Message);
        }
    }

    private void RegisterRateWrite()
    {
        try
        {
            Add(Definitions.RateWrite, "ROTATION VELOCITY BODY X", "radians per second");
            Add(Definitions.RateWrite, "ROTATION VELOCITY BODY Y", "radians per second");
            Add(Definitions.RateWrite, "ROTATION VELOCITY BODY Z", "radians per second");
            _sim!.RegisterDataDefineStruct<RateWritePacket>(Definitions.RateWrite);
            _rateWriteReady = true;
            _logger.Info("Registered rate-write definition (ROTATION VELOCITY BODY X/Y/Z).");
        }
        catch (Exception ex)
        {
            _rateWriteReady = false;
            _logger.Warn("Could not register rate writes: " + ex.Message);
        }
    }

    private void MapAxisEvents()
    {
        try
        {
            _sim!.MapClientEventToSimEvent(Events.AxisElevator, "AXIS_ELEVATOR_SET");
            _sim.MapClientEventToSimEvent(Events.AxisAileron, "AXIS_AILERONS_SET");
            _sim.MapClientEventToSimEvent(Events.AxisRudder, "AXIS_RUDDER_SET");
            if (DirectInput)
            {
                // No notification group at all: measured in MSFS 2024, a client
                // that holds these events in a maskable group cannot get its own
                // sends through to add-ons like the PMDG 777.
                _directActive = true;
                _axisWriteReady = true;
                _route.BeginDirect();
                OpenMonitor();
                _logger.Info("Direct route: HeavyPro sends the stick where hardware enters. The stick's axes must be unbound in MSFS.");
                return;
            }
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisElevator, true);
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisAileron, true);
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisRudder, true);
            _sim.SetNotificationGroupPriority(Groups.Stick, 10000000u);
            OpenSender();
            _axisWriteReady = true;
            _route.Begin();
            _logger.Info("AXIS events mapped maskable at priority 10000000. Waiting for a joystick event.");
        }
        catch (Exception ex)
        {
            _axisWriteReady = false;
            _logger.Warn("Could not map AXIS events: " + ex.Message);
        }
    }

    public void ApplyInfluence(InfluenceCommand command)
    {
        if (_sim == null)
            return;

        var now = DateTime.UtcNow;
        var armed = command.Armed && !_writesDisabled && now >= _writesPausedUntil;
        if (_shapingArmed && !armed)
            ReleaseToPhysical();
        _shapingArmed = armed;
        _lastInfluence = now;
        if (!armed || !command.Active)
        {
            // Shaped output has settled: send the exact stick value once so the
            // sim is not left a few counts off centre.
            if (armed && _wasWriting)
                ReleaseToPhysical();
            _wasWriting = false;
            return;
        }
        _wasWriting = true;

        try
        {
            if (command.WriteRates && _rateWriteReady)
            {
                var packet = new RateWritePacket
                {
                    X = command.RateX,
                    Y = command.RateY,
                    Z = command.RateZ
                };
                _sim.SetDataOnSimObject(
                    Definitions.RateWrite,
                    Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                    packet);
            }

            // Only axes captured through the masked route are written. Writing an
            // axis the hardware still drives directly would fight it.
            if (command.WriteAxes && _axisWriteReady)
            {
                if (_physicalAxis.ContainsKey(Events.AxisElevator))
                    TransmitAxis(Events.AxisElevator, command.ElevatorAxis);
                if (_physicalAxis.ContainsKey(Events.AxisAileron))
                    TransmitAxis(Events.AxisAileron, command.AileronAxis);
                if (_physicalAxis.ContainsKey(Events.AxisRudder))
                    TransmitAxis(Events.AxisRudder, command.RudderAxis);
            }

            if (command.WriteYoke && _yokeWriteReady)
            {
                var yoke = new YokeWritePacket
                {
                    X = command.YokeXOut,
                    Y = command.YokeYOut,
                    Rudder = command.RudderOut
                };
                _sim.SetDataOnSimObject(
                    Definitions.YokeWrite,
                    Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                    yoke);
            }

            if ((_writeLogSkip++ % 120) == 0)
                _logger.Info("Write " + command.Reason);
        }
        catch (Exception ex)
        {
            _writesDisabled = true;
            _shapingArmed = false;
            ReleaseToPhysical();
            _logger.Error("Influence write failed — further writes disabled this session", ex);
            StatusMessage?.Invoke(this, "Write failed. Master writes paused. See log.");
        }
    }

    public void ApplyViewCue(ViewCue cue)
    {
        if (_sim == null || _cameraDisabled)
            return;
        try
        {
            var method = _sim.GetType().GetMethod("CameraSetRelative6Dof");
            if (method == null)
            {
                _cameraDisabled = true;
                _logger.Warn("CameraSetRelative6Dof is not on this SimConnect build — view cue off.");
                return;
            }
            method.Invoke(_sim, new object[] { cue.X, cue.Y, cue.Z, cue.Pitch, cue.Bank, cue.Heading });
        }
        catch (Exception ex)
        {
            _cameraDisabled = true;
            _logger.Warn("View cue disabled: " + ex.Message);
        }
    }

    private void TransmitAxis(Events ev, int value)
    {
        var clamped = (int)Numeric.Clamp(value, -16383, 16383);
        if (_recentAxisWrites.TryGetValue(ev, out var recent) && recent.Value == clamped)
            return;
        _recentAxisWrites[ev] = (clamped, DateTime.UtcNow.Ticks);
        if (!_sentHistory.TryGetValue(ev, out var history))
            _sentHistory[ev] = history = new Queue<(int, long)>();
        history.Enqueue((clamped, DateTime.UtcNow.Ticks));
        if (history.Count > 400)
            history.Dequeue();
        WriteBridge(ev, clamped);
        if (SendFromOwnGroup)
        {
            _sim!.TransmitClientEvent(
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                ev,
                unchecked((uint)clamped),
                Groups.Stick,
                SIMCONNECT_EVENT_FLAG.DEFAULT);
            return;
        }
        if (SendThroughAddonHandler)
        {
            _sender!.TransmitClientEvent(
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                ev,
                unchecked((uint)clamped),
                EventPriority.AfterMask,
                SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
            return;
        }
        _sim!.TransmitClientEvent(
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            ev,
            unchecked((uint)clamped),
            // Direct: enter where hardware does, so add-ons that run their own
            // control model (PMDG, iniBuilds) take it like the real stick; measured
            // smooth on the PMDG 777. Masked: below our own masking group.
            DirectInput ? EventPriority.Highest : EventPriority.Standard,
            // Without this flag SimConnect reads the priority as a group ID,
            // rejects the event, and the masked stick never reaches the sim.
            SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    private void AddInt(Definitions def, string name)
    {
        _sim!.AddToDataDefinition(def, name, "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        Track($"{def} {name} (bool)");
    }

    private void Add(Definitions def, string name, string units)
    {
        _sim!.AddToDataDefinition(def, name, units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        Track($"{def} {name} ({units})");
    }

    private void Track(string what)
    {
        var id = _sim!.GetLastSentPacketID();
        _sentNames[id] = what;
    }

    private void AddString(Definitions def, string name)
    {
        _sim!.AddToDataDefinition(def, name, null, SIMCONNECT_DATATYPE.STRING256, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
    }

    private FlightSnapshot MapFlight(FlightPacket f, AircraftIdentity identity, FenixFcuPacket fenix, FlagsPacket flags, YokeSensePacket yoke)
    {
        var onGround = FlightStateResolver.ResolveOnGround(
            0,
            f.OnGround,
            f.AltitudeAgl,
            f.RadioHeight,
            f.AirspeedIndicated,
            0,
            flags.SimOnGround);
        var ap = FlightStateResolver.ResolveAutopilot(
            f.Autopilot,
            f.FlightDirector,
            f.HeadingLock,
            f.AltitudeLock,
            fenix.Ap1,
            fenix.Ap2,
            fenix.Ap1Light,
            fenix.Ap2Light,
            flags.AutopilotMaster);

        return new FlightSnapshot
        {
            Utc = DateTime.UtcNow,
            Aircraft = identity,
            Class = AircraftCatalog.Classify(identity, f.MaxGrossWeight),
            AirspeedIndicatedKnots = f.AirspeedIndicated,
            AirspeedTrueKnots = f.AirspeedTrue,
            AirspeedMach = f.AirspeedMach,
            GroundSpeedKnots = f.GroundVelocity,
            VerticalSpeedFpm = f.VerticalSpeed,
            PitchDegrees = f.Pitch,
            BankDegrees = f.Bank,
            HeadingTrueDegrees = f.HeadingTrue,
            LatitudeDegrees = f.Latitude,
            LongitudeDegrees = f.Longitude,
            AltitudeFeet = f.Altitude,
            AngleOfAttackDegrees = f.Alpha,
            SideslipDegrees = f.Beta,
            GForce = f.GForce,
            RotationVelocityBodyX = f.RotVelX,
            RotationVelocityBodyY = f.RotVelY,
            RotationVelocityBodyZ = f.RotVelZ,
            AccelerationBodyX = f.AccX,
            AccelerationBodyY = f.AccY,
            AccelerationBodyZ = f.AccZ,
            VelocityBodyX = f.VelX,
            VelocityBodyY = f.VelY,
            VelocityBodyZ = f.VelZ,
            DynamicPressurePsf = f.DynamicPressure,
            OnGround = onGround,
            AltitudeAglFeet = f.AltitudeAgl,
            RadioHeightFeet = f.RadioHeight,
            TotalWeightPounds = f.TotalWeight,
            EmptyWeightPounds = f.EmptyWeight,
            MaxGrossWeightPounds = f.MaxGrossWeight,
            PitchMoi = f.PitchMoi,
            RollMoi = f.RollMoi,
            YawMoi = f.YawMoi,
            FuelWeightPounds = f.FuelWeight,
            FlapsHandlePercent = f.FlapsHandlePercent,
            FlapsHandleIndex = f.FlapsHandleIndex,
            TrailingEdgeFlapsPercent = f.TrailingEdgeFlaps,
            GearHandlePosition = f.GearHandle,
            GearTotalExtendedPercent = f.GearExtended,
            SpoilersHandlePercent = f.SpoilersHandle,
            Throttle1Percent = f.Throttle1,
            Throttle2Percent = f.Throttle2,
            EngineN1_1 = f.N1_1,
            EngineN1_2 = f.N1_2,
            YokeX = f.YokeX,
            YokeY = f.YokeY,
            YokeXWithAp = f.YokeXAp,
            YokeYWithAp = f.YokeYAp,
            CapturedPitch = Captured(Events.AxisElevator),
            CapturedRoll = Captured(Events.AxisAileron),
            CapturedYaw = Captured(Events.AxisRudder),
            YokeXIndicator = yoke.XIndicator,
            YokeYIndicator = yoke.YIndicator,
            RudderPedal = f.RudderPedal,
            ElevatorPosition = f.ElevatorPos,
            AileronPosition = f.AileronPos,
            RudderPosition = f.RudderPos,
            ElevatorDeflectionPct = f.ElevatorDefl,
            AileronDeflectionPct = f.AileronDefl,
            RudderDeflectionPct = f.RudderDefl,
            ElevatorTrimPct = f.ElevatorTrim,
            AutopilotMaster = ap,
            AutopilotLabel = FlightStateResolver.AutopilotLabel(ap, fenix.Ap1, fenix.Ap2, f.Autopilot),
            FenixAp1 = fenix.Ap1,
            FenixAp2 = fenix.Ap2
        };
    }

    private void CloseSim(string message)
    {
        try
        {
            _monitor?.Dispose();
        }
        catch (Exception)
        {
            // The monitor only reports; nothing to recover.
        }
        _monitor = null;
        try
        {
            _sender?.Dispose();
        }
        catch (Exception)
        {
            // Only sends; nothing to recover.
        }
        _sender = null;
        _foreign.Clear();
        _sentHistory.Clear();
        try
        {
            // Hand the stick back to the MSFS binding before letting go.
            if (_bridgeLoaded)
                SetBridge(false);
        }
        catch (Exception)
        {
            // The module also stops on its own once ALIVE stops changing.
        }
        try
        {
            _sim?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Error("Error disposing SimConnect", ex);
        }

        _sim = null;
        _rateWriteReady = false;
        _axisWriteReady = false;
        _yokeWriteReady = false;
        _shapingArmed = false;
        _flightLayoutBroken = false;
        _panelReady = false;
        _bridgeReady = false;
        _bridgeLoaded = false;
        _bridgeShaped.Clear();
        _directActive = false;
        _wasWriting = false;
        _sentNames.Clear();
        _writesDisabled = false;
        _writesPausedUntil = DateTime.MinValue;
        _physicalAxis.Clear();
        _recentAxisWrites.Clear();
        _route.Reset();
        SetState(ConnectionState.Disconnected, message);
    }

    private void SetState(ConnectionState state, string text)
    {
        _state = state;
        StatusText = text;
        _logger.Info($"State={state} {text}");
        StatusMessage?.Invoke(this, text);
        StateChanged?.Invoke(this, state);
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('\0');
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct IdentityPacket
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AtcModel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AtcType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AtcId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Category;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FlightPacket
    {
        public double AirspeedIndicated;
        public double AirspeedTrue;
        public double AirspeedMach;
        public double GroundVelocity;
        public double VerticalSpeed;
        public double Pitch;
        public double Bank;
        public double HeadingTrue;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public double Alpha;
        public double Beta;
        public double GForce;
        public double RotVelX;
        public double RotVelY;
        public double RotVelZ;
        public double AccX;
        public double AccY;
        public double AccZ;
        public double VelX;
        public double VelY;
        public double VelZ;
        public double DynamicPressure;
        public double OnGround;
        public double TotalWeight;
        public double EmptyWeight;
        public double MaxGrossWeight;
        public double PitchMoi;
        public double RollMoi;
        public double YawMoi;
        public double FuelWeight;
        public double FlapsHandlePercent;
        public double FlapsHandleIndex;
        public double TrailingEdgeFlaps;
        public double GearHandle;
        public double GearExtended;
        public double SpoilersHandle;
        public double Throttle1;
        public double Throttle2;
        public double N1_1;
        public double N1_2;
        public double YokeX;
        public double YokeY;
        public double YokeXAp;
        public double YokeYAp;
        public double RudderPedal;
        public double ElevatorPos;
        public double AileronPos;
        public double RudderPos;
        public double ElevatorDefl;
        public double AileronDefl;
        public double RudderDefl;
        public double ElevatorTrim;
        public double Autopilot;
        public double AltitudeAgl;
        public double RadioHeight;
        public double FlightDirector;
        public double HeadingLock;
        public double AltitudeLock;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct YokeSensePacket
    {
        public double XIndicator;
        public double YIndicator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct YokeWritePacket
    {
        public double X;
        public double Y;
        public double Rudder;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FlagsPacket
    {
        public int SimOnGround;
        public int AutopilotMaster;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FenixFcuPacket
    {
        public double Ap1;
        public double Ap2;
        public double Ap1Light;
        public double Ap2Light;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PanelStatePacket
    {
        public double Alive, Master, State, Preset, Inertia, InP, OutP, InR, OutR;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BridgeWritePacket
    {
        public double Elevator, Aileron, Rudder, Override;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BridgeStatusPacket
    {
        public double Version, Overrides;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PanelRequestPacket
    {
        public double Master, Preset, Inertia;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RateWritePacket
    {
        public double X;
        public double Y;
        public double Z;
    }
}
#endif
