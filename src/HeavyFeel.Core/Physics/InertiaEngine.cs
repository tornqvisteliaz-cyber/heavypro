using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Physics;

/// <summary>
/// Mass-based command acceleration. The AXIS value cannot jump;
/// it ramps at a rate set by live weight, MOI and aircraft class.
/// Centered stick ramps back to zero then writes stop.
/// Never writes body rates or thrust — those fight MSFS physics.
/// </summary>
public sealed class InertiaEngine
{
    private const double A320TypicalTakeoffLb = 154000;
    private const double Deadzone = 0.04;

    private double _outPitch;
    private double _outRoll;
    private double _outYaw;
    private DateTime _last = DateTime.MinValue;
    private bool _rampingOut;
    private int _lastElv = int.MinValue;
    private int _lastAil = int.MinValue;
    private int _lastRud = int.MinValue;

    public InfluenceCommand Step(AppSettings settings, FlightSnapshot snap, bool masterEnable)
    {
        if (!masterEnable)
        {
            Reset();
            return InfluenceCommand.Idle("Master off — no writes");
        }

        if (snap.AutopilotMaster)
        {
            Reset();
            return InfluenceCommand.Idle("Autopilot on — writes paused");
        }

        var stickY = Clamp1(snap.StickY);
        var stickX = Clamp1(snap.StickX);
        var stickR = Clamp1(snap.RudderPedal);
        var holding =
            Math.Abs(stickY) > Deadzone
            || Math.Abs(stickX) > Deadzone
            || Math.Abs(stickR) > Deadzone;

        var profile = ProfileLibrary.For(snap.Aircraft, snap.Class);
        var phase = FlightPhaseResolver.Resolve(snap);
        var movingOut =
            Math.Abs(_outPitch) > Deadzone
            || Math.Abs(_outRoll) > Deadzone
            || Math.Abs(_outYaw) > Deadzone;

        if (!holding && !movingOut)
        {
            ResetSoft();
            return InfluenceCommand.Idle($"{phase} stick released — hardware has the jet") with
            {
                Phase = phase.ToString(),
                ProfileName = profile.Name
            };
        }

        var dt = 0.016;
        if (_last != DateTime.MinValue)
        {
            var raw = (snap.Utc - _last).TotalSeconds;
            if (raw > 0.001 && raw < 0.12)
                dt = raw;
        }
        _last = snap.Utc;

        var massScale = LiveMassScale(snap);
        var moiScale = MoiScale(snap);
        var size = Math.Clamp(massScale * moiScale * profile.MassFactor, 0.40, 1.60);
        var feel = settings.Inertia / 100.0;
        var response = settings.ControlResponse / 100.0;
        var pitchDamp = settings.PitchDamping / 100.0;
        var rollDamp = settings.RollDamping / 100.0;
        var yawDamp = settings.YawDamping / 100.0;
        var ground = settings.GroundInertia / 100.0;
        var turb = settings.TurbulenceResponse / 100.0;
        var phaseMul = FlightPhaseResolver.PhaseFactor(phase, profile);

        // Each slider must move maxRate a lot. 0 = almost raw, 100 = heavy.
        var maxRate = (3.6 / size) * (0.35 + response * 1.25) / (0.35 + feel * 1.35);
        maxRate /= Math.Max(0.80, phaseMul);
        if (snap.OnGround)
            maxRate /= 0.70 + ground * 0.80;
        if (phase is FlightPhase.Takeoff or FlightPhase.Landing)
            maxRate *= 0.88;

        var gBump = Math.Abs(snap.GForce - 1.0);
        if (gBump > 0.04)
            maxRate /= 1.0 + turb * gBump * 0.55;

        var flaps = Math.Max(snap.FlapsHandlePercent, snap.TrailingEdgeFlapsPercent) / 100.0;
        if (flaps > 0.15)
            maxRate *= 1.0 - flaps * 0.12;

        maxRate = Math.Clamp(maxRate, 0.35, 8.0);

        var pitchRate = Math.Clamp(maxRate / (0.55 + pitchDamp * 1.20) * profile.PitchResponse, 0.25, 8.0);
        var rollRate = Math.Clamp(maxRate / (0.55 + rollDamp * 1.20) * profile.RollResponse, 0.25, 8.0);
        var yawRate = Math.Clamp(maxRate / (0.55 + yawDamp * 1.20) * profile.YawResponse, 0.25, 8.0);

        // Held stick is scaled. A heavy jet never reaches the raw deflection.
        // Released stick still slews back to zero, then writes stop.
        var gain = Authority(feel, response, size, snap.OnGround ? ground : 0);
        var targetY = holding ? stickY * gain : 0;
        var targetX = holding ? stickX * gain : 0;
        var targetR = holding ? stickR * gain : 0;
        _rampingOut = !holding && movingOut;

        _outPitch = Slew(_outPitch, targetY, pitchRate, dt);
        _outRoll = Slew(_outRoll, targetX, rollRate, dt);
        _outYaw = Slew(_outYaw, targetR, yawRate, dt);

        var pitch = Clamp1(_outPitch);
        var roll = Clamp1(_outRoll);
        var yaw = Clamp1(_outYaw);

        var elv = ToAxis(pitch);
        var ail = ToAxis(roll);
        var rud = ToAxis(yaw);
        if (elv == _lastElv && ail == _lastAil && rud == _lastRud)
        {
            return InfluenceCommand.Idle($"{phase} identical AXIS — skip") with
            {
                Phase = phase.ToString(),
                ProfileName = profile.Name,
                Mix = maxRate,
                RawPitch = stickY,
                OutPitch = pitch,
                RawRoll = stickX,
                OutRoll = roll
            };
        }

        _lastElv = elv;
        _lastAil = ail;
        _lastRud = rud;

        var kg = snap.TotalWeightPounds > 1000 ? snap.TotalWeightPounds * 0.45359237 : 0;
        return new InfluenceCommand
        {
            Active = true,
            WriteRates = false,
            WriteAxes = true,
            WriteYoke = true,
            YokeXOut = roll,
            YokeYOut = pitch,
            RudderOut = yaw,
            ElevatorAxis = elv,
            AileronAxis = ail,
            RudderAxis = rud,
            Phase = phase.ToString(),
            ProfileName = profile.Name,
            Mix = gain,
            RawPitch = stickY,
            OutPitch = pitch,
            RawRoll = stickX,
            OutRoll = roll,
            Reason = $"{phase} {profile.Name} gain={gain:0.00} rawY={stickY:+0.00;-0.00;0} outY={pitch:+0.00;-0.00;0} {kg:0}kg"
        };
    }

