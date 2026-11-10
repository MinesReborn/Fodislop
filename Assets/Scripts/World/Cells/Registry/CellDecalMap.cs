#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;

namespace Kern.World;

/// <summary>
/// Декали мира: одна общая текстура на набор клеток. Строится из
/// <c>WorldInitPacket.DecalMap</c> и раздаётся реестру блоков и каталогу декалей,
/// поэтому имя декали хранится не в каждой конфигурации клетки, а один раз.
/// </summary>
public sealed class CellDecalMap
{
    private readonly Dictionary<CellType, string> _byCell;

    private CellDecalMap(Dictionary<CellType, string> byCell, string[] textureNames)
    {
        _byCell = byCell;
        TextureNames = textureNames;
    }

    public static CellDecalMap Empty { get; } = new(new Dictionary<CellType, string>(), Array.Empty<string>());

    /// <summary>Уникальные текстуры декалей в устойчивом порядке: индекс среза атласа зависит от него.</summary>
    public IReadOnlyList<string> TextureNames { get; }

    public static CellDecalMap FromEntries(CellDecalEntry[]? entries)
    {
        if (entries == null || entries.Length == 0)
        {
            return Empty;
        }

        var byCell = new Dictionary<CellType, string>();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CellDecalEntry entry in entries)
        {
            string textureName = entry.TextureName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(textureName))
            {
                throw new InvalidDataException("WorldInitPacket.DecalMap contains an empty decal texture name.");
            }

            if (!names.Add(textureName))
            {
                throw new InvalidDataException(
                    $"WorldInitPacket.DecalMap contains duplicate decal texture '{textureName}'.");
            }

            byte[] cells = entry.Cells ?? Array.Empty<byte>();
            if (cells.Length == 0)
            {
                throw new InvalidDataException(
                    $"WorldInitPacket.DecalMap entry '{textureName}' lists no cells.");
            }

            foreach (byte cell in cells)
            {
                var type = (CellType)cell;
                if (!byCell.TryAdd(type, textureName))
                {
                    throw new InvalidDataException(
                        $"Cell '{type}' is assigned to more than one decal texture in WorldInitPacket.DecalMap.");
                }
            }
        }

        return new CellDecalMap(byCell, names.ToArray());
    }

    public bool TryGet(CellType type, out string? textureName) =>
        _byCell.TryGetValue(type, out textureName);
}
