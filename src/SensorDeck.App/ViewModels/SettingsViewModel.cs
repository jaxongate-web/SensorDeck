using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SensorDeck.App.Services;
using SensorDeck.Core.Models;
using SensorDeck.Core.Settings;

namespace SensorDeck.App.ViewModels;

/// <summary>One option in a segmented control (e.g. "°C" / "°F").</summary>
public sealed partial class ChoiceOption<T>(string label, T value, Action<T> onSelected) : ObservableObject
{
    public string Label { get; } = label;
    public T Value { get; } = value;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    partial void OnIsSelectedChanged(bool value) { if (value) onSelected(Value); }
}

/// <summary>Warm/hot stepper pair for one component's temperature warnings (stored in °C).</summary>
public sealed partial class ThresholdViewModel : ObservableObject
{
    private readonly Func<AppSettings, TempThreshold> _get;
    private readonly SettingsService _settings;

    public ThresholdViewModel(string label, string iconKey, Func<AppSettings, TempThreshold> get, SettingsService settings)
    {
        Label = label;
        IconKey = iconKey;
        _get = get;
        _settings = settings;
        Refresh();
    }

    public string Label { get; }
    public string IconKey { get; }
    [ObservableProperty] public partial string WarmText { get; set; } = "";
    [ObservableProperty] public partial string HotText { get; set; } = "";

    public void Refresh()
    {
        var t = _get(_settings.Current);
        WarmText = Format.TemperatureText(t.Warm);
        HotText = Format.TemperatureText(t.Hot);
    }

    private void Change(double warmDelta, double hotDelta)
    {
        _settings.Update(s =>
        {
            var t = _get(s);
            t.Warm += warmDelta;
            t.Hot += hotDelta;
            if (t.Warm >= t.Hot) { if (warmDelta > 0) t.Hot = t.Warm + 1; else t.Warm = t.Hot - 1; }
        });
        Refresh();
    }

    [RelayCommand] private void WarmUp() => Change(1, 0);
    [RelayCommand] private void WarmDown() => Change(-1, 0);
    [RelayCommand] private void HotUp() => Change(0, 1);
    [RelayCommand] private void HotDown() => Change(0, -1);
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly Func<MonitorStatus?> _status;
    private bool _syncing;

    public SettingsViewModel(SettingsService settings, Func<MonitorStatus?> status)
    {
        _settings = settings;
        _status = status;

        TemperatureUnits =
        [
            new("°C  Celsius", TemperatureUnit.Celsius, v => Set(s => s.TemperatureUnit = v)),
            new("°F  Fahrenheit", TemperatureUnit.Fahrenheit, v => Set(s => s.TemperatureUnit = v)),
        ];
        RefreshRates = AppSettings.RefreshOptions
            .Select(o => new ChoiceOption<double>(o < 1 ? $"{o:0.0} s" : $"{o:0} s", o, v => Set(s => s.RefreshSeconds = v))).ToList();
        GraphRanges = AppSettings.GraphRangeOptions
            .Select(o => new ChoiceOption<int>($"{o} min", o, v => Set(s => s.DefaultGraphMinutes = v))).ToList();

        Thresholds =
        [
            new("Processor", "IconCpu", s => s.CpuTemp, settings),
            new("Graphics", "IconGpu", s => s.GpuTemp, settings),
            new("Storage", "IconDrive", s => s.StorageTemp, settings),
        ];

        SyncFromSettings();
        settings.Changed += _ => SyncFromSettings();
    }

    public IReadOnlyList<ChoiceOption<TemperatureUnit>> TemperatureUnits { get; }
    public IReadOnlyList<ChoiceOption<double>> RefreshRates { get; }
    public IReadOnlyList<ChoiceOption<int>> GraphRanges { get; }
    public IReadOnlyList<ThresholdViewModel> Thresholds { get; }

    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }
    [ObservableProperty] public partial bool StartMinimized { get; set; }
    [ObservableProperty] public partial bool MinimizeToTray { get; set; }
    [ObservableProperty] public partial bool CloseToTray { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool CanChangeStartup { get; set; }
    [ObservableProperty] public partial string? StartupHint { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }

    public string SettingsPath => _settings.FilePath;
    public string AppVersion { get; } = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string LhmVersion { get; } =
        typeof(LibreHardwareMonitor.Hardware.Computer).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    partial void OnAlwaysOnTopChanged(bool value) => Set(s => s.AlwaysOnTop = value);
    partial void OnStartMinimizedChanged(bool value) => Set(s => s.StartMinimized = value);
    partial void OnMinimizeToTrayChanged(bool value) => Set(s => s.MinimizeToTray = value);
    partial void OnCloseToTrayChanged(bool value) => Set(s => s.CloseToTray = value);

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_syncing) return;
        string? error = value ? StartupTask.Enable(Environment.ProcessPath!) : StartupTask.Disable();
        if (error is not null)
        {
            StatusMessage = $"Couldn't change startup: {error}";
            _syncing = true;
            StartWithWindows = !value;
            _syncing = false;
        }
        else
        {
            StatusMessage = value ? "SensorDeck will start when you sign in to Windows." : "SensorDeck will no longer start with Windows.";
        }
    }

    private void Set(Action<AppSettings> change)
    {
        if (_syncing) return;
        _settings.Update(change);
        StatusMessage = _settings.LastSaveError is { } err ? $"Settings apply now but couldn't be saved: {err}" : null;
    }

    /// <summary>Re-reads state that can change outside the app (startup task, admin rights).</summary>
    public void Refresh()
    {
        var status = _status();
        bool elevated = status?.IsElevated ?? SensorDeck.Core.Sensors.SystemAccess.IsElevated();
        CanChangeStartup = elevated;
        StartupHint = elevated
            ? "Starts minimised when you sign in, using a Task Scheduler task (needed for apps that run as administrator)."
            : "Requires administrator rights — restart SensorDeck as administrator to change this.";

        _syncing = true;
        try { StartWithWindows = StartupTask.IsEnabled(); }
        catch { StartWithWindows = false; }
        finally { _syncing = false; }
    }

    private void SyncFromSettings()
    {
        _syncing = true;
        try
        {
            var s = _settings.Current;
            foreach (var o in TemperatureUnits) o.IsSelected = o.Value == s.TemperatureUnit;
            foreach (var o in RefreshRates) o.IsSelected = o.Value == s.RefreshSeconds;
            foreach (var o in GraphRanges) o.IsSelected = o.Value == s.DefaultGraphMinutes;
            AlwaysOnTop = s.AlwaysOnTop;
            StartMinimized = s.StartMinimized;
            MinimizeToTray = s.MinimizeToTray;
            CloseToTray = s.CloseToTray;
            foreach (var t in Thresholds) t.Refresh();
        }
        finally { _syncing = false; }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _settings.ResetToDefaults();
        StatusMessage = "Settings reset to defaults.";
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        string dir = Path.GetDirectoryName(_settings.FilePath)!;
        Directory.CreateDirectory(dir);
        _settings.SaveNow();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private static void OpenLink(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