    public void Reset()
    {
        _outPitch = _outRoll = _outYaw = 0;
        _last = DateTime.MinValue;
        _rampingOut = false;
        _lastElv = _lastAil = _lastRud = int.MinValue;
    }

    private void ResetSoft()
    {
        _outPitch = _outRoll = _outYaw = 0;
        _last = DateTime.MinValue;
        _rampingOut = false;
        _lastElv = _lastAil = _lastRud = int.MinValue;
    }

    public static string Phase(FlightSnapshot snap) => FlightPhaseResolver.Resolve(snap).ToString();

    public static double LiveMassScale(FlightSnapshot snap)
    {
        if (snap.TotalWeightPounds > 20000)
        {
            var reference = A320TypicalTakeoffLb;
            if (snap.Aircraft.IsPmdg777 || snap.Class == AircraftClass.WideBody)
                reference = 500000;
            else if (snap.Class == AircraftClass.HeavyWide)
                reference = 650000;
            else if (snap.Class == AircraftClass.LightGa)
                reference = 2400;
            return Math.Clamp(snap.TotalWeightPounds / reference, 0.72, 1.40);
        }

        var frac = snap.MassFraction;
        if (frac > 0)
            return Math.Clamp(0.85 + frac * 0.35, 0.72, 1.35);

        return 1.0;
    }

    public static double MoiScale(FlightSnapshot snap)
    {
        if (snap.PitchMoi < 1000)
            return 1.0;
        var refMoi = snap.PitchMoi < 50_000 ? 20_000.0 : 1_500_000.0;
        return Math.Clamp(snap.PitchMoi / refMoi, 0.80, 1.25);
    }

    public static double Authority(double feel, double response, double size, double ground)
    {
        var heavy = feel * Math.Clamp(size, 0.45, 1.6);
        var gain = 1.0 - heavy * 0.42 - ground * 0.12 + response * 0.18;
        return Math.Clamp(gain, 0.38, 1.0);
    }

    private static double Slew(double current, double target, double maxPerSec, double dt)
    {
        var maxStep = Math.Max(0.05, maxPerSec) * dt;
        var delta = target - current;
        if (delta > maxStep)
            return current + maxStep;
        if (delta < -maxStep)
            return current - maxStep;
        return target;
    }

    private static double Clamp1(double v) => Math.Clamp(v, -1.0, 1.0);

    private static int ToAxis(double unit) => (int)Math.Round(Clamp1(unit) * 16384.0);
}
