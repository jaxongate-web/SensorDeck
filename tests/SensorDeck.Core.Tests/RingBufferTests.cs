using SensorDeck.Core.History;

namespace SensorDeck.Core.Tests;

public class RingBufferTests
{
    [Fact]
    public void Keeps_most_recent_samples_in_order()
    {
        var rb = new RingBuffer(3);
        for (int i = 1; i <= 5; i++) rb.Add(i);
        Assert.Equal(3, rb.Count);
        Assert.Equal([3.0, 4.0, 5.0], [rb[0], rb[1], rb[2]]);
        Assert.Equal(5, rb.Latest);
    }

    [Fact]
    public void Missing_values_are_gaps_not_zero()
    {
        var rb = new RingBuffer(4);
        rb.Add(10);
        rb.Add(null);
        Assert.True(double.IsNaN(rb[1]));
        Assert.Null(rb.Latest);
        Assert.Equal((10.0, 10.0), rb.Range());
    }

    [Fact]
    public void Resize_keeps_newest()
    {
        var rb = new RingBuffer(5);
        for (int i = 1; i <= 5; i++) rb.Add(i);
        rb.Resize(2);
        Assert.Equal([4.0, 5.0], [rb[0], rb[1]]);
        rb.Resize(4);
        rb.Add(6);
        Assert.Equal([4.0, 5.0, 6.0], [rb[0], rb[1], rb[2]]);
    }
}
