using SensorDeck.Core.History;

namespace SensorDeck.Core.Tests;

public class AxisScaleTests
{
    [Theory]
    [InlineData(0.7, 1)]
    [InlineData(1.6, 2)]
    [InlineData(2.2, 2.5)]
    [InlineData(3.4, 5)]
    [InlineData(7, 10)]
    [InlineData(340, 500)]
    public void Nice_steps(double raw, double expected) => Assert.Equal(expected, AxisScale.NiceStep(raw), 6);

    [Fact]
    public void Percent_axis_is_fixed()
    {
        Assert.Equal((0.0, 100.0, 25.0), AxisScale.Compute(AxisMode.Percent, 3, 9));
    }

    [Fact]
    public void Auto_axis_covers_data_with_round_ticks()
    {
        var (min, max, step) = AxisScale.Compute(AxisMode.Auto, 38, 47, minSpan: 15);
        Assert.True(min <= 38 && max >= 47);
        Assert.Equal(0, min % step, 6);
        Assert.Equal(0, max % step, 6);
        Assert.True(max - min <= 40, "axis should stay reasonably tight around the data");
    }

    [Fact]
    public void Flat_data_still_gets_minimum_span()
    {
        var (min, max, _) = AxisScale.Compute(AxisMode.Auto, 41, 41, minSpan: 15);
        Assert.True(max - min >= 15);
        Assert.True(min >= 0, "non-negative data must not produce a negative axis");
    }

    [Fact]
    public void Auto_from_zero_starts_at_zero_and_covers_max()
    {
        var (min, max, step) = AxisScale.Compute(AxisMode.AutoFromZero, 210, 2610, minSpan: 1000);
        Assert.Equal(0, min);
        Assert.True(max >= 2610);
        Assert.Equal(0, max % step, 6);
    }

    [Fact]
    public void No_data_gives_placeholder_axis()
    {
        Assert.Equal((0.0, 100.0, 25.0), AxisScale.Compute(AxisMode.Auto, null, null));
    }
}
