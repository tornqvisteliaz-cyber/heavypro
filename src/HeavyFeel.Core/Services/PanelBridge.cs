namespace HeavyFeel.Core.Services;

/// <summary>
/// What the in-game HeavyPro panel shows. Published to L:HEAVYPRO_* variables.
/// State: 0 offline, 1 pass-through, 2 shaping, 3 AP paused, 4 set up stick,
/// 5 unbind in MSFS, 6 move stick, 7 fault. Preset: 0 custom, 1 normal, 2 medium, 3 realistic.
/// </summary>
public readonly record struct PanelState(
    bool Master, int State, int Preset, double Inertia,
    double InPitch, double OutPitch, double InRoll, double OutRoll);

/// <summary>
/// A request from the in-game panel. 0 means "no request" in every field, because an
/// L:var that was never written reads as 0. Master: 1 off, 2 on. Preset: 1-3.
/// Inertia: the wanted value plus one (1-101).
/// </summary>
public readonly record struct PanelRequest(int Master, int Preset, int Inertia)
{
    public bool Any => Master != 0 || Preset != 0 || Inertia != 0;
}
