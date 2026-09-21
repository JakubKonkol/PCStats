namespace PCStats.Models;

/// <summary>Physical quantity a metric represents. Mirrors the sensor types exposed by the hardware layer.</summary>
public enum MetricKind
{
    Voltage,
    Current,
    Power,
    Clock,
    Temperature,
    Load,
    Frequency,
    Fan,
    Flow,
    Control,
    Level,
    Factor,
    Data,
    SmallData,
    Throughput,
    TimeSpan,
    Energy,
    Noise,
    Conductivity,
    Humidity,
    Other,
}

public enum HardwareCategory
{
    Cpu,
    Gpu,
    Memory,
    Storage,
    Network,
    Motherboard,
    Other,
}

/// <summary>How a synthetic metric combines its source sensors.</summary>
public enum Aggregation
{
    None,
    Max,
    Average,
    Sum,
}

public enum LayoutMode
{
    Tiles,
    Compact,
}

public enum Severity
{
    Normal,
    Warning,
    Critical,
}
