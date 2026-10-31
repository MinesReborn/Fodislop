#ifndef KERN_TERRAIN_SAMPLING_INCLUDED
#define KERN_TERRAIN_SAMPLING_INCLUDED

#include "Assets/Shaders/Terrain/Autotiling/TerrainTileAddressing.hlsl"
#include "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl"
#include "Assets/Shaders/Terrain/Geometry/TerrainGeometryContract.hlsl"

// Порядок обязателен: cbuffer-файл зовёт TerrainAtlasTexelSize, — сначала идёт
// выборка атласа. Guard'ы делают повторное включение безопасным.
#include "Assets/Shaders/Terrain/Textures/TerrainAtlasSampling.hlsl"
#include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"

// Coefficient in linear source space; apply before spatial reduction, never
// threshold the averaged blue host rock and bright green crystal together.
float4 TerrainSourceEmission(float4 source)
{
    // Peak-channel brightness preserves saturated blue/red emitters too.
    float brightness = max(source.r, max(source.g, source.b));
    float mask = smoothstep(_EmissionBrightnessStart, _EmissionBrightnessEnd, brightness);
    mask *= source.a >= _AlphaCutoff ? source.a : 0.0;
    return float4(source.rgb * mask, mask);
}

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
    const uint uvStep = KERN_TERRAIN_UV_BITS_PER_CORNER;
    float2 uv00 = float2(packedUvs & 1u, (packedUvs >> 1u) & 1u);
    float2 uv10 = float2((packedUvs >> uvStep) & 1u, (packedUvs >> (uvStep + 1u)) & 1u);
    float2 uv01 = float2((packedUvs >> (3u * uvStep)) & 1u, (packedUvs >> (3u * uvStep + 1u)) & 1u);
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

    // A parallelogram has no bilinear term. Solve its linear map once instead
    // of repeating the same Jacobian four times; displaced/rotated axes remain valid.
    [branch]
    if (twist.x == 0.0 && twist.y == 0.0)
    {
        float2 residual = cellSample - p00;
        float determinant = TerrainUvCross(axisU, axisV);
        return saturate(float2(
            TerrainUvCross(residual, axisV),
            TerrainUvCross(axisU, residual)) / determinant);
    }

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
        int animationType = (int)(animData.x + 0.5);
        if (animationType == (int)KERN_TERRAIN_ANIMATION_TYPE_LEGACY3TEST ||
            animationType == (int)KERN_TERRAIN_ANIMATION_TYPE_LEGACY4TEST ||
            animationType == (int)KERN_TERRAIN_ANIMATION_TYPE_LEGACY6TEST ||
            animationType == (int)KERN_TERRAIN_ANIMATION_TYPE_LEGACY8TEST)
        {
            speed *= 20.01;
        }
        float frameIndex = floor(fmod(timeY * speed, frameCount));
        animOffsetUV = frameIndex * frameHeightTiles * tileSizeUV.y;
    }
    res.animOffsetUV = animOffsetUV;

    float2 tilesCount = ceil(subAtlasSizeUV / tileSizeUV - 0.0001);
    tilesCount = max(tilesCount, 1.0);

    // Раскладка каналов — PackedColumn в TerrainCellFormat.hlsl.
    //
    // В w когда-то читали значение больше 1.5 как «выбросить квад», но
    // писателя у него не было ни в одном коммите, и канал только выглядел
    // занятым. Читателей сняли; выбрасывание квада выражено там, где оно
    // и принимается, — atlasIndex < 0 в LoadTerrainCellVertex.
    bool isTiling = fmod(worldPos.w, 2.0) > 0.5;
    int packedColumn = (int)(worldPos.z + 0.5);
    bool worldTextureAnchor = ((uint)packedColumn & KERN_TERRAIN_COLUMN_WORLD_TEXTURE_ANCHOR) != 0u;
    float tileGroupColumn = (float)((uint)packedColumn & KERN_TERRAIN_COLUMN_MASK);

    // Общая мировая картинка адресуется по позиции фрагмента. У тайлгруппы
    // колонки листа — варианты по соседям, а не части общей картинки:
    // колонка, отражения и поворот должны сохраняться при любой привязке.
    // Контуры Wall/Corner/Door держат узлы сетки, поэтому их локальный UV
    // остаётся на исходной сетке 32×32 без растяжения геометрией.
    if (worldTextureAnchor && !isTiling)
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

// Ширина экранного пикселя в текселях атласа для PixelArtSampleUV.
//
// finalUV клетки рвётся: бесшовная текстура оборачивается через frac на границах
// клеток, кратных его ширине, а клетка за краем берёт соседний тайл. fwidth
// от такой UV в квадах 2×2 на этих линиях — целый лист, сглаживание шва
// прилипало к углу текселя, и прямые грани на целых границах мерцали с зумом.
// Координата носителя (packedData.yz) непрерывна на всём примитиве, клетка —
// ровно один тайл. Её производную вызывающий берёт в начале фрагмента, до
// ветвлений и clip. Максимум по осям: поворот тайла автотайлинга меняет оси
// местами.
float2 TerrainPixelArtWidthTexels(
    float2 carrierPixelWidth,
    float4 tileSize,
    float4 atlasTexelSize)
{
    float cellsPerPixel = max(carrierPixelWidth.x, carrierPixelWidth.y);
    return cellsPerPixel * tileSize.xy * atlasTexelSize.zw;
}

half4 SampleTerrainSurfaceTexel(
    int atlasSlot,
    TerrainTileUvResult tile,
    float4 atlasTexelSize,
    float4 tileSize,
    float2 carrierPixelWidth)
{
    float2 uv = PixelArtSampleUV(
        tile.finalUV,
        atlasTexelSize.zw,
        TerrainPixelArtWidthTexels(carrierPixelWidth, tileSize, atlasTexelSize));
    uv = ClampTerrainTileUV(uv, tile);
    [branch]
    if (_PixelArtFiltering < 0.5)
    {
        return TerrainSampleAtlas(atlasSlot, sampler_PointClamp, uv);
    }

    return TerrainSampleAtlas(atlasSlot, sampler_LinearClamp, uv);
}

#endif
