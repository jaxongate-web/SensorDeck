namespace SensorDeck.Core.Models;

/// <summary>
/// A single reading taken from a real hardware sensor. When the hardware, driver or
/// permissions don't expose the reading, <see cref="Value"/> is null and
/// <see cref="Reason"/> explains why. A missing reading is never reported as zero.
/// </summary>
public sealed record SensorValue(double? Value, string? Reason = null)
{
    public bool IsSupported => Value is not null;

    public static SensorValue Of(double value) => new(value);

    public static SensorValue Unsupported(string reason) => new(null, reason);

    /// <summary>Wraps a raw LHM value, treating null/NaN/Infinity as unsupported.</summary>
    public static SensorValue From(float? raw, string reasonIfMissing) =>
        raw is float f && float.IsFinite(f) ? new SensorValue(f) : new SensorValue(null, reasonIfMissing);

    public override string ToString() => Value?.ToString("0.##") ?? $"Not supported ({Reason})";
}
