using LibreHardwareMonitor.Hardware;
using SensorDeck.Core.Models;

namespace SensorDeck.Core.Sensors;

/// <summary>Turns the current LHM hardware tree into an immutable <see cref="HardwareSnapshot"/>.</summary>
public sealed class SnapshotBuilder
{
    private readonly HashSet<string> _seenSpinning = new();

    public HardwareSnapshot Build(IEnumerable<IHardware> hardware, MonitorStatus status, string memoryDescription, bool includeRaw)
    {
        var all = hardware.ToList();

        var cpus = all.Where(h => h.HardwareType == HardwareType.Cpu)
            .Select(h => SensorSelector.ReadCpu(h, status)).ToList();

        var gpus = all.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .Select(SensorSelector.ReadGpu).ToList();

        var memory = SensorSelector.ReadMemory(all.Where(h => h.HardwareType == HardwareType.Memory), memoryDescription);

        var fans = new List<FanReading>();
        foreach (var board in all.Where(h => h.HardwareType == HardwareType.Motherboard))
            fans.AddRange(SensorSelector.ReadFans(board, "Motherboard", includeStopped: false, _seenSpinning));
        foreach (var ctl in all.Where(h => h.HardwareType is HardwareType.Cooler or HardwareType.EmbeddedController))
            fans.AddRange(SensorSelector.ReadFans(ctl, ctl.Name, includeStopped: false, _seenSpinning));
        foreach (var gpu in gpus) fans.AddRange(gpu.Fans);

        var drives = all.Where(h => h.HardwareType == HardwareType.Storage)
            .Select(SensorSelector.ReadStorage).ToList();

        var raw = includeRaw ? CollectRaw(all) : [];

        return new HardwareSnapshot(DateTime.Now, status, cpus, gpus, memory, fans, drives, raw);
    }

    /// <summary>One-off build straight from a <see cref="Computer"/> (used by the probe tool).</summary>
    public static HardwareSnapshot Build(Computer computer, MonitorStatus status, bool includeRaw)
    {
        var info = Info.SystemInfoReader.Read(computer, status);
        return new SnapshotBuilder().Build(computer.Hardware, status, info.MemorySummary, includeRaw);
    }

    private static List<RawSensor> CollectRaw(IEnumerable<IHardware> hardware)
    {
        var list = new List<RawSensor>(256);
        foreach (var root in hardware)
            foreach (var hw in SensorSelector.Flatten(root))
                foreach (var s in hw.Sensors)
                    list.Add(new RawSensor(s.Identifier.ToString(), hw.Name, hw.HardwareType.ToString(), s.Name, s.SensorType.ToString(),
                        Finite(s.Value), Finite(s.Min), Finite(s.Max)));
        return list;
    }

    private static double? Finite(float? v) => v is float f && float.IsFinite(f) ? f : null;
}
