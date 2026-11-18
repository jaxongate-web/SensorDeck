namespace SensorDeck.Core.Settings;

public enum TemperatureUnit { Celsius, Fahrenheit }

/// <summary>Warm/hot colour thresholds for a temperature, always stored in °C.</summary>
public sealed class TempThreshold
{
    public double Warm { get; set; }
    public double Hot { get; set; }

    public TempThreshold() { }
    public TempThreshold(double warm, double hot) { Warm = warm; Hot = hot; }

    internal void Normalize(double defaultWarm, double defaultHot)
    {
        if (!double.IsFinite(Warm)) Warm = defaultWarm;
        if (!double.IsFinite(Hot)) Hot = defaultHot;
        Warm = Math.Clamp(Math.Round(Warm), 20, 115);
        Hot = Math.Clamp(Math.Round(Hot), 25, 120);
        if (Hot <= Warm) Hot = Math.Min(120, Warm + 5);
    }
}

/// <summary>Everything the user can customise. Persisted as JSON by <see cref="SettingsStore"/>.</summary>
public sealed class AppSettings
{
    public static readonly double[] RefreshOptions = [0.5, 1, 2, 5];
    public static readonly int[] GraphRangeOptions = [1, 5, 10];

    public int SchemaVersion { get; set; } = 1;

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;
    public double RefreshSeconds { get; set; } = 1;
    public int DefaultGraphMinutes { get; set; } = 1;

    public TempThreshold CpuTemp { get; set; } = new(80, 90);
    public TempThreshold GpuTemp { get; set; } = new(75, 85);
    public TempThreshold StorageTemp { get; set; } = new(55, 70);

    public bool AlwaysOnTop { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool CloseToTray { get; set; }

    /// <summary>Tray icon is needed whenever either tray behaviour is on.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesTray => MinimizeToTray || CloseToTray;

    /// <summary>Repairs out-of-range or hand-edited values so a bad file can never break the app.</summary>
    public AppSettings Normalize()
    {
        if (!Enum.IsDefined(TemperatureUnit)) TemperatureUnit = TemperatureUnit.Celsius;
        RefreshSeconds = RefreshOptions.OrderBy(o => Math.Abs(o - (double.IsFinite(RefreshSeconds) ? RefreshSeconds : 1))).First();
        DefaultGraphMinutes = GraphRangeOptions.OrderBy(o => Math.Abs(o - DefaultGraphMinutes)).First();
        (CpuTemp ??= new(80, 90)).Normalize(80, 90);
        (GpuTemp ??= new(75, 85)).Normalize(75, 85);
        (StorageTemp ??= new(55, 70)).Normalize(55, 70);
        return this;
    }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        TemperatureUnit = TemperatureUnit,
        RefreshSeconds = RefreshSeconds,
        DefaultGraphMinutes = DefaultGraphMinutes,
        CpuTemp = new(CpuTemp.Warm, CpuTemp.Hot),
        GpuTemp = new(GpuTemp.Warm, GpuTemp.Hot),
        StorageTemp = new(StorageTemp.Warm, StorageTemp.Hot),
        AlwaysOnTop = AlwaysOnTop,
        StartMinimized = StartMinimized,
        MinimizeToTray = MinimizeToTray,
        CloseToTray = CloseToTray,
    };
}
