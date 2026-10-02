#nullable enable

namespace Kern.Core;

/// <summary>
/// Optional read-only diagnostic capability owned by the existing FrameTelemetry scope.
/// Read on the Unity main thread alongside telemetry, before the next terrain reset.
/// This stamps the start of a telemetry window, not successful terrain commit or lighting output.
/// Missing capability or a mismatched observation frame means unavailable evidence, not zero work.
/// </summary>
public interface IFrameTelemetryProducerStamp
{
    /// <summary>Unity frame number at ResetFrameTimers; -1 before the first reset. Not a source revision.</summary>
    int ProducerFrameId { get; }

    /// <summary>False before the first reset and after disposal. Freshness requires comparing frame IDs too.</summary>
    bool ProducerLifecycleValid { get; }
}
