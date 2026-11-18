global using TemperatureUnit = SensorDeck.Core.Settings.TemperatureUnit;
using System.Globalization;
using SensorDeck.App.ViewModels;
using SensorDeck.Core.Settings;

namespace SensorDeck.App.Services;

/// <summary>Display formatting and colour thresholds shared by all pages.</summary>
public static class Format
{
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    /// <summary>Kept in sync with the user's setting by <see cref="SettingsService"/>.</summary>
    public static TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public static string TemperatureSymbol => TemperatureUnit == TemperatureUnit.Fahrenheit ? "°F" : "°C";

    public static double ToDisplayTemperature(double celsius) =>
        TemperatureUnit == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;

    public static (string, string) Temperature(double celsius) =>
        (ToDisplayTemperature(celsius).ToString("0", Culture), TemperatureSymbol);

    /// <summary>"80 °C" / "176 °F" as a single string.</summary>
    public static string TemperatureText(double celsius) { var (v, u) = Temperature(celsius); return $"{v} {u}"; }

    public static (string, string) Percent(double p) => (p.ToString("0", Culture), "%");

    public static (string, string) Clock(double mhz) => mhz >= 1000
        ? ((mhz / 1000).ToString("0.00", Culture), "GHz")
        : (mhz.ToString("0", Culture), "MHz");

    public static (string, string) Watts(double w) => (w.ToString(w < 10 ? "0.0" : "0", Culture), "W");

    public static (string, string) Rpm(double rpm) => (rpm.ToString("#,0", Culture), "RPM");

    public static string Gb(double gb) => gb.ToString("0.0", Culture);

    public static Func<double, Severity> TempSeverity(TempThreshold t) =>
        c => c >= t.Hot ? Severity.Hot : c >= t.Warm ? Severity.Warm : Severity.Normal;

    public static Severity LoadSeverity(double percent) =>
        percent >= 95 ? Severity.Hot : percent >= 85 ? Severity.Warm : Severity.Normal;
}
