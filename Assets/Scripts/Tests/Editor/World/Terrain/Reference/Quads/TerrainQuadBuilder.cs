#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

// Прежняя CPU-сборка квада в вершины — эталон вида для тестов
// эквивалентности. Рабочий путь вершин больше не строит: шейдер собирает квад
// из буфера клеток (TerrainCellData.hlsl), а этот код замороженно
// воспроизводит то, что раньше уходило на GPU вершинами.
internal static class TerrainQuadBuilder
{
    /// <summary>
    /// Собрать квад одного слоя клетки в четыре вершины.
    /// </summary>
    ///
    /// Буфер клеток вершин не хранит: шейдер собирает квад из клетки и её
    /// соседей. Вершины нужны накладке дверей и тестам-оракулам, для которых
    /// это и есть эталон вида.
    ///
    /// Слой решает почти всё: фон берёт тип подложки и остаётся
    /// прямоугольным, передний план берёт тип клетки и несёт смещённую
    /// геометрию, свет и кайму. Поэтому слой — это перечисление, а не булево
    /// «isBackground» с индексом вершины, по которому раньше приходилось
    /// угадывать, в какую половину буфера пишут.
    public static TerrainQuadResult FillQuad(
        in TerrainCellSources sources,
        in TerrainQuadSite site,
        TerrainQuadLayer layer,
        Span<TerrainVertex> quad)
    {
        ITerrainCellDataSource cellCache = sources.CellCache;
        TerrainDistortionSettings distortion = sources.Distortion;
        IReadOnlyList<IAtlasDescriptor> atlases = sources.Atlases;
        int worldWidth = sources.WorldWidth;
        int worldHeight = sources.WorldHeight;
        int x = site.LocalX;
        int y = site.LocalY;
        int gridX = site.GridX;
        int unityY = site.UnityY;
        float cellSize = site.CellSize;
        bool isBackground = layer == TerrainQuadLayer.Background;

        if (unityY < 0 || unityY >= worldHeight || gridX < 0 || gridX >= worldWidth)
        {
            quad.Clear();
            return TerrainQuadResult.None;
        }

        int cx = x + 1;
        int cy = y + 1;
        int serverY = WorldCoordinateConverter.UnityToServerY(unityY, worldHeight);

        CachedCellData ccd = cellCache.GetCellData(cx, cy);
        CellType cellFgType = ccd.Type;
        BlockDefinition foregroundBlock = BlockRegistry.Get(cellFgType);

        bool isDoor = !isBackground && foregroundBlock.Outline == CellOutline.Door;

        if (ccd.State != TerrainCellState.Loaded)
        {
            quad.Clear();
            return TerrainQuadResult.NoAtlas(isDoor);
        }

        // Соседство клетки: маски, кайма и автотайл считаются здесь же — тем
        // же правилом, что шейдер выводит из соседей в буфере клеток.
        CachedCellData left = cellCache.GetCellData(cx - 1, cy);
        CachedCellData bottomLeft = cellCache.GetCellData(cx - 1, cy - 1);
        CachedCellData bottom = cellCache.GetCellData(cx, cy - 1);
        CachedCellData bottomRight = cellCache.GetCellData(cx + 1, cy - 1);
        CachedCellData right = cellCache.GetCellData(cx + 1, cy);
        CachedCellData topRight = cellCache.GetCellData(cx + 1, cy + 1);
        CachedCellData top = cellCache.GetCellData(cx, cy + 1);
        CachedCellData topLeft = cellCache.GetCellData(cx - 1, cy + 1);



        // Фон (Background или Underlay) лежит сам на себе. Под передним планом
        // всегда используется глобальная подложка, не соседняя дорога или пол.
        CellType backgroundType = UnderOf(cellFgType);
        bool floor = backgroundType == cellFgType;
        CellType cellType = isBackground ? backgroundType : cellFgType;
        if ((floor && !isBackground) || cellType == CellType.Unloaded)
        {
            quad.Clear();
            return TerrainQuadResult.NoAtlas(isDoor);
        }

        TerrainTypeFields type = TerrainCellPacker.ResolveTypeFields(
            cellType,
            GetMetadata(cellType, sources.MetadataLookup),
            atlases);
        BlockDefinition block = type.Block;
        int atlasIndex = type.Slot;

        // Shared lattice offsets describe the deformable mass, not every
        // surface sharing its vertices. Roads/ground can occupy the foreground
        // layer too; a neighboring Cause must not warp their carrier or UVs.
        bool applyDistortion = !isBackground && distortion.EnableDistortion &&
            TerrainVertexDistortionCalculator.IsWavy(ccd);
        Vector3 off00 = applyDistortion ? Node(sources, x, y) : Vector3.zero;
        Vector3 off10 = applyDistortion ? Node(sources, x + 1, y) : Vector3.zero;
        Vector3 off01 = applyDistortion ? Node(sources, x, y + 1) : Vector3.zero;
        Vector3 off11 = applyDistortion ? Node(sources, x + 1, y + 1) : Vector3.zero;

        TerrainCellGeometry geometry = TerrainCellGeometry.FromOffsets(
            off00,
            off10,
            off11,
            off01);
        bool anchored = geometry.IsAnchored;

        // Backgrounds have their own adjacency. Reusing the
        // foreground descriptor (or zero when the types differ) assigns the
        // wrong autotile variant under exposed edges and distorted underlays.
        int descriptor = isBackground
            ? TerrainBackgroundTileResolver.ResolveDescriptor(
                sources,
                x,
                y,
                type.HasTileGroup,
                type.TileGroupId)
            : TerrainCellMaskCalculator.CalculateTilingDescriptor(
                ccd, left, bottomLeft, bottom, bottomRight, right, topRight, top, topLeft);
        int cornerSideMask = TerrainCellMaskCalculator.CalculateCornerSideMask(ccd, left, right, top, bottom);
        bool useNeighborVariants =
            !isBackground &&
            foregroundBlock.Outline == CellOutline.Wall &&
            cornerSideMask != 0;
        bool tiling = type.HasTileGroup || useNeighborVariants;

        if (useNeighborVariants)
        {
            descriptor = ResolveBuildingWallVariant(descriptor, cornerSideMask);
        }

        bool hasRoundedPhysicalContour = !isBackground && block.Outline == CellOutline.Round;

        // Маска соседства кладётся и фоновым квадам тоже.
        //
        // ЗАЧЕМ. Клетке пола нужно знать, что над ней блок, — иначе
        // шейдеру нечем нарисовать падающую от блока тень, а без тени
        // выдавленность блока читается как обводка, а не как высота. Маска в
        // (x, y) описывает какие соседи — блоки этой клетки в переднем плане, что
        // фону и требуется: бит 1 означает «сверху блок».
        //
        // ПОЧЕМУ ЭТО БЕЗОПАСНО. Единственный прежний потребитель битов 0-3 —
        // ветка скругления контура, а она включается флагом isRoundable,
        // который у фона всегда снят. Поле материалов трогает маску только под
        // тем же флагом. Так что до этой правки у фона стоял ноль не по
        // смыслу, а потому что читать его было некому.
        byte foregroundSides = TerrainCellMaskCalculator.CalculateForegroundSidesMask(top, left, bottom, right);
        // Кайма — только у переднего плана: фон её не рисует, а
        // ring-адрес у фонового текселя тот же, и чужой код каймы въехал бы
        // в соседний слой.
        TerrainCellMaskCalculator.CalculateRimMasks(
            ccd, top, left, bottom, right, topLeft, topRight, bottomLeft, bottomRight,
            out byte rimMask,
            out byte rimCornerMask);
        // Серверная группа определяет наличие фаски. Без проверки группа 0
        // получает rimCode=1 и рисует фаску по всем четырём сторонам.
        bool hasRim = !isBackground &&
            ccd.ReliefGroup != 0;
        // Масса для света — передний план-блок.
        bool isPhysicalMass = !isBackground && block.DrawLayer == CellDrawLayer.Foreground;
        byte glow = block.Emission ? (byte)255 : (byte)0;
        TerrainLightingData lightingData = TerrainLightingData.Pack(
            foregroundSides,
            glow != 0,
            hasRoundedPhysicalContour,
            isPhysicalMass,
            block.Emission ? 1f : 0f,
            rimMask,
            hasRim,
            rimCornerMask);
        float zOffset = isBackground ? 0.1f : 0.0f;
        float lx = x * cellSize;
        float ly = y * cellSize;
        quad[0].Position = new Vector3(lx, ly, zOffset) + off00;
        quad[1].Position = new Vector3(lx + cellSize, ly, zOffset) + off10;
        quad[2].Position = new Vector3(lx + cellSize, ly + cellSize, zOffset) + off11;
        quad[3].Position = new Vector3(lx, ly + cellSize, zOffset) + off01;

        var uvs = TerrainQuadUvs.Canonical;
        if (tiling && descriptor != 0)
        {
            uvs = uvs.Transform(descriptor);
        }

        quad[0].UV0 = uvs.C0;
        quad[1].UV0 = uvs.C1;
        quad[2].UV0 = uvs.C2;
        quad[3].UV0 = uvs.C3;

        // Текстуры нет — шейдер рисует клетку диагностическим шумом по
        // мировой клетке (SampleMissingTexture), а не тихой дырой в кадре.
        float animOffset = ResolveAnimationOffset(block, type.HasAtlasRect, gridX, serverY);

        // Единый режим эффекта, его параметры и скорость — из строки типа.
        Vector4 animDataVec = new(
            (float)block.AnimationType,
            TerrainCellData.HalfValue(TerrainCellData.HalfBits(block.FrameAnimationSpeed)),
            animOffset,
            0f);
        Vector4 tileSizeVec = new Vector4(type.TileSize, type.TileSize, (float)type.FrameCount, type.FrameHeightTiles);
        // Признак текстуры по миру — бит 5 в z, над колонкой тайлгруппы
        // (она занимает биты 0-4). В w его класть нельзя: там значение
        // больше 1.5 уже означает «отбросить», и Terrain.shader вместе с
        // TerrainCellBuilder выкидывали по нему всю породу и все кристаллы.
        int packedColumn = descriptor & 0x1F;
        if (block.TextureAnchor == CellTextureAnchor.World)
        {
            packedColumn |= 32;
        }

        Vector4 worldPosVec = new Vector4(gridX, serverY, packedColumn, tiling ? 1f : 0f);

        float decalPlacement = TerrainDecalCatalog.Place(type.DecalTextureIndex, gridX, serverY);
        Vector4 glowVec = new Vector4(
            decalPlacement != 0f ? type.DecalTextureIndex : 0f,
            lightingData.PackedFlags,
            lightingData.PackedContour,
            decalPlacement);

        // Surface data is identical at every corner. Quantize it once;
        // keep position, transformed UV0 and geometry anchors per vertex.
        TerrainVertex surface = default;
        surface.UV1 = type.AtlasRect;
        surface.UV2 = tileSizeVec;
        surface.UV3 = worldPosVec;
        surface.UV4 = animDataVec;
        surface.UV6 = glowVec;
        for (int i = 0; i < 4; i++)
        {
            ref TerrainVertex vertex = ref quad[i];
            vertex.CopySurfaceFrom(in surface);
            Vector2 anchor = geometry.GetCorner(i);
            vertex.UV5 = new Vector4(anchored ? 1f : 0f, anchor.x, anchor.y, 0f);
        }

        return new TerrainQuadResult(atlasIndex, isDoor);
    }

