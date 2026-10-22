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
