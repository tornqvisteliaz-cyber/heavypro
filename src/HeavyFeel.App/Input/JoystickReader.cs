using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HeavyFeel.App.Input;

/// <summary>A game controller Windows knows about. Key is VID:PID, stable across replugging.</summary>
public sealed class JoystickDevice
{
    public JoystickDevice(int id, string key, string name, int axisCount, int axisMask = 0b11)
    {
        Id = id;
        Key = key;
        Name = name;
        AxisCount = axisCount;
        AxisMask = axisMask;
    }

    /// <summary>Bit per axis in X, Y, Z, R, U, V order: which axes the device really has.</summary>
    public int AxisMask { get; }

    public bool HasAxis(string? axisName)
    {
        var i = JoystickReader.AxisIndex(axisName);
        return i >= 0 && (AxisMask & (1 << i)) != 0;
    }

    public int Id { get; }
    public string Key { get; }
    public string Name { get; }
    public int AxisCount { get; }

    public override string ToString() => Name;
}

/// <summary>
/// Reads sticks and pedals through WinMM (no driver needed). Axes come back as
/// -1..1 in the order X, Y, Z, R, U, V; Y is +1 with the stick pulled back.
/// </summary>
public static class JoystickReader
{
    public static readonly string[] AxisNames = { "X", "Y", "Z", "R", "U", "V" };

    public static IReadOnlyList<JoystickDevice> List()
    {
        var list = new List<JoystickDevice>();
        for (var id = 0; id < 16; id++)
        {
            var caps = new JoyCaps();
            if (joyGetDevCaps(id, ref caps, Marshal.SizeOf<JoyCaps>()) != 0)
                continue;
            var info = new JoyInfoEx { Size = Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnAll };
            if (joyGetPosEx(id, ref info) != 0 || caps.NumAxes == 0)
                continue;
            var key = $"{caps.Mid:X4}:{caps.Pid:X4}";
            list.Add(new JoystickDevice(id, key, FriendlyName(caps.Mid, caps.Pid) ?? $"Controller {key}", caps.NumAxes, AxisMask(caps.Caps)));
        }
        return list;
    }

    public static bool TryRead(int id, out double[] axes)
    {
        axes = new double[6];
        var caps = new JoyCaps();
        var info = new JoyInfoEx { Size = Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnAll };
        if (joyGetDevCaps(id, ref caps, Marshal.SizeOf<JoyCaps>()) != 0 || joyGetPosEx(id, ref info) != 0)
            return false;
        axes[0] = Norm(info.X, caps.Xmin, caps.Xmax);
        axes[1] = Norm(info.Y, caps.Ymin, caps.Ymax);
        axes[2] = Norm(info.Z, caps.Zmin, caps.Zmax);
        axes[3] = Norm(info.R, caps.Rmin, caps.Rmax);
        axes[4] = Norm(info.U, caps.Umin, caps.Umax);
        axes[5] = Norm(info.V, caps.Vmin, caps.Vmax);
        return true;
    }

    public static int AxisIndex(string? name) => Array.IndexOf(AxisNames, (name ?? "").ToUpperInvariant());

    // X and Y always exist; JOYCAPS_HASZ/HASR/HASU/HASV (1, 2, 4, 8) mark the rest.
    // A missing axis reads as fully deflected, so it must never be used.
    private static int AxisMask(int caps) =>
        0b11 | ((caps & 1) != 0 ? 1 << 2 : 0) | ((caps & 2) != 0 ? 1 << 3 : 0) | ((caps & 4) != 0 ? 1 << 4 : 0) | ((caps & 8) != 0 ? 1 << 5 : 0);

    private static double Norm(int value, int min, int max)
    {
        if (max <= min)
            return 0;
        var n = (value - min) * 2.0 / (max - min) - 1.0;
        return n < -1 ? -1 : n > 1 ? 1 : n;
    }

    // Windows keeps the product name the device reports under the joystick OEM key.
    private static string? FriendlyName(int vid, int pid)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{vid:X4}&PID_{pid:X4}");
            var name = key?.GetValue("OEMName") as string;
            return string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private const int JoyReturnAll = 0xFF;

    [DllImport("winmm.dll")]
    private static extern int joyGetPosEx(int id, ref JoyInfoEx info);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int joyGetDevCaps(int id, ref JoyCaps caps, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public int Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Pname;
        public int Xmin, Xmax, Ymin, Ymax, Zmin, Zmax, NumButtons, PeriodMin, PeriodMax, Rmin, Rmax, Umin, Umax, Vmin, Vmax, Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxd;
    }
}
