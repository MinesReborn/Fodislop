#nullable enable

namespace Kern.World.Terrain;

/// <summary>Какой слой клетки собирается.</summary>
///
/// Фон — прямоугольник под клеткой: её тип — TerrainQuadBuilder.UnderOf,
/// геометрия не смещается, света и каймы она не несёт. Передний план — сама
/// клетка со всем этим.
public enum TerrainQuadLayer
{
    Background = 0,
    Foreground = 1,
}

/// <summary>
/// Место клетки: её адрес в окне и её адрес в мире.
/// </summary>
///
/// Локальные координаты индексируют кольцевые массивы окна, мировые идут в
/// тексель и в хэш искажения. Их нельзя путать, и держать их одной парой
/// int'ов в списке из восемнадцати параметров — способ однажды перепутать.
public readonly struct TerrainQuadSite
{
    public TerrainQuadSite(int localX, int localY, int gridX, int unityY, float cellSize)
    {
        LocalX = localX;
        LocalY = localY;
        GridX = gridX;
        UnityY = unityY;
        CellSize = cellSize;
    }

    public int LocalX { get; }

    public int LocalY { get; }

    public int GridX { get; }

    public int UnityY { get; }

    public float CellSize { get; }
}

/// <summary>Чем кончилась сборка квада.</summary>
///
/// Отрицательный атлас означает «квада нет»: клетка за миром, не загружена
/// или слой для неё пуст. Признак двери нужен накладке, которая рисуется
/// отдельным мешем поверх террейна.
public readonly struct TerrainQuadResult
{
    public TerrainQuadResult(int atlasIndex, bool isDoor)
    {
        AtlasIndex = atlasIndex;
        IsDoor = isDoor;
    }

    public int AtlasIndex { get; }

    public bool IsDoor { get; }

    public static TerrainQuadResult None => new(-1, false);

    public static TerrainQuadResult NoAtlas(bool isDoor) => new(-1, isDoor);

    public bool HasAtlas => AtlasIndex >= 0;
}
