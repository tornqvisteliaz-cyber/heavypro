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
}

public interface ISimClientFactory
{
    ISimClient Create();
    string BackendName { get; }
}
