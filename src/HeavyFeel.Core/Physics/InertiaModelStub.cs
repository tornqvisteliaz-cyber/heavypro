using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// Placeholder for later phases. Phase 1 must not write anything to the sim.
/// The stub only computes debug numbers from live state so we can show
/// that sliders + aircraft state are wired without sending events.
/// </summary>
public static class InertiaModelStub
{
    public static double PreviewTimeConstant(AppSettings settings, FlightSnapshot snap)
    {
        var mass = snap.MassFraction;
        var q = Math.Max(snap.DynamicPressurePsf, 0);
        var qHat = Math.Clamp(q / 150.0, 0.15, 3.0);
        var tau0 = 0.08 + (settings.Inertia / 100.0) * 0.40;
        var damp = 1.0 + (settings.PitchDamping / 100.0) * mass;
        var ground = snap.OnGround ? 1.0 + settings.GroundInertia / 200.0 : 1.0;
        return tau0 * damp * ground / qHat;
    }
}
