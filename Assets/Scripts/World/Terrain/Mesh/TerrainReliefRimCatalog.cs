#nullable enable

using MinesServer.Data;

namespace Kern.World.Terrain;

// Семьи клеток для соединения непрерывных листов рельефа.
public enum TerrainRimFamily : byte
{
    None = 0,
    Crystal = 1,
    Rock = 2,
    GreenBlueRock = 3,
}

public static class TerrainReliefRimCatalog
{
    public static TerrainRimFamily GetFamily(CellType cellType)
    {
        if (cellType is CellType.Green or CellType.Blue or CellType.Rock)
        {
            return TerrainRimFamily.GreenBlueRock;
        }

        if (cellType == CellType.Unloaded)
        {
            return TerrainRimFamily.None;
        }

        CellVisualProperties visuals = MapCellConfigCatalog.GetVisualProperties(cellType);
        if (!visuals.IsContinuousSheet)
        {
            return TerrainRimFamily.None;
        }

        return visuals.IsRockSheet
            ? TerrainRimFamily.Rock
            : TerrainRimFamily.Crystal;
    }

}
