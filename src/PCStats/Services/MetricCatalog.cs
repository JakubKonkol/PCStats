using LibreHardwareMonitor.Hardware;
using PCStats.Models;

namespace PCStats.Services;

/// <summary>
/// Translates the raw sensor tree into <see cref="MetricDescriptor"/>s: friendly labels,
/// capacity pairings (used vs total), synthetic aggregates and the recommended default set.
/// </summary>
public static class MetricCatalog
{
    /// <param name="roots">LibreHardwareMonitor hardware tree.</param>
    /// <param name="external">Descriptors from other sources (e.g. NVML); listed first so they win default slots.</param>
    public static IReadOnlyList<MetricDescriptor> Build(IEnumerable<IHardware> roots, IEnumerable<MetricDescriptor> external)
    {
        var list = new List<MetricDescriptor>(external);
        var seen = list.Select(m => m.Id).ToHashSet();
        foreach (var hw in roots)
            Visit(hw, list, seen);

        AddSyntheticMetrics(list);
        return AssignRecommended(list);
    }

    /// <summary>Windows exposes every filter driver binding as its own NIC; none of them carry useful numbers.</summary>
    private static readonly string[] NoiseAdapterMarkers =
    [
        "-WFP ", "-QoS Packet Scheduler", "Virtual Switch Extension Filter", "Kernel Debugger", "debuger jądra",
    ];

