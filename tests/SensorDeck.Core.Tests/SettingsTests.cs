using System.Xml.Linq;
using SensorDeck.Core.Settings;

namespace SensorDeck.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SensorDeckTests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(TemperatureUnit.Celsius, s.TemperatureUnit);
        Assert.Equal(1, s.RefreshSeconds);
        Assert.Equal(80, s.CpuTemp.Warm);
        Assert.False(s.UsesTray);
    }

    [Fact]
    public void Round_trips_all_values()
    {
        var store = new SettingsStore(FilePath);
        var s = new AppSettings
        {
            TemperatureUnit = TemperatureUnit.Fahrenheit,
            RefreshSeconds = 2,
            DefaultGraphMinutes = 5,
            CpuTemp = new(70, 85),
            AlwaysOnTop = true,
            CloseToTray = true,
        };
        store.Save(s);
        var back = store.Load();

        Assert.Equal(TemperatureUnit.Fahrenheit, back.TemperatureUnit);
        Assert.Equal(2, back.RefreshSeconds);
        Assert.Equal(5, back.DefaultGraphMinutes);
        Assert.Equal(70, back.CpuTemp.Warm);
        Assert.Equal(85, back.CpuTemp.Hot);
        Assert.True(back.AlwaysOnTop);
        Assert.True(back.UsesTray);
        Assert.Contains("\"fahrenheit\"", File.ReadAllText(FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_is_kept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(1, s.RefreshSeconds);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Hand_edited_out_of_range_values_are_repaired()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "refreshSeconds": 0.01, "defaultGraphMinutes": 99,
              "cpuTemp": { "warm": 95, "hot": 60 }, "gpuTemp": null, "temperatureUnit": "Celsius" }
            """);
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(0.5, s.RefreshSeconds);
        Assert.Equal(10, s.DefaultGraphMinutes);
        Assert.True(s.CpuTemp.Hot > s.CpuTemp.Warm);
        Assert.Equal(75, s.GpuTemp.Warm);
    }

    [Fact]
    public void Startup_task_xml_is_valid_and_has_no_battery_or_time_limits()
    {
        string xml = StartupTask.BuildXml(@"C:\Program Files\SensorDeck\SensorDeck.exe", @"PC\Jane & Co");
        var doc = XDocument.Parse(xml.Replace("encoding=\"UTF-16\"", ""));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("false", doc.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("PT0S", doc.Descendants(ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal(@"PC\Jane & Co", doc.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value);
        Assert.Equal("\"C:\\Program Files\\SensorDeck\\SensorDeck.exe\"", doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal("--minimized", doc.Descendants(ns + "Arguments").Single().Value);
    }
}
