using PCStats.Models;

namespace PCStats.Services;

public interface IHardwareMonitor : IDisposable
{
    /// <summary>All metrics discovered on this machine. Populated after <see cref="InitializeAsync"/> completes.</summary>
    IReadOnlyList<MetricDescriptor> Metrics { get; }

    /// <summary>False when the kernel driver used for CPU sensors (PawnIO) is not installed.</summary>
    bool IsCpuDriverAvailable { get; }

    /// <summary>Most recent reading; <see cref="HardwareSnapshot.Empty"/> before the first poll.</summary>
    HardwareSnapshot Latest { get; }

    /// <summary>Raised on a background thread after every polling cycle.</summary>
    event EventHandler<HardwareSnapshot>? SnapshotUpdated;

    Task InitializeAsync();

    void SetUpdateInterval(TimeSpan interval);

    /// <summary>
    /// Restricts polling to the hardware nodes that feed the given metric ids (dependencies included).
    /// Pass null to poll everything, e.g. while the settings window shows live previews.
    /// </summary>
    void SetActiveMetrics(IReadOnlyCollection<string>? metricIds);
}
