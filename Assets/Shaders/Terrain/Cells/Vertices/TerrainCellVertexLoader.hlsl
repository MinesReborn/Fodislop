#ifndef KERN_TERRAIN_CELL_VERTEX_LOADER_INCLUDED
#define KERN_TERRAIN_CELL_VERTEX_LOADER_INCLUDED
TerrainCellVertex LoadTerrainCellVertex(
    float3 address,
    float2 cornerBase,
    bool retainBackground)
{
    TerrainCellVertex v = (TerrainCellVertex)0;
    // Слой 2 — накладка дверей: передний план, адрес уже в сетке, смещение
    // видимого окна к нему не прибавляется (меш накладки собран по сетке).
    int layer = (int)round(address.z);
    bool overlay = layer == 2;
    int x = (int)round(address.x) + (overlay ? 0 : (int)round(_TerrainCellViewOffset.x));
    int y = (int)round(address.y) + (overlay ? 0 : (int)round(_TerrainCellViewOffset.y));
    layer = overlay ? 1 : layer;
    int2 cornerStep = (int2)round(cornerBase);
    int corner = cornerStep.y == 0
        ? (cornerStep.x == 0 ? 0 : 1)
        : (cornerStep.x == 1 ? 2 : 3);
    int2 origin = (int2)round(_TerrainCellOrigin.xy);
    int gridX = origin.x + x;
    int unityY = origin.y + y;
    bool foreground = layer > 0;
    v.layer = layer;
    v.atlasIndex = -1.0;

    // Незагруженная клетка рисуется как заглушка Unloaded.
    uint cell = TerrainLoadCell(gridX, unityY);
    uint foregroundType = TerrainCellType(cell);

    // Верхняя граница мира — отдельная сцена, а не клетки RedRock.
    if (foregroundType == 0u && unityY >= (int)round(_TerrainCellOrigin.z))
    {
        return v;
    }

    // An unloaded cell has no background layer. Its foreground placeholder
    // is handled below; resolving an underfill here paints through missing data.
    if (foregroundType == 0u && !foreground)
    {
        return v;
    }

    // Соседи и признаки их передних типов читаются ровно по разу: всё
    // ниже — узлы, маски, кайма, перекрытие, изгибы — берёт их отсюда.
    uint neighbours[8];
    TerrainTypeRow neighbourRows[8];
    [unroll]
    for (int n = 0; n < 8; n++)
    {
        neighbours[n] = TerrainLoadCell(gridX + KERN_TERRAIN_NEIGHBOUR_X[n], unityY + KERN_TERRAIN_NEIGHBOUR_Y[n]);
        neighbourRows[n] = TerrainTypeRowOf(TerrainCellType(neighbours[n]));
    }

    TerrainTypeRow foregroundRow = TerrainTypeRowOf(foregroundType);
    bool foregroundIsBackground = TerrainTypeIsBackground(foregroundRow);
    bool foregroundCause = TerrainTypeWavy(foregroundRow);
    // Фон рисуется собственным типом клетки; под передним планом всегда лежит
    // глобальная подложка, чтобы соседние дороги и полы не проступали под ним.
    uint layerType = foreground
        ? (foregroundIsBackground ? 0u : foregroundType)
        : TerrainTypeUnder(foregroundType, foregroundRow);

    bool unloaded = foreground && foregroundType == 0u;
    if (layerType == 0u && !unloaded)
    {
        return v;
    }

    TerrainTypeRow row = TerrainTypeRowOf(layerType);
    v.reliefGroup = TerrainTypeReliefGroup(row);

    // Смещение геометрии применяется только к переднему плану. В фоновом
    // проходе оно нужно лишь для проверки, закрывает ли передний план фон.
    float4 geometryX = float4(0.0, 1.0, 1.0, 0.0);
    float4 geometryY = float4(0.0, 0.0, 1.0, 1.0);
    bool foregroundAnchored = false;
    if (foregroundCause)
    {
        // Узел — левый нижний угол клетки; клетки вокруг него: tl, tr, bl, br.
        TerrainTypeRow left = neighbourRows[KERN_TERRAIN_LEFT];
        TerrainTypeRow right = neighbourRows[KERN_TERRAIN_RIGHT];
        TerrainTypeRow top = neighbourRows[KERN_TERRAIN_TOP];
        TerrainTypeRow bottom = neighbourRows[KERN_TERRAIN_BOTTOM];
        float2 node00 = TerrainNodeCells(TerrainNode(gridX, unityY,
            left, foregroundRow, neighbourRows[KERN_TERRAIN_BOTTOM_LEFT], bottom));
        float2 node10 = TerrainNodeCells(TerrainNode(gridX + 1, unityY,
            foregroundRow, right, bottom, neighbourRows[KERN_TERRAIN_BOTTOM_RIGHT]));
        float2 node11 = TerrainNodeCells(TerrainNode(gridX + 1, unityY + 1,
            top, neighbourRows[KERN_TERRAIN_TOP_RIGHT], foregroundRow, right));
        float2 node01 = TerrainNodeCells(TerrainNode(gridX, unityY + 1,
            neighbourRows[KERN_TERRAIN_TOP_LEFT], top, left, foregroundRow));
        foregroundAnchored =
            any(node00 != float2(0.0, 0.0)) ||
            any(node10 != float2(0.0, 0.0)) ||
            any(node11 != float2(0.0, 0.0)) ||
            any(node01 != float2(0.0, 0.0));
        if (foreground)
        {
            geometryX += float4(node00.x, node10.x, node11.x, node01.x);
            geometryY += float4(node00.y, node10.y, node11.y, node01.y);
        }
    }

    // Фон под передним планом, закрывающим каждый пиксель клетки,
    // отбрасывается так же, как незаполненный квад (как
    // TerrainForegroundOcclusion.CoversCell до переноса в шейдер): у
    // переднего плана есть текстура, она непрозрачна в своём атласе, контур
    // не скруглён, а смещённая клетка лежит внутри массы — её
    // общие узлы закрывают прямоугольник встык с соседями.
    if (!foreground)
    {
        bool occluded = !foregroundIsBackground &&
            TerrainTypeHasAtlasRect(foregroundRow) &&
            !TerrainTypeRound(foregroundRow) &&
            TerrainTypeOpaqueOwn(foregroundRow);
        if (occluded && foregroundAnchored)
        {
            [unroll]
            for (int m = 0; m < 8; m++)
            {
                occluded = occluded && TerrainOpaqueMassCell(neighbourRows[m]);
            }
        }

#if defined(KERN_TERRAIN_DEBUG_BACKGROUND_TILE_VIEW)
        occluded = occluded && _TerrainDebugBackgroundTileIdentity == 0;
#endif
        bool exposedPresentationGround = false;
        if (occluded && retainBackground)
        {
            // A raised solid mass still covers its interior. Keep the ground
            // only near openings, including diagonal openings reached by the
            // bounded half-cell displacement. Field consumers do not enter here.
            [unroll]
            for (int groundNeighbour = 0; groundNeighbour < 8; groundNeighbour++)
            {
                exposedPresentationGround = exposedPresentationGround ||
                    TerrainTypeIsBackground(neighbourRows[groundNeighbour]);
            }
        }
        if (occluded && (!retainBackground || !exposedPresentationGround))
        {
            return v;
        }
    }

    // Дескриптор автотайла: передний план — по типам соседей и стене пака,
    // фон — по фоновым типам соседей (в кайме фона нет, как и в
    // TerrainBackgroundTileResolver за окном).
    TerrainTypeRow layerRows[8];
    [unroll]
    for (int t = 0; t < 8; t++)
    {
        if (foreground)
        {
            layerRows[t] = neighbourRows[t];
        }
        else
        {
            layerRows[t] = TerrainTypeRowOf(TerrainTypeUnder(TerrainCellType(neighbours[t]), neighbourRows[t]));
        }
    }

    uint descriptor = TerrainTilingDescriptor(row, layerRows);
    bool tiling = TerrainTypeHasTileGroup(row);
    if (foreground && TerrainTypeWall(row))
    {
        uint cornerSideMask =
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_LEFT]) ? 1u : 0u) |
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_RIGHT]) ? 2u : 0u) |
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_TOP]) ? 4u : 0u) |
            (TerrainTypeCorner(neighbourRows[KERN_TERRAIN_BOTTOM]) ? 8u : 0u);
        if (cornerSideMask != 0u)
        {
            descriptor = TerrainBuildingWallVariant(descriptor, cornerSideMask);
            tiling = true;
        }
    }

    uint uvBits = TerrainCornerUvBits(descriptor, tiling);
    v.uvBits = uvBits;
    v.uv = float2(
        (uvBits >> (corner * KERN_TERRAIN_UV_BITS_PER_CORNER)) & 1u,
        (uvBits >> (corner * KERN_TERRAIN_UV_BITS_PER_CORNER + 1)) & 1u);
    v.atlasIndex = (float)TerrainTypeSlot(row);

    // Мировая клетка — от адреса: высота мира переводит строку Unity в
    // серверную, как CoordinateUtils.UnityToServerY.
    int serverY = (int)round(_TerrainCellOrigin.z) - 1 - unityY;
    float tileSize = TerrainTypeTileSize(row);
    v.subAtlasRect = TerrainTypeAtlasRect(row);
    v.tileSizeUV = float4(tileSize, tileSize, TerrainTypeFrameCount(row), TerrainTypeFrameHeight(row));
    v.worldPos = float4(
        gridX,
        serverY,
        (float)((descriptor & KERN_TERRAIN_COLUMN_MASK) | (TerrainTypeTextureAnchor(row) == KERN_TERRAIN_TEXTURE_ANCHOR_WORLD ? KERN_TERRAIN_COLUMN_WORLD_TEXTURE_ANCHOR : 0u)),
        tiling ? 1.0 : 0.0);
    v.animData = float4(
        (float)TerrainTypeAnimationType(row),
        TerrainTypeFrameSpeed(row),
        TerrainAnimationPhase(row, TerrainTypeHasAtlasRect(row), gridX, serverY),
        0.0);

    // Соседи-блоки — общие для слоёв (TerrainCellMaskRules): сверху,
    // слева, снизу, справа.
    uint foregroundSides =
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_TOP]) ? 0u : 1u) |
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_LEFT]) ? 0u : 2u) |
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_BOTTOM]) ? 0u : 4u) |
        (TerrainTypeIsBackground(neighbourRows[KERN_TERRAIN_RIGHT]) ? 0u : 8u);

    // Кайма — только у переднего плана с ненулевой группой каймы
    // (TerrainCellMaskCalculator.CalculateRimMasks): стороны, где сосед —
    // та же поверхность, и вогнутые углы.
    uint contour = 0u;
    if (foreground)
    {
        contour = TerrainTypeRound(row) ? KERN_TERRAIN_ROUNDABLE_CONTOUR_FLAG : 0u;
        if (TerrainTypeReliefGroup(row) != 0u)
        {
            bool top = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_TOP]);
            bool left = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_LEFT]);
            bool bottom = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_BOTTOM]);
            bool right = TerrainSameRim(row, neighbourRows[KERN_TERRAIN_RIGHT]);
            uint rimMask = (top ? 1u : 0u) | (left ? 2u : 0u) | (bottom ? 4u : 0u) | (right ? 8u : 0u);
            uint rimCorners =
                (bottom && left && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_BOTTOM_LEFT]) ? 1u : 0u) |
                (bottom && right && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_BOTTOM_RIGHT]) ? 2u : 0u) |
                (top && right && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_TOP_RIGHT]) ? 4u : 0u) |
                (top && left && !TerrainSameRim(row, neighbourRows[KERN_TERRAIN_TOP_LEFT]) ? 8u : 0u);
            contour += ((rimMask + 1u) << KERN_TERRAIN_RIM_SHIFT) +
                (rimCorners << KERN_TERRAIN_RIM_CORNERS_SHIFT);
        }
    }

    // Флаги света — раскладка TerrainLightingData; масса — блок
    // передний план.
    uint lightingFlags = foregroundSides |
        (TerrainTypeGlows(row) ? KERN_TERRAIN_GLOW_FLAG : 0u) |
        (foregroundType != 0u && foreground && !TerrainTypeIsBackground(row) ? KERN_TERRAIN_PHYSICAL_MASS_FLAG : 0u);
    uint decalTextureIndex = TerrainTypeDecalTextureIndex(row);
    float decalPlacement = TerrainDecalPlacement(decalTextureIndex, gridX, serverY);
    v.lightContourDecal = float4(
        decalPlacement != 0.0 ? (float)decalTextureIndex : 0.0,
        (float)lightingFlags + TerrainTypeGlow(row) * KERN_TERRAIN_GLOW_FRACTION_SCALE,
        (float)contour,
        decalPlacement);

    if (!foreground)
    {
        geometryX = float4(0.0, 1.0, 1.0, 0.0);
        geometryY = float4(0.0, 0.0, 1.0, 1.0);
    }

    bool anchored = foreground && foregroundAnchored;
    // Rasterize a carrier enclosing the ENTIRE pixel silhouette. Rasterizing
    // the displaced polygon first loses fragments on the outward half of every
    // staircase; fragment clipping cannot bring those fragments back.
    // Corners are cell-local, so the interpolant and POSITION use one scale.
    float2 carrierMin = float2(0.0, 0.0);
    float2 carrierMax = float2(1.0, 1.0);
    if (anchored)
    {
        float2 firstCorner = TerrainGeometryCorner(geometryX, geometryY, 0);
        float2 boundsMin = firstCorner;
        float2 boundsMax = firstCorner;
        [unroll]
        for (int corner = 1; corner < KERN_TERRAIN_SIDE_COUNT; corner++)
        {
            float2 finalCorner = TerrainGeometryCorner(geometryX, geometryY, corner);
            boundsMin = min(boundsMin, finalCorner);
            boundsMax = max(boundsMax, finalCorner);
        }

        carrierMin = boundsMin;
        carrierMax = boundsMax;
    }
    // Only the AO field extends its raster carrier for signed-distance filter
    // support. Screen and material passes cannot inherit this global state.
    float2 carrierPadding = float2(0.0, 0.0);
