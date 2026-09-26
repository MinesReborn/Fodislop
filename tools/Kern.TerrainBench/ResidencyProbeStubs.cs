#nullable enable

// Minimal contracts for compiling the production residency probe in this
// standalone harness; implementations below remain deterministic fixtures.
namespace Kern
{
    public enum ChunkReadStatus
    {
        Available,
        Loading,
        Missing,
        Failed,
    }

    public readonly record struct ChunkReadResult<T>(ChunkReadStatus Status, T[]? Data, System.Exception? Error)
        where T : unmanaged;

    public interface IWorldLayer<T> where T : unmanaged
    {
        int ChunkSize { get; }

        int HeightChunks { get; }

        ChunkReadResult<T> ReadChunk(int chunkIndex, bool touchLru = true);
    }
}

namespace Kern.Core.Interfaces
{
    using Kern;
    using MinesServer.Data;
    using UnityEngine;

    public interface IWorldDataStorage
    {
        IWorldLayer<CellType>? CellLayer { get; }

        string GetWorldCodeName();
    }

    public interface IMapDataProvider
    {
        ushort WorldWidth { get; }

        ushort WorldHeight { get; }
    }

    public interface IConnectionService
    {
    }

    public interface IWorldRegionRequester
    {
        void RequestWorldRegion(string worldCodeName, RectInt serverRegion);
    }
}
