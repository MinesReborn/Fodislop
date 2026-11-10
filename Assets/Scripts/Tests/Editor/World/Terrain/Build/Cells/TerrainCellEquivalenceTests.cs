#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Клетка, её соседи и строка типа дают шейдеру ровно те атрибуты, что вершины
// того же квада: рабочий FillQuad по всему окну тестового мира, оба слоя, классическое
// искажение. Расшифровка — TerrainCellOracle.
[TestFixture]
public sealed class TerrainCellEquivalenceTests
{
    private const int Width = 48;
    private const int Height = 32;
    private const int OriginX = 96;
    private const int OriginY = 64;

    [Test]
    public void CellAndTypeRowReproduceQuadVertices()
    {
        var world = new TerrainTestWorld();
        var distortion = new TerrainDistortionSettings();
        TerrainCellSources sources = world.BuildSources(
            new TerrainCellCache(), distortion,
            OriginX, OriginY, Width, Height);

        // Окно и кайма — тем же кодом, что TerrainCellFillExecutor и
        // TerrainCellBuilder.RefreshMargin; вершины квадов — эталон.
        var cells = new Dictionary<(int, int), TerrainCell>();
        int occluded = 0;
        var quads = new List<(int X, int Y, int Layer, TerrainVertex[] Vertices, TerrainQuadResult Result)>();
        for (int x = -1; x <= Width; x++)
        {
            for (int y = -1; y <= Height; y++)
            {
                if (x < 0 || y < 0 || x == Width || y == Height)
                {
                    cells[(OriginX + x, OriginY + y)] = TerrainCellPacker.PackCell(sources, x, y);
                    continue;
                }

                var site = new TerrainQuadSite(x, y, OriginX + x, OriginY + y, 1f);
                var background = new TerrainVertex[4];
                var foreground = new TerrainVertex[4];
                TerrainQuadResult backgroundResult = TerrainQuadBuilder.FillQuad(sources, site, TerrainQuadLayer.Background, background);
                TerrainQuadResult foregroundResult = TerrainQuadBuilder.FillQuad(sources, site, TerrainQuadLayer.Foreground, foreground);
                // Фон под передним планом, закрывающим клетку целиком, не рисуется:
                // это правило жило в сборке клеток, теперь — в шейдере.
                if (backgroundResult.HasAtlas &&
                    TerrainOcclusionReference.CoversCell(x, y, foregroundResult.AtlasIndex, foreground, sources))
                {
                    backgroundResult = TerrainQuadResult.None;
                    occluded++;
                }

                quads.Add((x, y, TerrainCellData.BackgroundLayer, background, backgroundResult));
                quads.Add((x, y, TerrainCellData.ForegroundLayer, foreground, foregroundResult));
                cells[(OriginX + x, OriginY + y)] = TerrainCellPacker.PackCell(sources, x, y);
            }
        }

        var rows = new TerrainTypeRow[TerrainCellData.TypeCount];
        // Как TerrainCellBuffers: строка без метаданных (Unloaded их не
        // получает) несёт только cells.json.
        for (int index = 0; index < rows.Length; index++)
        {
            var type = (CellType)index;
            rows[index] = TerrainCellData.PackType(
                type != CellType.Unloaded && sources.MetadataLookup.TryGet(type, out CellMetadata metadata)
                    ? TerrainCellPacker.ResolveTypeFields(type, in metadata, sources.Atlases)
                    : TerrainCellPacker.ResolveTypeFields(type, default, System.Array.Empty<Kern.Core.Interfaces.IAtlasDescriptor>()));
        }

        var oracle = new TerrainCellOracle(
            (gx, uy) => cells[(gx, uy)],
            (gx, uy) => TerrainVertexDistortionCalculator.ComputeNode(
                sources.CellCache, distortion, gx - OriginX, uy - OriginY,
                TerrainTestWorld.WorldWidth, TerrainTestWorld.WorldHeight),
            type => rows[type],
            slot => sources.Atlases[slot].Size,
            TerrainCellData.PackTileDescriptors(),
            (uint)BlockRegistry.UnderlayType,
            TerrainTestWorld.WorldHeight);
        int anchored = 0;
        foreach ((int x, int y, int layer, TerrainVertex[] vertices, TerrainQuadResult result) in quads)
        {
            oracle.AssertMatchesQuad(
                vertices, result, OriginX + x, OriginY + y, layer, $"({x},{y}) слой {layer}");
            anchored += result.HasAtlas && layer == TerrainCellData.ForegroundLayer && vertices[0].UV5x != 0 ? 1 : 0;
        }

        Assert.That(anchored, Is.GreaterThan(0), "мир без смещённых клеток не проверяет геометрию");
        Assert.That(occluded, Is.GreaterThan(0), "мир без закрытого фона не проверяет перекрытие");
    }
}
