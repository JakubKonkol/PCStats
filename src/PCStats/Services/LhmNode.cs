using LibreHardwareMonitor.Hardware;

namespace PCStats.Services;

/// <summary>Wraps one LibreHardwareMonitor hardware node (sub-hardware is flattened into its own node).</summary>
internal sealed class LhmNode : IPollNode
{
    private readonly IHardware _hardware;
    private readonly bool _cpuDriverAvailable;

    public LhmNode(IHardware hardware, bool cpuDriverAvailable)
    {
        _hardware = hardware;
        _cpuDriverAvailable = cpuDriverAvailable;
        Id = hardware.Identifier.ToString();
        SensorIds = hardware.Sensors.Select(s => s.Identifier.ToString()).ToHashSet();
    }

    public string Id { get; }

    public string Name => _hardware.Name;

    public IReadOnlyCollection<string> SensorIds { get; }

    public void Update() => _hardware.Update();

    public void Collect(Dictionary<string, float> values)
    {
        foreach (var sensor in _hardware.Sensors)
        {
            if (IsUnreadableWithoutDriver(sensor))
                continue; // LHM reports 0 instead of null here; "—" is more honest than "0 °C"

            if (sensor.Value is { } v && !float.IsNaN(v))
                values[sensor.Identifier.ToString()] = v;
        }
    }

    private bool IsUnreadableWithoutDriver(ISensor sensor) =>
        !_cpuDriverAvailable
        && _hardware.HardwareType == HardwareType.Cpu
        && sensor.SensorType is SensorType.Temperature or SensorType.Clock or SensorType.Voltage or SensorType.Power or SensorType.Factor;
}
