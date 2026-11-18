using System.Windows.Threading;
using SensorDeck.Core.Settings;

namespace SensorDeck.App.Services;

/// <summary>
/// Holds the live settings, applies changes immediately (via <see cref="Changed"/>) and saves them
/// to disk shortly after the last change so rapid clicks don't each write the file.
/// </summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;

    public SettingsService(SettingsStore store)
    {
        _store = store;
        Current = store.Load();
        Format.TemperatureUnit = Current.TemperatureUnit;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
    }

    public AppSettings Current { get; private set; }
    public string FilePath => _store.FilePath;
    public string? LastSaveError { get; private set; }

    public event Action<AppSettings>? Changed;

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Current.Normalize();
        Format.TemperatureUnit = Current.TemperatureUnit;
        Changed?.Invoke(Current);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ResetToDefaults()
    {
        Current = new AppSettings();
        Update(_ => { });
    }

    public void SaveNow()
    {
        try
        {
            _store.Save(Current);
            LastSaveError = null;
        }
        catch (Exception ex)
        {
            // Settings still apply for this session; just report that they couldn't be saved.
            LastSaveError = ex.Message;
        }
    }

    public void Flush()
    {
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            SaveNow();
        }
    }
}