#if defined(KERN_TERRAIN_AO_FIELD)
    carrierPadding = max(_TerrainGeometryCarrierPaddingWorld, float2(0.0, 0.0)) /
        max(_TerrainCellGridSize.z, 0.0001);
#endif
    carrierMin -= carrierPadding;
    carrierMax += carrierPadding;
    float2 carrierCorner = lerp(carrierMin, carrierMax, cornerBase);
    v.packedData = float4(anchored ? 1.0 : 0.0, carrierCorner, 0.0);
    v.geometryCornersX = anchored
        ? geometryX
        : float4(0.0, 1.0, 1.0, 0.0);
    v.geometryCornersY = anchored
        ? geometryY
        : float4(0.0, 0.0, 1.0, 1.0);
    float cellSize = _TerrainCellGridSize.z;
    v.positionOS = float3(
        (x + carrierCorner.x) * cellSize,
        (y + carrierCorner.y) * cellSize,
        layer == 0 ? 0.1 : 0.0);
    return v;
}

// Physical fields and existing consumers retain their original culling.
TerrainCellVertex LoadTerrainCellVertex(float3 address, float2 cornerBase)
{
    return LoadTerrainCellVertex(address, cornerBase, false);
}

// Клип-позиция, которую растеризатор отбрасывает целиком.
float4 TerrainCulledPosition()
{
    return float4(2.0, 2.0, 2.0, 1.0);
}


#endif // KERN_TERRAIN_CELL_VERTEX_LOADER_INCLUDED
