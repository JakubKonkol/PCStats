using CommunityToolkit.Mvvm.ComponentModel;
using PCStats.Models;
using PCStats.Services;

namespace PCStats.ViewModels;

/// <summary>One metric on the widget: formatted value, proportional bar, severity and history.</summary>
public sealed partial class MetricTileViewModel : ObservableObject
{
    public const int HistoryLength = 90;

    private readonly double[] _history = new double[HistoryLength];
    private int _historyCount;
    private int _historyHead;
    private readonly float? _fixedMaximum;

    public MetricTileViewModel(MetricDescriptor descriptor, string? customLabel)
    {
        Descriptor = descriptor;
        Label = string.IsNullOrWhiteSpace(customLabel) ? descriptor.DefaultLabel : customLabel.Trim();
        _fixedMaximum = MetricFormatter.FixedMaximum(descriptor.Kind);
        SparklineMaximum = _fixedMaximum ?? double.NaN;
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Label { get; }

    [ObservableProperty]
    public partial string ValueText { get; private set; } = "—";

    [ObservableProperty]
    public partial string UnitText { get; private set; } = string.Empty;

    /// <summary>Compact "value unit" form for the list layout.</summary>
    [ObservableProperty]
    public partial string CompactText { get; private set; } = "—";

    /// <summary>Capacity hint such as "z 8,0 GB"; empty when the metric has no total.</summary>
    [ObservableProperty]
    public partial string SecondaryText { get; private set; } = string.Empty;

    /// <summary>0–1 fill ratio for the bar.</summary>
    [ObservableProperty]
    public partial double BarRatio { get; private set; }

    /// <summary>False when the metric has no meaningful maximum (clocks, power, fans…).</summary>
    [ObservableProperty]
    public partial bool HasBar { get; private set; }

    [ObservableProperty]
    public partial Severity Severity { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<double> History { get; private set; } = [];

    /// <summary>NaN lets the sparkline auto-scale.</summary>
    public double SparklineMaximum { get; }

    public void Apply(HardwareSnapshot snapshot, AppSettings settings)
    {
        var value = snapshot.Get(Id);
        if (value is null)
        {
            ValueText = "—";
            UnitText = string.Empty;
            CompactText = "—";
            SecondaryText = string.Empty;
            BarRatio = 0;
            HasBar = false;
            Severity = Severity.Normal;
            return;
        }

        var v = value.Value;
        var (text, unit) = MetricFormatter.Format(Descriptor.Kind, v);
        ValueText = text;
        UnitText = unit;
        CompactText = MetricFormatter.FormatCompact(Descriptor.Kind, v);

        var total = ResolveTotal(snapshot);
        var max = _fixedMaximum ?? total;
        HasBar = settings.ShowBars && max is > 0;
        BarRatio = max is > 0 ? Math.Clamp(v / max.Value, 0, 1) : 0;
        SecondaryText = total is > 0 ? "of " + MetricFormatter.FormatCompact(Descriptor.Kind, total.Value) : string.Empty;
        Severity = Evaluate(v, total, settings);

        Push(v);
    }

    private float? ResolveTotal(HardwareSnapshot snapshot)
    {
        if (Descriptor.TotalSourceIds.Count == 0)
            return null;

        float sum = 0;
        foreach (var id in Descriptor.TotalSourceIds)
        {
            var part = snapshot.Get(id);
            if (part is null)
                return null;
            sum += part.Value;
        }

        return sum;
    }

    private Severity Evaluate(float v, float? total, AppSettings settings)
    {
        if (Descriptor.Kind == MetricKind.Temperature)
        {
            if (v >= settings.TemperatureCritical) return Severity.Critical;
            if (v >= settings.TemperatureWarning) return Severity.Warning;
            return Severity.Normal;
        }

        // Memory pools: highlight when nearly exhausted.
        if (total is > 0 && Descriptor.Kind is MetricKind.Data or MetricKind.SmallData)
        {
            var ratio = v / total.Value;
            if (ratio >= 0.97f) return Severity.Critical;
            if (ratio >= 0.90f) return Severity.Warning;
        }

        return Severity.Normal;
    }

    private void Push(double v)
    {
        _history[_historyHead] = v;
        _historyHead = (_historyHead + 1) % HistoryLength;
        if (_historyCount < HistoryLength)
            _historyCount++;

        // Materialise in chronological order; 90 doubles per tick is negligible.
        var ordered = new double[_historyCount];
        var start = (_historyHead - _historyCount + HistoryLength) % HistoryLength;
        for (var i = 0; i < _historyCount; i++)
            ordered[i] = _history[(start + i) % HistoryLength];
        History = ordered;
    }
}
