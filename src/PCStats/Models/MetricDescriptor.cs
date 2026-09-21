namespace PCStats.Models;

/// <summary>Static description of a single measurable value (a hardware sensor or a synthetic aggregate).</summary>
public sealed record MetricDescriptor
{
    /// <summary>Stable identifier, e.g. <c>/amdcpu/0/temperature/2</c>. Persisted in user settings.</summary>
    public required string Id { get; init; }

    public required string HardwareName { get; init; }

    public required HardwareCategory Category { get; init; }

    /// <summary>Raw sensor name as reported by the hardware layer, e.g. <c>GPU Memory Used</c>.</summary>
    public required string SensorName { get; init; }

    public required MetricKind Kind { get; init; }

    /// <summary>Human friendly label shown in the UI unless the user overrides it.</summary>
    public required string DefaultLabel { get; init; }

    /// <summary>
    /// Sensor ids whose sum represents the capacity this metric is measured against
    /// (e.g. VRAM total for VRAM used). Used to draw a proportional bar and a "of X" hint.
    /// </summary>
    public IReadOnlyList<string> TotalSourceIds { get; init; } = [];

    /// <summary>For synthetic metrics: sensor ids folded into this value.</summary>
    public IReadOnlyList<string> AggregateSourceIds { get; init; } = [];

    public Aggregation Aggregation { get; init; } = Aggregation.None;

    /// <summary>Position in the default selection shown on first launch, or -1 if not part of it.</summary>
    public int RecommendedOrder { get; init; } = -1;

    public bool IsSynthetic => Aggregation != Aggregation.None;
}
