using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SensorDeck.App.Controls;
using SensorDeck.App.Services;
using SensorDeck.Core.History;
using SensorDeck.Core.Models;

namespace SensorDeck.App.ViewModels;

/// <summary>A legend row for multi-series graphs (e.g. one line per drive).</summary>
public sealed partial class LegendEntry(GraphSeries series) : ObservableObject
{
    public GraphSeries Series { get; } = series;
    public Brush Brush => Series.Stroke;
    [ObservableProperty] public partial string Name { get; set; } = series.Name;
    [ObservableProperty] public partial string ValueText { get; set; } = "–";
}

/// <summary>One graph card: history buffer(s), current value, and min/avg/max over the visible window.</summary>
public sealed partial class GraphCardViewModel : ObservableObject
{
    private readonly Func<double, (string Text, string Unit)> _format;
    private readonly Color _color;

    public GraphCardViewModel(string title, string iconKey, Color color, AxisMode axis, double minSpan,
        Func<double, (string, string)> format, Func<double, double>? transform = null, Func<double, string>? axisLabel = null)
    {
        AxisLabelFormat = axisLabel;
        Title = title;
        IconKey = iconKey;
        _color = color;
        AxisMode = axis;
        MinSpan = minSpan;
        _format = format;
        Transform = transform;
        // Axis/hover labels receive display-unit values: transformed cards (temperatures) are labelled
        // directly in the current unit; the rest go through the normal formatter.
        LabelFormat = transform is not null
            ? v => $"{v:0} {Format.TemperatureSymbol}"
            : v => { var (t, u) = format(v); return u == "%" ? $"{t}%" : $"{t} {u}"; };
    }

    public string Title { get; }
    public string IconKey { get; }
    public AxisMode AxisMode { get; }
    public double MinSpan { get; }
    public Func<double, double>? Transform { get; }
    public Func<double, string> LabelFormat { get; }
    public Func<double, string>? AxisLabelFormat { get; }
    public System.Collections.ObjectModel.ObservableCollection<LegendEntry> Legend { get; } = new();

    [ObservableProperty] public partial IReadOnlyList<GraphSeries> Series { get; set; } = [];
    [ObservableProperty] public partial string DeviceName { get; set; } = "Detecting…";
    [ObservableProperty] public partial string CurrentText { get; set; } = "–";
    [ObservableProperty] public partial string CurrentUnit { get; set; } = "";
    [ObservableProperty] public partial string? StatsText { get; set; }
    [ObservableProperty] public partial bool IsSupported { get; set; } = true;
    [ObservableProperty] public partial string? Reason { get; set; }
    [ObservableProperty] public partial long Revision { get; set; }
    [ObservableProperty] public partial bool IsMultiSeries { get; set; }

    /// <summary>Single-series card: append a sample (null = unsupported/missing, drawn as a gap).</summary>
    public void Push(SensorValue value, int capacity, int visibleSamples)
    {
        if (Series.Count == 0) Series = [new GraphSeries(Title, _color, capacity)];
        var s = Series[0];
        s.Buffer.Add(value.Value);

        if (value.Value is double v)
        {
            (CurrentText, CurrentUnit) = _format(v);
            IsSupported = true;
            Reason = null;
        }
        else
        {
            CurrentText = "Not supported";
            CurrentUnit = "";
            IsSupported = HasData(s, visibleSamples); // keep showing history if this is just a gap
            Reason = value.Reason;
        }

        UpdateStats(visibleSamples);
        Revision++;
    }

    /// <summary>Multi-series card (one line per device). Keys identify series across ticks.</summary>
    public void PushMany(IReadOnlyList<(string Name, SensorValue Value)> values, Color[] palette, int capacity, int visibleSamples)
    {
        IsMultiSeries = true;
        var list = Series.ToList();
        bool changed = false;
        foreach (var (name, _) in values)
        {
            if (list.Any(s => s.Name == name)) continue;
            var s = new GraphSeries(name, palette[list.Count % palette.Length], capacity);
            // Back-fill so a new line lines up with existing history.
            int existing = list.Count > 0 ? list[0].Buffer.Count : 0;
            for (int i = 0; i < existing; i++) s.Buffer.Add(null);
            list.Add(s);
            Legend.Add(new LegendEntry(s));
            changed = true;
        }
        if (changed) Series = list;

        foreach (var s in Series)
        {
            var match = values.FirstOrDefault(v => v.Name == s.Name);
            var val = match.Name is null ? null : match.Value.Value;
            s.Buffer.Add(val);
            var entry = Legend.First(l => l.Series == s);
            entry.ValueText = val is double d ? Fmt(d) : "Not supported";
        }

        IsSupported = Series.Any(s => HasData(s, visibleSamples));
        Reason = IsSupported ? null : values.Select(v => v.Value.Reason).FirstOrDefault(r => r is not null) ?? Reason;
        Revision++;
    }

