using PCStats.Models;

namespace PCStats.Services;

/// <summary>Turns raw sensor values into display strings with sensible units and precision.</summary>
public static class MetricFormatter
{
    public static (string Value, string Unit) Format(MetricKind kind, float v) => kind switch
    {
        MetricKind.Load or MetricKind.Control or MetricKind.Level or MetricKind.Humidity => (v.ToString("0"), "%"),
        MetricKind.Temperature => (v.ToString("0"), "°C"),
        MetricKind.Clock => v >= 1000 ? ((v / 1000f).ToString("0.00"), "GHz") : (v.ToString("0"), "MHz"),
        MetricKind.Power => (v < 10 ? v.ToString("0.0") : v.ToString("0"), "W"),
        MetricKind.Voltage => (v.ToString("0.000"), "V"),
        MetricKind.Current => (v.ToString("0.00"), "A"),
        MetricKind.Fan => (v.ToString("0"), "RPM"),
        MetricKind.Data => (v.ToString("0.0"), "GB"),
        MetricKind.SmallData => v >= 1024 ? ((v / 1024f).ToString("0.0"), "GB") : (v.ToString("0"), "MB"),
        MetricKind.Throughput => FormatThroughput(v),
        MetricKind.Frequency => (v.ToString("0"), "Hz"),
        MetricKind.Factor => (v.ToString("0.00"), "×"),
        MetricKind.Energy => (v.ToString("0"), "mWh"),
        MetricKind.Noise => (v.ToString("0"), "dBA"),
        MetricKind.TimeSpan => (v.ToString("0"), "s"),
        MetricKind.Flow => (v.ToString("0.0"), "L/h"),
        MetricKind.Conductivity => (v.ToString("0"), "µS/cm"),
        _ => (v.ToString("0.##"), string.Empty),
    };

    /// <summary>Single string, e.g. "4.65 GHz".</summary>
    public static string FormatCompact(MetricKind kind, float v)
    {
        var (value, unit) = Format(kind, v);
        return string.IsNullOrEmpty(unit) ? value : $"{value} {unit}";
    }

    /// <summary>Upper bound of the natural range for kinds that have one (percentages, temperatures).</summary>
    public static float? FixedMaximum(MetricKind kind) => kind switch
    {
        MetricKind.Load or MetricKind.Control or MetricKind.Level or MetricKind.Humidity => 100f,
        MetricKind.Temperature => 100f,
        _ => null,
    };

    private static (string, string) FormatThroughput(float bytesPerSecond)
    {
        const float kb = 1024f, mb = kb * 1024f, gb = mb * 1024f;
        return bytesPerSecond switch
        {
            >= gb => ((bytesPerSecond / gb).ToString("0.00"), "GB/s"),
            >= mb => ((bytesPerSecond / mb).ToString("0.0"), "MB/s"),
            >= kb => ((bytesPerSecond / kb).ToString("0"), "KB/s"),
            _ => (bytesPerSecond.ToString("0"), "B/s"),
        };
    }
}
