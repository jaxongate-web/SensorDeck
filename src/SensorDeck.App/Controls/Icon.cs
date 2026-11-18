using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SensorDeck.App.Controls;

/// <summary>Renders a 24×24 stroke icon geometry, scaled to the control size, coloured by Foreground.</summary>
public sealed class Icon : Control
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender));

    static Icon()
    {
        ForegroundProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(
            Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
        WidthProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(18.0));
        HeightProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(18.0));
        FocusableProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        double scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
        // Pen thickness is in 24-grid units, so it scales with the icon.
        var pen = new Pen(Foreground, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, Data);
        dc.Pop();
        dc.Pop();
    }
}
