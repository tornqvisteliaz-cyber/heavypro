using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;

namespace HeavyFeel.Core.Services;

public interface ISimClient : IDisposable
{
    ConnectionState State { get; }
    string StatusText { get; }
    bool IsConnected { get; }

    event EventHandler<ConnectionState>? StateChanged;
    event EventHandler<FlightSnapshot>? SnapshotReceived;
    event EventHandler<AircraftIdentity>? AircraftChanged;
    event EventHandler<string>? StatusMessage;

    void Connect();
    void Disconnect();

    /// <summary>
    /// Write the current inertia command. No-op when the command is inactive
    /// or the backend cannot write.
    /// </summary>
    void ApplyInfluence(InfluenceCommand command);
    void ApplyViewCue(ViewCue cue);
    void SetFeelGain(double gain);
    InputRouteStatus RouteStatus { get; }

    /// <summary>
    /// Direct route: HeavyPro reads the stick itself and is the only source of
    /// stick events, sent where hardware enters the event chain. Set before Connect.
    /// When false, the stick stays bound in MSFS and its events are masked.
    /// </summary>
    bool DirectInput { get; set; }

    /// <summary>
    /// The stick stays bound in MSFS: the HeavyPro WASM bridge answers MSFS's raw
    /// stick events with HeavyPro's value, so add-ons that hide those events from
    /// SimConnect (PMDG, iniBuilds) still end each frame on the shaped stick.
    /// </summary>
    bool CounterMsfsBindings { get; set; }

    /// <summary>
    /// Axes ("pitch", "roll", "yaw") an MSFS control profile still binds on HeavyPro's own
    /// stick or pedals (from the profile files). On the direct route HeavyPro passes those
    /// axes through instead of fighting them, and keeps shaping the rest.
    /// </summary>
    IReadOnlyCollection<string> KnownConflictAxes { get; set; }

    /// <summary>The HeavyPro WASM bridge is running in the sim.</summary>
    bool BridgeLoaded { get; }

    /// <summary>
    /// Direct route only: the physical stick in AXIS event units (-16384..16384,
    /// MSFS sign convention). Null leaves that axis to MSFS.
    /// </summary>
    void FeedPhysical(int? elevator, int? aileron, int? rudder);

    /// <summary>
    /// Direct route: names the axes ("pitch", "roll", "yaw") that still reach the
    /// sim from an MSFS binding, so MSFS and HeavyPro fight. Null when clean.
    /// </summary>
    string? InputConflict { get; }

    /// <summary>The in-game HeavyPro panel asked for a change.</summary>
    event EventHandler<PanelRequest>? PanelRequested;

    /// <summary>Shows HeavyPro's state on the in-game panel.</summary>
    void PublishPanelState(PanelState state);
    string RouteDetail { get; }
}

public interface ISimClientFactory
{
    ISimClient Create();
    string BackendName { get; }
}
