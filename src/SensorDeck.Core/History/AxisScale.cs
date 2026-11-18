namespace SensorDeck.Core.History;

public enum AxisMode
{
    /// <summary>Fixed 0–100 (percent readings).</summary>
    Percent,
    /// <summary>Fits the data with padding (temperatures).</summary>
    Auto,
    /// <summary>Starts at zero and fits the maximum (clocks, power).</summary>
    AutoFromZero,
}

/// <summary>Computes "nice" axis ranges and tick steps for the live graphs.</summary>
public static class AxisScale
{
    /// <summary>Returns (min, max, step) with round tick values covering [dataMin, dataMax].</summary>
    public static (double Min, double Max, double Step) Compute(AxisMode mode, double? dataMin, double? dataMax, double minSpan = 10)
    {
        if (mode == AxisMode.Percent) return (0, 100, 25);

        if (dataMin is not double lo || dataMax is not double hi)
            return (0, 100, 25); // no data yet: neutral placeholder axis

        if (mode == AxisMode.AutoFromZero)
        {
            double top = Math.Max(hi * 1.1, minSpan);
            double step = NiceStep(top / 4);
            return (0, Math.Ceiling(top / step) * step, step);
        }

        // Centre the (at least minSpan wide) window on the data, with 15 % padding each side.
        double span = Math.Max(hi - lo, minSpan);
        double s = NiceStep(span / 3);
        double mid = (lo + hi) / 2, half = span / 2 * 1.15;
        double min = Math.Floor((mid - half) / s) * s;
        double max = Math.Ceiling((mid + half) / s) * s;
        if (lo >= 0 && min < 0) min = 0; // don't show negative temperatures/clocks that can't happen
        return (min, max, s);
    }

    /// <summary>Rounds a raw step up to 1, 2, 2.5 or 5 × 10^n.</summary>
    public static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw)) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double norm = raw / mag;
        double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10;
        return nice * mag;
    }
}
