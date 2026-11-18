using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SensorDeck.Core.History;

namespace SensorDeck.App.Controls;

/// <summary>One line on a graph: a named history buffer drawn in a colour.</summary>
public sealed class GraphSeries
{
    public GraphSeries(string name, Color color, int capacity)
    {
        Name = name;
        Color = color;
        Buffer = new RingBuffer(capacity);
        Stroke = Freeze(new SolidColorBrush(color));
        Pen = Freeze(new Pen(Stroke, 1.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Fill = Freeze(new LinearGradientBrush(Color.FromArgb(70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90));
    }

    public string Name { get; set; }
    public Color Color { get; }
    public RingBuffer Buffer { get; }
    public SolidColorBrush Stroke { get; }
    public Pen Pen { get; }
    public Brush Fill { get; }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>
/// Lightweight live line graph drawn directly with StreamGeometry in OnRender — no chart library,
/// no per-point visuals. Missing samples (NaN) break the line instead of dropping to zero.
/// Hovering shows a crosshair with exact values.
/// </summary>
public sealed class LiveGraph : FrameworkElement
{
    private const double MinAxisWidth = 36, TopPad = 8, BottomPad = 22, RightPad = 8;

    public static readonly DependencyProperty SeriesProperty = Dp(nameof(Series), typeof(IReadOnlyList<GraphSeries>), null);
    public static readonly DependencyProperty VisibleSamplesProperty = Dp(nameof(VisibleSamples), typeof(int), 60);
    public static readonly DependencyProperty RevisionProperty = Dp(nameof(Revision), typeof(long), 0L);
    public static readonly DependencyProperty AxisModeProperty = Dp(nameof(AxisMode), typeof(AxisMode), AxisMode.Auto);
    public static readonly DependencyProperty TransformProperty = Dp(nameof(Transform), typeof(Func<double, double>), null);
    public static readonly DependencyProperty LabelFormatProperty = Dp(nameof(LabelFormat), typeof(Func<double, string>), null);
    public static readonly DependencyProperty AxisLabelFormatProperty = Dp(nameof(AxisLabelFormat), typeof(Func<double, string>), null);
    public static readonly DependencyProperty SampleSecondsProperty = Dp(nameof(SampleSeconds), typeof(double), 1.0);
    public static readonly DependencyProperty MinSpanProperty = Dp(nameof(MinSpan), typeof(double), 10.0);

    private static DependencyProperty Dp(string name, Type type, object? def) =>
        DependencyProperty.Register(name, type, typeof(LiveGraph), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<GraphSeries>? Series { get => (IReadOnlyList<GraphSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public int VisibleSamples { get => (int)GetValue(VisibleSamplesProperty); set => SetValue(VisibleSamplesProperty, value); }
    /// <summary>Bump to request a redraw after new samples were added.</summary>
    public long Revision { get => (long)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public AxisMode AxisMode { get => (AxisMode)GetValue(AxisModeProperty); set => SetValue(AxisModeProperty, value); }
    /// <summary>Converts stored values to display units (e.g. °C → °F).</summary>
    public Func<double, double>? Transform { get => (Func<double, double>?)GetValue(TransformProperty); set => SetValue(TransformProperty, value); }
    /// <summary>Formats a display-unit value for axis labels and the hover readout.</summary>
    public Func<double, string>? LabelFormat { get => (Func<double, string>?)GetValue(LabelFormatProperty); set => SetValue(LabelFormatProperty, value); }
    /// <summary>Optional shorter format for Y-axis tick labels (falls back to <see cref="LabelFormat"/>).</summary>
    public Func<double, string>? AxisLabelFormat { get => (Func<double, string>?)GetValue(AxisLabelFormatProperty); set => SetValue(AxisLabelFormatProperty, value); }
    public double SampleSeconds { get => (double)GetValue(SampleSecondsProperty); set => SetValue(SampleSecondsProperty, value); }
    public double MinSpan { get => (double)GetValue(MinSpanProperty); set => SetValue(MinSpanProperty, value); }

    // Shared, frozen drawing resources
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface BoldFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x75, 0x88)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xF2)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x27, 0x36))), 1));
    private static readonly Pen CrossPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x8A, 0x94, 0xA6))), 1) { DashStyle = DashStyles.Dash });
    private static readonly Brush TipBg = Frozen(new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x20, 0x2D)));
    private static readonly Pen TipBorder = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x35, 0x47))), 1));
    private static readonly Brush DotCenter = Frozen(new SolidColorBrush(Color.FromRgb(0x12, 0x17, 0x22)));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private Point? _hover;

    public LiveGraph()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) { _hover = e.GetPosition(this); InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 80 || h < 50) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-testable for hover

        var series = Series ?? [];
        int n = Math.Max(2, VisibleSamples);
        var tf = Transform ?? (v => v);
        var fmt = LabelFormat ?? (v => v.ToString("0", CultureInfo.CurrentCulture));
        var axisFmt = AxisLabelFormat ?? fmt;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // ----- range over the visible window -----
        double? lo = null, hi = null;
        foreach (var s in series)
        {
            var b = s.Buffer;
            for (int i = Math.Max(0, b.Count - n); i < b.Count; i++)
            {
                double v = b[i];
                if (double.IsNaN(v)) continue;
                v = tf(v);
                lo = lo is null ? v : Math.Min(lo.Value, v);
                hi = hi is null ? v : Math.Max(hi.Value, v);
            }
        }
        var (min, max, step) = AxisScale.Compute(AxisMode, lo, hi, MinSpan);

        // Measure the Y labels first so the axis column is exactly as wide as it needs to be.
        var ticks = new List<(double Value, FormattedText Label)>();
        for (double t = min; t <= max + step * 0.01; t += step)
            ticks.Add((t, Text(axisFmt(t), LabelFace, 10.5, LabelBrush, dpi)));
        double axisWidth = Math.Max(MinAxisWidth, ticks.Max(t => t.Label.Width) + 12);

        var plot = new Rect(axisWidth, TopPad, w - axisWidth - RightPad, h - TopPad - BottomPad);
        double range = max - min;
        double Y(double v) => plot.Bottom - (v - min) / range * plot.Height;
        double dx = plot.Width / (n - 1);

        // ----- grid + Y labels -----
        foreach (var (t, ft) in ticks)
        {
            double y = Math.Round(Y(t)) + 0.5;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            dc.DrawText(ft, new Point(plot.Left - 8 - ft.Width, y - ft.Height / 2));
        }

        // ----- X labels -----
        double windowSec = (n - 1) * SampleSeconds;
        DrawXLabel(dc, $"-{FormatSpan(windowSec)}", plot.Left, plot.Bottom + 5, 0, dpi);
        DrawXLabel(dc, $"-{FormatSpan(windowSec / 2)}", plot.Left + plot.Width / 2, plot.Bottom + 5, 0.5, dpi);
        DrawXLabel(dc, "now", plot.Right, plot.Bottom + 5, 1, dpi);

        // ----- series -----
        dc.PushClip(new RectangleGeometry(new Rect(plot.Left - 4, plot.Top - 4, plot.Width + 8, plot.Height + 8)));
        bool fillArea = series.Count == 1;
        foreach (var s in series)
        {
            var b = s.Buffer;
            int start = Math.Max(0, b.Count - n);
            var line = new StreamGeometry();
            StreamGeometry? area = fillArea ? new StreamGeometry() : null;
            using (var ctx = line.Open())
            using (var actx = area?.Open())
            {
                bool inRun = false;
                Point runStart = default, last = default;
                for (int i = start; i < b.Count; i++)
                {
                    double v = b[i];
                    double x = plot.Right - (b.Count - 1 - i) * dx;
                    if (double.IsNaN(v))
                    {
                        if (inRun && actx is not null) CloseArea(actx, runStart, last, plot.Bottom);
                        inRun = false;
                        continue;
                    }
                    var p = new Point(x, Y(tf(v)));
                    if (!inRun)
                    {
                        ctx.BeginFigure(p, false, false);
                        actx?.BeginFigure(new Point(p.X, plot.Bottom), true, true);
                        actx?.LineTo(p, false, false);
                        runStart = p;
                        inRun = true;
                    }
                    else
                    {
                        ctx.LineTo(p, true, true);
                        actx?.LineTo(p, false, false);
                    }
                    last = p;
                }
                if (inRun && actx is not null) CloseArea(actx, runStart, last, plot.Bottom);
            }
            line.Freeze();
            if (area is not null) { area.Freeze(); dc.DrawGeometry(s.Fill, null, area); }
            dc.DrawGeometry(null, s.Pen, line);

            // live end-point marker
            if (b.Count > 0 && !double.IsNaN(b[b.Count - 1]))
            {
                var p = new Point(plot.Right, Y(tf(b[b.Count - 1])));
                dc.DrawEllipse(DotCenter, s.Pen, p, 3.2, 3.2);
            }
        }
        dc.Pop();

        // ----- hover crosshair + readout -----
        if (_hover is Point hp && plot.Contains(new Point(hp.X, plot.Top + 1)) && series.Count > 0)
        {
            int back = (int)Math.Round((plot.Right - hp.X) / dx);
            double x = plot.Right - back * dx;
            dc.DrawLine(CrossPen, new Point(x, plot.Top), new Point(x, plot.Bottom));

            var lines = new List<(string Text, Brush Brush)> { ($"{FormatSpan(back * SampleSeconds)} ago", LabelBrush) };
            if (back == 0) lines[0] = ("now", LabelBrush);
            foreach (var s in series)
            {
                int idx = s.Buffer.Count - 1 - back;
                string val = idx < 0 ? "no data yet"
                    : double.IsNaN(s.Buffer[idx]) ? "not available"
                    : fmt(tf(s.Buffer[idx]));
                lines.Add((series.Count > 1 ? $"{s.Name}: {val}" : val, s.Stroke));
                if (idx >= 0 && !double.IsNaN(s.Buffer[idx]))
                    dc.DrawEllipse(DotCenter, s.Pen, new Point(x, Y(tf(s.Buffer[idx]))), 3.2, 3.2);
            }

            var texts = lines.Select((l, i) => Text(l.Text, i == 0 ? LabelFace : BoldFace, i == 0 ? 10.5 : 12, i == 0 ? LabelBrush : TextBrush, dpi)).ToList();
            double tw = texts.Max(t => t.Width) + 20 + (series.Count > 1 ? 10 : 0), th = texts.Sum(t => t.Height) + 12;
            double tx = x + 10 + tw > plot.Right ? x - 10 - tw : x + 10;
            var box = new Rect(tx, plot.Top + 4, tw, th);
            dc.DrawRoundedRectangle(TipBg, TipBorder, box, 6, 6);
            double ty = box.Top + 6;
            for (int i = 0; i < texts.Count; i++)
            {
                double tx2 = box.Left + 10;
                if (i > 0 && series.Count > 1)
                {
                    dc.DrawEllipse(lines[i].Brush, null, new Point(tx2 + 3, ty + texts[i].Height / 2), 3, 3);
                    tx2 += 10;
                }
                dc.DrawText(texts[i], new Point(tx2, ty));
                ty += texts[i].Height;
            }
        }
    }

    private static void CloseArea(StreamGeometryContext ctx, Point start, Point last, double bottom)
    {
        ctx.LineTo(new Point(last.X, bottom), false, false);
        ctx.LineTo(new Point(start.X, bottom), false, false);
    }

    private void DrawXLabel(DrawingContext dc, string s, double x, double y, double align, double dpi)
    {
        var ft = Text(s, LabelFace, 10.5, LabelBrush, dpi);
        dc.DrawText(ft, new Point(x - ft.Width * align, y));
    }

    private static FormattedText Text(string s, Typeface face, double size, Brush brush, double dpi) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, dpi);

    internal static string FormatSpan(double seconds) =>
        seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60:0} min"
        : seconds >= 90 ? $"{seconds / 60:0.#} min"
        : $"{seconds:0} s";
}
