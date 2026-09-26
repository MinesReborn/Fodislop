#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.World.Terrain.Background;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World.Terrain;

internal static class TerrainQuadBuilder
{
    private readonly record struct CellRenderProperties(
        Vector4 AtlasRect,
        float UVTileSize,
        CellAnimationType Animation,
        float AnimationSpeed,
        int AnimationFrameCount,
        float FrameHeightTiles,
        bool HasTileGroup,
        int TileGroupID,
        CellConfigProperties Properties,
        Color32 MinimapColor,
        int AtlasIndex);

    /// <summary>
    /// Собрать квад одного слоя клетки в четыре вершины.
    /// </summary>
    ///
    /// Слой решает почти всё: фон берёт тип из карты заливки и остаётся
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
        TerrainPrecalculator precalc = sources.Precalc;
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
            return TerrainQuadResult.None;
        }

        int cx = x + 1;
        int cy = y + 1;
        int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

        CachedCellData ccd = cellCache.GetCellData(cx, cy);
        CellType cellFgType = ccd.Type;
        CellVisualProperties foregroundVisuals = MapCellConfigCatalog.GetVisualProperties(cellFgType);

        bool isDoor = !isBackground && cellFgType == CellType.BuildingDoor;

        if (ccd.State != TerrainCellState.Loaded)
        {
            return TerrainQuadResult.NoAtlas(isDoor);
        }

        bool organicTerrain = precalc.EnableDistortion &&
            precalc.DistortionStyle == TerrainDistortionStyle.Organic &&
            TerrainVertexDistortionCalculator.IsCause(ccd);
        bool organicCause = !isBackground && organicTerrain;
        int organicEdges = 0;
        bool needsOrganicUnderlay = false;
        if (organicTerrain)
        {
            CachedCellData bottomNeighbor = cellCache.GetCellData(cx, cy - 1);
            CachedCellData rightNeighbor = cellCache.GetCellData(cx + 1, cy);
            CachedCellData topNeighbor = cellCache.GetCellData(cx, cy + 1);
            CachedCellData leftNeighbor = cellCache.GetCellData(cx - 1, cy);
            needsOrganicUnderlay =
                IsOrganicEmptyEdge(bottomNeighbor) ||
                IsOrganicEmptyEdge(rightNeighbor) ||
                IsOrganicEmptyEdge(topNeighbor) ||
                IsOrganicEmptyEdge(leftNeighbor);
            if (organicCause)
            {
                int bottom = OrganicEdgeBend(bottomNeighbor, gridX, unityY, false, 1);
                int right = OrganicEdgeBend(rightNeighbor, gridX + 1, unityY, true, -1);
                int top = OrganicEdgeBend(topNeighbor, gridX, unityY + 1, false, -1);
                int left = OrganicEdgeBend(leftNeighbor, gridX, unityY, true, 1);
                organicEdges = TerrainCellGeometry.EncodeOrganicEdges(
                    bottom,
                    right,
                    top,
                    left);
            }
        }

        CellType backgroundType = isBackground
            ? TerrainCellLayers.ResolveBackground(cellFgType, sources.FloodFill.Buffer[x, y], ccd.Properties)
            : cellFgType;

        // Силуэт переднего плана считается до выбора слоя: от него зависит,
        // нужна ли под ним подложка.
        //
        // Общие смещённые рёбра закрывают друг друга. Подложка нужна только
        // там, где Organic врезает открытый край внутрь собственной клетки;
        // внутри сплошного массива второй слой не рисуем.
        bool foregroundFillsCell = !foregroundVisuals.IsRoundableLoose && !needsOrganicUnderlay;

        if (!TerrainCellLayers.TryGetType(
            cellFgType, backgroundType, isBackground, foregroundFillsCell, out CellType cellType))
        {
            return TerrainQuadResult.NoAtlas(isDoor);
        }

        CellVisualProperties cellVisuals = MapCellConfigCatalog.GetVisualProperties(cellType);

        bool isSameCell = !isBackground || cellType == cellFgType;

        CellRenderProperties renderProps = GetRenderProperties(
            isSameCell,
            in ccd,
            cellType,
            sources.MetadataLookup);

        Vector4 atlasRect = renderProps.AtlasRect;
        float uvTileSize = renderProps.UVTileSize;
        CellAnimationType animType = renderProps.Animation;
        float animSpeed = renderProps.AnimationSpeed;
        int animFrames = renderProps.AnimationFrameCount;
        float frameHeight = renderProps.FrameHeightTiles;
        bool hasTileGroup = renderProps.HasTileGroup;
        CellConfigProperties props = renderProps.Properties;
        Color32 minimapColor = renderProps.MinimapColor;
        int atlasIndex = renderProps.AtlasIndex;

        if (atlasIndex < 0 || atlasIndex >= atlases.Count)
        {
            atlasIndex = 0;
        }

        bool hasTexture = atlasRect.z > 0f && atlasRect.w > 0f && uvTileSize > 0f;

        if (!hasTexture)
        {
            atlasRect = Vector4.zero;
            uvTileSize = atlases.Count > 0 ? (1f / atlases[0].Size) : 0f;
            animType = CellAnimationType.None;
            animSpeed = 0f;
            animFrames = 1;
            frameHeight = 1f;
        }

        float zOffset = isBackground ? 0.1f : 0.0f;
        float lx = x * cellSize;
        float ly = y * cellSize;

        Vector3 off00 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x, y].ToVector3();
        Vector3 off10 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x + 1, y].ToVector3();
        Vector3 off01 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x, y + 1].ToVector3();
        Vector3 off11 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x + 1, y + 1].ToVector3();

        TerrainCellGeometry geometry = TerrainCellGeometry.FromOffsets(
            off00,
            off10,
            off11,
            off01);
        float anchorFlag = geometry.IsAnchored || organicCause ? 1f : 0f;

        quad[0].Position = new Vector3(lx, ly, zOffset) + off00;
        quad[1].Position = new Vector3(lx + cellSize, ly, zOffset) + off10;
        quad[2].Position = new Vector3(lx + cellSize, ly + cellSize, zOffset) + off11;
        quad[3].Position = new Vector3(lx, ly + cellSize, zOffset) + off01;

        // Flood-filled backgrounds have their own adjacency. Reusing the
        // foreground descriptor (or zero when the types differ) assigns the
        // wrong autotile variant under exposed edges and organic underlays.
        int descriptor = isBackground
            ? ResolveBackgroundTilingDescriptor(
                sources,
                x,
                y,
                renderProps.HasTileGroup,
                renderProps.TileGroupID)
            : precalc.CellTilingDescriptors[x, y];
        int cornerSideMask = precalc.CellCornerVariants[x, y];
        bool useNeighborVariants =
            !isBackground &&
            cellFgType == CellType.BuildingWall &&
            cornerSideMask != 0;
        float packedW = hasTileGroup || useNeighborVariants ? 1f : 0f;

        if (useNeighborVariants)
        {
            descriptor = ResolveBuildingWallVariant(descriptor, cornerSideMask);
        }

        var uvs = TerrainQuadUvs.Canonical;
        if ((hasTileGroup || useNeighborVariants) && descriptor != 0)
        {
            uvs = uvs.Transform(descriptor);
        }

        quad[0].UV0 = uvs.C0;
        quad[1].UV0 = uvs.C1;
        quad[2].UV0 = uvs.C2;
        quad[3].UV0 = uvs.C3;

        // Текстуры нет — клетка рисуется цветом миникарты и непрозрачной.
        // Это не фолбек, а диагностический вид: так видно, какого типа клетки
        // сервер не отдал, вместо тихой дыры в кадре.
        bool hasAtlasRect = atlasRect.z >= 0.0001f;
        Color color = Color.white;
        if (!hasAtlasRect)
        {
            color = (Color)minimapColor;
            color.a = 1f;
        }

        TerrainAnimationSettings animationSettings =
            TerrainAnimationProfileCatalog.Get(cellType, animSpeed);
        float animOffset = ResolveAnimationOffset(
            animationSettings, animType, hasAtlasRect, gridX, serverY);

        // Любой непустой блок переднего плана — физическая масса: свет обязан
        // поглощаться всеми блоками одинаково, без зависимости от уникальных
        // свойств DropsShadow/Passable (иначе у блоков без DropsShadow
        // occupancy = 0 и свет проходит насквозь).
        // Дороги (Road, GoldenRoad, BuildingRoad, PolymerRoad) не являются
        // физической массой для света — они пропускают его.
        bool isPhysicalMass =
            !isBackground &&
            cellFgType != CellType.Empty &&
            !foregroundVisuals.IsRoad &&
            !foregroundVisuals.IsNonPhysicalMass;
        Vector4 animDataVec = new(
            (float)animType,
            animationSettings.Speed,
            animOffset,
            (float)animationSettings.Profile);
        Vector4 tileSizeVec = new Vector4(uvTileSize, uvTileSize, (float)animFrames, frameHeight);
        // Признак сплошного листа — бит 5 в z, над колонкой тайлгруппы
        // (она занимает биты 0-4). В w его класть нельзя: там значение
        // больше 1.5 уже означает «отбросить», и Terrain.shader вместе с
        // TerrainCellBuilder выкидывали по нему всю породу и все кристаллы.
        int packedColumn = descriptor & 0x1F;
        if (cellVisuals.IsContinuousSheet)
        {
            packedColumn |= 32;
        }

        Vector4 worldPosVec = new Vector4(gridX, serverY, packedColumn, packedW);

        bool isGlowing = (props & CellConfigProperties.Glowing) != 0;

        // Read RGB directly from Color32 bytes — no intermediate Color allocation
        int packedLightingColor = minimapColor.r |
            (minimapColor.g << 8) |
            (minimapColor.b << 16);

        bool hasRoundedPhysicalContour =
            !isBackground && foregroundVisuals.IsRoundableLoose;

        // Маска соседства кладётся и фоновым квадам тоже.
        //
        // ЗАЧЕМ. Клетке пола нужно знать, что над ней твёрдый блок, — иначе
        // шейдеру нечем нарисовать падающую от блока тень, а без тени
        // выдавленность блока читается как обводка, а не как высота. Маска в
        // (x, y) описывает твёрдость соседей этой клетки в переднем плане, что
        // фону и требуется: бит 1 означает «сверху блок».
        //
        // ПОЧЕМУ ЭТО БЕЗОПАСНО. Единственный прежний потребитель битов 0-3 —
        // ветка скругления контура, а она включается флагом isRoundable,
        // который у фона всегда снят. Поле материалов трогает маску только под
        // тем же флагом. Так что до этой правки у фона стоял ноль не по
        // смыслу, а потому что читать его было некому.
        byte solidConnectivityMask = precalc.CellSolidBoundaryMasks[x, y];
        float emissionPower = isGlowing
            ? Mathf.Max(1f / byte.MaxValue, minimapColor.a / 255f)
            : 0f;
        // Кайма рельефа — только у переднего плана: фон её не рисует, а
        // ring-адрес у фонового текселя тот же, и чужой код рельефа въехал бы
        // в соседний слой.
        byte reliefMask = precalc.CellReliefMasks[x, y];
        byte reliefCornerMask = precalc.CellReliefCornerMasks[x, y];
        // Серверная группа определяет наличие фаски. Без проверки группа 0
        // получает reliefCode=1 и рисует фаску по всем четырём сторонам.
        bool hasRelief = !isBackground &&
            ccd.ReliefGroup != 0;
        TerrainLightingData lightingData = TerrainLightingData.Pack(
            solidConnectivityMask,
            isGlowing,
            hasRoundedPhysicalContour,
            isPhysicalMass,
            emissionPower,
            reliefMask,
            hasRelief,
            reliefCornerMask);
        bool hasGroundDecalSurface = TerrainDecalCatalog.IsGroundDecalSurface(
            cellType,
            isBackground);
        bool hasStoneDecalSurface = TerrainDecalCatalog.IsStoneDecalSurface(
            cellType,
            isBackground);
        float decalPlacement = hasGroundDecalSurface
            ? TerrainDecalCatalog.GetGroundPlacement(gridX, serverY)
            : hasStoneDecalSurface
                ? TerrainDecalCatalog.GetStonePlacement(gridX, serverY)
                : 0f;
        Vector4 glowVec = new Vector4(
            packedLightingColor,
            lightingData.PackedFlags,
            lightingData.PackedContour,
            decalPlacement);

        // Surface data is identical at every corner. Quantize it once;
        // keep position, transformed UV0 and geometry anchors per vertex.
        TerrainVertex surface = default;
        surface.Color = color;
        surface.UV1 = atlasRect;
        surface.UV2 = tileSizeVec;
        surface.UV3 = worldPosVec;
        surface.UV4 = animDataVec;
        surface.UV6 = glowVec;
        for (int i = 0; i < 4; i++)
        {
            ref TerrainVertex vertex = ref quad[i];
            vertex.CopySurfaceFrom(in surface);
            Vector2 anchor = geometry.GetCorner(i);
            vertex.UV5 = new Vector4(anchorFlag, anchor.x, anchor.y, organicEdges);
        }

        return new TerrainQuadResult(atlasIndex, isDoor);
    }

    private static int OrganicEdgeBend(
        CachedCellData neighbor,
        int edgeX,
        int edgeY,
        bool vertical,
        int inwardSign)
    {
        if (neighbor.State != TerrainCellState.Loaded ||
            TerrainVertexDistortionCalculator.IsBlock(neighbor) ||
            (!TerrainVertexDistortionCalculator.IsCause(neighbor) && !IsOrganicEmptyEdge(neighbor)))
        {
            return 0;
        }

        int bend = TerrainVertexDistortionCalculator.ComputeOrganicEdgeBend(edgeX, edgeY, vertical);
        return TerrainVertexDistortionCalculator.IsCause(neighbor)
            ? bend
            : Math.Min(Math.Abs(bend), 1) * inwardSign;
    }

    private static int ResolveBackgroundTilingDescriptor(
        in TerrainCellSources sources,
        int x,
        int y,
        bool hasTileGroup,
        int tileGroupId)
    {
        if (!hasTileGroup)
        {
            return 0;
        }

        byte mask = 0;
        if (BackgroundCellMatchesTileGroup(sources, x - 1, y, tileGroupId))
        {
            mask |= 1 << 0;
        }

        if (BackgroundCellMatchesTileGroup(sources, x - 1, y - 1, tileGroupId))
        {
            mask |= 1 << 1;
        }

        if (BackgroundCellMatchesTileGroup(sources, x, y - 1, tileGroupId))
        {
            mask |= 1 << 2;
        }

        if (BackgroundCellMatchesTileGroup(sources, x + 1, y - 1, tileGroupId))
        {
            mask |= 1 << 3;
        }

        if (BackgroundCellMatchesTileGroup(sources, x + 1, y, tileGroupId))
        {
            mask |= 1 << 4;
        }

        if (BackgroundCellMatchesTileGroup(sources, x + 1, y + 1, tileGroupId))
        {
            mask |= 1 << 5;
        }

        if (BackgroundCellMatchesTileGroup(sources, x, y + 1, tileGroupId))
        {
            mask |= 1 << 6;
        }

        if (BackgroundCellMatchesTileGroup(sources, x - 1, y + 1, tileGroupId))
        {
            mask |= 1 << 7;
        }

        return TileBitmaskConverter.GetDescriptor(mask);
    }

    private static bool BackgroundCellMatchesTileGroup(
        in TerrainCellSources sources,
        int x,
        int y,
        int tileGroupId)
    {
        var floodFill = sources.FloodFill.Buffer;
        if ((uint)x >= (uint)floodFill.Width || (uint)y >= (uint)floodFill.Height)
        {
            return false;
        }

        CachedCellData foreground = sources.CellCache.GetCellData(x + 1, y + 1);
        CellType background = TerrainCellLayers.ResolveBackground(
            foreground.Type,
            floodFill[x, y],
            foreground.Properties);
        if (!sources.MetadataLookup.TryGet(background, out CellMetadata metadata))
        {
            throw new InvalidOperationException(
                $"Terrain background metadata for cell type '{background}' was not warmed before tile-group resolution.");
        }

        return metadata.HasTileGroup && metadata.TileGroupID == tileGroupId;
    }

    private static bool IsOrganicEmptyEdge(CachedCellData neighbor) =>
        neighbor.State == TerrainCellState.Loaded &&
        neighbor.Type == CellType.Empty &&
        !TerrainVertexDistortionCalculator.IsBlock(neighbor) &&
        !TerrainVertexDistortionCalculator.IsCause(neighbor);

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
        TerrainAnimationSettings animationSettings,
        CellAnimationType animType,
        bool hasAtlasRect,
        int gridX,
        int serverY)
    {
        if (!hasAtlasRect)
        {
            return animationSettings.PaletteIndex;
        }

        if (animationSettings.Profile == TerrainAnimationProfile.Default &&
            animType == CellAnimationType.Blinking)
        {
            uint seed = HashCell(gridX, serverY);
            return (seed % 6283) / 1000f;
        }

        if (animationSettings.Profile == TerrainAnimationProfile.FacetedCrystal)
        {
            return (HashCell(gridX, serverY) & 0xFFFF) / 65536f;
        }

        return animationSettings.PaletteIndex;
    }

    private static uint HashCell(int gridX, int serverY)
    {
        uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
        seed = (seed ^ (seed >> 13)) * 1274126177;
        return seed ^ (seed >> 16);
    }

    private static CellRenderProperties GetRenderProperties(
        bool isSameCell,
        in CachedCellData ccd,
        CellType cellType,
        ITerrainMetadataLookup metadataLookup)
    {
        if (isSameCell)
        {
            return new CellRenderProperties(
                ccd.AtlasRect,
                ccd.UVTileSize,
                ccd.Animation,
                ccd.AnimationSpeed,
                ccd.AnimationFrameCount,
                ccd.FrameHeightTiles,
                ccd.HasTileGroup,
                ccd.TileGroupID,
                ccd.Properties,
                ccd.MinimapColor,
                ccd.AtlasIndex);
        }

        // Без фолбеков: промах означает, что прогрев не покрыл тип. Тихая
        // подстановка пустой метаданности нарисовала бы правдоподобную
        // подделку вместо того, чтобы показать дефект.
        if (!metadataLookup.TryGet(cellType, out CellMetadata meta))
        {
            throw new InvalidOperationException(
                $"Terrain metadata for cell type '{cellType}' was not warmed before the build.");
        }

        return new CellRenderProperties(
            meta.AtlasRect,
            meta.UVTileSize,
            meta.Animation,
            meta.AnimationSpeed,
            meta.AnimationFrameCount,
            meta.FrameHeightTiles,
            meta.HasTileGroup,
            meta.TileGroupID,
            meta.Properties,
            meta.MinimapColor,
            meta.AtlasIndex);
    }
}
