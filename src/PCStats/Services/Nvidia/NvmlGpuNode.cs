using System.Text;
using PCStats.Models;

namespace PCStats.Services.Nvidia;

/// <summary>
/// One NVIDIA GPU read straight from NVML. Covers the everyday metrics (load, temperature, VRAM,
/// power, clocks, fan) at a fraction of the cost of the LibreHardwareMonitor node, which also
/// walks D3D statistics of every running process on each refresh.
/// </summary>
internal sealed class NvmlGpuNode : IPollNode
{
    private readonly IntPtr _device;
    private readonly Dictionary<string, float> _latest = new();
    private readonly string _prefix;

    private NvmlGpuNode(uint index, IntPtr device, string name)
    {
        _device = device;
        _prefix = $"nvml/{index}";
        Id = _prefix;
        Name = name;

        var hardwareName = $"{name} (NVML)";
        Metrics =
        [
            Describe("load/core", MetricKind.Load, "GPU Core", hardwareName),
            Describe("load/vram", MetricKind.Load, "GPU Memory", hardwareName),
            Describe("load/memctl", MetricKind.Load, "GPU Memory Controller", hardwareName),
            Describe("load/encoder", MetricKind.Load, "GPU Video Encoder", hardwareName),
            Describe("load/decoder", MetricKind.Load, "GPU Video Decoder", hardwareName),
            Describe("temperature/core", MetricKind.Temperature, "GPU Core", hardwareName),
            Describe("smalldata/used", MetricKind.SmallData, "GPU Memory Used", hardwareName, totals: [$"{_prefix}/smalldata/total"]),
            Describe("smalldata/free", MetricKind.SmallData, "GPU Memory Free", hardwareName),
            Describe("smalldata/total", MetricKind.SmallData, "GPU Memory Total", hardwareName),
            Describe("power/usage", MetricKind.Power, "GPU Package", hardwareName, totals: [$"{_prefix}/power/limit"]),
            Describe("power/limit", MetricKind.Power, "GPU Power Limit", hardwareName),
            Describe("clock/core", MetricKind.Clock, "GPU Core", hardwareName),
            Describe("clock/memory", MetricKind.Clock, "GPU Memory", hardwareName),
            Describe("clock/video", MetricKind.Clock, "GPU Video", hardwareName),
            Describe("control/fan", MetricKind.Control, "GPU Fan", hardwareName),
        ];
        SensorIds = Metrics.Select(m => m.Id).ToList();
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<MetricDescriptor> Metrics { get; }

    public IReadOnlyCollection<string> SensorIds { get; }

    /// <summary>Enumerates NVIDIA GPUs; returns an empty list when NVML is unavailable (no NVIDIA driver).</summary>
    public static IReadOnlyList<NvmlGpuNode> Discover()
    {
        try
        {
            if (Nvml.Init() != Nvml.Success || Nvml.DeviceGetCount(out var count) != Nvml.Success)
                return [];

            var nodes = new List<NvmlGpuNode>();
            for (uint i = 0; i < count; i++)
            {
                if (Nvml.DeviceGetHandleByIndex(i, out var device) != Nvml.Success)
                    continue;

                var name = new StringBuilder(96);
                var label = Nvml.DeviceGetName(device, name, (uint)name.Capacity) == Nvml.Success ? name.ToString() : $"NVIDIA GPU {i}";
                nodes.Add(new NvmlGpuNode(i, device, label));
            }

            Logger.Info($"NVML: {nodes.Count} GPU(s)");
            return nodes;
        }
        catch (DllNotFoundException)
        {
            return [];
        }
        catch (Exception ex)
        {
            Logger.Warn("NVML initialisation failed", ex);
            return [];
        }
    }

    public static void ShutdownLibrary()
    {
        try
        {
            Nvml.Shutdown();
        }
        catch (DllNotFoundException)
        {
            // never initialised
        }
    }

    public void Update()
    {
        _latest.Clear();

        if (Nvml.DeviceGetUtilizationRates(_device, out var util) == Nvml.Success)
        {
            _latest[$"{_prefix}/load/core"] = util.Gpu;
            _latest[$"{_prefix}/load/memctl"] = util.Memory;
        }

        if (Nvml.DeviceGetTemperature(_device, Nvml.TemperatureSensor.Gpu, out var celsius) == Nvml.Success)
            _latest[$"{_prefix}/temperature/core"] = celsius;

        if (Nvml.DeviceGetMemoryInfo(_device, out var mem) == Nvml.Success && mem.Total > 0)
        {
            const float mib = 1024f * 1024f;
            _latest[$"{_prefix}/smalldata/used"] = mem.Used / mib;
            _latest[$"{_prefix}/smalldata/free"] = mem.Free / mib;
            _latest[$"{_prefix}/smalldata/total"] = mem.Total / mib;
            _latest[$"{_prefix}/load/vram"] = 100f * mem.Used / mem.Total;
        }

        if (Nvml.DeviceGetPowerUsage(_device, out var mw) == Nvml.Success)
            _latest[$"{_prefix}/power/usage"] = mw / 1000f;

        if (Nvml.DeviceGetEnforcedPowerLimit(_device, out var limit) == Nvml.Success)
            _latest[$"{_prefix}/power/limit"] = limit / 1000f;

        if (Nvml.DeviceGetClockInfo(_device, Nvml.ClockType.Graphics, out var core) == Nvml.Success)
            _latest[$"{_prefix}/clock/core"] = core;

        if (Nvml.DeviceGetClockInfo(_device, Nvml.ClockType.Memory, out var memClock) == Nvml.Success)
            _latest[$"{_prefix}/clock/memory"] = memClock;

        if (Nvml.DeviceGetClockInfo(_device, Nvml.ClockType.Video, out var video) == Nvml.Success)
            _latest[$"{_prefix}/clock/video"] = video;

        if (Nvml.DeviceGetFanSpeed(_device, out var fan) == Nvml.Success)
            _latest[$"{_prefix}/control/fan"] = fan;

        if (Nvml.DeviceGetEncoderUtilization(_device, out var enc, out _) == Nvml.Success)
            _latest[$"{_prefix}/load/encoder"] = enc;

        if (Nvml.DeviceGetDecoderUtilization(_device, out var dec, out _) == Nvml.Success)
            _latest[$"{_prefix}/load/decoder"] = dec;
    }

    public void Collect(Dictionary<string, float> values)
    {
        foreach (var (id, value) in _latest)
            values[id] = value;
    }

    private MetricDescriptor Describe(string suffix, MetricKind kind, string sensorName, string hardwareName, IReadOnlyList<string>? totals = null) =>
        new()
        {
            Id = $"{_prefix}/{suffix}",
            HardwareName = hardwareName,
            Category = HardwareCategory.Gpu,
            SensorName = sensorName,
            Kind = kind,
            DefaultLabel = MetricCatalog.LabelFor(HardwareCategory.Gpu, kind, sensorName, hardwareName),
            TotalSourceIds = totals ?? [],
        };
}
