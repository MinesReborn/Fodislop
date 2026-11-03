#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Та же проверка, что TerrainCellEquivalenceTests в Unity, на пёстром мире
// вне редактора: у каждого типа свой атлас, кадры, анимация, свечение,
// тайлгруппа, кайма и искажение; часть типов без текстуры. Слой клетки и
// строка типа обязаны дать ровно вершины FillQuad.
[TestFixture]
public sealed class TerrainCellEquivalenceHarnessTests
{
    private const int Width = 64;
    private const int Height = 48;
    private const int OriginX = 300;
    private const int OriginY = 500;
    private const int WorldWidth = 2048;
    private const int WorldHeight = 2048;

    private static readonly CellType[] s_palette =
    [
        CellType.Empty, CellType.Empty, CellType.Empty, CellType.Road,
        CellType.Rock, CellType.Rock, CellType.Rock, CellType.RedRock, CellType.BlackRock,
        CellType.Lava, CellType.XGreen, CellType.Green, CellType.WhiteSand, CellType.BlueSand,
        CellType.BuildingWall, CellType.BuildingCorner, CellType.BuildingDoor, CellType.GoldenRock,
        CellType.AliveCyan, CellType.GrayAcid, CellType.Boulder1,
    ];

    private static readonly int[] s_worlds = [11, 12, 13];

    [TestCase(11)]
    [TestCase(12)]
    [TestCase(13)]
    public void CellAndTypeRowReproduceQuadVertices(int seed)
    {
        World world = BuildWorld(seed);
        var oracle = new TerrainCellOracle(
            (gx, uy) => world.Cells[(gx, uy)],
            (gx, uy) => TerrainVertexDistortionCalculator.ComputeNode(
                world.Sources.CellCache, world.Sources.Distortion, gx - OriginX, uy - OriginY, WorldWidth, WorldHeight),
            type => world.Rows[type],
            slot => world.Sources.Atlases[slot].Size,
            TerrainCellData.PackTileDescriptors(),
            (uint)BlockRegistry.UnderlayType,
            WorldHeight);
        int anchored = 0;
        int decals = 0;
        foreach (Quad quad in world.Quads)
        {
            oracle.AssertMatchesQuad(
                quad.Vertices, quad.Result, OriginX + quad.X, OriginY + quad.Y, quad.Layer,
                $"({quad.X},{quad.Y}) слой {quad.Layer} клетка {(CellType)world.Cells[(OriginX + quad.X, OriginY + quad.Y)].Bits}");
            if (quad.Result.HasAtlas)
            {
                anchored += quad.Layer == 1 && quad.Vertices[0].UV5x != 0 ? 1 : 0;
                decals += quad.Vertices[0].UV6.w != 0f ? 1 : 0;
            }
        }

        Assert.That(anchored, Is.GreaterThan(0), "мир без смещённых клеток не проверяет геометрию");
        Assert.That(world.Occluded, Is.GreaterThan(0), "мир без закрытого фона не проверяет перекрытие");
        Assert.That(decals, Is.GreaterThan(0), "мир без декалей не проверяет их хэш");
    }

    // Те же миры для растрового шима: он прогоняет настоящий
    // TerrainCellData.hlsl по этим клеткам и сверяет с этими вершинами.
    // Запускается раннером шима (tools/Kern.TerrainRasterTests), который
    // передаёт каталог в KERN_TERRAIN_SHIM_FIXTURES.
    [Test]
    public void ExportWorldsForHlslShim()
    {
        string? directory = Environment.GetEnvironmentVariable("KERN_TERRAIN_SHIM_FIXTURES");
        if (string.IsNullOrEmpty(directory))
        {
            Assert.Ignore("Выгрузку заказывает раннер растрового шима.");
        }

        Directory.CreateDirectory(directory);
        foreach (int seed in s_worlds)
        {
            World world = BuildWorld(seed);
            using var writer = new BinaryWriter(File.Create(Path.Combine(directory, $"world-{seed}.bin")));
            WriteWorld(writer, world);
        }
    }

