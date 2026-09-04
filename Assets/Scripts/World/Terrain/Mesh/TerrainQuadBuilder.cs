#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Fodinae.World.Terrain;

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
        CellConfigProperties Properties,
        Color32 MinimapColor,
        int AtlasIndex);

    public static bool IsBuildingBlock(CellType type)
    {
        return type is CellType.BuildingWall or
            CellType.BuildingDoor or
            CellType.BuildingCorner;
    }

    public static int FillQuadData(
        TerrainVertex[] vertexBuffer,
        bool[] foregroundOverlayFlags,
        float cellSize,
        int x,
        int y,
        int gridX,
        int unityY,
        TerrainCellCache cellCache,
        TerrainPrecalculator precalc,
        BackgroundFloodFill bgFloodFill,
        int worldWidth,
        int worldHeight,
        bool isBackground,
        int vIdx,
        IReadOnlyList<IAtlasDescriptor> atlases,
        bool useColorLod,
        MapManager mapManager,
        ITextureService textureManager)
    {
        if (unityY < 0 || unityY >= worldHeight || gridX < 0 || gridX >= worldWidth)
        {
            return -1;
        }

        int cx = x + 1;
        int cy = y + 1;
        int serverY = CoordinateUtils.UnityToServerY(unityY, worldHeight);

        CachedCellData ccd = cellCache.GetCellData(cx, cy);
        CellType cellFgType = ccd.Type;

        if (!isBackground)
        {
            foregroundOverlayFlags[vIdx / 8] = cellFgType == CellType.BuildingDoor;
        }

        if (ccd.State != TerrainCellState.Loaded)
        {
            return -1;
        }

        CellType cellType = isBackground ? bgFloodFill.Buffer[x, y] : cellFgType;

        if (isBackground && IsBuildingBlock(cellFgType))
        {
            cellType = CellType.Road;
        }

        bool isSameCell = !isBackground || cellType == cellFgType;

        if (isBackground && (cellType == cellFgType || cellType == CellType.Unloaded))
        {
            return -1;
        }

        CellRenderProperties renderProps = GetRenderProperties(
            isSameCell,
            in ccd,
            cellType,
            cellCache,
            mapManager,
            textureManager,
            atlases);

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

        Vector3 off00 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x, y];
        Vector3 off10 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x + 1, y];
        Vector3 off01 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x, y + 1];
        Vector3 off11 = isBackground ? Vector3.zero : precalc.GridVertexOffsets[x + 1, y + 1];

        bool isAnchored = !isBackground && (off00 != Vector3.zero || off10 != Vector3.zero || off01 != Vector3.zero || off11 != Vector3.zero);
        float anchorFlag = isAnchored ? 1f : 0f;
        Vector2 anchor0 = isAnchored ? new Vector2(off00.x, off00.y) : new Vector2(0f, 0f);
        Vector2 anchor1 = isAnchored ? new Vector2(1f + off10.x, off10.y) : new Vector2(1f, 0f);
        Vector2 anchor2 = isAnchored ? new Vector2(1f + off11.x, 1f + off11.y) : new Vector2(1f, 1f);
        Vector2 anchor3 = isAnchored ? new Vector2(off01.x, 1f + off01.y) : new Vector2(0f, 1f);

        vertexBuffer[vIdx + 0].Position = new Vector3(lx, ly, zOffset) + off00;
        vertexBuffer[vIdx + 1].Position = new Vector3(lx + cellSize, ly, zOffset) + off10;
        vertexBuffer[vIdx + 2].Position = new Vector3(lx + cellSize, ly + cellSize, zOffset) + off11;
        vertexBuffer[vIdx + 3].Position = new Vector3(lx, ly + cellSize, zOffset) + off01;

        Vector2 uv0 = new Vector2(0, 0);
        Vector2 uv1 = new Vector2(1, 0);
        Vector2 uv2 = new Vector2(1, 1);
        Vector2 uv3 = new Vector2(0, 1);

        int descriptor = isSameCell ? precalc.CellTilingDescriptors[x, y] : 0;
        int cornerSideMask = precalc.CellCornerVariants[x, y];
        bool useNeighborVariants =
            !isBackground &&
            cellFgType == CellType.BuildingWall &&
            cornerSideMask != 0;
        float packedW = hasTileGroup || useNeighborVariants ? 1f : 0f;

        if (useNeighborVariants)
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

            descriptor = transforms | (column & 0x1F);
        }

        if ((hasTileGroup || useNeighborVariants) && descriptor != 0)
        {
            if ((descriptor & 0x40) != 0)
            {
                (uv0.x, uv1.x) = (uv1.x, uv0.x);
                (uv3.x, uv2.x) = (uv2.x, uv3.x);
            }

            if ((descriptor & 0x20) != 0)
            {
                (uv0.y, uv3.y) = (uv3.y, uv0.y);
                (uv1.y, uv2.y) = (uv2.y, uv1.y);
            }

            if ((descriptor & 0x80) != 0)
            {
                Vector2 t = uv0;
                uv0 = uv1;
                uv1 = uv2;
                uv2 = uv3;
                uv3 = t;
            }
        }

        vertexBuffer[vIdx + 0].UV0 = uv0;
        vertexBuffer[vIdx + 1].UV0 = uv1;
        vertexBuffer[vIdx + 2].UV0 = uv2;
        vertexBuffer[vIdx + 3].UV0 = uv3;

        bool useFallback = useColorLod || atlasRect.z < 0.0001f;
        Color color = useFallback ? (Color)minimapColor : Color.white;

        if (atlasRect.z < 0.0001f)
        {
            color.a = 1f;
        }

        float animOffset = 0f;

        if (!useFallback && animType == CellAnimationType.Blinking)
        {
            uint seed = (uint)((gridX * 374761397) + (serverY * 668265263));
            seed = (seed ^ (seed >> 13)) * 1274126177;
            seed = seed ^ (seed >> 16);
            animOffset = (seed % 6283) / 1000f;
        }

        // Любой непустой блок переднего плана — физическая масса: свет обязан
        // поглощаться всеми блоками одинаково, без зависимости от уникальных
        // свойств DropsShadow/Passable (иначе у блоков без DropsShadow
        // occupancy = 0 и свет проходит насквозь).
        bool isPhysicalMass =
            !isBackground &&
            cellFgType != CellType.Empty;
        Vector4 animDataVec = new(
            (float)animType,
            animSpeed,
            animOffset,
            0f);
        Vector4 tileSizeVec = new Vector4(uvTileSize, uvTileSize, (float)animFrames, frameHeight);
        Vector4 worldPosVec = new Vector4(gridX, serverY, descriptor & 0x1F, packedW);

        bool isGlowing = (props & CellConfigProperties.Glowing) != 0;

        // Read RGB directly from Color32 bytes — no intermediate Color allocation
        int packedLightingColor = minimapColor.r |
            (minimapColor.g << 8) |
            (minimapColor.b << 16);

        float glowFlags = 0f;

        if (isGlowing)
        {
            glowFlags += 1f;
        }

        if (!isBackground && MapManager.IsRoundableLoose(cellFgType))
        {
            glowFlags += 2f;
        }

        byte solidConnectivityMask = !isBackground && isSameCell
            ? precalc.CellSolidBoundaryMasks[x, y]
            : (byte)0;
        float solidBoundaryMask = solidConnectivityMask & 15;
        float solidDiagonalMask = solidConnectivityMask >> 4;
        bool hasRoundedPhysicalContour =
            !isBackground && MapManager.IsRoundableLoose(cellFgType);
        float emissionPower = isGlowing
            ? Mathf.Max(1f / byte.MaxValue, minimapColor.a / 255f)
            : 0f;
        float packedLightingFlags = solidBoundaryMask +
            (isGlowing ? 16f : 0f) +
            (hasRoundedPhysicalContour ? 32f : 0f) +
            (isPhysicalMass ? 64f : 0f) +
            (emissionPower * 0.25f);
        Vector4 glowVec = new Vector4(
            packedLightingColor,
            packedLightingFlags,
            glowFlags + (solidDiagonalMask * 4f),
            0f);

        ReadOnlySpan<Vector2> anchors = [anchor0, anchor1, anchor2, anchor3];

        for (int i = 0; i < 4; i++)
        {
            ref TerrainVertex vertex = ref vertexBuffer[vIdx + i];
            vertex.Color = color;
            vertex.UV1 = atlasRect;
            vertex.UV2 = tileSizeVec;
            vertex.UV3 = worldPosVec;
            vertex.UV4 = animDataVec;
            vertex.UV5 = new Vector4(anchorFlag, anchors[i].x, anchors[i].y, 0f);
            vertex.UV6 = glowVec;
        }

        return atlasIndex;
    }

    private static CellRenderProperties GetRenderProperties(
        bool isSameCell,
        in CachedCellData ccd,
        CellType cellType,
        TerrainCellCache cellCache,
        MapManager mapManager,
        ITextureService textureManager,
        IReadOnlyList<IAtlasDescriptor> atlases)
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
                ccd.Properties,
                ccd.MinimapColor,
                ccd.AtlasIndex);
        }

        CellMetadata meta = cellCache.GetMetadata(cellType, mapManager, textureManager, atlases);

        return new CellRenderProperties(
            meta.AtlasRect,
            meta.UVTileSize,
            meta.Animation,
            meta.AnimationSpeed,
            meta.AnimationFrameCount,
            meta.FrameHeightTiles,
            meta.HasTileGroup,
            meta.Properties,
            meta.MinimapColor,
            meta.AtlasIndex);
    }
}
