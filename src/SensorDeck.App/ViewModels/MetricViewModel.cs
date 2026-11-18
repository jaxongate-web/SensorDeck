using CommunityToolkit.Mvvm.ComponentModel;
using SensorDeck.Core.Models;

namespace SensorDeck.App.ViewModels;

public enum Severity { Normal, Warm, Hot }

/// <summary>One labelled reading on a card (e.g. "Temperature 54 °C") with a 0–1 bar fraction.</summary>
public sealed partial class MetricViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty] public partial string ValueText { get; set; } = "–";
    [ObservableProperty] public partial string Unit { get; set; } = "";
    [ObservableProperty] public partial bool IsSupported { get; set; } = true;
    [ObservableProperty] public partial string? Reason { get; set; }
    [ObservableProperty] public partial double Fraction { get; set; }
    [ObservableProperty] public partial Severity Severity { get; set; }

    /// <summary>Raw value of the last supported reading (for history and peak tracking).</summary>
    public double? Raw { get; private set; }

    public void Update(SensorValue value, Func<double, (string Text, string Unit)> format,
        Func<double, double> fraction, Func<double, Severity>? severity = null)
    {
        if (value.Value is not double v)
        {
            SetUnsupported(value.Reason ?? "Not supported");
            return;
        }

        Raw = v;
        var (text, unit) = format(v);
        ValueText = text;
        Unit = unit;
        Fraction = Math.Clamp(fraction(v), 0, 1);
        Severity = severity?.Invoke(v) ?? Severity.Normal;
        IsSupported = true;
        Reason = null;
    }

    public void SetUnsupported(string reason)
    {
        Raw = null;
        IsSupported = false;
        Reason = reason;
        ValueText = "Not supported";
        Unit = "";
        Fraction = 0;
        Severity = Severity.Normal;
    }
}