    // Раскладка файла читается в scenario.cpp (loadWorld): заголовок, таблица
    // автотайла, строки типов, кольцо клеток, затем квады с ожидаемыми
    // атрибутами вершин, уже переведёнными из half во float.
    private static void WriteWorld(BinaryWriter writer, World world)
    {
        int ringWidth = Width + 2;
        int ringHeight = Height + 2;
        writer.Write(WorldWidth);
        writer.Write(WorldHeight);
        writer.Write(world.Sources.Distortion.EnableDistortion ? 1 : 0);
        foreach (Vector4 vector in TerrainCellData.PackDistortion())
        {
            writer.Write(vector.x);
            writer.Write(vector.y);
            writer.Write(vector.z);
            writer.Write(vector.w);
        }

        writer.Write(OriginX);
        writer.Write(OriginY);
        writer.Write(ringWidth);
        writer.Write(ringHeight);
        foreach (uint word in TerrainCellData.PackTileDescriptors())
        {
            writer.Write(word);
        }

        // Размеры атласов по слотам: из них шейдер выводит размер тайла.
        for (int slot = 0; slot < 8; slot++)
        {
            writer.Write(slot < world.Sources.Atlases.Count ? world.Sources.Atlases[slot].Size : 1);
        }

        foreach (TerrainTypeRow row in world.Rows)
        {
            foreach (uint word in new[] { row.AtlasXY, row.AtlasWH, row.Look, row.SpeedGlowTile })
            {
                writer.Write(word);
            }
        }

        writer.Write((int)BlockRegistry.UnderlayType);

        var ring = new byte[ringWidth * ringHeight];
        foreach (((int gx, int uy), TerrainCell cell) in world.Cells)
        {
            ring[(Ring(uy, ringHeight) * ringWidth) + Ring(gx, ringWidth)] = cell.Bits;
        }

        writer.Write(ring);

        writer.Write(world.Quads.Count);
        foreach (Quad quad in world.Quads)
        {
            writer.Write(quad.X);
            writer.Write(quad.Y);
            writer.Write(quad.Layer);
            writer.Write(quad.Result.AtlasIndex);
            foreach (TerrainVertex v in quad.Vertices)
            {
                foreach (float value in new[]
                {
                    H(v.UV0x), H(v.UV0y),
                    v.UV1.x, v.UV1.y, v.UV1.z, v.UV1.w,
                    H(v.UV2x), H(v.UV2y), H(v.UV2z), H(v.UV2w),
                    v.UV3.x, v.UV3.y, v.UV3.z, v.UV3.w,
                    H(v.UV4x), H(v.UV4y), H(v.UV4z), H(v.UV4w),
                    H(v.UV5x), H(v.UV5y), H(v.UV5z), H(v.UV5w),
                    v.UV6.x, v.UV6.y, v.UV6.z, v.UV6.w,
                })
                {
                    writer.Write(value);
                }
            }
        }
    }

    private static float H(ushort half) => Mathf.HalfToFloat(half);

    private static int Ring(int value, int size) => ((value % size) + size) % size;

    private sealed record Quad(int X, int Y, int Layer, TerrainVertex[] Vertices, TerrainQuadResult Result);

    private sealed class World
    {
        public readonly Dictionary<(int, int), TerrainCell> Cells = [];
        public readonly List<Quad> Quads = [];
        public readonly TerrainTypeRow[] Rows = new TerrainTypeRow[TerrainCellData.TypeCount];
        public int Occluded;

        public TerrainCellSources Sources;
    }

    private static World BuildWorld(int seed)
    {
        TerrainDecalCatalog.Configure(
            BlockRegistry.Blocks.Values.Select(block => block.DecalTextureName));
        var lookup = new VariedMetadata(seed);
        var grid = new Grid(lookup, seed);
        var distortion = new TerrainDistortionSettings();
        IAtlasDescriptor[] atlases = [new TestAtlas(1024), new TestAtlas(512)];
        var sources = new TerrainCellSources(
            grid, distortion, WorldWidth, WorldHeight, atlases, lookup);

        // Окно и кайма — тем же кодом, что TerrainCellFillExecutor и
        // TerrainCellBuilder.RefreshMargin; вершины квадов — эталон.
        var world = new World { Sources = sources };
        for (int x = -1; x <= Width; x++)
        {
            for (int y = -1; y <= Height; y++)
            {
                if (x < 0 || y < 0 || x == Width || y == Height)
                {
                    world.Cells[(OriginX + x, OriginY + y)] = TerrainCellPacker.PackCell(sources, x, y);
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
                    world.Occluded++;
                }

                world.Quads.Add(new Quad(x, y, 0, background, backgroundResult));
                world.Quads.Add(new Quad(x, y, 1, foreground, foregroundResult));
                world.Cells[(OriginX + x, OriginY + y)] = TerrainCellPacker.PackCell(sources, x, y);
            }
        }

        // Как TerrainCellBuffers: строка без метаданных (Unloaded их не
        // получает) несёт только cells.json.
        for (int index = 0; index < world.Rows.Length; index++)
        {
            var type = (CellType)index;
            world.Rows[index] = TerrainCellData.PackType(
                type != CellType.Unloaded && lookup.TryGet(type, out CellMetadata metadata)
                    ? TerrainCellPacker.ResolveTypeFields(type, in metadata, atlases)
                    : TerrainCellPacker.ResolveTypeFields(type, default, System.Array.Empty<IAtlasDescriptor>()));
        }

        return world;
    }

