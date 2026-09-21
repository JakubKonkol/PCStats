namespace PCStats.Models;

/// <summary>Immutable point-in-time reading of every known metric.</summary>
public sealed class HardwareSnapshot
{
    public required IReadOnlyDictionary<string, float> Values { get; init; }

    public required DateTime Timestamp { get; init; }

    public float? Get(string id) => Values.TryGetValue(id, out var v) ? v : null;

    public static HardwareSnapshot Empty { get; } = new()
    {
        Values = new Dictionary<string, float>(),
        Timestamp = DateTime.MinValue,
    };
}