    private static bool IsNoiseAdapter(IHardware hw) =>
        hw.HardwareType == HardwareType.Network
        && NoiseAdapterMarkers.Any(m => hw.Name.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static void Visit(IHardware hw, List<MetricDescriptor> list, HashSet<string> seen)
    {
        if (IsNoiseAdapter(hw))
            return;

        var category = Map(hw.HardwareType);
        foreach (var sensor in hw.Sensors)
        {
            var id = sensor.Identifier.ToString();
            if (!seen.Add(id))
            {
                // LHM occasionally registers the same identifier twice (seen on NVIDIA load sensors); keep the first.
                Logger.Warn($"Duplicate sensor identifier skipped: {id} ({hw.Name} / {sensor.Name})");
                continue;
            }

            var kind = Map(sensor.SensorType);
            list.Add(new MetricDescriptor
            {
                Id = id,
                HardwareName = hw.Name,
                Category = category,
                SensorName = sensor.Name,
                Kind = kind,
                DefaultLabel = LabelFor(category, kind, sensor.Name, hw.Name),
                TotalSourceIds = TotalSourcesFor(hw, sensor, category, kind),
            });
        }

        foreach (var sub in hw.SubHardware)
            Visit(sub, list, seen);
    }

    // ---------------------------------------------------------------- labels

    /// <summary>Friendly English label for a sensor, falling back to "sensor (hardware)".</summary>
    public static string LabelFor(HardwareCategory category, MetricKind kind, string sensor, string hardware)
    {
        var known = category switch
        {
            HardwareCategory.Cpu => CpuLabel(kind, sensor),
            HardwareCategory.Gpu => GpuLabel(kind, sensor),
            HardwareCategory.Memory => MemoryLabel(kind, sensor, hardware),
            HardwareCategory.Storage => StorageLabel(kind, sensor, hardware),
            HardwareCategory.Network => NetworkLabel(kind, sensor, hardware),
            _ => null,
        };
        return known ?? $"{sensor} ({ShortName(hardware)})";
    }

    private static string? CpuLabel(MetricKind kind, string s) => (kind, s) switch
    {
        (MetricKind.Load, "CPU Total") => "CPU",
        (MetricKind.Load, "CPU Core Max") => "CPU (max core)",
        (MetricKind.Load, _) when s.StartsWith("CPU Core #") => "CPU core " + s[10..].Replace(" Thread #", " / thread "),
        (MetricKind.Temperature, "Core (Tctl/Tdie)") => "CPU temp",
        (MetricKind.Temperature, "CPU Package") => "CPU temp",
        (MetricKind.Temperature, "Core Average") => "CPU temp (avg)",
        (MetricKind.Temperature, "Core Max") => "CPU temp (max)",
        (MetricKind.Temperature, _) when s.StartsWith("CCD") => s.Replace(" (Tdie)", "") + " temp",
        (MetricKind.Temperature, _) when s.StartsWith("CPU Core #") => "Core " + s[10..] + " temp",
        (MetricKind.Clock, "Bus Speed") => "CPU bus",
        (MetricKind.Clock, _) when s.StartsWith("Core #") => "Core " + s[6..] + " clock",
        (MetricKind.Clock, _) when s.StartsWith("CPU Core #") => "Core " + s[10..] + " clock",
        (MetricKind.Power, "Package") or (MetricKind.Power, "CPU Package") => "CPU power",
        (MetricKind.Power, "Core (SMU)") or (MetricKind.Power, "CPU Cores") => "CPU cores power",
        (MetricKind.Power, "CPU Memory") => "Memory controller power",
        (MetricKind.Voltage, "Core (SVI2 TFN)") or (MetricKind.Voltage, "CPU Core") => "CPU voltage",
        (MetricKind.Voltage, "SoC (SVI2 TFN)") => "SoC voltage",
        (MetricKind.Voltage, _) when s.StartsWith("Core #") => "Core " + s[6..] + " voltage",
        (MetricKind.Voltage, _) when s.StartsWith("CPU Core #") => "Core " + s[10..] + " voltage",
        (MetricKind.Factor, _) => s + " (CPU)",
        _ => null,
    };

    private static string? GpuLabel(MetricKind kind, string s) => (kind, s) switch
    {
        (MetricKind.Load, "GPU Core") => "GPU",
        (MetricKind.Load, "GPU Memory") => "VRAM %",
        (MetricKind.Load, "GPU Memory Controller") => "VRAM controller",
        (MetricKind.Load, "GPU Video Engine") => "Video encoder",
        (MetricKind.Load, "GPU Video Encoder") => "Video encoder",
        (MetricKind.Load, "GPU Video Decoder") => "Video decoder",
        (MetricKind.Load, "GPU Bus") => "GPU bus",
        (MetricKind.Load, "GPU Power") => "GPU power limit %",
        (MetricKind.Load, "GPU Board Power") => "Board power limit %",
        (MetricKind.Load, "D3D 3D") => "GPU (D3D)",
        (MetricKind.Load, _) when s.StartsWith("D3D ") => s[4..] + " (D3D)",
        (MetricKind.Temperature, "GPU Core") => "GPU temp",
        (MetricKind.Temperature, "GPU Hot Spot") => "GPU hotspot",
        (MetricKind.Temperature, "GPU Memory Junction") => "VRAM temp",
        (MetricKind.Temperature, "GPU Memory") => "VRAM temp",
        (MetricKind.Clock, "GPU Core") => "GPU clock",
        (MetricKind.Clock, "GPU Memory") => "VRAM clock",
        (MetricKind.Clock, "GPU Shader") => "Shader clock",
        (MetricKind.Clock, "GPU Video") => "Video clock",
        (MetricKind.SmallData, "GPU Memory Used") => "VRAM",
        (MetricKind.SmallData, "GPU Memory Total") => "VRAM total",
        (MetricKind.SmallData, "GPU Memory Free") => "VRAM free",
        (MetricKind.SmallData, "D3D Dedicated Memory Used") => "VRAM (D3D)",
        (MetricKind.SmallData, "D3D Shared Memory Used") => "GPU shared memory",
        (MetricKind.Power, "GPU Package") or (MetricKind.Power, "GPU Power") or (MetricKind.Power, "GPU Core") => "GPU power",
        (MetricKind.Power, "GPU Board") => "GPU board power",
        (MetricKind.Power, "GPU Power Limit") => "GPU power limit",
        (MetricKind.Fan, _) => s,
        (MetricKind.Control, _) => s + " %",
        (MetricKind.Throughput, "GPU PCIe Rx") => "PCIe receive",
        (MetricKind.Throughput, "GPU PCIe Tx") => "PCIe transmit",
        (MetricKind.Voltage, "GPU Core") => "GPU voltage",
        _ => null,
    };

    /// <summary>LHM exposes "Total Memory" and "Virtual Memory" (RAM + page file) as separate nodes with identical sensor names.</summary>
    private static bool IsVirtualMemory(string hardware) => hardware.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    private static string? MemoryLabel(MetricKind kind, string s, string hw)
    {
        var virt = IsVirtualMemory(hw);
        return (kind, s) switch
        {
            (MetricKind.Load, "Memory") => virt ? "Virtual memory %" : "RAM %",
            (MetricKind.Data, "Memory Used") => virt ? "Virtual memory" : "RAM",
            (MetricKind.Data, "Memory Available") => virt ? "Virtual memory free" : "RAM free",
            (MetricKind.Load, "Virtual Memory") => "Virtual memory %",
            (MetricKind.Data, "Virtual Memory Used") => "Virtual memory",
            (MetricKind.Data, "Virtual Memory Available") => "Virtual memory free",
            (MetricKind.Temperature, _) => $"RAM temp ({ShortName(hw)})",
            _ => null,
        };
    }

    private static string? StorageLabel(MetricKind kind, string s, string hw)
    {
        var name = ShortName(hw);
        return (kind, s) switch
        {
            (MetricKind.Temperature, _) => $"Temp {name}",
            (MetricKind.Load, "Used Space") => $"Used {name}",
            (MetricKind.Load, "Total Activity") => $"Activity {name}",
            (MetricKind.Load, "Read Activity") => $"Read % {name}",
            (MetricKind.Load, "Write Activity") => $"Write % {name}",
            (MetricKind.Throughput, "Read Rate") => $"Read {name}",
            (MetricKind.Throughput, "Write Rate") => $"Write {name}",
            (MetricKind.Level, "Remaining Life") => $"Life left {name}",
            _ => null,
        };
    }

    private static string? NetworkLabel(MetricKind kind, string s, string hw)
    {
        var name = ShortName(hw);
        return (kind, s) switch
        {
            (MetricKind.Throughput, "Upload Speed") => $"Upload {name}",
            (MetricKind.Throughput, "Download Speed") => $"Download {name}",
            (MetricKind.Load, "Network Utilization") => $"Network % {name}",
            (MetricKind.Data, "Data Uploaded") => $"Uploaded {name}",
            (MetricKind.Data, "Data Downloaded") => $"Downloaded {name}",
            _ => null,
        };
    }

    /// <summary>Trims vendor noise so labels fit a tile ("Samsung SSD 980 PRO 1TB" → "980 PRO 1TB").</summary>
    private static string ShortName(string hardware)
    {
        var name = hardware.Replace("(NVML)", "").Trim();
        foreach (var prefix in new[]
                 {
                     "NVIDIA GeForce ", "NVIDIA ", "AMD Radeon ", "AMD ", "Intel(R) ", "Intel ",
                     "Samsung SSD ", "Samsung ", "Kingston ", "WD ", "WDC ", "Seagate ", "Crucial ",
                 })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        return name.Length > 18 ? name[..18].TrimEnd() + "…" : name;
    }

    // ------------------------------------------------------------- pairing

    private static IReadOnlyList<string> TotalSourcesFor(IHardware hw, ISensor sensor, HardwareCategory category, MetricKind kind)
    {
        string? Sibling(SensorType type, string name) =>
            hw.Sensors.FirstOrDefault(x => x.SensorType == type && x.Name == name)?.Identifier.ToString();

        switch (category, kind, sensor.Name)
        {
            case (HardwareCategory.Gpu, MetricKind.SmallData, "GPU Memory Used"):
            case (HardwareCategory.Gpu, MetricKind.SmallData, "D3D Dedicated Memory Used"):
            {
                var total = Sibling(SensorType.SmallData, "GPU Memory Total");
                return total is null ? [] : [total];
            }
            case (HardwareCategory.Memory, MetricKind.Data, "Memory Used"):
            {
                var available = Sibling(SensorType.Data, "Memory Available");
                return available is null ? [] : [sensor.Identifier.ToString(), available];
            }
            case (HardwareCategory.Memory, MetricKind.Data, "Virtual Memory Used"):
            {
                var available = Sibling(SensorType.Data, "Virtual Memory Available");
                return available is null ? [] : [sensor.Identifier.ToString(), available];
            }
            default:
                return [];
        }
    }

    // ----------------------------------------------------------- synthetic

    private static void AddSyntheticMetrics(List<MetricDescriptor> list)
    {
        var cpuCoreClocks = list
            .Where(m => m.Category == HardwareCategory.Cpu && m.Kind == MetricKind.Clock && m.SensorName.Contains("Core"))
            .Select(m => m.Id)
            .ToList();

        if (cpuCoreClocks.Count == 0)
            return;

        var cpuName = list.First(m => m.Category == HardwareCategory.Cpu).HardwareName;
        list.Add(new MetricDescriptor
        {
            Id = "synthetic/cpu/clock/max",
            HardwareName = cpuName,
            Category = HardwareCategory.Cpu,
            SensorName = "Core clock (max)",
            Kind = MetricKind.Clock,
            DefaultLabel = "CPU clock",
            AggregateSourceIds = cpuCoreClocks,
            Aggregation = Aggregation.Max,
        });
        list.Add(new MetricDescriptor
        {
            Id = "synthetic/cpu/clock/avg",
            HardwareName = cpuName,
            Category = HardwareCategory.Cpu,
            SensorName = "Core clock (average)",
            Kind = MetricKind.Clock,
            DefaultLabel = "CPU clock (avg)",
            AggregateSourceIds = cpuCoreClocks,
            Aggregation = Aggregation.Average,
        });
    }

    // --------------------------------------------------------- recommended

    private static IReadOnlyList<MetricDescriptor> AssignRecommended(List<MetricDescriptor> list)
    {
        // Discrete GPUs first so an iGPU never wins the default slots.
        var gpuName = list
            .Where(m => m.Category == HardwareCategory.Gpu)
            .Select(m => m.HardwareName)
            .Distinct()
            .OrderBy(n => n.Contains("(NVML)", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(n => n.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();

        MetricDescriptor? Find(HardwareCategory cat, MetricKind kind, params string[] names)
        {
            var candidates = list
                .Where(m => m.Category == cat && m.Kind == kind)
                .Where(m => cat != HardwareCategory.Gpu || m.HardwareName == gpuName)
                .Where(m => cat != HardwareCategory.Memory || !IsVirtualMemory(m.HardwareName))
                .ToList();
            foreach (var n in names)
            {
                var hit = candidates.FirstOrDefault(m => m.SensorName == n);
                if (hit is not null)
                    return hit;
            }

            return null;
        }

        var picks = new[]
        {
            Find(HardwareCategory.Cpu, MetricKind.Load, "CPU Total"),
            list.FirstOrDefault(m => m.Id == "synthetic/cpu/clock/max"),
            Find(HardwareCategory.Cpu, MetricKind.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core Average", "Core Max")
                ?? list.FirstOrDefault(m => m.Category == HardwareCategory.Cpu && m.Kind == MetricKind.Temperature),
            Find(HardwareCategory.Memory, MetricKind.Data, "Memory Used"),
            Find(HardwareCategory.Gpu, MetricKind.Load, "GPU Core", "D3D 3D"),
            Find(HardwareCategory.Gpu, MetricKind.Temperature, "GPU Core"),
            Find(HardwareCategory.Gpu, MetricKind.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"),
            Find(HardwareCategory.Gpu, MetricKind.Power, "GPU Package", "GPU Power", "GPU Core"),
        };

        var order = 0;
        var recommended = picks.OfType<MetricDescriptor>().Distinct().ToDictionary(m => m.Id, _ => order++);
        return list
            .Select(m => recommended.TryGetValue(m.Id, out var o) ? m with { RecommendedOrder = o } : m)
            .ToList();
    }

    // ------------------------------------------------------------- mapping

    private static HardwareCategory Map(HardwareType type) => type switch
    {
        HardwareType.Cpu => HardwareCategory.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareCategory.Gpu,
        HardwareType.Memory => HardwareCategory.Memory,
        HardwareType.Storage => HardwareCategory.Storage,
        HardwareType.Network => HardwareCategory.Network,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareCategory.Motherboard,
        _ => HardwareCategory.Other,
    };

    private static MetricKind Map(SensorType type) => type switch
    {
        SensorType.Voltage => MetricKind.Voltage,
        SensorType.Current => MetricKind.Current,
        SensorType.Power => MetricKind.Power,
        SensorType.Clock => MetricKind.Clock,
        SensorType.Temperature => MetricKind.Temperature,
        SensorType.Load => MetricKind.Load,
        SensorType.Frequency => MetricKind.Frequency,
        SensorType.Fan => MetricKind.Fan,
        SensorType.Flow => MetricKind.Flow,
        SensorType.Control => MetricKind.Control,
        SensorType.Level => MetricKind.Level,
        SensorType.Factor => MetricKind.Factor,
        SensorType.Data => MetricKind.Data,
        SensorType.SmallData => MetricKind.SmallData,
        SensorType.Throughput => MetricKind.Throughput,
        SensorType.TimeSpan => MetricKind.TimeSpan,
        SensorType.Energy => MetricKind.Energy,
        SensorType.Noise => MetricKind.Noise,
        SensorType.Conductivity => MetricKind.Conductivity,
        SensorType.Humidity => MetricKind.Humidity,
        _ => MetricKind.Other,
    };
}
