using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace SensorDeck.Core.Info;

public sealed record MemoryModuleInfo(
    string Slot,
    string Manufacturer,
    string PartNumber,
    int SizeMb,
    string Type,
    int SpeedMts);

public sealed record ComponentInfo(string Category, string Name, IReadOnlyList<KeyValuePair<string, string>> Details);

/// <summary>Static, detected-once description of the machine (names, models, capacities).</summary>
public sealed record SystemInfo(
    string CpuName,
    string MemorySummary,
    string Motherboard,
    string Os,
    IReadOnlyList<MemoryModuleInfo> MemoryModules,
    IReadOnlyList<ComponentInfo> Components,
    IReadOnlyList<DriveDetails> Drives)
{
    /// <summary>Windows' description of a drive LHM reports by model name, if available.</summary>
    public DriveDetails? FindDrive(string name) =>
        Drives.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Drives.FirstOrDefault(d => d.Name.Length > 0 && (name.Contains(d.Name, StringComparison.OrdinalIgnoreCase)
                                                            || d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)));
}

public static class SystemInfoReader
{
    public static SystemInfo Read(Computer computer, Models.MonitorStatus? status = null)
    {
        var sm = computer.SMBios;
        var hardware = computer.Hardware;
        var drives = WindowsInfo.ReadDrives();
        var gpuDrivers = WindowsInfo.ReadGpuDrivers();

        var cpuHw = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var smCpu = sm?.Processors?.FirstOrDefault();
        string cpuName = cpuHw?.Name ?? smCpu?.Version?.Trim() ?? "Unknown CPU";

        var modules = (sm?.MemoryDevices ?? [])
            .Where(m => m.Size > 0)
            .Select(m => new MemoryModuleInfo(
                Clean(m.DeviceLocator),
                Clean(m.ManufacturerName),
                Clean(m.PartNumber),
                (int)m.Size,
                FormatMemoryType(m.Type),
                m.ConfiguredSpeed > 0 ? m.ConfiguredSpeed : m.Speed))
            .ToList();

        string memSummary = DescribeMemory(modules, hardware);

        // LHM's motherboard name uses short vendor names ("MSI …" rather than "Micro-Star International Co., Ltd. …").
        string board = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard)?.Name is { Length: > 0 } lhmBoard
            ? lhmBoard
            : sm?.Board is { } b && !string.IsNullOrWhiteSpace(b.ProductName)
                ? $"{Clean(b.ManufacturerName)} {Clean(b.ProductName)}".Trim()
                : "Unknown motherboard";

        var components = new List<ComponentInfo>();

        // CPU
        var cpuDetails = new List<KeyValuePair<string, string>>();
        if (smCpu is not null)
        {
            if (smCpu.CoreCount > 0) Add(cpuDetails, "Cores", smCpu.CoreCount.ToString());
            if (smCpu.ThreadCount > 0) Add(cpuDetails, "Threads", smCpu.ThreadCount.ToString());
            if (smCpu.MaxSpeed > 0) Add(cpuDetails, "Max speed (SMBIOS)", $"{smCpu.MaxSpeed} MHz");
            if (!string.IsNullOrWhiteSpace(smCpu.SocketDesignation)) Add(cpuDetails, "Socket", smCpu.SocketDesignation.Trim());
        }
        Add(cpuDetails, "Logical processors", System.Environment.ProcessorCount.ToString());
        components.Add(new ComponentInfo("Processor", cpuName, cpuDetails));

        // GPUs
        foreach (var gpu in hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
        {
            var d = new List<KeyValuePair<string, string>>();
            Add(d, "Vendor", gpu.HardwareType switch
            {
                HardwareType.GpuNvidia => "NVIDIA",
                HardwareType.GpuAmd => "AMD",
                _ => "Intel",
            });
            Add(d, "Type", Sensors.SensorSelector.ClassifyGpu(gpu).ToString());
            var total = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name is "GPU Memory Total" or "D3D Dedicated Memory Total");
            if (total?.Value is float mb && mb > 0) Add(d, "Dedicated memory", $"{mb / 1024f:0.#} GB");
            foreach (var p in gpu.Properties) Add(d, p.Key, p.Value);
            var drv = gpuDrivers.FirstOrDefault(g => string.Equals(g.Name, gpu.Name, StringComparison.OrdinalIgnoreCase));
            if (drv is not null && drv.Version.Length > 0)
            {
                string friendly = WindowsInfo.FriendlyDriverVersion(gpu.Name, drv.Version);
                Add(d, "Driver", friendly == drv.Version ? drv.Version : $"{friendly} ({drv.Version})");
                if (drv.Date is DateTime dd) Add(d, "Driver date", dd.ToString("yyyy-MM-dd"));
            }
            components.Add(new ComponentInfo("Graphics", gpu.Name, d));
        }

