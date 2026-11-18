using System.Globalization;
using SensorDeck.Core.Info;
using SensorDeck.Core.Models;

namespace SensorDeck.Core.Tests;

public class InfoFormattingTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("Temperature", 41.25, "41.3 °C")]
    [InlineData("Load", 3.24, "3.2 %")]
    [InlineData("Clock", 5400.4, "5400 MHz")]
    [InlineData("Voltage", 0.925, "0.925 V")]
    [InlineData("Fan", 1180.6, "1181 RPM")]
    [InlineData("SmallData", 1163, "1163 MB")]
    [InlineData("Data", 11.389, "11.39 GB")]
    [InlineData("Throughput", 5549056, "5.5 MB/s")]
    [InlineData("Throughput", 99328, "99.3 KB/s")]
    [InlineData("Power", 10.136, "10.1 W")]
    public void Raw_sensor_units(string type, double value, string expected) =>
        Assert.Equal(expected, SensorUnits.Format(type, value, Inv));

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "32.0.15.9186", "591.86")]
    [InlineData("NVIDIA GeForce RTX 5080", "32.0.15.6094", "560.94")]
    [InlineData("Intel(R) UHD Graphics 770", "32.0.101.7088", "32.0.101.7088")]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "garbage", "garbage")]
    public void Nvidia_driver_versions_use_familiar_numbering(string gpu, string windowsVersion, string expected) =>
        Assert.Equal(expected, WindowsInfo.FriendlyDriverVersion(gpu, windowsVersion));

    [Fact]
    public void Drive_summary_uses_decimal_capacity()
    {
        var d = new DriveDetails("CT2000T500SSD8", 2_000_398_934_016, "NVMe", "SSD");
        Assert.Equal("NVMe SSD · 2.0 TB", d.Summary);
        Assert.Equal("USB · 64 GB", new DriveDetails("Stick", 64_000_000_000, "USB", "Unspecified").Summary);
    }

    [Fact]
    public void Drive_lookup_matches_model_names()
    {
        var info = new SystemInfo("cpu", "mem", "board", "os", [], [],
            [new DriveDetails("Samsung SSD 990 PRO 2TB", 2_000_000_000_000, "NVMe", "SSD")]);
        Assert.NotNull(info.FindDrive("Samsung SSD 990 PRO 2TB"));
        Assert.Null(info.FindDrive("WD Blue"));
    }
}
