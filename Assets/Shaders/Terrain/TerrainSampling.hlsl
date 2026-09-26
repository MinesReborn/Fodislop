#ifndef KERN_TERRAIN_SAMPLING_INCLUDED
#define KERN_TERRAIN_SAMPLING_INCLUDED

#include "TerrainTileAddressing.hlsl"
#include "TerrainGeometryContract.hlsl"

// Порядок обязателен: cbuffer-файл зовёт TerrainAtlasTexelSize, — сначала идёт
// выборка атласа. Guard'ы делают повторное включение безопасным.
#include "TerrainAtlasSampling.hlsl"
#include "TerrainMaterialCBuffer.hlsl"

struct TerrainTileUvResult
{
    float2 finalUV;
    float2 minTileUV;
    float2 maxTileUV;
    float2 availableTileSize;
    float2 baseUV;
    float2 tileOffsetUV;
    float2 identityTileOffsetUV;
    float2 identityAvailableTileSize;
    float animOffsetUV;
    bool isValid;
};

float TerrainUvCross(float2 a, float2 b)
{
    return a.x * b.y - a.y * b.x;
}

// Map the interpolated carrier UV back onto the displaced cell polygon. The
// carrier is an axis-aligned box, so using its UV directly makes adjacent
// autotile variants sample different interior texels when a shared vertex
// crosses a cell edge. Inverting the cell's bilinear geometry keeps UVs on the
// authored tile edge while the polygon itself moves.
float2 TerrainResolveGeometryLocalUv(
    float2 cellSample,
    float4 cornersX,
    float4 cornersY);

float2 TerrainResolveGeometryTileUV(
    float2 carrierUV,
    float2 cellSample,
    float4 cornersX,
    float4 cornersY,
    float uvBits,
    float anchored)
{
    // cellSample is the geometry-local position from the carrier. UVs may be
    // rotated/flipped for autotile selection, so they are not a valid fallback
    // coordinate for a continuous world sheet.
    if (anchored < 0.5 || uvBits < 0.0)
    {
        return carrierUV;
    }

    float2 cellLocalUv = TerrainResolveGeometryLocalUv(
        cellSample, cornersX, cornersY);

    uint packedUvs = (uint)round(uvBits);
    float2 uv00 = float2(packedUvs & 1u, (packedUvs >> 1u) & 1u);
    float2 uv10 = float2((packedUvs >> 2u) & 1u, (packedUvs >> 3u) & 1u);
    float2 uv01 = float2((packedUvs >> 6u) & 1u, (packedUvs >> 7u) & 1u);
    return uv00 + (uv10 - uv00) * cellLocalUv.x + (uv01 - uv00) * cellLocalUv.y;
}

// Recover the cell-local position from the geometry carrier so atlas
// autotile flips cannot change the authored tile orientation.
float2 TerrainResolveGeometryLocalUv(
    float2 cellSample,
    float4 cornersX,
    float4 cornersY)
{
    float2 p00 = QuantizeTerrainGeometryPoint(float2(cornersX.x, cornersY.x));
    float2 p10 = QuantizeTerrainGeometryPoint(float2(cornersX.y, cornersY.y));
    float2 p11 = QuantizeTerrainGeometryPoint(float2(cornersX.z, cornersY.z));
    float2 p01 = QuantizeTerrainGeometryPoint(float2(cornersX.w, cornersY.w));
    float2 axisU = p10 - p00;
    float2 axisV = p01 - p00;
    float2 twist = p00 - p10 + p11 - p01;

    float2 local = 0.5;
    [unroll]
    for (int iteration = 0; iteration < 4; iteration++)
    {
        float2 mapped = p00 + axisU * local.x + axisV * local.y + twist * local.x * local.y;
        float2 residual = cellSample - mapped;
        float2 tangentU = axisU + twist * local.y;
        float2 tangentV = axisV + twist * local.x;
        float determinant = TerrainUvCross(tangentU, tangentV);
        local.x += TerrainUvCross(residual, tangentV) / determinant;
        local.y += TerrainUvCross(tangentU, residual) / determinant;
    }

    return saturate(local);
}

void TerrainSetResolvedTileIdentity(
    inout TerrainTileUvResult tile,
    float2 baseUV,
    float2 tileSizeUV)
{
    // Use the final atlas address so animated frames and scrolling sheets are
    // classified as the tile actually sampled at this fragment.
    float2 resolvedOffset = max(tile.finalUV - baseUV, 0.0);
    tile.identityTileOffsetUV = floor(resolvedOffset / tileSizeUV) * tileSizeUV;
    tile.identityAvailableTileSize = tileSizeUV;
}

