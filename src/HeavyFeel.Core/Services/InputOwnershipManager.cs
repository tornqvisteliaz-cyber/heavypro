namespace HeavyFeel.Core.Services;

public enum InputRouteStatus
{
    Unavailable,
    Initializing,
    Active,
    Bypassed,
    Fault
}

/// <summary>
/// Tracks whether a documented input route is actually established.
/// ACTIVE is set only after the route has received a masked axis event.
/// </summary>
public sealed class InputOwnershipManager
{
    public InputRouteStatus Status { get; private set; } = InputRouteStatus.Unavailable;
    public string Detail { get; private set; } = "No verified input route.";
    public bool SawMaskedAxis { get; private set; }

    public void Reset()
    {
        Status = InputRouteStatus.Unavailable;
        Detail = "No verified input route.";
        SawMaskedAxis = false;
    }

    public void Begin()
    {
        Status = InputRouteStatus.Initializing;
        Detail = "Maskable AXIS route requested. Waiting for a joystick event.";
    }

    public void BeginDirect()
    {
        Status = InputRouteStatus.Initializing;
        Detail = "Direct route. Waiting for the stick to be read.";
    }

    public void NoteDirectInput()
    {
        if (Status == InputRouteStatus.Active)
            return;
        SawMaskedAxis = true;
        Status = InputRouteStatus.Active;
        Detail = "Direct route. HeavyPro reads the stick and is its only source in MSFS.";
    }

    public void NoteMaskedAxis()
    {
        SawMaskedAxis = true;
        Status = InputRouteStatus.Active;
        Detail = "Masked route. The stick's MSFS events are held back and re-sent shaped.";
    }

    public void Bypass(string reason)
    {
        Status = InputRouteStatus.Bypassed;
        Detail = reason;
    }

    public void Fault(string reason)
    {
        Status = InputRouteStatus.Fault;
        Detail = reason;
    }
}