    private static Vector3 Node(in TerrainCellSources sources, int x, int y) =>
        TerrainVertexDistortionCalculator.ComputeNode(
            sources.CellCache, sources.Distortion, x, y, sources.WorldWidth, sources.WorldHeight).ToVector3();


    /// <summary>
    /// Вариант стены здания по соседним углам.
    /// </summary>
    ///
    /// Стена выбирает колонку тайла по числу примыкающих углов, а отражения
    /// берёт из собственного дескриптора автотайлинга. Одиночный угол справа
    /// или снизу — это тот же тайл, отражённый по горизонтали; два угла по
    /// вертикали — повёрнутый.
    private static int ResolveBuildingWallVariant(int descriptor, int cornerSideMask)
    {
        bool hasLeft = (cornerSideMask & 1) != 0;
        bool hasRight = (cornerSideMask & 2) != 0;
        bool hasTop = (cornerSideMask & 4) != 0;
        bool hasBottom = (cornerSideMask & 8) != 0;
        int cornerCount =
            (hasLeft ? 1 : 0) +
            (hasRight ? 1 : 0) +
            (hasTop ? 1 : 0) +
            (hasBottom ? 1 : 0);
        int column = RenderingConstants.BUILDING_WALL_VARIANT_BASE_TILE +
            Math.Min(cornerCount, 2);
        byte transforms = (byte)(descriptor & 0xE0);

        if ((cornerCount == 1 && hasRight) ||
            (cornerCount == 1 && hasBottom))
        {
            transforms ^= 0x40;
        }

        if (cornerCount >= 2 && !hasLeft && !hasRight)
        {
            transforms ^= 0x80;
        }

        return transforms | (column & 0x1F);
    }

