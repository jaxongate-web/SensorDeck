using System.Globalization;

namespace SensorDeck.Core.Models;

/// <summary>Formats raw LibreHardwareMonitor sensor values with the unit implied by their sensor type.</summary>
public static class SensorUnits
{
    public static string Format(string sensorType, double value, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        return sensorType switch
        {
            "Temperature" => value.ToString("0.0", c) + " °C",
            "Load" or "Control" or "Level" => value.ToString("0.0", c) + " %",
            "Clock" => value.ToString("0", c) + " MHz",
            "Frequency" => value.ToString("0", c) + " Hz",
            "Voltage" => value.ToString("0.000", c) + " V",
            "Current" => value.ToString("0.000", c) + " A",
            "Power" => value.ToString("0.0", c) + " W",
            "Fan" => value.ToString("0", c) + " RPM",
            "Flow" => value.ToString("0.0", c) + " L/h",
            "Data" => value.ToString("0.00", c) + " GB",
            "SmallData" => value.ToString("0", c) + " MB",
            "Throughput" => Throughput(value, c),
            "TimeSpan" => TimeSpan.FromSeconds(value).ToString(@"d\.hh\:mm\:ss", c),
            "Timing" => value.ToString("0.###", c) + " ns",
            "Energy" => value.ToString("0", c) + " mWh",
            "Noise" => value.ToString("0.0", c) + " dBA",
            "Humidity" => value.ToString("0", c) + " %",
            "Conductivity" => value.ToString("0.0", c) + " µS/cm",
            "Factor" => value.ToString("0.000", c),
            _ => value.ToString("0.###", c),
        };
    }

    private static string Throughput(double bytesPerSecond, CultureInfo c) => bytesPerSecond switch
    {
        >= 1_000_000_000 => (bytesPerSecond / 1e9).ToString("0.00", c) + " GB/s",
        >= 1_000_000 => (bytesPerSecond / 1e6).ToString("0.0", c) + " MB/s",
        >= 1_000 => (bytesPerSecond / 1e3).ToString("0.0", c) + " KB/s",
        _ => bytesPerSecond.ToString("0", c) + " B/s",
    };
}
