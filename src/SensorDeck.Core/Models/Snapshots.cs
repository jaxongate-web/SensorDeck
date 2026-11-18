namespace SensorDeck.Core.Models;

/// <summary>Live CPU readings. Clock is the average of the per-core clocks, in MHz.</summary>
public sealed record CpuSnapshot(
    string Name,
    SensorValue Temperature,
    SensorValue Usage,
    SensorValue ClockMhz,
    SensorValue MaxCoreClockMhz,
    SensorValue PowerWatts);

public enum GpuKind { Discrete, Integrated }

/// <summary>Live GPU readings. VRAM figures are in MB.</summary>
public sealed record GpuSnapshot(
    string Name,
    GpuKind Kind,
    SensorValue Temperature,
    SensorValue Usage,
    SensorValue ClockMhz,
    SensorValue MemoryClockMhz,
    SensorValue VramUsedMb,
    SensorValue VramTotalMb,
    SensorValue PowerWatts,
    IReadOnlyList<FanReading> Fans);

/// <summary>System RAM usage in GB plus a description such as "32 GB DDR5-6000".</summary>
public sealed record MemorySnapshot(
    string Description,
    SensorValue UsedGb,
    SensorValue TotalGb,
    SensorValue LoadPercent,
    IReadOnlyList<SensorValue> ModuleTemperatures);

public sealed record FanReading(
    string Name,
    string Source,
    SensorValue Rpm,
    SensorValue ControlPercent);

public sealed record StorageSnapshot(
    string Name,
    SensorValue Temperature,
    SensorValue UsedSpacePercent);

/// <summary>A raw sensor exactly as LibreHardwareMonitor reports it (for the Hardware page).</summary>
public sealed record RawSensor(
    string Id,
    string HardwareName,
    string HardwareType,
    string SensorName,
    string SensorType,
    double? Value,
    double? Min,
    double? Max);

/// <summary>Why some readings may be missing on this machine.</summary>
public sealed record MonitorStatus(bool IsElevated, bool IsDriverInstalled)
{
    /// <summary>Text explaining a missing low-level reading (CPU temps, motherboard fans).</summary>
    public string LowLevelReason =>
        !IsElevated ? "Requires administrator rights"
        : !IsDriverInstalled ? "Requires the PawnIO driver"
        : "Not exposed by this hardware";
}

/// <summary>Everything read during one polling tick.</summary>
public sealed record HardwareSnapshot(
    DateTime Timestamp,
    MonitorStatus Status,
    IReadOnlyList<CpuSnapshot> Cpus,
    IReadOnlyList<GpuSnapshot> Gpus,
    MemorySnapshot Memory,
    IReadOnlyList<FanReading> Fans,
    IReadOnlyList<StorageSnapshot> Drives,
    IReadOnlyList<RawSensor> RawSensors)
{
    public CpuSnapshot? PrimaryCpu => Cpus.Count > 0 ? Cpus[0] : null;

    /// <summary>The first discrete GPU, falling back to an integrated one.</summary>
    public GpuSnapshot? PrimaryGpu =>
        Gpus.FirstOrDefault(g => g.Kind == GpuKind.Discrete) ?? Gpus.FirstOrDefault();
}
