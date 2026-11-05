using HeavyPro.Core.Models;

namespace HeavyPro.Core.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
    string FilePath { get; }
}
