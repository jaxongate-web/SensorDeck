using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SensorDeck.App.Services;
using SensorDeck.Core.Models;

namespace SensorDeck.App.ViewModels;

/// <summary>CPU or GPU card: three headline metrics plus power, labelled with the detected device.</summary>
public sealed partial class ProcessorCardViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
    public MetricViewModel Temperature { get; } = new("Temperature");
    public MetricViewModel Usage { get; } = new("Usage");
    public MetricViewModel Clock { get; } = new("Clock speed");
    public MetricViewModel Power { get; } = new("Power");

    [ObservableProperty] public partial string DeviceName { get; set; } = "Detecting…";
    [ObservableProperty] public partial bool IsPresent { get; set; } = true;

    private double _peakClock;

    internal void UpdateClock(SensorValue clock)
    {
        if (clock.Value is double mhz) _peakClock = Math.Max(_peakClock, mhz);
        Clock.Update(clock, Format.Clock, v => _peakClock > 0 ? v / _peakClock : 0);
    }
}

public sealed partial class MemoryCardViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
    public MetricViewModel Load { get; } = new("In use");

    [ObservableProperty] public partial string DeviceName { get; set; } = "Detecting…";
    [ObservableProperty] public partial string UsedText { get; set; } = "–";
    [ObservableProperty] public partial string TotalText { get; set; } = "";
    [ObservableProperty] public partial bool IsSupported { get; set; } = true;
    [ObservableProperty] public partial string? Reason { get; set; }

    internal void Update(SensorValue used, SensorValue total, double divisor)
    {
        if (used.Value is double u && total.Value is double t && t > 0)
        {
            UsedText = Format.Gb(u / divisor);
            TotalText = $"/ {Format.Gb(t / divisor)} GB";
            Load.Update(SensorValue.Of(u / t * 100), Format.Percent, p => p / 100, Format.LoadSeverity);
            IsSupported = true;
            Reason = null;
        }
        else
        {
            IsSupported = false;
            Reason = used.Reason ?? total.Reason ?? "Not supported";
            UsedText = "Not supported";
            TotalText = "";
            Load.SetUnsupported(Reason);
        }
    }
}

public sealed partial class FanViewModel(string key) : ObservableObject
{
    public string Key { get; } = key;
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Source { get; set; } = "";
    public MetricViewModel Speed { get; } = new("Speed");
    [ObservableProperty] public partial string? DutyText { get; set; }
}

public sealed partial class DriveViewModel(string name, string subtitle) : ObservableObject
{
    public string Name { get; } = name;
    /// <summary>e.g. "NVMe SSD · 2.0 TB" from Windows, or just "Drive" if unknown.</summary>
    public string Subtitle { get; } = subtitle;
    public MetricViewModel Temperature { get; } = new("Temperature");
    public MetricViewModel UsedSpace { get; } = new("Used space");
}

public sealed partial class DashboardViewModel : ObservableObject
{
    public ProcessorCardViewModel Cpu { get; } = new("PROCESSOR");
    public ProcessorCardViewModel Gpu { get; } = new("GRAPHICS");
    public MemoryCardViewModel Memory { get; } = new("MEMORY");
    public MemoryCardViewModel Vram { get; } = new("VIDEO MEMORY");
    public ObservableCollection<FanViewModel> Fans { get; } = new();
    public ObservableCollection<DriveViewModel> Drives { get; } = new();

    /// <summary>Current user settings (warning thresholds). Replaced by MainViewModel when settings change.</summary>
    public SensorDeck.Core.Settings.AppSettings Settings { get; set; } = new();

    /// <summary>Static hardware details (set once detection finishes) used for friendlier labels.</summary>
    public SensorDeck.Core.Info.SystemInfo? SystemInfo { get; set; }

    [ObservableProperty] public partial string? FansNote { get; set; }
    [ObservableProperty] public partial string? DrivesNote { get; set; }

