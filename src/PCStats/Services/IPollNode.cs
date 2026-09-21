namespace PCStats.Services;

/// <summary>A unit of hardware the monitor can refresh independently, so unused ones can be skipped.</summary>
internal interface IPollNode
{
    string Id { get; }

    string Name { get; }

    /// <summary>Ids of every metric this node produces; used to decide whether it must be polled.</summary>
    IReadOnlyCollection<string> SensorIds { get; }

    /// <summary>Talks to the hardware. May be slow; called on the monitor thread only.</summary>
    void Update();

    /// <summary>Copies the latest readings into the snapshot being built.</summary>
    void Collect(Dictionary<string, float> values);
}
