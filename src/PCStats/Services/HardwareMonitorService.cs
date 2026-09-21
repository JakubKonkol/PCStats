using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using PCStats.Models;
using PCStats.Services.Nvidia;

namespace PCStats.Services;

/// <summary>
/// Polls hardware on a dedicated background thread. All access to the LHM object graph and NVML
/// happens on that single thread; consumers only ever see immutable snapshots.
/// </summary>
public sealed class HardwareMonitorService : IHardwareMonitor
{
    private const int CostReportEveryCycles = 60;

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
    };

    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IPollNode> _nodes = [];
    private readonly Dictionary<string, double> _updateCostMs = new();
    private Thread? _thread;
    private volatile int _intervalMs = 1000;
    private volatile HashSet<string>? _activeNodeIds;
    private IReadOnlyList<MetricDescriptor> _synthetic = [];
    private int _cycles;

    public IReadOnlyList<MetricDescriptor> Metrics { get; private set; } = [];

    public bool IsCpuDriverAvailable { get; private set; }

    public HardwareSnapshot Latest { get; private set; } = HardwareSnapshot.Empty;

    public event EventHandler<HardwareSnapshot>? SnapshotUpdated;

    public Task InitializeAsync()
    {
        if (_thread is not null)
            return _ready.Task;

        _thread = new Thread(Run)
        {
            Name = "PCStats.HardwareMonitor",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
        return _ready.Task;
    }

    public void SetUpdateInterval(TimeSpan interval) =>
        _intervalMs = (int)Math.Clamp(interval.TotalMilliseconds, 250, 10_000);

    public void SetActiveMetrics(IReadOnlyCollection<string>? metricIds)
    {
        if (metricIds is null)
        {
            _activeNodeIds = null;
            return;
        }

        // Expand synthetic / total dependencies, then keep only the nodes that own one of those sensors.
        var byId = Metrics.ToDictionary(m => m.Id);
        var sensorIds = new HashSet<string>();
        foreach (var id in metricIds)
        {
            if (!byId.TryGetValue(id, out var m))
                continue;
            sensorIds.Add(m.Id);
            sensorIds.UnionWith(m.AggregateSourceIds);
            sensorIds.UnionWith(m.TotalSourceIds);
        }

        _activeNodeIds = _nodes
            .Where(n => n.SensorIds.Any(sensorIds.Contains))
            .Select(n => n.Id)
            .ToHashSet();
    }

    private void Run()
    {
        try
        {
            IsCpuDriverAvailable = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
            Logger.Info($"Opening hardware monitor (PawnIO installed: {IsCpuDriverAvailable})");

            _computer.Open();
            Flatten(_computer.Hardware);
            var nvidia = NvmlGpuNode.Discover();
            _nodes.AddRange(nvidia);

            foreach (var node in _nodes)
                Logger.Info($"  node: {node.Id} '{node.Name}' sensors={node.SensorIds.Count}");

            UpdateAll();

            Metrics = MetricCatalog.Build(_computer.Hardware, nvidia.SelectMany(n => n.Metrics));
            _synthetic = Metrics.Where(m => m.IsSynthetic).ToList();
            Logger.Info($"Discovered {Metrics.Count} metrics across {_nodes.Count} nodes");

            Publish();
            _ready.TrySetResult();
        }
        catch (Exception ex)
        {
            Logger.Error("Hardware monitor failed to start", ex);
            _ready.TrySetException(ex);
            return;
        }

        var token = _cts.Token;
        var stopwatch = new Stopwatch();
        while (!token.IsCancellationRequested)
        {
            stopwatch.Restart();
            try
            {
                UpdateAll();
                Publish();
            }
            catch (Exception ex)
            {
                Logger.Warn("Polling cycle failed", ex);
            }

            var remaining = _intervalMs - (int)stopwatch.ElapsedMilliseconds;
            if (remaining > 0)
                token.WaitHandle.WaitOne(remaining);
        }

        try
        {
            _computer.Close();
            NvmlGpuNode.ShutdownLibrary();
        }
        catch (Exception ex)
        {
            Logger.Warn("Closing hardware monitor failed", ex);
        }
    }

    private void Flatten(IEnumerable<IHardware> roots)
    {
        foreach (var hw in roots)
        {
            _nodes.Add(new LhmNode(hw, IsCpuDriverAvailable));
            Flatten(hw.SubHardware);
        }
    }

    private bool IsActive(IPollNode node)
    {
        var active = _activeNodeIds;
        return active is null || active.Contains(node.Id);
    }

    private void UpdateAll()
    {
        foreach (var node in _nodes)
        {
            if (!IsActive(node))
                continue;

            var started = Stopwatch.GetTimestamp();
            try
            {
                node.Update();
            }
            catch (Exception ex)
            {
                // One misbehaving device must not blank out every other reading.
                Logger.Warn($"Update failed for {node.Name}", ex);
            }

            _updateCostMs[node.Id] = _updateCostMs.GetValueOrDefault(node.Id) + Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        // Periodic cost report so a slow sensor can be spotted in log.txt without a profiler.
        if (++_cycles % CostReportEveryCycles == 0)
        {
            var report = string.Join(", ", _updateCostMs
                .OrderByDescending(kv => kv.Value)
                .Take(6)
                .Select(kv => $"{kv.Key}={kv.Value / CostReportEveryCycles:0.0}ms"));
            Logger.Info($"Polling cost per cycle (top): {report}");
            _updateCostMs.Clear();
        }
    }

    private void Publish()
    {
        var values = new Dictionary<string, float>(capacity: 256);
        foreach (var node in _nodes)
        {
            if (IsActive(node))
                node.Collect(values); // inactive nodes would only contribute stale numbers
        }

        foreach (var synthetic in _synthetic)
        {
            var sources = synthetic.AggregateSourceIds
                .Select(id => values.TryGetValue(id, out var v) ? v : (float?)null)
                .OfType<float>()
                .ToList();
            if (sources.Count == 0)
                continue;

            values[synthetic.Id] = synthetic.Aggregation switch
            {
                Aggregation.Max => sources.Max(),
                Aggregation.Average => sources.Average(),
                Aggregation.Sum => sources.Sum(),
                _ => sources[0],
            };
        }

        var snapshot = new HardwareSnapshot { Values = values, Timestamp = DateTime.Now };
        Latest = snapshot;
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _cts.Dispose();
    }
}
