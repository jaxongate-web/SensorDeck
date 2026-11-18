using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using SensorDeck.Core.Models;
using SensorValue = SensorDeck.Core.Models.SensorValue;

namespace SensorDeck.Core.Sensors;

/// <summary>
/// Picks the right LibreHardwareMonitor sensor for each reading. Sensor names differ between
/// vendors (Intel "CPU Package" vs AMD "Core (Tctl/Tdie)"), so each reading has an ordered
/// list of candidates. Pure functions over IHardware so they can be unit-tested with fakes.
/// </summary>
public static partial class SensorSelector
{
    internal const string NoValueReason = "Sensor reported no value";
    internal const string NotExposedReason = "Not exposed by this hardware";

    // ---------- CPU ----------

    internal static readonly string[] CpuTempNames =
        ["CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Package", "Core Max", "Core Average", "CPU Cores"];

    public static CpuSnapshot ReadCpu(IHardware cpu, MonitorStatus status)
    {
        string lowLevel = status.LowLevelReason;

        var temp = Pick(cpu, SensorType.Temperature, CpuTempNames, lowLevel);
        var usage = Pick(cpu, SensorType.Load, ["CPU Total"], NotExposedReason);

        // Per-core clocks: "P-Core #1", "E-Core #3", "Core #1", "CPU Core #1". Skip bus/effective/average.
        var coreClocks = cpu.Sensors
            .Where(s => s.SensorType == SensorType.Clock
                        && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase)
                        && !s.Name.Contains("Average", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Value)
            .Where(v => v is float f && float.IsFinite(f) && f > 0)
            .Select(v => (double)v!.Value)
            .ToList();

        var clock = coreClocks.Count > 0 ? SensorValue.Of(coreClocks.Average()) : SensorValue.Unsupported(lowLevel);
        var maxClock = coreClocks.Count > 0 ? SensorValue.Of(coreClocks.Max()) : SensorValue.Unsupported(lowLevel);

        // Package power reads 0 W when the low-level driver is unavailable; a running CPU never draws 0 W.
        var power = Pick(cpu, SensorType.Power, ["CPU Package", "Package"], lowLevel, requirePositive: true);

        return new CpuSnapshot(cpu.Name, temp, usage, clock, maxClock, power);
    }

    // ---------- GPU ----------

    public static GpuSnapshot ReadGpu(IHardware gpu)
    {
        var kind = ClassifyGpu(gpu);
        var temp = Pick(gpu, SensorType.Temperature, ["GPU Core", "GPU Hot Spot", "GPU Temperature"], NotExposedReason);
        var usage = Pick(gpu, SensorType.Load, ["GPU Core", "D3D 3D"], NotExposedReason);
        var clock = Pick(gpu, SensorType.Clock, ["GPU Core"], NotExposedReason, requirePositive: true);
        var memClock = Pick(gpu, SensorType.Clock, ["GPU Memory"], NotExposedReason, requirePositive: true);

        SensorValue vramUsed, vramTotal;
        if (kind == GpuKind.Integrated)
        {
            const string shared = "Integrated GPU uses shared system memory";
            vramUsed = SensorValue.Unsupported(shared);
            vramTotal = SensorValue.Unsupported(shared);
        }
        else
        {
            vramUsed = Pick(gpu, SensorType.SmallData, ["GPU Memory Used", "D3D Dedicated Memory Used"], NotExposedReason);
            vramTotal = Pick(gpu, SensorType.SmallData, ["GPU Memory Total", "D3D Dedicated Memory Total"], NotExposedReason, requirePositive: true);
        }

        var power = Pick(gpu, SensorType.Power, ["GPU Package", "GPU Power", "GPU Core"], NotExposedReason);
        var fans = ReadFans(gpu, "GPU", includeStopped: true, seenSpinning: null);

        return new GpuSnapshot(gpu.Name, kind, temp, usage, clock, memClock, vramUsed, vramTotal, power, fans);
    }

    [GeneratedRegex(@"Radeon\(TM\) Graphics$|Radeon Graphics$|Vega \d+ Graphics|Radeon \d{3,4}M\b|Radeon Vega", RegexOptions.IgnoreCase)]
    private static partial Regex AmdIntegratedPattern();