    private sealed class VariedMetadata(int seed) : ITerrainMetadataLookup
    {
        public bool TryGet(CellType type, out CellMetadata metadata)
        {
            int t = (byte)type;
            uint hash = Hash(t, seed);
            bool textured = type == CellType.Empty || hash % 7 != 0;
            metadata = new CellMetadata
            {
                IsPopulated = true,
                // Вид — из cells.json, как в TerrainCellMetadataCache.
                Outline = BlockRegistry.Get(type).Outline,
                AtlasIndex = (int)(hash % 3) - 1,
                AtlasRect = textured
                    ? new Vector4((hash % 8) / 16f, ((hash >> 3) % 8) / 16f, 0.0625f * (1 + (hash % 3)), 0.0625f)
                    : Vector4.zero,
                HasTileGroup = hash % 4 == 1 || type is CellType.BuildingWall or CellType.BuildingCorner or CellType.BuildingDoor,
                TileGroupId = (int)(hash % 3),
                DecalTextureIndex = TerrainDecalCatalog.TextureIndex(BlockRegistry.Get(type).DecalTextureName),
                ReliefGroup = BlockRegistry.Get(type).ReliefGroup,
                AnimationFrameCount = 1 + (int)(hash % 4),
                IsTextureReady = textured,
            };
            return true;
        }

        public CachedCellData CellOf(CellType type, TerrainCellState state)
        {
            TryGet(type, out CellMetadata m);
            return new CachedCellData
            {
                State = state,
                Type = type,
                // Признак фона — из cells.json, как в TerrainCellMetadataCache.
                IsBackground = BlockRegistry.Get(type).DrawLayer != CellDrawLayer.Foreground,
                Outline = m.Outline,
                AtlasIndex = m.AtlasIndex,
                AtlasRect = m.AtlasRect,
                HasTileGroup = m.HasTileGroup,
                TileGroupId = m.TileGroupId,
                ReliefGroup = m.ReliefGroup,
                AnimationFrameCount = m.AnimationFrameCount,
                IsTextureReady = m.IsTextureReady,
            };
        }
    }

    private sealed class Grid : ITerrainCellDataSource
    {
        private readonly CachedCellData[,] _cells = new CachedCellData[Width + 2, Height + 2];

        public Grid(VariedMetadata metadata, int seed)
        {
            for (int x = 0; x < Width + 2; x++)
            {
                for (int y = 0; y < Height + 2; y++)
                {
                    uint hash = Hash((CacheMinX + x) * 7919 + (CacheMinY + y), seed);
                    CellType type = s_palette[hash % (uint)s_palette.Length];
                    TerrainCellState state = hash % 97 == 0 ? TerrainCellState.Unloaded : TerrainCellState.Loaded;
                    _cells[x, y] = state == TerrainCellState.Loaded
                        ? metadata.CellOf(type, state)
                        : new CachedCellData
                        {
                            State = state,
                            Type = CellType.Unloaded,
                            // Шейдер берёт контур незагруженной клетки из её строки cells.json.
                            Outline = BlockRegistry.Get(CellType.Unloaded).Outline,
                        };
                }
            }
        }

        public int CacheMinX => OriginX - 1;

        public int CacheMinY => OriginY - 1;

        public int CacheWidth => Width + 2;

        public int CacheHeight => Height + 2;

        public CachedCellData GetCellData(int x, int y) =>
            (uint)x < Width + 2 && (uint)y < Height + 2
                ? _cells[x, y]
                : new CachedCellData { State = TerrainCellState.OutsideWorld, Type = CellType.Unloaded };

        public CachedCellInfo GetCell(int x, int y)
        {
            CachedCellData data = GetCellData(x, y);
            return new CachedCellInfo { Type = data.Type };
        }
    }

    // Непрозрачность разная по типу и по атласу: так проверяется и «в своём
    // атласе», и «хоть в одном».
    private sealed class TestAtlas(int size) : IAtlasDescriptor
    {
        public int Size => size;

        public bool IsFullyOpaque(CellType cellType) => ((byte)cellType * 7 + size) % 3 != 0;
    }

    private static uint Hash(int value, int seed)
    {
        uint hash = unchecked((uint)value * 2654435761u) ^ unchecked((uint)seed * 40503u);
        hash ^= hash >> 15;
        hash = unchecked(hash * 2246822519u);
        return hash ^ (hash >> 13);
    }
}
