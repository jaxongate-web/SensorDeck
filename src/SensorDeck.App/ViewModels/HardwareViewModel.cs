using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SensorDeck.Core.Info;
using SensorDeck.Core.Models;

namespace SensorDeck.App.ViewModels;

public sealed class ComponentCardViewModel(ComponentInfo info)
{
    public string Category { get; } = info.Category.ToUpperInvariant();
    public string Name { get; } = info.Name;
    public IReadOnlyList<KeyValuePair<string, string>> Details { get; } = info.Details;
    public string IconKey { get; } = info.Category switch
    {
        "Processor" => "IconCpu",
        "Graphics" => "IconGpu",
        "Memory" => "IconMemory",
        "Storage" => "IconDrive",
        "Motherboard" => "IconBoard",
        "Operating system" => "IconWindows",
        _ => "IconInfo",
    };
}

public sealed partial class StatusItem(string label) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty] public partial string Value { get; set; } = "Checking…";
    [ObservableProperty] public partial bool IsGood { get; set; }
    [ObservableProperty] public partial string? Hint { get; set; }
}

public sealed partial class RawSensorRow(string id, string name, string type) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Type { get; } = type;
    [ObservableProperty] public partial string ValueText { get; set; } = "";
    [ObservableProperty] public partial string MinText { get; set; } = "";
    [ObservableProperty] public partial string MaxText { get; set; } = "";
    [ObservableProperty] public partial bool HasValue { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
}

public sealed partial class RawSensorGroup(string hardwareName, string hardwareType) : ObservableObject
{
    public string HardwareName { get; } = hardwareName;
    public string HardwareType { get; } = hardwareType;
    public string IconKey { get; } = hardwareType switch
    {
        "Cpu" => "IconCpu",
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => "IconGpu",
        "Memory" => "IconMemory",
        "Storage" => "IconDrive",
        "Motherboard" or "SuperIO" or "EmbeddedController" => "IconBoard",
        "Cooler" or "Controller" => "IconFan",
        _ => "IconInfo",
    };
    public ObservableCollection<RawSensorRow> Rows { get; } = new();
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    [ObservableProperty] public partial string CountText { get; set; } = "";
}

public sealed partial class HardwareViewModel : ObservableObject
{
    private readonly Dictionary<string, RawSensorRow> _rows = new();
    private SystemInfo? _info;

    public ObservableCollection<ComponentCardViewModel> Components { get; } = new();
    public ObservableCollection<RawSensorGroup> Groups { get; } = new();

    public StatusItem AdminStatus { get; } = new("Administrator rights");
    public StatusItem DriverStatus { get; } = new("PawnIO driver");
    public StatusItem SensorStatus { get; } = new("Sensors reporting");

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial bool HideEmpty { get; set; }
    [ObservableProperty] public partial string CopyButtonText { get; set; } = "Copy summary";
    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool HasSensors { get; set; }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnHideEmptyChanged(bool value) => ApplyFilter();

    public void Load(SystemInfo info)
    {
        _info = info;
        Components.Clear();
        foreach (var c in info.Components) Components.Add(new ComponentCardViewModel(c));
        IsLoading = false;
    }

    public void ApplyStatus(MonitorStatus status)
    {
        AdminStatus.IsGood = status.IsElevated;
        AdminStatus.Value = status.IsElevated ? "Granted" : "Not granted";
        AdminStatus.Hint = status.IsElevated ? null : "Needed for CPU sensors, storage temperatures and fan headers";

        DriverStatus.IsGood = status.IsDriverInstalled;
        DriverStatus.Value = status.IsDriverInstalled ? "Installed" : "Not installed";
        DriverStatus.Hint = status.IsDriverInstalled ? null : "Needed for CPU temperature, clock speed and motherboard fans";
    }

    /// <summary>Updates the raw sensor table in place (rows are reused between ticks).</summary>
    public void ApplyRaw(IReadOnlyList<RawSensor> sensors)
    {
        if (sensors.Count == 0) return;
        HasSensors = true;
        bool structureChanged = false;

        foreach (var s in sensors)
        {
            if (!_rows.TryGetValue(s.Id, out var row))
            {
                row = new RawSensorRow(s.Id, s.SensorName, s.SensorType);
                _rows[s.Id] = row;
                var group = Groups.FirstOrDefault(g => g.HardwareName == s.HardwareName && g.HardwareType == s.HardwareType);
                if (group is null) Groups.Add(group = new RawSensorGroup(s.HardwareName, s.HardwareType));
                group.Rows.Add(row);
                structureChanged = true;
            }

            row.HasValue = s.Value is not null;
            row.ValueText = s.Value is double v ? SensorUnits.Format(s.SensorType, v) : "No value";
            row.MinText = s.Min is double mn ? SensorUnits.Format(s.SensorType, mn) : "–";
            row.MaxText = s.Max is double mx ? SensorUnits.Format(s.SensorType, mx) : "–";
        }

        int total = sensors.Count, reporting = sensors.Count(s => s.Value is not null);
        SensorStatus.Value = $"{reporting} of {total}";
        SensorStatus.IsGood = reporting == total;
        SensorStatus.Hint = reporting == total ? null : $"{total - reporting} sensors exist but report no value on this system";

        foreach (var g in Groups)
        {
            int withValue = g.Rows.Count(r => r.HasValue);
            g.CountText = withValue == g.Rows.Count ? $"{g.Rows.Count} sensors" : $"{g.Rows.Count} sensors · {g.Rows.Count - withValue} without a value";
        }

        if (structureChanged || HideEmpty) ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = SearchText.Trim();
        foreach (var g in Groups)
        {
            bool groupMatch = q.Length > 0 && g.HardwareName.Contains(q, StringComparison.OrdinalIgnoreCase);
            int visible = 0;
            foreach (var r in g.Rows)
            {
                bool match = q.Length == 0 || groupMatch
                             || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                             || r.Type.Contains(q, StringComparison.OrdinalIgnoreCase);
                r.IsVisible = match && (!HideEmpty || r.HasValue);
                if (r.IsVisible) visible++;
            }
            g.IsVisible = visible > 0;
        }
    }

    [RelayCommand]
    private void CopySummary()
    {
        if (_info is null) return;
        var sb = new StringBuilder();
        sb.AppendLine("SensorDeck hardware summary");
        sb.AppendLine(new string('=', 28));
        foreach (var c in _info.Components)
        {
            sb.AppendLine();
            sb.AppendLine($"{c.Category}: {c.Name}");
            foreach (var d in c.Details) sb.AppendLine($"  {d.Key}: {d.Value}");
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            CopyButtonText = "Copied ✓";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopyButtonText = "Clipboard busy — try again";
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { CopyButtonText = "Copy summary"; timer.Stop(); };
        timer.Start();
    }

    [RelayCommand]
    private void ToggleAll()
    {
        bool expand = Groups.Any(g => !g.IsExpanded);
        foreach (var g in Groups) g.IsExpanded = expand;
    }
}
