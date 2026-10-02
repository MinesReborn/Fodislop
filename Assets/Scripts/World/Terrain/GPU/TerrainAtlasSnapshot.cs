#nullable enable

using System;
using Kern.Core.Interfaces;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Числовой снимок атласа для фоновой сборки: какие типы в нём лежат и какие
/// из них непрозрачны целиком. Живой атлас меняется на главном потоке при
/// приезде текстур, поэтому рабочий поток читает только снимок.
/// </summary>
public sealed class TerrainAtlasSnapshot : IAtlasDescriptor
{
    private const int CellTypeCount = 256;

    private readonly bool[] _containedCells;
    private readonly bool[] _opaqueCells;

    private TerrainAtlasSnapshot(int size, bool[] containedCells, bool[] opaqueCells)
    {
        Size = size;
        _containedCells = containedCells;
        _opaqueCells = opaqueCells;
    }

    public Texture2D? Texture => null;

    public int Size { get; }

    public bool ContainsCell(CellType cellType) => _containedCells[(byte)cellType];

    public bool IsFullyOpaque(CellType cellType) => _opaqueCells[(byte)cellType];

    public static TerrainAtlasSnapshot Capture(IAtlasDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        // CellType — байт: таблица на все значения дешевле хеш-множеств и
        // не зависит от того, какие имена объявлены в перечислении.
        var containedCells = new bool[CellTypeCount];
        var opaqueCells = new bool[CellTypeCount];
        for (int type = 0; type < CellTypeCount; type++)
        {
            var cellType = (CellType)type;
            if (!descriptor.ContainsCell(cellType))
            {
                continue;
            }

            containedCells[type] = true;
            opaqueCells[type] = descriptor.IsFullyOpaque(cellType);
        }

        return new TerrainAtlasSnapshot(descriptor.Size, containedCells, opaqueCells);
    }
}