TerrainTileUvResult ResolveTerrainTileUV(
    float2 cornerUV,
    float2 geometryCellPosition,
    float4 subAtlasRect,
    float4 tileSize,
    float4 worldPos,
    float4 animData,
    float4 packedData,
    float timeY,
    float2 atlasTexelSize)
{
    TerrainTileUvResult res;
    res.baseUV = subAtlasRect.xy;
    res.tileOffsetUV = 0.0;
    res.identityTileOffsetUV = 0.0;
    res.identityAvailableTileSize = 0.0;
    res.availableTileSize = 0.0;
    res.finalUV = 0.0;
    res.minTileUV = 0.0;
    res.maxTileUV = 0.0;
    res.animOffsetUV = 0.0;
    res.isValid = false;

    float2 baseUV = subAtlasRect.xy;
    float2 subAtlasSizeUV = subAtlasRect.zw;
    float2 tileSizeUV = tileSize.xy;
    if (subAtlasSizeUV.x <= 0.0 || subAtlasSizeUV.y <= 0.0 ||
        tileSizeUV.x <= 0.0 || tileSizeUV.y <= 0.0)
    {
        return res;
    }

    res.isValid = true;
    float frameCount = tileSize.z;
    float frameHeightTiles = tileSize.w;
    float animOffsetUV = 0.0;
    if (frameCount > 1.5)
    {
        float speed = animData.y;
        float frameIndex = floor(fmod(timeY * speed, frameCount));
        animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
    }
    res.animOffsetUV = animOffsetUV;

    float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
    tilesCount = max(tilesCount, 1.0);

    // Раскладка упакованных каналов клетки:
    //   w — бит 0: автотайлинг по соседям. Биты выше свободны.
    //   z — биты 0-4: колонка тайлгруппы (descriptor & 0x1F);
    //       бит 5: сплошной лист.
    //
    // В w когда-то читали значение больше 1.5 как «выбросить квад», но
    // писателя у него не было ни в одном коммите, и канал только выглядел
    // занятым. Читателей сняли; выбрасывание квада выражено там, где оно
    // и принимается, — atlasIndex < 0 в LoadTerrainCellVertex и
    // TerrainCellLayers.TryGetType.
    bool isTiling = fmod(worldPos.w, 2.0) > 0.5;
    int packedColumn = (int)(worldPos.z + 0.5);
    bool isContinuousSheet = (packedColumn & 32) != 0;
    float tileGroupColumn = (float)(packedColumn & 31);

    // Кристаллы и камень адресуют лист по фактической позиции фрагмента в
    // деформированной геометрии. Это сохраняет непрерывность текстуры через
    // шов клеток и двигает рисунок вместе с дисторшном.
    //
    // Тайл на клетку этого не умеет. Его UV зажат в свою клетку, геометрия
    // уезжает без него, и на каждой границе остаётся шов: массив читается
    // кладкой из штампов, а не одним камнем.
    if (isContinuousSheet)
    {
        float2 sheetPosition = float2(worldPos.x, -worldPos.y - 1.0) + geometryCellPosition;
        float2 sheetUV = frac(sheetPosition / tilesCount);
        res.finalUV = baseUV + sheetUV * subAtlasSizeUV;
        res.finalUV.y += animOffsetUV;
        res.availableTileSize = subAtlasSizeUV;
        res.minTileUV = baseUV + atlasTexelSize * 0.5;
        res.maxTileUV = baseUV + subAtlasSizeUV - atlasTexelSize * 0.5;
        TerrainSetResolvedTileIdentity(res, baseUV, tileSizeUV);
        return res;
    }

    float2 wrapped = KernResolveTerrainTileIndex(
        worldPos.xy,
        tilesCount,
        tileGroupColumn,
        isTiling ? 1.0 : 0.0);
    float2 tileOffsetUV = wrapped * tileSizeUV;
    float2 availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
    float2 quadUV = cornerUV;
    if (packedData.x > 0.5)
    {
        // Geometry UVs follow the displaced polygon inside this cell. When
        // distortion pushes part over a grid edge, use the periodic address
        // implied by this material's tile layout. Grouped autotiles keep their
        // per-cell base column; the neighbor's distinct descriptor is not
        // available in this fragment contract.
        float2 stepUV = float2(
            geometryCellPosition.x > 1.0 ? 1.0 : (geometryCellPosition.x < 0.0 ? -1.0 : 0.0),
            geometryCellPosition.y > 1.0 ? -1.0 : (geometryCellPosition.y < 0.0 ? 1.0 : 0.0));
        bool outsideX = stepUV.x != 0.0;
        bool outsideY = stepUV.y != 0.0;
        if (outsideX || outsideY)
        {
            if (isTiling)
            {
                // An autotile's neighbor owns another descriptor and often
                // another UV transform. Repeating this cell's local UV at the
                // displaced edge jumps from one side of its atlas tile to the
                // other, creating a visible seam. Keep this cell's selected
                // variant and extend its edge texels across the small overlap.
                quadUV = clamp(cornerUV, 0.0, 1.0);
            }
            else
            {
                quadUV = frac(geometryCellPosition);
                float2 wrappedPosition = worldPos.xy + stepUV;
                float2 wrappedTile = KernResolveTerrainTileIndex(
                    wrappedPosition,
                    tilesCount,
                    tileGroupColumn,
                    0.0);
                tileOffsetUV = wrappedTile * tileSizeUV;
                availableTileSize = min(tileSizeUV, subAtlasSizeUV - tileOffsetUV);
            }
        }
        else
        {
            quadUV = cornerUV;
        }
    }

    quadUV = clamp(quadUV, KernTileAddressEpsilon, 1.0 - KernTileAddressEpsilon);

    float2 finalUV = baseUV + tileOffsetUV + quadUV * availableTileSize;
    finalUV.y += animOffsetUV;

    res.baseUV = baseUV;
    res.tileOffsetUV = tileOffsetUV;
    res.availableTileSize = availableTileSize;
    res.finalUV = finalUV;
    res.minTileUV = baseUV + tileOffsetUV + atlasTexelSize * 0.5;
    res.maxTileUV = baseUV + tileOffsetUV + availableTileSize - atlasTexelSize * 0.5;
    TerrainSetResolvedTileIdentity(res, baseUV, tileSizeUV);
    return res;
}

float2 ClampTerrainTileUV(float2 uv, TerrainTileUvResult tile)
{
    float2 minTile = tile.minTileUV;
    float2 maxTile = tile.maxTileUV;
    minTile.y += tile.animOffsetUV;
    maxTile.y += tile.animOffsetUV;
    return clamp(uv, minTile, maxTile);
}

#endif
