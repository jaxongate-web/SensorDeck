using System.Windows;
using System.Windows.Controls;

namespace SensorDeck.App.Controls;

/// <summary>
/// Lays children out two per row with equal column widths; each row is only as tall as its
/// taller child (unlike UniformGrid, which makes every cell as tall as the tallest overall).
/// </summary>
public sealed class TwoColumnPanel : Panel
{
    protected override Size MeasureOverride(Size available)
    {
        double colWidth = double.IsInfinity(available.Width) ? double.PositiveInfinity : available.Width / 2;
        double height = 0, rowHeight = 0, maxCol = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(colWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            maxCol = Math.Max(maxCol, child.DesiredSize.Width);
            if (i % 2 == 1 || i == InternalChildren.Count - 1) { height += rowHeight; rowHeight = 0; }
        }
        double width = double.IsInfinity(available.Width) ? maxCol * 2 : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double colWidth = final.Width / 2, y = 0;
        for (int i = 0; i < InternalChildren.Count; i += 2)
        {
            var a = InternalChildren[i];
            var b = i + 1 < InternalChildren.Count ? InternalChildren[i + 1] : null;
            double rowHeight = Math.Max(a.DesiredSize.Height, b?.DesiredSize.Height ?? 0);
            a.Arrange(new Rect(0, y, colWidth, rowHeight));
            b?.Arrange(new Rect(colWidth, y, colWidth, rowHeight));
            y += rowHeight;
        }
        return final;
    }
}
