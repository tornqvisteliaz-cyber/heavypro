#if HAS_SIMCONNECT
using System.Runtime.InteropServices;
using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using HeavyFeel.Core.Services;
using HeavyFeel.Logging;
using Microsoft.FlightSimulator.SimConnect;

namespace HeavyFeel.SimConnect;

/// <summary>
/// Official SimConnect client. Reads telemetry every frame.
/// When Master Enable is on, writes body rates and AXIS_*_SET commands.
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
        Payload = 8
    }

    private enum Requests : uint
    {
        Identity = 1,
        Flight = 2,
        FenixFcu = 3,
        Flags = 4,
        YokeSense = 5,
        Payload = 6
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
        HighestMaskable = 10000000
    }

    private readonly AppLogger _logger;
    private readonly IntPtr _hwnd;
    private Microsoft.FlightSimulator.SimConnect.SimConnect? _sim;
    private ConnectionState _state = ConnectionState.Disconnected;
    private AircraftIdentity _identity = new();
    private FenixFcuPacket _fenixFcu;
    private FlagsPacket _flags;
    private YokeSensePacket _yokeSense;
    private bool _rateWriteReady;
    private bool _axisWriteReady;
    private bool _yokeWriteReady;
    private bool _writesDisabled;
    private bool _cameraDisabled;
    private int _writeLogSkip;
    private double _feelGain = 0.55;
    private bool _echo;
    private bool _payloadReady;
    private bool _payloadBaselineSet;
    private double _payloadBaseline;
    private double _payloadWritten = -1;
    private bool _onGround = true;
    private double _ias;

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
            RegisterPayload();
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
                SIMCONNECT_PERIOD.VISUAL_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);

            try
            {
                sender.RequestDataOnSimObject(
                    Requests.FenixFcu,
                    Definitions.FenixFcu,
                    Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    SIMCONNECT_PERIOD.VISUAL_FRAME,
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
                    SIMCONNECT_PERIOD.VISUAL_FRAME,
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
                    SIMCONNECT_PERIOD.VISUAL_FRAME,
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
        _logger.Error($"SimConnect exception dwException={data.dwException} sendId={data.dwSendID} index={data.dwIndex}");
        StatusMessage?.Invoke(this, $"SimConnect exception {data.dwException} (index {data.dwIndex}). See log.");
    }

    private void OnEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        switch ((Events)data.uEventID)
        {
            case Events.AircraftLoaded:
                _logger.Info("AircraftLoaded event.");
                StatusMessage?.Invoke(this, "Aircraft loaded. Refreshing identity…");
                break;
            case Events.SimStart:
                SetState(ConnectionState.SimRunning, "Simulator running.");
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
            case Events.AxisThrottle:
                CaptureStick(sender, (Events)data.uEventID, data.dwData);
                break;
        }
    }

    public void SetFeelGain(double gain) => _feelGain = Math.Clamp(gain, 0.35, 1.0);

    public void ApplyPayloadBoost(double extraPounds)
    {
        if (_sim == null || !_payloadReady)
            return;
        if (!_payloadBaselineSet)
            return;
        var target = extraPounds <= 1 ? _payloadBaseline : _payloadBaseline + extraPounds;
        if (Math.Abs(target - _payloadWritten) < 20)
            return;
        try
        {
            _sim.SetDataOnSimObject(
                Definitions.Payload,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_DATA_SET_FLAG.DEFAULT,
                new PayloadPacket { Station1 = target });
            _payloadWritten = target;
            _logger.Info(extraPounds <= 1
                ? $"Payload restored {_payloadBaseline:0} lb"
                : $"Payload {_payloadBaseline:0} -> {target:0} lb");
        }
        catch (Exception ex)
        {
            _payloadReady = false;
            _logger.Warn("Payload write refused: " + ex.Message);
        }
    }

    private void RegisterPayload()
    {
        try
        {
            _sim!.AddToDataDefinition(Definitions.Payload, "PAYLOAD STATION WEIGHT:1", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _sim.RegisterDataDefineStruct<PayloadPacket>(Definitions.Payload);
            _sim.RequestDataOnSimObject(
                Requests.Payload,
                Definitions.Payload,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);
            _payloadReady = true;
            _logger.Info("Payload station 1 registered.");
        }
        catch (Exception ex)
        {
            _payloadReady = false;
            _logger.Warn("Payload station not available: " + ex.Message);
        }
    }

    private void CaptureStick(Microsoft.FlightSimulator.SimConnect.SimConnect sender, Events ev, uint data)
    {
        if (_echo || _writesDisabled)
            return;
        var raw = unchecked((int)data);
        if (raw > 16384)
            raw -= 65536;
        var unit = Math.Clamp(raw / 16384.0, -1, 1);
        if (ev == Events.AxisThrottle)
        {
            var throttle = Math.Clamp(raw / 16383.0, 0, 1);
            if (_onGround && _ias < 180)
                throttle *= 0.42;
            try
            {
                _echo = true;
                TransmitAxis(ev, (int)Math.Round(throttle * 16383.0));
            }
            finally
            {
                _echo = false;
            }
            return;
        }
        var shaped = StickCurve.Shape(unit, _feelGain);
        var axis = (int)Math.Round(shaped * 16384.0);
        try
        {
            _echo = true;
            TransmitAxis(ev, axis);
            if ((_writeLogSkip++ % 90) == 0)
                _logger.Info($"Owned stick {ev} raw={unit:+0.00;-0.00;0} out={shaped:+0.00;-0.00;0} gain={_feelGain:0.00}");
        }
        catch (Exception ex)
        {
            _logger.Warn("Owned stick rewrite failed: " + ex.Message);
        }
        finally
        {
            _echo = false;
        }
    }

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
                    if (data.dwData[0] is FlightPacket f)
                    {
                        var snap = MapFlight(f, _identity, _fenixFcu, _flags, _yokeSense);
                        _onGround = snap.OnGround;
                        _ias = snap.AirspeedIndicatedKnots;
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
                case Requests.Payload:
                    if (data.dwData[0] is PayloadPacket payload && !_payloadBaselineSet && payload.Station1 > 0)
                    {
                        _payloadBaseline = payload.Station1;
                        _payloadBaselineSet = true;
                        _logger.Info($"Payload baseline {payload.Station1:0} lb");
                    }
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

    private int _flightFieldIndex;

    private void RegisterFlight()
    {
        _flightFieldIndex = 0;
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
        Add(Definitions.Flight, "DYNAMIC PRESSURE", "pounds per square foot");
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
        Add(Definitions.Flight, "IS ON GROUND", "number");
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
            AddInt(Definitions.Flags, "IS ON GROUND");
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
            _sim.MapClientEventToSimEvent(Events.AxisThrottle, "AXIS_THROTTLE_SET");
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisElevator, false);
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisAileron, false);
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisRudder, false);
            _sim.AddClientEventToNotificationGroup(Groups.Stick, Events.AxisThrottle, false);
            _sim.SetNotificationGroupPriority(Groups.Stick, EventPriority.HighestMaskable);
            _axisWriteReady = true;
            _logger.Info("HeavyPro owns AXIS events. No other program.");
        }
        catch (Exception ex)
        {
            _axisWriteReady = false;
            _logger.Warn("Could not map AXIS events: " + ex.Message);
        }
    }

    public void ApplyInfluence(InfluenceCommand command)
    {
        if (_sim == null || _writesDisabled || command is not { Active: true })
            return;

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

            if (command.WriteAxes && _axisWriteReady)
            {
                TransmitAxis(Events.AxisElevator, command.ElevatorAxis);
                TransmitAxis(Events.AxisAileron, command.AileronAxis);
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
        var clamped = Math.Clamp(value, -16383, 16384);
        _sim!.TransmitClientEvent(
            Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
            ev,
            unchecked((uint)clamped),
            EventPriority.Highest,
            SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
    }

    private void AddInt(Definitions def, string name)
    {
        _sim!.AddToDataDefinition(def, name, "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
        _logger.Info($"DefineINT32 {name}");
    }

    private void Add(Definitions def, string name, string units)
    {
        _flightFieldIndex++;
        _logger.Info($"Define[{_flightFieldIndex}] {name} ({units})");
        _sim!.AddToDataDefinition(def, name, units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
    }

    private void AddString(Definitions def, string name)
    {
        _sim!.AddToDataDefinition(def, name, null, SIMCONNECT_DATATYPE.STRING256, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
    }

    private static FlightSnapshot MapFlight(FlightPacket f, AircraftIdentity identity, FenixFcuPacket fenix, FlagsPacket flags, YokeSensePacket yoke)
    {
        var onGround = FlightStateResolver.ResolveOnGround(
            f.IsOnGround,
            f.OnGround,
            f.AltitudeAgl,
            f.RadioHeight,
            f.AirspeedIndicated,
            flags.IsOnGround,
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
        _writesDisabled = false;
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
        public double IsOnGround;
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
        public int IsOnGround;
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
    private struct PayloadPacket
    {
        public double Station1;
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
