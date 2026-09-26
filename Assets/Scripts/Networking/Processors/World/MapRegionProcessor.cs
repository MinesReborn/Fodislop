#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.World;

namespace Kern.Networking.Processors;

public sealed class MapRegionProcessor(IWorldDataStorage storage) : IPacketProcessor<MapRegionPacket>
{
    private readonly IRegionBatchStorage? _batchStorage = storage as IRegionBatchStorage;

    public void BeginBatch() => _batchStorage?.BeginRegionBatch();

    public void EndBatch() => _batchStorage?.EndRegionBatch();

    public void Process(MapRegionPacket packet)
    {
        if (!storage.IsReady || storage.CellLayer == null)
        {
            throw new InvalidOperationException(
                $"[MapRegionProcessor] MapStorage is not ready for region " +
                $"({packet.X},{packet.Y}) {packet.Width + 1}x{packet.Height + 1}.");
        }

        if (packet.Payload == null)
        {
            throw new InvalidOperationException(
                $"[MapRegionProcessor] Map region ({packet.X},{packet.Y}) has null payload.");
        }

        int width = packet.Width + 1;
        int height = packet.Height + 1;
        long expectedCellCount = (long)width * height;
        if (width <= 0 || height <= 0 || packet.Payload.Length < expectedCellCount)
        {
            throw new InvalidOperationException(
                $"[MapRegionProcessor] Invalid region ({packet.X},{packet.Y}) " +
                $"{width}x{height}: payload has {packet.Payload.Length} cells, " +
                $"expected at least {expectedCellCount}.");
        }

        storage.SetRegion(
            packet.X,
            packet.Y,
            width,
            height,
            packet.Payload.AsSpan());
    }
}