    /// <summary>
    /// Фаза анимации клетки: константа профиля или разброс по её координате.
    /// </summary>
    ///
    /// Разброс нужен, чтобы соседние клетки одного типа не мигали и не
    /// переливались в такт. Он детерминирован от мировой координаты, поэтому
    /// одна и та же клетка всегда получает одну и ту же фазу — при сдвиге
    /// окна и при пересборке она не перескакивает.
    ///
    /// Без текстуры разброса нет: клетка рисуется плоским цветом миникарты, и
    /// анимировать в ней нечего.
    private static float ResolveAnimationOffset(
        BlockDefinition block,
        bool hasAtlasRect,
        int gridX,
        int serverY)
    {
        if (!hasAtlasRect)
        {
            return block.AnimationPalette;
        }

        if (block.AnimationType == CellAnimationType.Blinking)
        {
            uint seed = HashCell(gridX, serverY);
            return (seed % 6283) / 1000f;
        }

        if (block.AnimationType == CellAnimationType.Faceted)
        {
            return (HashCell(gridX, serverY) & 0xFFFF) / 65536f;
        }

        return block.AnimationPalette;
    }

    internal static CellType UnderOf(CellType type) =>
        BlockRegistry.Get(type).DrawLayer != CellDrawLayer.Foreground ? type : BlockRegistry.UnderlayType;

    private static uint HashCell(int gridX, int serverY)
    {
        uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
        seed = (seed ^ (seed >> 13)) * 1274126177;
        return seed ^ (seed >> 16);
    }

    // Без фолбеков: промах означает, что прогрев не покрыл тип. Тихая
    // подстановка пустой метаданности нарисовала бы правдоподобную подделку
    // вместо того, чтобы показать дефект.
    private static CellMetadata GetMetadata(CellType cellType, ITerrainMetadataLookup metadataLookup) =>
        metadataLookup.TryGet(cellType, out CellMetadata meta)
            ? meta
            : throw new InvalidOperationException(
                $"Terrain metadata for cell type '{cellType}' was not warmed before the build.");

}
