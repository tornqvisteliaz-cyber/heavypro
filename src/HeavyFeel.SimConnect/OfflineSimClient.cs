using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;
using HeavyFeel.Core.Services;

namespace HeavyFeel.SimConnect;

/// <summary>
/// Used when the official managed SimConnect assembly was not present at build time,
/// or when the user wants to inspect the UI without MSFS. Never talks to the sim.
/// </summary>
public sealed class OfflineSimClient : ISimClient
{
    private ConnectionState _state = ConnectionState.Disconnected;

    public ConnectionState State => _state;
    public string StatusText { get; private set; } = "Offline — SimConnect library not loaded.";
    public bool IsConnected => false;

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<FlightSnapshot>? SnapshotReceived;
    public event EventHandler<AircraftIdentity>? AircraftChanged;
    public event EventHandler<string>? StatusMessage;

    public void Connect()
    {
        _state = ConnectionState.Error;
        StatusText = "Offline build. Copy MSFS 2024 SimConnect DLLs into the lib folder and rebuild. See README.";
        StatusMessage?.Invoke(this, StatusText);
        StateChanged?.Invoke(this, _state);
    }

    public void Disconnect()
    {
        _state = ConnectionState.Disconnected;
        StatusText = "Disconnected (offline).";
        StatusMessage?.Invoke(this, StatusText);
        StateChanged?.Invoke(this, _state);
    }

    public void ApplyInfluence(InfluenceCommand command)
    {
    }

    public void ApplyViewCue(ViewCue cue)
    {
    }

    public void SetFeelGain(double gain)
    {
    }

    public void Dispose()
    {
    }
}
