#nullable enable

using System;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>
/// Разбор имени загруженного файла текстуры.
/// </summary>
///
/// Сервис текстур сообщает о загрузке именем файла, а террейну нужен тип
/// клетки: «Cells/42.png» — это CellType 42. Отдельным типом, потому что это
/// разбор строки, а не работа рендерера, и его видно в тесте.
public static class TerrainCellTextureName
{
    private const string CellPrefix = "Cells/";
    private const string DecalAtlas = "terrain-decals.png";
    private const string DecalStoneAtlas = "terrain-decals-stone.png";

    public static bool TryParseCellType(string filename, out CellType cellType)
    {
        cellType = default;
        if (!filename.StartsWith(CellPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int extensionIndex = filename.LastIndexOf('.');
        ReadOnlySpan<char> id = filename.AsSpan(
            CellPrefix.Length,
            (extensionIndex >= 0 ? extensionIndex : filename.Length) - CellPrefix.Length);
        if (!int.TryParse(id, out int cellTypeId) || (uint)cellTypeId > ushort.MaxValue)
        {
            return false;
        }

        cellType = (CellType)cellTypeId;
        return true;
    }

    // Второй атлас обязан попадать сюда наравне с первым: без этого его
    // загрузка не перебиндит материалы, и stone-декали останутся сэмплить
    // незаполненный _TerrainDecalStoneAtlas, если он приехал вторым.
    public static bool IsDecalAtlas(string filename) =>
        string.Equals(filename, DecalAtlas, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(filename, DecalStoneAtlas, StringComparison.OrdinalIgnoreCase);
}
