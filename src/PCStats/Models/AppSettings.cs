namespace PCStats.Models;

/// <summary>User preferences persisted as JSON. Keep every property serialisable and defaulted.</summary>
public sealed class AppSettings
{
    /// <summary>Ordered list of metrics shown in the widget.</summary>
    public List<MetricSelection> Metrics { get; set; } = [];

    // --- Appearance ---
    public LayoutMode Layout { get; set; } = LayoutMode.Tiles;
    public int Columns { get; set; } = 2;
    public double Scale { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.88;
    public string AccentColor { get; set; } = "#3B82F6";
    public bool ShowHeader { get; set; } = true;
    public bool ShowSparklines { get; set; } = true;
    public bool ShowBars { get; set; } = true;

    // --- Behaviour ---
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickThrough { get; set; }
    public int UpdateIntervalMs { get; set; } = 1000;
    public int TemperatureWarning { get; set; } = 80;
    public int TemperatureCritical { get; set; } = 90;

    // --- Window state ---
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Metrics = Metrics.Select(m => m.Clone()).ToList();
        return copy;
    }
}

public sealed class MetricSelection
{
    public required string Id { get; set; }

    /// <summary>User supplied label; null means "use the default label".</summary>
    public string? CustomLabel { get; set; }

    public MetricSelection Clone() => new() { Id = Id, CustomLabel = CustomLabel };
}
