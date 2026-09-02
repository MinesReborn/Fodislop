#nullable enable

using System.Threading;
using Cysharp.Threading.Tasks;
using MinesServer.Data;

namespace Fodinae.Core.Interfaces
{
    public interface IWorldDataStorage
    {
        event System.Action<int, int>? CellChanged;
        event System.Action<int, int, int, int>? RegionChanged;

        bool IsReady { get; }
        long Revision { get; }
        IWorldLayer<CellType>? CellLayer { get; }
        void SetCell(int x, int y, CellType type);
        void SetRegion(int startX, int startY, int width, int height, CellType[] cells);
        void SetRegion(int startX, int startY, int width, int height, System.ReadOnlySpan<CellType> cells);
        CellType GetCell(int x, int y);
        void InitWorld(string worldCodeName, int width, int height);
        void Dispose();
        UniTask DisposeAsync(CancellationToken cancellationToken = default);
        void Flush();
        UniTask FlushAsync(bool durable, CancellationToken cancellationToken = default);
        bool IsInitialized();
        string GetWorldCodeName();

#if UNITY_EDITOR
        void EnsureEditorInitialized();
#endif
    }

    public interface IWorldPersistence
    {
        bool HasDirtyChunks { get; }

        void Flush(bool durable);
    }
}
