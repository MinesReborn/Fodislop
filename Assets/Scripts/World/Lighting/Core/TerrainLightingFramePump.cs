#nullable enable

using System;
using Kern.Core.Interfaces.WorldLighting;

namespace Kern.World.Lighting;

/// <summary>Delivers only the newest unacknowledged committed Terrain frame to Lighting.</summary>
internal sealed class TerrainLightingFramePump
{
    public bool ProcessLatest(
        ITerrainLightingExchange exchange,
        Func<TerrainLightingFrameSnapshot, bool> processFrame)
    {
        if (exchange == null)
        {
            throw new ArgumentNullException(nameof(exchange));
        }

        if (processFrame == null)
        {
            throw new ArgumentNullException(nameof(processFrame));
        }

        if (!exchange.TryReadLatestTerrainFrame(
            afterFrameSequence: 0,
            out TerrainLightingFrameSnapshot frame))
        {
            return false;
        }

        if (!processFrame(frame))
        {
            return false;
        }

        exchange.AcknowledgeTerrainFrame(frame.FrameSequence);
        return true;
    }
}
