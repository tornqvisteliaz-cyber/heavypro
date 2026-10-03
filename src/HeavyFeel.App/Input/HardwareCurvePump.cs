using System.Runtime.InteropServices;
using HeavyFeel.Core.Models;
using HeavyFeel.Core.Physics;

namespace HeavyFeel.App.Input;

/// <summary>
/// Reads the Windows joystick and, if vJoy is installed, sends a heavier curve
/// into a virtual device. MSFS must be bound to vJoy, not the physical stick.
/// </summary>
public sealed class HardwareCurvePump
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<FlightSnapshot> _snap;
    private readonly Action<string> _log;
    private bool _acquired;
    private bool _missingLogged;

    public HardwareCurvePump(Func<AppSettings> settings, Func<FlightSnapshot> snap, Action<string> log)
    {
        _settings = settings;
        _snap = snap;
        _log = log;
    }

    public string LastStatus { get; private set; } = "hardware curve idle";

    public void Tick()
    {
        var settings = _settings();
        if (!settings.UseHardwareCurve || !settings.MasterEnable)
        {
            LastStatus = "hardware curve off";
            return;
        }

        if (!JoyOk(out var rawX, out var rawY, out var rawR))
        {
            LastStatus = "no Windows joystick";
            return;
        }

        var snap = _snap();
        var gain = InertiaEngine.Authority(
            settings.Inertia / 100.0,
            settings.ControlResponse / 100.0,
            InertiaEngine.LiveMassScale(snap) * AircraftCatalog.InertiaScale(snap.Class),
            snap.OnGround ? settings.GroundInertia / 100.0 : 0);

        var x = StickCurve.Shape(rawX, gain);
        var y = StickCurve.Shape(rawY, gain);
        var r = StickCurve.Shape(rawR, gain);

        if (!EnsureVJoy())
        {
            LastStatus = $"curve ready gain={gain:0.00} — install vJoy and bind it in MSFS";
            return;
        }

        VJoy.SetAxis(StickCurve.ToVJoy(x), 1, VJoy.HidX);
        VJoy.SetAxis(StickCurve.ToVJoy(y), 1, VJoy.HidY);
        VJoy.SetAxis(StickCurve.ToVJoy(r), 1, VJoy.HidZ);
        LastStatus = $"vJoy gain={gain:0.00} in={rawY:+0.00;-0.00;0} out={y:+0.00;-0.00;0}";
    }

    private bool EnsureVJoy()
    {
        if (_acquired)
            return true;
        try
        {
            if (!VJoy.Enabled())
                return false;
            _acquired = VJoy.Acquire(1);
            if (_acquired && !_missingLogged)
            {
                _log("vJoy device 1 acquired. Bind MSFS aileron/elevator/rudder to vJoy, not the physical stick.");
                _missingLogged = true;
            }
            return _acquired;
        }
        catch (DllNotFoundException)
        {
            if (!_missingLogged)
            {
                _log("vJoyInterface.dll missing. Install vJoy, then restart HeavyPro.");
                _missingLogged = true;
            }
            return false;
        }
    }

    private static bool JoyOk(out double x, out double y, out double r)
    {
        x = y = r = 0;
        var info = new JoyInfoEx { Size = Marshal.SizeOf<JoyInfoEx>(), Flags = 0x1 | 0x4 | 0x100 };
        if (joyGetPosEx(0, ref info) != 0)
            return false;
        x = info.X / 32767.5 - 1.0;
        y = info.Y / 32767.5 - 1.0;
        r = info.R / 32767.5 - 1.0;
        return true;
    }

    [DllImport("winmm.dll")]
    private static extern int joyGetPosEx(int id, ref JoyInfoEx info);

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public int Size;
        public int Flags;
        public int X;
        public int Y;
        public int Z;
        public int R;
        public int U;
        public int V;
        public int Buttons;
        public int ButtonNumber;
        public int Pov;
        public int Reserved1;
        public int Reserved2;
    }
}

internal static class VJoy
{
    public const uint HidX = 0x30;
    public const uint HidY = 0x31;
    public const uint HidZ = 0x32;

    [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool vJoyEnabled();

    [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool AcquireVJD(uint id);

    [DllImport("vJoyInterface.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool SetAxis(int value, uint id, uint usage);

    public static bool Enabled() => vJoyEnabled();
    public static bool Acquire(uint id) => AcquireVJD(id);
}
