#nullable enable

using Kern.World.Terrain.Background;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;

namespace Kern.Tests.World;

public sealed class BackgroundFloodFillScrollTests
{
    private const int Width = 48;
    private const int Height = 32;

    // Пещеры из хеша: порода с полостями, у окна — кайма кэша в одну клетку.
    private sealed class CaveCells : ICachedCellDataProvider
    {
        public int OriginX { get; set; } = 500;

        public int OriginY { get; set; } = 700;

        public CachedCellInfo GetCell(int x, int y)
        {
            int worldX = OriginX + x - 1;
            int worldY = OriginY + y - 1;
            uint hash = (uint)((worldX * 374761393) ^ (worldY * 668265263));
            hash = (hash ^ (hash >> 13)) * 1274126177;
            int noise = (int)((hash ^ (hash >> 16)) % 100);
            bool solid = noise < 62;
            return new CachedCellInfo
            {
                Type = solid ? CellType.Rock : noise > 94 ? CellType.Road : CellType.Empty,
                Properties = solid ? 0 : CellConfigProperties.Passable,
            };
        }
    }

    // Регрессия производительности: сдвиг перезаливал всю связную породу окна
    // (73% стоимости шага камеры), и фон внутри перещёлкивался на ничьих.
    // Внутренность обязана переехать как есть.
    [TestCase(1, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(-1, -1)]
    [TestCase(-2, 1)]
    public void ComputeScrolled_KeepsInteriorUnchanged(int dx, int dy)
    {
        var cells = new CaveCells();
        var fill = new BackgroundFloodFill();
        fill.Allocate(Width, Height);
        fill.ComputeFull(cells);
        var before = (CellType[,])fill.Buffer.Clone();

        cells.OriginX += dx;
        cells.OriginY += dy;
        fill.ComputeScrolled(dx, dy, cells);

        // Внутренность — всё, кроме вошедшей каймы и соседней с ней линии.
        for (int x = 2; x < Width - 2; x++)
        {
            for (int y = 2; y < Height - 2; y++)
            {
                int sourceX = x + dx;
                int sourceY = y + dy;
                if (sourceX < 0 || sourceX >= Width || sourceY < 0 || sourceY >= Height)
                {
                    continue;
                }

                Assert.That(fill.Buffer[x, y], Is.EqualTo(before[sourceX, sourceY]), $"cell {x},{y}");
            }
        }
    }

    [TestCase(1, 0)]
    [TestCase(1, 1)]
    [TestCase(-3, 2)]
    public void ComputeScrolled_LeavesNoUnloadedAndFloorKeepsItsType(int dx, int dy)
    {
        var cells = new CaveCells();
        var fill = new BackgroundFloodFill();
        fill.Allocate(Width, Height);
        fill.ComputeFull(cells);
        cells.OriginX += dx;
        cells.OriginY += dy;
        fill.ComputeScrolled(dx, dy, cells);

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Assert.That(fill.Buffer[x, y], Is.Not.EqualTo(CellType.Unloaded), $"cell {x},{y}");
                CachedCellInfo cell = cells.GetCell(x + 1, y + 1);
                if ((cell.Properties & CellConfigProperties.Passable) != 0)
                {
                    Assert.That(fill.Buffer[x, y], Is.EqualTo(cell.Type), $"floor {x},{y}");
                }
            }
        }
    }
}
