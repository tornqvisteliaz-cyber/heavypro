using HeavyFeel.Core.Models;

namespace HeavyFeel.Core.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    string FilePath { get; }
}