    public void SetUnsupported(string reason)
    {
        IsSupported = false;
        Reason = reason;
        CurrentText = "Not supported";
        CurrentUnit = "";
        StatsText = null;
    }

    public void UpdateStats(int visibleSamples)
    {
        if (Series.Count != 1) return;
        var b = Series[0].Buffer;
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        int count = 0;
        for (int i = Math.Max(0, b.Count - visibleSamples); i < b.Count; i++)
        {
            double v = b[i];
            if (double.IsNaN(v)) continue;
            min = Math.Min(min, v); max = Math.Max(max, v); sum += v; count++;
        }
        StatsText = count == 0 ? null
            : $"Min {Fmt(min)}  ·  Avg {Fmt(sum / count)}  ·  Max {Fmt(max)}";
    }

    public void ResizeHistory(int capacity)
    {
        foreach (var s in Series) s.Buffer.Resize(capacity);
        Revision++;
    }

    private string Fmt(double v) { var (t, u) = _format(v); return u == "%" ? $"{t}%" : $"{t} {u}"; }

    private static bool HasData(GraphSeries s, int visible)
    {
        var b = s.Buffer;
        for (int i = Math.Max(0, b.Count - visible); i < b.Count; i++)
            if (!double.IsNaN(b[i])) return true;
        return false;
    }
}

public sealed partial class TimeRangeOption(string label, int seconds, Action<TimeRangeOption> onSelected) : ObservableObject
{
    public string Label { get; } = label;
    public int Seconds { get; } = seconds;

    [ObservableProperty] public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) onSelected(this);
    }
}

public sealed partial class GraphsViewModel : ObservableObject
{
    /// <summary>Longest selectable window; history buffers hold this much.</summary>
    public const int MaxWindowSeconds = 600;

