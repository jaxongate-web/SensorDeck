namespace SensorDeck.Core.History;

/// <summary>
/// Fixed-capacity circular buffer of samples. Missing readings are stored as NaN so graphs
/// show a gap instead of a misleading zero. Not thread-safe: use from the UI thread only.
/// </summary>
public sealed class RingBuffer
{
    private double[] _data;
    private int _start;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _data = new double[capacity];
    }

    public int Capacity => _data.Length;
    public int Count { get; private set; }

    /// <summary>Incremented on every change so renderers can skip redundant redraws.</summary>
    public long Version { get; private set; }

    public void Add(double? value)
    {
        double v = value ?? double.NaN;
        if (Count < _data.Length)
        {
            _data[(_start + Count) % _data.Length] = v;
            Count++;
        }
        else
        {
            _data[_start] = v;
            _start = (_start + 1) % _data.Length;
        }
        Version++;
    }

    /// <summary>Sample i, where 0 is the oldest.</summary>
    public double this[int i]
    {
        get
        {
            if ((uint)i >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(i));
            return _data[(_start + i) % _data.Length];
        }
    }

    public double? Latest => Count == 0 ? null : this[Count - 1] is var v && double.IsNaN(v) ? null : v;

    /// <summary>Changes capacity, keeping the most recent samples.</summary>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        if (capacity == _data.Length) return;
        int keep = Math.Min(Count, capacity);
        var next = new double[capacity];
        for (int i = 0; i < keep; i++) next[i] = this[Count - keep + i];
        _data = next;
        _start = 0;
        Count = keep;
        Version++;
    }

    public void Clear()
    {
        _start = 0;
        Count = 0;
        Version++;
    }

    public (double Min, double Max)? Range()
    {
        double min = double.MaxValue, max = double.MinValue;
        bool any = false;
        for (int i = 0; i < Count; i++)
        {
            double v = this[i];
            if (double.IsNaN(v)) continue;
            any = true;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return any ? (min, max) : null;
    }
}
