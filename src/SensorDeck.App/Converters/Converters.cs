using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SensorDeck.App.ViewModels;

namespace SensorDeck.App.Converters;

/// <summary>Visible when the value is non-null (and non-empty for strings). Parameter "invert" flips it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value is string s ? !string.IsNullOrEmpty(s) : value is not null;
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Bool to Visibility. Parameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Looks up an application resource (e.g. an icon geometry) by its key.</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a reading's severity to the text / bar brush (blue → amber → red).</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public Brush Normal { get; set; } = Brushes.White;
    public Brush Warm { get; set; } = Brushes.Orange;
    public Brush Hot { get; set; } = Brushes.Red;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Severity.Hot => Hot,
        Severity.Warm => Warm,
        _ => Normal,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