    public void Apply(HardwareSnapshot s)
    {
        // ----- CPU -----
        if (s.PrimaryCpu is { } cpu)
        {
            Cpu.IsPresent = true;
            Cpu.DeviceName = cpu.Name;
            Cpu.Temperature.Update(cpu.Temperature, Format.Temperature, c => c / 100, Format.TempSeverity(Settings.CpuTemp));
            Cpu.Usage.Update(cpu.Usage, Format.Percent, p => p / 100, Format.LoadSeverity);
            Cpu.UpdateClock(cpu.ClockMhz);
            Cpu.Power.Update(cpu.PowerWatts, Format.Watts, _ => 0);
        }
        else
        {
            Cpu.IsPresent = false;
            Cpu.DeviceName = "No CPU sensors detected";
        }

        // ----- GPU -----
        if (s.PrimaryGpu is { } gpu)
        {
            Gpu.IsPresent = true;
            Gpu.DeviceName = gpu.Name;
            Gpu.Temperature.Update(gpu.Temperature, Format.Temperature, c => c / 100, Format.TempSeverity(Settings.GpuTemp));
            Gpu.Usage.Update(gpu.Usage, Format.Percent, p => p / 100, Format.LoadSeverity);
            Gpu.UpdateClock(gpu.ClockMhz);
            Gpu.Power.Update(gpu.PowerWatts, Format.Watts, _ => 0);

            Vram.DeviceName = gpu.Name;
            Vram.Update(gpu.VramUsedMb, gpu.VramTotalMb, 1024);
        }
        else
        {
            Gpu.IsPresent = false;
            Gpu.DeviceName = "No GPU sensors detected";
            Vram.DeviceName = "No GPU detected";
            Vram.Update(SensorValue.Unsupported("No GPU detected"), SensorValue.Unsupported("No GPU detected"), 1);
        }

        // ----- RAM -----
        Memory.DeviceName = s.Memory.Description;
        Memory.Update(s.Memory.UsedGb, s.Memory.TotalGb, 1);

        // ----- Fans -----
        SyncFans(s.Fans);
        bool fullAccess = s.Status.IsElevated && s.Status.IsDriverInstalled;
        FansNote = fullAccess
            ? (s.Fans.Count == 0 ? "No fan sensors are exposed by this system." : null)
            : $"Motherboard fans: {s.Status.LowLevelReason}";

        // ----- Storage -----
        SyncDrives(s.Drives);
        DrivesNote = s.Drives.Count > 0 ? null
            : s.Status.IsElevated ? "No drives reported sensor data."
            : "Drive temperatures require administrator rights.";
    }

    private void SyncFans(IReadOnlyList<FanReading> fans)
    {
        var keys = fans.Select(f => $"{f.Source}|{f.Name}").ToList();
        for (int i = Fans.Count - 1; i >= 0; i--)
            if (!keys.Contains(Fans[i].Key)) Fans.RemoveAt(i);

        for (int i = 0; i < fans.Count; i++)
        {
            var f = fans[i];
            var vm = Fans.FirstOrDefault(x => x.Key == keys[i]);
            if (vm is null)
            {
                vm = new FanViewModel(keys[i]) { Name = f.Name, Source = f.Source };
                Fans.Insert(Math.Min(i, Fans.Count), vm);
            }
            vm.Speed.Update(f.Rpm, Format.Rpm, rpm => rpm / 3000);
            string? duty = f.ControlPercent.Value is double d ? $"{d:0} % duty" : null;
            // 0 RPM on a GPU is normally the card's zero-RPM idle mode, not a fault.
            if (f.Rpm.Value == 0) duty = duty is null ? "stopped" : $"{duty} · stopped";
            vm.DutyText = duty;
        }
    }

    private void SyncDrives(IReadOnlyList<StorageSnapshot> drives)
    {
        for (int i = Drives.Count - 1; i >= 0; i--)
            if (drives.All(d => d.Name != Drives[i].Name)) Drives.RemoveAt(i);

        foreach (var d in drives)
        {
            var vm = Drives.FirstOrDefault(x => x.Name == d.Name);
            if (vm is null) Drives.Add(vm = new DriveViewModel(d.Name, SystemInfo?.FindDrive(d.Name)?.Summary ?? "Drive"));
            vm.Temperature.Update(d.Temperature, Format.Temperature, c => c / 80, Format.TempSeverity(Settings.StorageTemp));
            vm.UsedSpace.Update(d.UsedSpacePercent, Format.Percent, p => p / 100);
        }
    }
}
