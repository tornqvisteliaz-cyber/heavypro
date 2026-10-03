using System.Text;

namespace HeavyFeel.Logging;

public sealed class AppLogger : IDisposable
{
    private readonly object _gate = new();
    private readonly string _logDirectory;
    private StreamWriter? _writer;
    private string _currentDate = "";

    public AppLogger(string? overrideDirectory = null)
    {
        _logDirectory = overrideDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HeavyFeel",
            "logs");
        Directory.CreateDirectory(_logDirectory);
        EnsureWriter();
    }

    public string LogDirectory => _logDirectory;

    public event EventHandler<string>? LineWritten;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex is null ? message : $"{message} | {ex}");
    }

    public void Telemetry(string message) => Write("TELEM", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
        }

        LineWritten?.Invoke(this, line);
    }

    private void EnsureWriter()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        if (_writer != null && _currentDate == date)
            return;

        _writer?.Dispose();
        _currentDate = date;
        var path = Path.Combine(_logDirectory, $"heavyfeel-{date}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
