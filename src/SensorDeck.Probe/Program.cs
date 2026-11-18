// SensorDeck.Probe — dumps every sensor LibreHardwareMonitor can see on this PC,
// then the readings SensorDeck's own selection logic picks from them.
// Usage: SensorDeck.Probe [--raw-only]
using System.Text;
using LibreHardwareMonitor.Hardware;

Console.OutputEncoding = Encoding.UTF8;
bool rawOnly = args.Contains("--raw-only");

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsStorageEnabled = true,
};
computer.Open();

// Two passes: some sensors (load, throughput) only have values after a second update.
for (int pass = 0; pass < 2; pass++)
{
    foreach (var hw in computer.Hardware) UpdateRecursive(hw);
    if (pass == 0) Thread.Sleep(1000);
}

Console.WriteLine("=== RAW SENSORS ===");
foreach (var hw in computer.Hardware) Dump(hw, 0);

Console.WriteLine();
Console.WriteLine("=== SMBIOS ===");
var sm = computer.SMBios;
Console.WriteLine($"Board: {sm.Board?.ManufacturerName} {sm.Board?.ProductName}");
foreach (var p in sm.Processors ?? [])
    Console.WriteLine($"CPU: {p.Version} cores={p.CoreCount} threads={p.ThreadCount} max={p.MaxSpeed} socket={p.SocketDesignation}");
foreach (var m in sm.MemoryDevices ?? [])
    Console.WriteLine($"DIMM: {m.DeviceLocator} size={m.Size} type={m.Type} speed={m.Speed} cfg={m.ConfiguredSpeed} {m.ManufacturerName} {m.PartNumber}");

#if !BOOTSTRAP
if (!rawOnly)
{
    Console.WriteLine();
    Console.WriteLine("=== SENSORDECK SELECTION ===");
    var snap = SensorDeck.Core.Sensors.SnapshotBuilder.Build(computer, SensorDeck.Core.Sensors.SystemAccess.Detect(), includeRaw: false);
    Console.WriteLine($"Status: elevated={snap.Status.IsElevated} driver={snap.Status.IsDriverInstalled}");
    foreach (var c in snap.Cpus) Console.WriteLine($"CPU  {c}");
    foreach (var g in snap.Gpus) Console.WriteLine($"GPU  {g}");
    Console.WriteLine($"MEM  {snap.Memory}");
    foreach (var f in snap.Fans) Console.WriteLine($"FAN  {f}");
    foreach (var d in snap.Drives) Console.WriteLine($"DISK {d}");
}
#endif

computer.Close();

static void UpdateRecursive(IHardware hw)
{
    hw.Update();
    foreach (var sub in hw.SubHardware) UpdateRecursive(sub);
}

static void Dump(IHardware hw, int depth)
{
    string pad = new(' ', depth * 2);
    Console.WriteLine($"{pad}[{hw.HardwareType}] {hw.Name}");
    foreach (var s in hw.Sensors)
        Console.WriteLine($"{pad}  {s.SensorType,-12} {s.Name,-32} {Fmt(s.Value)}");
    foreach (var sub in hw.SubHardware) Dump(sub, depth + 1);
}

static string Fmt(float? v) => v is float f ? f.ToString("0.###") : "—";