    // Discrete Intel Arc cards carry a model number ("Arc(TM) A770", "Arc B580"); integrated "Arc Graphics" doesn't.
    [GeneratedRegex(@"Arc(\(TM\))?\s+[AB]\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex IntelDiscretePattern();

    public static GpuKind ClassifyGpu(IHardware gpu) => gpu.HardwareType switch
    {
        HardwareType.GpuIntel when !IntelDiscretePattern().IsMatch(gpu.Name) => GpuKind.Integrated,
        HardwareType.GpuAmd when AmdIntegratedPattern().IsMatch(gpu.Name) => GpuKind.Integrated,
        _ => GpuKind.Discrete,
    };

    // ---------- Memory ----------

    public static MemorySnapshot ReadMemory(IEnumerable<IHardware> memoryHardware, string description)
    {
        var list = memoryHardware.ToList();
        // "Total Memory" (0.9.6+) / "Generic Memory" (older) is physical RAM; skip "Virtual Memory".
        var physical = list.FirstOrDefault(h => h.Name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                       ?? list.FirstOrDefault(h => h.Name.Contains("Generic", StringComparison.OrdinalIgnoreCase))
                       ?? list.FirstOrDefault(h => !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                                                   && Find(h, SensorType.Data, "Memory Used") is not null);

        SensorValue used, total, load;
        if (physical is null)
        {
            used = total = load = SensorValue.Unsupported(NotExposedReason);
        }
        else
        {
            used = Pick(physical, SensorType.Data, ["Memory Used"], NotExposedReason);
            var available = Pick(physical, SensorType.Data, ["Memory Available"], NotExposedReason);
            total = used.IsSupported && available.IsSupported
                ? SensorValue.Of(used.Value!.Value + available.Value!.Value)
                : SensorValue.Unsupported(NotExposedReason);
            load = Pick(physical, SensorType.Load, ["Memory"], NotExposedReason);
        }

        // DDR5 modules with an SPD hub expose their own temperature sensor.
        var moduleTemps = list
            .SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Temperature)
            .Select(s => SensorValue.From(s.Value, NoValueReason))
            .ToList();

        return new MemorySnapshot(description, used, total, load, moduleTemps);
    }

    // ---------- Fans ----------

    /// <summary>
    /// Fan sensors on a hardware node and its sub-hardware. Motherboard headers that have
    /// never spun are hidden (they are usually unconnected); GPU fans are always shown
    /// because 0 RPM there means zero-RPM idle mode.
    /// </summary>
    public static List<FanReading> ReadFans(IHardware hw, string source, bool includeStopped, ISet<string>? seenSpinning)
    {
        var result = new List<FanReading>();
        foreach (var node in Flatten(hw))
        {
            foreach (var fan in node.Sensors.Where(s => s.SensorType == SensorType.Fan))
            {
                var rpm = SensorValue.From(fan.Value, NoValueReason);
                string key = fan.Identifier.ToString();
                if (rpm.Value > 0) seenSpinning?.Add(key);

                bool show = includeStopped || rpm.Value > 0 || (seenSpinning?.Contains(key) ?? false);
                if (!show) continue;

                var control = node.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Control && s.Name == fan.Name);
                result.Add(new FanReading(
                    fan.Name,
                    source,
                    rpm,
                    control is null ? SensorValue.Unsupported(NotExposedReason) : SensorValue.From(control.Value, NoValueReason)));
            }
        }
        return result;
    }

    // ---------- Storage ----------

    public static StorageSnapshot ReadStorage(IHardware drive)
    {
        var temp = Pick(drive, SensorType.Temperature, ["Temperature", "Composite Temperature"], "Drive does not report temperature");
        if (!temp.IsSupported)
        {
            var any = drive.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value is float f && float.IsFinite(f));
            if (any is not null) temp = SensorValue.From(any.Value, NoValueReason);
        }
        var used = Pick(drive, SensorType.Load, ["Used Space"], NotExposedReason);
        return new StorageSnapshot(drive.Name, temp, used);
    }

    // ---------- Helpers ----------

    internal static IEnumerable<IHardware> Flatten(IHardware hw)
    {
        yield return hw;
        foreach (var sub in hw.SubHardware)
            foreach (var x in Flatten(sub))
                yield return x;
    }

    internal static ISensor? Find(IHardware hw, SensorType type, string name) =>
        hw.Sensors.FirstOrDefault(s => s.SensorType == type && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the first candidate sensor (in priority order) that has a usable value.
    /// If candidates exist but none has a value, the reason given is <paramref name="reasonIfMissing"/>.
    /// </summary>
    internal static SensorValue Pick(IHardware hw, SensorType type, string[] names, string reasonIfMissing, bool requirePositive = false)
    {
        foreach (var name in names)
        {
            var s = Find(hw, type, name);
            if (s?.Value is float f && float.IsFinite(f) && (!requirePositive || f > 0))
                return SensorValue.Of(f);
        }
        return SensorValue.Unsupported(reasonIfMissing);
    }
}
