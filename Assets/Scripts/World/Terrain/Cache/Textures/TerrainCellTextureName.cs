#nullable enable

using System;
using MinesServer.Data;

namespace Kern.World.Terrain;

/// <summary>
/// Разбор имени загруженного файла текстуры.
/// </summary>
///
/// Сервис текстур сообщает о загрузке именем файла, а террейну нужен тип
/// клетки: «Visual/Cells/Rock.png» — это CellType Rock. Отдельным типом,
/// потому что это разбор строки, а не работа рендерера, и его видно в тесте.
public static class TerrainCellTextureName
{
    private const string CellPrefix = "Visual/Cells/";

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
        string cellName = id.ToString();
        if (!Enum.TryParse(cellName, ignoreCase: true, out cellType) ||
            !Enum.IsDefined(typeof(CellType), cellType))
        {
            cellType = default;
            return false;
        }

        return true;
    }
}
