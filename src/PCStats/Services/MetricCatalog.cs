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

    /// <summary>Friendly Polish label for a sensor, falling back to "sensor (hardware)".</summary>
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
        (MetricKind.Load, "CPU Core Max") => "CPU (max rdzeń)",
        (MetricKind.Load, _) when s.StartsWith("CPU Core #") => "CPU rdzeń " + s[10..].Replace(" Thread #", " / wątek "),
        (MetricKind.Temperature, "Core (Tctl/Tdie)") => "Temp. CPU",
        (MetricKind.Temperature, "CPU Package") => "Temp. CPU",
        (MetricKind.Temperature, "Core Average") => "Temp. CPU (śr.)",
        (MetricKind.Temperature, "Core Max") => "Temp. CPU (max)",
        (MetricKind.Temperature, _) when s.StartsWith("CCD") => "Temp. " + s.Replace(" (Tdie)", ""),
        (MetricKind.Temperature, _) when s.StartsWith("CPU Core #") => "Temp. rdzeń " + s[10..],
        (MetricKind.Clock, "Bus Speed") => "Magistrala CPU",
        (MetricKind.Clock, _) when s.StartsWith("Core #") => "Takt rdzeń " + s[6..],
        (MetricKind.Clock, _) when s.StartsWith("CPU Core #") => "Takt rdzeń " + s[10..],
        (MetricKind.Power, "Package") or (MetricKind.Power, "CPU Package") => "Pobór CPU",
        (MetricKind.Power, "Core (SMU)") or (MetricKind.Power, "CPU Cores") => "Pobór rdzeni CPU",
        (MetricKind.Power, "CPU Memory") => "Pobór kontrolera RAM",
        (MetricKind.Voltage, "Core (SVI2 TFN)") or (MetricKind.Voltage, "CPU Core") => "Napięcie CPU",
        (MetricKind.Voltage, "SoC (SVI2 TFN)") => "Napięcie SoC",
        (MetricKind.Voltage, _) when s.StartsWith("Core #") => "Napięcie rdzeń " + s[6..],
        (MetricKind.Voltage, _) when s.StartsWith("CPU Core #") => "Napięcie rdzeń " + s[10..],
        (MetricKind.Factor, _) => s + " (CPU)",
        _ => null,
    };

    private static string? GpuLabel(MetricKind kind, string s) => (kind, s) switch
    {
        (MetricKind.Load, "GPU Core") => "GPU",
        (MetricKind.Load, "GPU Memory") => "VRAM %",
        (MetricKind.Load, "GPU Memory Controller") => "Kontroler VRAM",
        (MetricKind.Load, "GPU Video Engine") => "Enkoder wideo",
        (MetricKind.Load, "GPU Video Encoder") => "Enkoder wideo",
        (MetricKind.Load, "GPU Video Decoder") => "Dekoder wideo",
        (MetricKind.Load, "GPU Bus") => "Magistrala GPU",
        (MetricKind.Load, "GPU Power") => "Limit mocy GPU %",
        (MetricKind.Load, "GPU Board Power") => "Limit mocy płytki %",
        (MetricKind.Load, "D3D 3D") => "GPU (D3D)",
        (MetricKind.Load, _) when s.StartsWith("D3D ") => s[4..] + " (D3D)",
        (MetricKind.Temperature, "GPU Core") => "Temp. GPU",
        (MetricKind.Temperature, "GPU Hot Spot") => "Hotspot GPU",
        (MetricKind.Temperature, "GPU Memory Junction") => "Temp. VRAM",
        (MetricKind.Temperature, "GPU Memory") => "Temp. VRAM",
        (MetricKind.Clock, "GPU Core") => "Takt GPU",
        (MetricKind.Clock, "GPU Memory") => "Takt VRAM",
        (MetricKind.Clock, "GPU Shader") => "Takt shaderów",
        (MetricKind.Clock, "GPU Video") => "Takt enkodera",
        (MetricKind.SmallData, "GPU Memory Used") => "VRAM",
        (MetricKind.SmallData, "GPU Memory Total") => "VRAM całkowity",
        (MetricKind.SmallData, "GPU Memory Free") => "VRAM wolny",
        (MetricKind.SmallData, "D3D Dedicated Memory Used") => "VRAM (D3D)",
        (MetricKind.SmallData, "D3D Shared Memory Used") => "Pamięć współdz. GPU",
        (MetricKind.Power, "GPU Package") or (MetricKind.Power, "GPU Power") or (MetricKind.Power, "GPU Core") => "Pobór GPU",
        (MetricKind.Power, "GPU Board") => "Pobór płytki GPU",
        (MetricKind.Power, "GPU Power Limit") => "Limit mocy GPU",
        (MetricKind.Fan, _) => s.Replace("GPU Fan", "Wentylator GPU"),
        (MetricKind.Control, _) => s.Replace("GPU Fan", "Wentylator GPU") + " %",
        (MetricKind.Throughput, "GPU PCIe Rx") => "PCIe odbiór",
        (MetricKind.Throughput, "GPU PCIe Tx") => "PCIe wysyłanie",
        (MetricKind.Voltage, "GPU Core") => "Napięcie GPU",
        _ => null,
    };

    /// <summary>LHM exposes "Total Memory" and "Virtual Memory" (RAM + page file) as separate nodes with identical sensor names.</summary>
    private static bool IsVirtualMemory(string hardware) => hardware.Contains("Virtual", StringComparison.OrdinalIgnoreCase);

    private static string? MemoryLabel(MetricKind kind, string s, string hw)
    {
        var virt = IsVirtualMemory(hw);
        return (kind, s) switch
        {
            (MetricKind.Load, "Memory") => virt ? "Pamięć wirt. %" : "RAM %",
            (MetricKind.Data, "Memory Used") => virt ? "Pamięć wirtualna" : "RAM",
            (MetricKind.Data, "Memory Available") => virt ? "Pamięć wirt. wolna" : "RAM wolny",
            (MetricKind.Load, "Virtual Memory") => "Pamięć wirt. %",
            (MetricKind.Data, "Virtual Memory Used") => "Pamięć wirtualna",
            (MetricKind.Data, "Virtual Memory Available") => "Pamięć wirt. wolna",
            (MetricKind.Temperature, _) => $"Temp. RAM ({ShortName(hw)})",
            _ => null,
        };
    }

    private static string? StorageLabel(MetricKind kind, string s, string hw)
    {
        var name = ShortName(hw);
        return (kind, s) switch
        {
            (MetricKind.Temperature, _) => $"Temp. {name}",
            (MetricKind.Load, "Used Space") => $"Zajętość {name}",
            (MetricKind.Load, "Total Activity") => $"Aktywność {name}",
            (MetricKind.Load, "Read Activity") => $"Odczyt % {name}",
            (MetricKind.Load, "Write Activity") => $"Zapis % {name}",
            (MetricKind.Throughput, "Read Rate") => $"Odczyt {name}",
            (MetricKind.Throughput, "Write Rate") => $"Zapis {name}",
            (MetricKind.Level, "Remaining Life") => $"Żywotność {name}",
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
            (MetricKind.Load, "Network Utilization") => $"Sieć % {name}",
            (MetricKind.Data, "Data Uploaded") => $"Wysłano {name}",
            (MetricKind.Data, "Data Downloaded") => $"Pobrano {name}",
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
            DefaultLabel = "Takt CPU",
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
            DefaultLabel = "Takt CPU (śr.)",
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
