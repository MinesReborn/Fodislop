#ifndef KERN_TERRAIN_CELL_DATA_INCLUDED
#define KERN_TERRAIN_CELL_DATA_INCLUDED

// Квад террейна из текстур данных клетки (TerrainCellDataTextures).
//
// Меш идентификаторов (TerrainCellIdMesh) несёт в POSITION адрес квада
// (x, y, слой), а в TEXCOORD0 угол. Здесь восстанавливаются ровно те
// атрибуты, что TerrainQuadBuilder писал в вершину: раскладка текселей
// описана в TerrainCellDataPacker.

Texture2D<float4> _TerrainCellColor;
Texture2D<float4> _TerrainCellMeta;
Texture2D<float4> _TerrainCellAtlasRect;
Texture2D<float4> _TerrainCellTileSize;
Texture2D<float4> _TerrainCellAnimation;
Texture2D<float4> _TerrainCellWorld;
Texture2D<float4> _TerrainCellGlow;
Texture2D<float4> _TerrainCellGeometryX;
Texture2D<float4> _TerrainCellGeometryY;

// x, y — размер сетки в клетках; z — размер клетки в мире.
float4 _TerrainCellGridSize;

// Мировая клетка локального (0, 0) окна. Тексели лежат по кольцевому
// адресу: мировая координата по модулю размера сетки.
float4 _TerrainCellOrigin;

// Начало рисуемого окна внутри сетки. Экран рисует меш размером с видимое
// окно, поле материалов — меш всей сетки со смещением ноль.
float4 _TerrainCellViewOffset;
float2 _TerrainGeometryCarrierPaddingWorld;
int _TerrainDebugBackgroundTileIdentity;

int TerrainRing(int value, int size)
{
    int quotient = value / size;
    int remainder = value - quotient * size;
    return remainder < 0 ? remainder + size : remainder;
}

struct TerrainCellVertex
{
    float3 positionOS;
    float2 uv;
    float4 color;
    float4 subAtlasRect;
    float4 tileSizeUV;
    float4 worldPos;
    float4 animData;
    float4 packedData;
    float4 glowData;
    float4 geometryCornersX;
    float4 geometryCornersY;
    float uvBits;
    float atlasIndex;
    float layer;
};

