using System.Runtime.InteropServices;

namespace HeavyPro.App.Input;

/// <summary>
/// Asks Windows for 1 ms timer resolution while HeavyPro is connected to the sim. At the default
/// 15.6 ms the 8 ms stick timer only ticks every 15.6 ms; this makes it tick when it should.
/// Held once, given back on disconnect and on exit.
/// </summary>
internal static class TimerResolution
{
    private static bool _held;

    public static void Hold()
    {
        if (_held)
            return;
        _held = timeBeginPeriod(1) == 0;
    }

    public static void Release()
    {
        if (!_held)
            return;
        timeEndPeriod(1);
        _held = false;
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
}
