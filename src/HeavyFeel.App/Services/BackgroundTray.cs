using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace HeavyFeel.App.Services;

/// <summary>
/// HeavyPro in the background: the tray icon (Open / Exit), the signal a second launch
/// sends to bring the window back, and the "Start with Windows" entry.
/// </summary>
public sealed class BackgroundTray : IDisposable
{
    public const string BackgroundArg = "--background";
    private const string ShowSignalName = @"Local\HeavyPro.ShowWindow";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "HeavyPro";

    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly EventWaitHandle _showSignal;
    private readonly Thread _listener;
    private volatile bool _disposed;
    private bool _toldOnce;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public BackgroundTray()
    {
        _icon = new System.Windows.Forms.NotifyIcon { Text = "HeavyPro", Visible = true };
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/HeavyPro;component/Assets/HeavyPro.ico"))?.Stream;
        if (stream != null)
            _icon.Icon = new System.Drawing.Icon(stream);
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open HeavyPro", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Exit HeavyPro", null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();

        // A second launch sets this instead of starting another copy.
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _listener = new Thread(() =>
        {
            try
            {
                while (!_disposed)
                {
                    if (_showSignal.WaitOne(500) && !_disposed)
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() => OpenRequested?.Invoke()));
                }
            }
            catch (ObjectDisposedException)
            {
                // Exiting.
            }
        }) { IsBackground = true, Name = "HeavyPro show signal" };
        _listener.Start();
    }

    /// <summary>Tells the copy already running to show its window. False when none is listening.</summary>
    public static bool SignalRunningCopy()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
            return signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    /// <summary>Once per session, so closing the window never looks like quitting.</summary>
    public void TellStillRunning()
    {
        if (_toldOnce)
            return;
        _toldOnce = true;
        _icon.ShowBalloonTip(4000, "HeavyPro is still running",
            "It keeps shaping your stick in the background. Right-click the tray icon to open or exit it.",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, "\"" + Path.GetFullPath(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName) + "\" " + BackgroundArg);
            else if (key.GetValue(RunValue) != null)
                key.DeleteValue(RunValue);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _showSignal.Dispose();
    }
}