        // Memory
        var memDetails = new List<KeyValuePair<string, string>>();
        foreach (var m in modules)
            Add(memDetails, m.Slot, $"{m.SizeMb / 1024} GB {m.Type}-{m.SpeedMts} · {m.Manufacturer} {m.PartNumber}".Trim());
        components.Add(new ComponentInfo("Memory", memSummary, memDetails));

        // Motherboard + BIOS
        var boardDetails = new List<KeyValuePair<string, string>>();
        if (sm?.Bios is { } bios)
        {
            if (!string.IsNullOrWhiteSpace(bios.Vendor)) Add(boardDetails, "BIOS vendor", bios.Vendor.Trim());
            if (!string.IsNullOrWhiteSpace(bios.Version)) Add(boardDetails, "BIOS version", bios.Version.Trim());
            if (bios.Date is DateTime dt) Add(boardDetails, "BIOS date", dt.ToString("yyyy-MM-dd"));
        }
        var superIo = hardware.Where(h => h.HardwareType == HardwareType.Motherboard).SelectMany(h => h.SubHardware)
            .FirstOrDefault(h => h.HardwareType == HardwareType.SuperIO);
        Add(boardDetails, "Sensor chip", superIo?.Name
            ?? (status is { IsElevated: true, IsDriverInstalled: true } ? "Not detected" : $"Not available — {status?.LowLevelReason ?? "unknown"}"));
        components.Add(new ComponentInfo("Motherboard", board, boardDetails));

        // Storage: Windows lists every drive; LHM adds sensor access (admin only).
        var lhmDrives = hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in lhmDrives)
        {
            var d = new List<KeyValuePair<string, string>>();
            var win = drives.FirstOrDefault(x => string.Equals(x.Name, drive.Name, StringComparison.OrdinalIgnoreCase));
            if (win is not null) { AddDrive(d, win); listed.Add(win.Name); }
            foreach (var p in drive.Properties) Add(d, p.Key, p.Value);
            Add(d, "Sensors", "Available");
            components.Add(new ComponentInfo("Storage", drive.Name, d));
        }
        foreach (var win in drives.Where(x => !listed.Contains(x.Name)))
        {
            var d = new List<KeyValuePair<string, string>>();
            AddDrive(d, win);
            Add(d, "Sensors", status?.IsElevated == false ? "Requires administrator rights" : "Not reported by this drive");
            components.Add(new ComponentInfo("Storage", win.Name, d));
        }

        // Operating system
        string os = WindowsInfo.ReadOsName();
        components.Add(new ComponentInfo("Operating system", os,
        [
            new("Architecture", RuntimeInformation.OSArchitecture.ToString()),
            new(".NET runtime", RuntimeInformation.FrameworkDescription),
        ]));

        return new SystemInfo(cpuName, memSummary, board, os, modules, components, drives);

        static void AddDrive(List<KeyValuePair<string, string>> d, DriveDetails win)
        {
            if (win.SizeBytes > 0) Add(d, "Capacity", ByteFormat.Bytes(win.SizeBytes));
            Add(d, "Interface", win.Interface);
            if (win.MediaType != "Unspecified") Add(d, "Type", win.MediaType);
        }
    }

    /// <summary>"32 GB DDR5-6000 (2 × 16 GB)", falling back to OS-reported capacity if SMBIOS is empty.</summary>
    public static string DescribeMemory(IReadOnlyList<MemoryModuleInfo> modules, IEnumerable<IHardware> hardware)
    {
        if (modules.Count > 0)
        {
            int totalGb = modules.Sum(m => m.SizeMb) / 1024;
            var first = modules[0];
            bool uniform = modules.All(m => m.SizeMb == first.SizeMb && m.Type == first.Type);
            string type = first.Type is "Unknown" or "Other" ? "" : $" {first.Type}";
            string speed = first.SpeedMts > 0 && type.Length > 0 ? $"-{first.SpeedMts}" : "";
            string layout = uniform && modules.Count > 1 ? $" ({modules.Count} × {first.SizeMb / 1024} GB)" : "";
            return $"{totalGb} GB{type}{speed}{layout}";
        }

        var mem = hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual"));
        var used = mem?.Sensors.FirstOrDefault(s => s.Name == "Memory Used")?.Value;
        var avail = mem?.Sensors.FirstOrDefault(s => s.Name == "Memory Available")?.Value;
        return used is float u && avail is float a ? $"{Math.Round(u + a)} GB" : "System memory";
    }

    public static string FormatMemoryType(MemoryType type) => type.ToString().TrimStart('_').Replace('_', '-');

    private static string Clean(string? s) => (s ?? "").Trim();

    private static void Add(List<KeyValuePair<string, string>> list, string key, string value) =>
        list.Add(new KeyValuePair<string, string>(key, value));
}
