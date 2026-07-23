using Fodinae.Scripts.World;
using MinesServer.Data;

namespace Fodinae.Scripts.Core.Interfaces
{
    public interface IWorldDataStorage
    {
        bool IsReady { get; }
        WorldLayer<CellType> CellLayer { get; }
        void SetCell(int x, int y, CellType type);
        CellType GetCell(int x, int y);
        void InitWorld(string worldCodeName, int width, int height);
        void Dispose();
        bool IsInitialized();
        string GetWorldCodeName();
        void EnsureEditorInitialized();
    }
}
