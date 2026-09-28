#nullable enable

using Kern.World;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

internal sealed class MinimapCellInvalidation(MapCellSampler sampler)
{
    private bool _pending;

    public bool IsPending => _pending;

    public void Clear() => _pending = false;

    public void OnChunkLoaded(int serverX, int serverY)
    {
        sampler.InvalidateChunk(serverX, serverY);
        _pending = true;
    }

    public void OnCellChanged(int serverX, int serverY)
    {
        sampler.InvalidateChunk(serverX, serverY);
        _pending = true;
    }

    public void OnRegionChanged(
        int startX,
        int startY,
        int width,
        int height,
        IWorldLayer<CellType>? cellLayer)
    {
        if (width <= 0 || height <= 0 || cellLayer == null)
        {
            return;
        }

        int chunkSize = cellLayer.ChunkSize;
        int endX = startX + width - 1;
        int endY = startY + height - 1;
        for (int chunkX = Mathf.Max(0, startX / chunkSize); chunkX <= endX / chunkSize; chunkX++)
        {
            for (int chunkY = Mathf.Max(0, startY / chunkSize); chunkY <= endY / chunkSize; chunkY++)
            {
                sampler.InvalidateChunk(chunkX * chunkSize, chunkY * chunkSize);
            }
        }

        _pending = true;
    }
}