TerrainCellVertex LoadTerrainCellVertex(
    float3 address,
    float2 cornerBase,
    float organicBendStrength,
    float organicBendPivot)
{
    TerrainCellVertex v = (TerrainCellVertex)0;
    int x = (int)round(address.x) + (int)round(_TerrainCellViewOffset.x);
    int y = (int)round(address.y) + (int)round(_TerrainCellViewOffset.y);
    int layer = (int)round(address.z);
    int2 cornerStep = (int2)round(cornerBase);
    int corner = cornerStep.y == 0
        ? (cornerStep.x == 0 ? 0 : 1)
        : (cornerStep.x == 1 ? 2 : 3);
    int2 origin = (int2)round(_TerrainCellOrigin.xy);
    int ringX = TerrainRing(origin.x + x, (int)round(_TerrainCellGridSize.x));
    int ringY = TerrainRing(origin.y + y, (int)round(_TerrainCellGridSize.y));
    int3 texel = int3(ringX, (ringY * 2) + layer, 0);

    float4 meta = _TerrainCellMeta.Load(texel);
    uint uvBits = (uint)round(meta.g * 255.0);
    v.uvBits = uvBits;
    v.uv = float2((uvBits >> (corner * 2)) & 1u, (uvBits >> (corner * 2 + 1)) & 1u);
    v.atlasIndex = round(meta.r * 255.0) - 1.0;
    v.layer = layer;

    // Фон под сплошным передним планом (флаг в синем канале meta) не виден
    // ни в одном пикселе: отбрасывается так же, как незаполненный квад.
    bool occludedBackground = layer == 0 && meta.b > 0.5;
#if defined(KERN_TERRAIN_DEBUG_BACKGROUND_TILE_VIEW)
    occludedBackground = occludedBackground && _TerrainDebugBackgroundTileIdentity == 0;
#endif
    if (occludedBackground)
    {
        v.atlasIndex = -1.0;
    }

    // Незаполненный квад (у фона — больше половины сетки) отбрасывается
    // вызывающим: остальные выборки ему не нужны.
    if (v.atlasIndex < 0.0)
    {
        return v;
    }

    v.color = _TerrainCellColor.Load(texel);
    v.subAtlasRect = _TerrainCellAtlasRect.Load(texel);
    v.tileSizeUV = _TerrainCellTileSize.Load(texel);
    v.worldPos = _TerrainCellWorld.Load(texel);
    v.animData = _TerrainCellAnimation.Load(texel);
    v.glowData = _TerrainCellGlow.Load(texel);

    float4 geometryX = _TerrainCellGeometryX.Load(texel);
    float4 geometryY = _TerrainCellGeometryY.Load(texel);
    // Geometry is a foreground-only contract.  Background texels can share
    // the same ring address and must never inherit a stale anchor bit from a
    // previous cell upload.
    bool anchored = layer > 0 && meta.a > 0.5;
    bool organic = anchored && meta.a < 0.75;
    float organicEdges = organic
        ? round(meta.b * 255.0) + 256.0 * (round(meta.a * 255.0) - 128.0)
        : 0.0;
    // Rasterize a carrier enclosing the ENTIRE pixel silhouette. Rasterizing
    // the displaced polygon first loses fragments on the outward half of every
    // staircase; fragment clipping cannot bring those fragments back.
    // Corners are cell-local, so the interpolant and POSITION use one scale.
    float2 carrierMin = float2(0.0, 0.0);
    float2 carrierMax = float2(1.0, 1.0);
    if (anchored)
    {
        float2 boundsMin = float2(
            min(min(geometryX.x, geometryX.y), min(geometryX.z, geometryX.w)),
            min(min(geometryY.x, geometryY.y), min(geometryY.z, geometryY.w)));
        float2 boundsMax = float2(
            max(max(geometryX.x, geometryX.y), max(geometryX.z, geometryX.w)),
            max(max(geometryY.x, geometryY.y), max(geometryY.z, geometryY.w)));
        // Include the four extra edge points in the carrier. The AO render
        // pass adds only its half-texel filter support after these bounds.
        if (organic)
        {
            int code = (int)organicEdges - 1;
            float bendScale = organicBendStrength / 32.0;
            float bottomBend = ((code % 5) - 2) * bendScale;
            code /= 5;
            float rightBend = ((code % 5) - 2) * bendScale;
            code /= 5;
            float topBend = ((code % 5) - 2) * bendScale;
            code /= 5;
            float leftBend = ((code % 5) - 2) * bendScale;
            float4 edgeT = float4(
                bottomBend > 0.0 ? organicBendPivot : 1.0 - organicBendPivot,
                rightBend > 0.0 ? organicBendPivot : 1.0 - organicBendPivot,
                topBend > 0.0 ? 1.0 - organicBendPivot : organicBendPivot,
                leftBend > 0.0 ? 1.0 - organicBendPivot : organicBendPivot);
            float4 edgeX = float4(
                lerp(geometryX.x, geometryX.y, edgeT.x),
                lerp(geometryX.y, geometryX.z, edgeT.y) + rightBend,
                lerp(geometryX.z, geometryX.w, edgeT.z),
                lerp(geometryX.w, geometryX.x, edgeT.w) + leftBend);
            float4 edgeY = float4(
                lerp(geometryY.x, geometryY.y, edgeT.x) + bottomBend,
                lerp(geometryY.y, geometryY.z, edgeT.y),
                lerp(geometryY.z, geometryY.w, edgeT.z) + topBend,
                lerp(geometryY.w, geometryY.x, edgeT.w));
            edgeX = round(edgeX * 32.0) / 32.0;
            edgeY = round(edgeY * 32.0) / 32.0;
            boundsMin = min(boundsMin, float2(
                min(min(edgeX.x, edgeX.y), min(edgeX.z, edgeX.w)),
                min(min(edgeY.x, edgeY.y), min(edgeY.z, edgeY.w))));
            boundsMax = max(boundsMax, float2(
                max(max(edgeX.x, edgeX.y), max(edgeX.z, edgeX.w)),
                max(max(edgeY.x, edgeY.y), max(edgeY.z, edgeY.w))));
        }
        carrierMin = boundsMin;
        carrierMax = boundsMax;
    }
    // The AO field uses a signed-distance edge filter. Extend its raster
    // carrier by half a field texel so the exterior half of that filter exists
    // even at the polygon's extreme corners. Other passes set this to zero.
    float2 carrierPadding = max(_TerrainGeometryCarrierPaddingWorld, float2(0.0, 0.0)) /
        max(_TerrainCellGridSize.z, 0.0001);
    carrierMin -= carrierPadding;
    carrierMax += carrierPadding;
    float2 carrierCorner = lerp(carrierMin, carrierMax, cornerBase);
    v.packedData = float4(anchored ? 1.0 : 0.0, carrierCorner, organicEdges);
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

// Клип-позиция, которую растеризатор отбрасывает целиком.
float4 TerrainCulledPosition()
{
    return float4(2.0, 2.0, 2.0, 1.0);
}

#endif