    private static readonly Color Blue = Color.FromRgb(0x60, 0xA5, 0xFA);
    private static readonly Color Violet = Color.FromRgb(0xA7, 0x8B, 0xFA);
    private static readonly Color Teal = Color.FromRgb(0x2D, 0xD4, 0xBF);
    private static readonly Color Indigo = Color.FromRgb(0x81, 0x8C, 0xF8);
    private static readonly Color[] DrivePalette =
    [
        Color.FromRgb(0x60, 0xA5, 0xFA), Color.FromRgb(0x22, 0xD3, 0xEE), Color.FromRgb(0xA7, 0x8B, 0xFA),
        Color.FromRgb(0x2D, 0xD4, 0xBF), Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0xFB, 0xBF, 0x24),
    ];

    private double _intervalSeconds = 1;

    public GraphsViewModel()
    {
        Func<double, double> temp = Format.ToDisplayTemperature;

        CpuUsage = new("CPU usage", "IconCpu", Blue, AxisMode.Percent, 10, Format.Percent);
        GpuUsage = new("GPU usage", "IconGpu", Violet, AxisMode.Percent, 10, Format.Percent);
        CpuTemp = new("CPU temperature", "IconCpu", Blue, AxisMode.Auto, 15, Format.Temperature, temp);
        GpuTemp = new("GPU temperature", "IconGpu", Violet, AxisMode.Auto, 15, Format.Temperature, temp);
        Ram = new("Memory usage", "IconMemory", Teal, AxisMode.Percent, 10, Format.Percent);
        Vram = new("Video memory usage", "IconVram", Indigo, AxisMode.Percent, 10, Format.Percent);
        Func<double, string> clockAxis = v => v == 0 ? "0" : v >= 1000 ? $"{v / 1000:0.#} GHz" : $"{v:0} MHz";
        CpuClock = new("CPU clock speed", "IconCpu", Blue, AxisMode.AutoFromZero, 1000, Format.Clock, axisLabel: clockAxis);
        GpuClock = new("GPU clock speed", "IconGpu", Violet, AxisMode.AutoFromZero, 1000, Format.Clock, axisLabel: clockAxis);
        Storage = new("Storage temperatures", "IconDrive", Blue, AxisMode.Auto, 15, Format.Temperature, temp);

        TimeRanges =
        [
            new("1 min", 60, OnRangeSelected),
            new("5 min", 300, OnRangeSelected),
            new("10 min", 600, OnRangeSelected),
        ];
        TimeRanges[0].IsSelected = true;
    }

    public GraphCardViewModel CpuUsage { get; }
    public GraphCardViewModel GpuUsage { get; }
    public GraphCardViewModel CpuTemp { get; }
    public GraphCardViewModel GpuTemp { get; }
    public GraphCardViewModel Ram { get; }
    public GraphCardViewModel Vram { get; }
    public GraphCardViewModel CpuClock { get; }
    public GraphCardViewModel GpuClock { get; }
    public GraphCardViewModel Storage { get; }

    private IEnumerable<GraphCardViewModel> AllCards => [CpuUsage, GpuUsage, CpuTemp, GpuTemp, Ram, Vram, CpuClock, GpuClock, Storage];

    public IReadOnlyList<TimeRangeOption> TimeRanges { get; }

    [ObservableProperty] public partial int VisibleSamples { get; set; } = 61;
    [ObservableProperty] public partial double SampleSeconds { get; set; } = 1;
    [ObservableProperty] public partial string RangeDescription { get; set; } = "Last 1 minute";

    private int Capacity => (int)Math.Ceiling(MaxWindowSeconds / _intervalSeconds) + 1;

    private void OnRangeSelected(TimeRangeOption option)
    {
        foreach (var o in TimeRanges) if (o != option) o.IsSelected = false;
        VisibleSamples = (int)Math.Round(option.Seconds / _intervalSeconds) + 1;
        RangeDescription = option.Seconds >= 60 ? $"Last {option.Seconds / 60} minute{(option.Seconds > 60 ? "s" : "")}" : $"Last {option.Seconds} s";
        foreach (var c in AllCards) { c.UpdateStats(VisibleSamples); c.Revision++; }
    }

    public void SelectRange(int minutes)
    {
        var option = TimeRanges.FirstOrDefault(o => o.Seconds == minutes * 60);
        if (option is not null) option.IsSelected = true;
    }

    /// <summary>Redraws graphs and stats after a display setting (e.g. °C/°F) changes.</summary>
    public void RefreshDisplay()
    {
        foreach (var c in AllCards) { c.UpdateStats(VisibleSamples); c.Revision++; }
    }

    /// <summary>Called on every polling tick, whether or not the Graphs page is visible.</summary>
    public void Apply(HardwareSnapshot s, TimeSpan interval)
    {
        if (Math.Abs(interval.TotalSeconds - _intervalSeconds) > 0.001)
        {
            _intervalSeconds = interval.TotalSeconds;
            SampleSeconds = _intervalSeconds;
            foreach (var c in AllCards) c.ResizeHistory(Capacity);
            var sel = TimeRanges.First(o => o.IsSelected);
            VisibleSamples = (int)Math.Round(sel.Seconds / _intervalSeconds) + 1;
        }

        int cap = Capacity, vis = VisibleSamples;

        if (s.PrimaryCpu is { } cpu)
        {
            foreach (var c in new[] { CpuUsage, CpuTemp, CpuClock }) c.DeviceName = cpu.Name;
            CpuUsage.Push(cpu.Usage, cap, vis);
            CpuTemp.Push(cpu.Temperature, cap, vis);
            CpuClock.Push(cpu.ClockMhz, cap, vis);
        }
        else
        {
            foreach (var c in new[] { CpuUsage, CpuTemp, CpuClock }) { c.DeviceName = "No CPU detected"; c.SetUnsupported("No CPU sensors detected"); }
        }

        if (s.PrimaryGpu is { } gpu)
        {
            foreach (var c in new[] { GpuUsage, GpuTemp, GpuClock, Vram }) c.DeviceName = gpu.Name;
            GpuUsage.Push(gpu.Usage, cap, vis);
            GpuTemp.Push(gpu.Temperature, cap, vis);
            GpuClock.Push(gpu.ClockMhz, cap, vis);
            Vram.Push(Percent(gpu.VramUsedMb, gpu.VramTotalMb), cap, vis);
        }
        else
        {
            foreach (var c in new[] { GpuUsage, GpuTemp, GpuClock, Vram }) { c.DeviceName = "No GPU detected"; c.SetUnsupported("No GPU sensors detected"); }
        }

        Ram.DeviceName = s.Memory.Description;
        Ram.Push(s.Memory.LoadPercent, cap, vis);

        Storage.DeviceName = s.Drives.Count switch
        {
            0 => "No drives reporting",
            1 => s.Drives[0].Name,
            var n => $"{n} drives",
        };
        if (s.Drives.Count == 0)
            Storage.SetUnsupported(s.Status.IsElevated ? "No drives report a temperature" : "Requires administrator rights");
        else
            Storage.PushMany(s.Drives.Select(d => (d.Name, d.Temperature)).ToList(), DrivePalette, cap, vis);
    }

    private static SensorValue Percent(SensorValue used, SensorValue total) =>
        used.Value is double u && total.Value is double t && t > 0
            ? SensorValue.Of(u / t * 100)
            : SensorValue.Unsupported(used.Reason ?? total.Reason ?? "Not supported");
}
