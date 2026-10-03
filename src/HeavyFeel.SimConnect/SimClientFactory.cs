using HeavyFeel.Core.Services;
using HeavyFeel.Logging;

namespace HeavyFeel.SimConnect;

public sealed class SimClientFactory : ISimClientFactory
{
    private readonly AppLogger _logger;
    private readonly IntPtr _windowHandle;

    public SimClientFactory(AppLogger logger, IntPtr windowHandle)
    {
        _logger = logger;
        _windowHandle = windowHandle;
    }

#if HAS_SIMCONNECT
    public string BackendName => "MSFS 2024 SimConnect";
    public ISimClient Create() => new OfficialSimClient(_logger, _windowHandle);
#else
    public string BackendName => "Offline (SimConnect DLL missing at build)";
    public ISimClient Create() => new OfflineSimClient();
#endif
}
