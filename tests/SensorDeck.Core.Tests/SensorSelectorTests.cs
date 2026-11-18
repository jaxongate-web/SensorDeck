using LibreHardwareMonitor.Hardware;
using SensorDeck.Core.Models;
using SensorDeck.Core.Sensors;

namespace SensorDeck.Core.Tests;

public class SensorSelectorTests
{
    private static readonly MonitorStatus FullAccess = new(IsElevated: true, IsDriverInstalled: true);
    private static readonly MonitorStatus NoAdmin = new(IsElevated: false, IsDriverInstalled: false);
    private static readonly MonitorStatus NoDriver = new(IsElevated: true, IsDriverInstalled: false);

    [Fact]
    public void Intel_cpu_uses_package_temperature_and_averages_core_clocks()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "Intel Core i7-14700")
            .With(SensorType.Temperature, "Core Max", 61)
            .With(SensorType.Temperature, "CPU Package", 58)
            .With(SensorType.Load, "CPU Total", 23.5f)
            .With(SensorType.Clock, "Bus Speed", 100)
            .With(SensorType.Clock, "P-Core #1", 5400)
            .With(SensorType.Clock, "E-Core #1", 4200)
            .With(SensorType.Power, "CPU Package", 65);

        var snap = SensorSelector.ReadCpu(cpu, FullAccess);

        Assert.Equal(58, snap.Temperature.Value);
        Assert.Equal(23.5, snap.Usage.Value!.Value, 3);
        Assert.Equal(4800, snap.ClockMhz.Value);      // bus speed excluded
        Assert.Equal(5400, snap.MaxCoreClockMhz.Value);
        Assert.Equal(65, snap.PowerWatts.Value);
    }

    [Fact]
    public void Amd_cpu_uses_tctl_tdie_and_ignores_effective_clocks()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "AMD Ryzen 7 9850X3D")
            .With(SensorType.Temperature, "Core (Tctl/Tdie)", 72)
            .With(SensorType.Temperature, "CCD1 (Tdie)", 70)
            .With(SensorType.Clock, "Core #1", 5000)
            .With(SensorType.Clock, "Core #2", 4000)
            .With(SensorType.Clock, "Cores (Average Effective)", 900)
            .With(SensorType.Load, "CPU Total", 10);

        var snap = SensorSelector.ReadCpu(cpu, FullAccess);

        Assert.Equal(72, snap.Temperature.Value);
        Assert.Equal(4500, snap.ClockMhz.Value);
    }

    [Fact]
    public void Missing_cpu_temperature_is_unsupported_with_reason_not_zero()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "Intel Core i7-14700")
            .With(SensorType.Temperature, "CPU Package", null)
            .With(SensorType.Clock, "P-Core #1", null)
            .With(SensorType.Power, "CPU Package", 0) // reads 0 W without the driver
            .With(SensorType.Load, "CPU Total", 5);

        var noAdmin = SensorSelector.ReadCpu(cpu, NoAdmin);
        Assert.False(noAdmin.Temperature.IsSupported);
        Assert.Null(noAdmin.Temperature.Value);
        Assert.Equal("Requires administrator rights", noAdmin.Temperature.Reason);
        Assert.False(noAdmin.ClockMhz.IsSupported);
        Assert.False(noAdmin.PowerWatts.IsSupported);
        Assert.True(noAdmin.Usage.IsSupported);

        Assert.Equal("Requires the PawnIO driver", SensorSelector.ReadCpu(cpu, NoDriver).Temperature.Reason);
    }

    [Fact]
    public void Nan_values_are_unsupported()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "X").With(SensorType.Temperature, "CPU Package", float.NaN);
        Assert.False(SensorSelector.ReadCpu(cpu, FullAccess).Temperature.IsSupported);
    }

    [Fact]
    public void Nvidia_gpu_reads_core_sensors_and_vram()
    {
        var gpu = new FakeHardware(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 4070 SUPER")
            .With(SensorType.Temperature, "GPU Core", 41)
            .With(SensorType.Temperature, "GPU Hot Spot", 49)
            .With(SensorType.Load, "GPU Core", 12)
            .With(SensorType.Clock, "GPU Core", 2610)
            .With(SensorType.SmallData, "D3D Dedicated Memory Used", 885)
            .With(SensorType.SmallData, "GPU Memory Used", 1163)
            .With(SensorType.SmallData, "GPU Memory Total", 12282)
            .With(SensorType.Fan, "GPU Fan 1", 0)
            .With(SensorType.Control, "GPU Fan 1", 0);

        var snap = SensorSelector.ReadGpu(gpu);

        Assert.Equal(GpuKind.Discrete, snap.Kind);
        Assert.Equal(41, snap.Temperature.Value);
        Assert.Equal(1163, snap.VramUsedMb.Value);
        Assert.Equal(12282, snap.VramTotalMb.Value);
        var fan = Assert.Single(snap.Fans);
        Assert.Equal(0, fan.Rpm.Value);          // zero-RPM idle is a real reading, still shown
        Assert.True(fan.ControlPercent.IsSupported);
    }

    [Theory]
    [InlineData(HardwareType.GpuIntel, "Intel(R) UHD Graphics 770", GpuKind.Integrated)]
    [InlineData(HardwareType.GpuIntel, "Intel(R) Arc(TM) Graphics", GpuKind.Integrated)]
    [InlineData(HardwareType.GpuIntel, "Intel(R) Arc(TM) A770 Graphics", GpuKind.Discrete)]
    [InlineData(HardwareType.GpuIntel, "Intel(R) Arc(TM) B580 Graphics", GpuKind.Discrete)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon(TM) Graphics", GpuKind.Integrated)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon RX 9070 XT", GpuKind.Discrete)]
    [InlineData(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 5080", GpuKind.Discrete)]
    public void Gpu_classification(HardwareType type, string name, GpuKind expected) =>
        Assert.Equal(expected, SensorSelector.ClassifyGpu(new FakeHardware(type, name)));

    [Fact]
    public void Primary_gpu_prefers_discrete_over_integrated()
    {
        var igpu = SensorSelector.ReadGpu(new FakeHardware(HardwareType.GpuIntel, "Intel(R) UHD Graphics 770"));
        var dgpu = SensorSelector.ReadGpu(new FakeHardware(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 5080"));
        var snap = new HardwareSnapshot(DateTime.Now, FullAccess, [], [igpu, dgpu],
            SensorSelector.ReadMemory([], "x"), [], [], []);
        Assert.Equal("NVIDIA GeForce RTX 5080", snap.PrimaryGpu!.Name);
        Assert.False(igpu.VramTotalMb.IsSupported);
    }

    [Fact]
    public void Memory_uses_physical_not_virtual()
    {
        var virt = new FakeHardware(HardwareType.Memory, "Virtual Memory")
            .With(SensorType.Data, "Memory Used", 40).With(SensorType.Data, "Memory Available", 20);
        var phys = new FakeHardware(HardwareType.Memory, "Total Memory")
            .With(SensorType.Data, "Memory Used", 12).With(SensorType.Data, "Memory Available", 20)
            .With(SensorType.Load, "Memory", 37.5f);

        var snap = SensorSelector.ReadMemory([virt, phys], "32 GB DDR5-6000");

        Assert.Equal(12, snap.UsedGb.Value);
        Assert.Equal(32, snap.TotalGb.Value);
        Assert.Equal(37.5, snap.LoadPercent.Value);
        Assert.Equal("32 GB DDR5-6000", snap.Description);
    }

    [Fact]
    public void Motherboard_fans_hidden_until_they_have_spun()
    {
        var superIo = new FakeHardware(HardwareType.SuperIO, "Nuvoton NCT6687D")
            .With(SensorType.Fan, "CPU Fan", 1180)
            .With(SensorType.Fan, "System Fan #1", 0);
        var board = new FakeHardware(HardwareType.Motherboard, "Board").WithSub(superIo);
        var seen = new HashSet<string>();

        var first = SensorSelector.ReadFans(board, "Motherboard", includeStopped: false, seen);
        Assert.Equal(["CPU Fan"], first.Select(f => f.Name));

        // Once a fan has been seen spinning it stays visible even when it stops.
        superIo.Set("System Fan #1", SensorType.Fan, 700);
        SensorSelector.ReadFans(board, "Motherboard", false, seen);
        superIo.Set("System Fan #1", SensorType.Fan, 0);
        var later = SensorSelector.ReadFans(board, "Motherboard", false, seen);
        Assert.Equal(2, later.Count);
    }

    [Fact]
    public void Storage_prefers_composite_temperature()
    {
        var nvme = new FakeHardware(HardwareType.Storage, "Samsung SSD 990 PRO 2TB")
            .With(SensorType.Temperature, "Warning Temperature", 85)
            .With(SensorType.Temperature, "Composite Temperature", 38)
            .With(SensorType.Load, "Used Space", 28.9f);
        var hdd = new FakeHardware(HardwareType.Storage, "USB Drive");

        Assert.Equal(38, SensorSelector.ReadStorage(nvme).Temperature.Value);
        var usb = SensorSelector.ReadStorage(hdd);
        Assert.False(usb.Temperature.IsSupported);
        Assert.Equal("Drive does not report temperature", usb.Temperature.Reason);
    }
}
