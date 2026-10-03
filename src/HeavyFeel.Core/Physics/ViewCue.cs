using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// Tiny cockpit offset from live G, ground speed and weight.
/// Not a camera addon and not a copy of anyone else's effect list.
/// </summary>
public readonly record struct ViewCue(double X, double Y, double Z, double Pitch, double Bank, double Heading, string Note)
{
    public static ViewCue Zero => new(0, 0, 0, 0, 0, 0, "off");
}

public sealed class ViewCueEngine
{
    private bool _wasGround = true;
    private double _thump;
    private double _phase;

    public ViewCue Step(AppSettings settings, FlightSnapshot snap)
    {
        if (!settings.MasterEnable || !settings.ViewCue)
        {
            Reset();
            return ViewCue.Zero;
        }

        var turb = settings.TurbulenceResponse / 100.0;
        var ground = settings.GroundInertia / 100.0;
        var size = Numeric.Clamp(InertiaEngine.LiveMassScale(snap) * AircraftCatalog.InertiaScale(snap.Class), 0.4, 1.5);
        var heavy = 1.0 / size;

        if (!_wasGround && snap.OnGround)
        {
            var sink = Math.Max(0, -snap.VerticalSpeedFpm);
            _thump = Numeric.Clamp(sink / 700.0, 0, 1.2) * heavy;
        }
        _wasGround = snap.OnGround;
        _thump *= 0.86;

        _phase += snap.OnGround
            ? 0.35 + snap.GroundSpeedKnots * 0.04
            : 0.12 + Math.Abs(snap.GForce - 1) * 0.8;
        if (_phase > 6.2832)
            _phase -= 6.2832;

        var rumble = snap.OnGround && snap.GroundSpeedKnots > 4
            ? Math.Sin(_phase) * Numeric.Clamp(snap.GroundSpeedKnots / 80.0, 0, 1) * ground * 0.012 * heavy
            : 0;

        var g = snap.GForce - 1.0;
        var z = rumble + _thump * 0.035 + g * 0.010 * turb * heavy;
        var pitch = g * 0.18 * turb * heavy + _thump * 0.35;
        var bank = Numeric.Clamp(snap.AccelerationBodyY / 40.0, -1, 1) * 0.15 * turb * heavy;
        var x = rumble * 0.35;

        z = Numeric.Clamp(z, -0.045, 0.045);
        pitch = Numeric.Clamp(pitch, -0.8, 0.8);
        bank = Numeric.Clamp(bank, -0.6, 0.6);
        x = Numeric.Clamp(x, -0.02, 0.02);

        return new ViewCue(x, 0, z, pitch, bank, 0,
            snap.OnGround ? $"view ground gs={snap.GroundSpeedKnots:0}" : $"view air G={snap.GForce:0.00}");
    }

    public void Reset()
    {
        _thump = 0;
        _phase = 0;
    }
}
