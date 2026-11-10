#ifndef KERN_TERRAIN_CELL_VARIATION_INCLUDED
#define KERN_TERRAIN_CELL_VARIATION_INCLUDED
float TerrainDecalPlacement(uint decalTextureIndex, int worldX, int serverY)
{
    if (decalTextureIndex == 0u)
    {
        return 0.0;
    }

    uint hash = asuint(worldX) * 374761393u;
    hash += asuint(serverY) * 668265263u;
    hash ^= decalTextureIndex * 2246822519u;
    hash = (hash ^ (hash >> 13)) * 1274126177u;
    hash ^= hash >> 16;
    if (hash % 100u >= KERN_TERRAIN_DECAL_PLACEMENT_PERCENT)
    {
        return 0.0;
    }

    uint packed = 1u + hash % KERN_TERRAIN_DECAL_VARIANTS +
        (((hash >> 8) & 3u) << KERN_TERRAIN_DECAL_ROTATION_SHIFT) +
        (((hash >> 10) & 1u) << KERN_TERRAIN_DECAL_MIRROR_SHIFT) +
        (((hash >> 12) & 3u) << KERN_TERRAIN_DECAL_OFFSET_X_SHIFT) +
        (((hash >> 14) & 3u) << KERN_TERRAIN_DECAL_OFFSET_Y_SHIFT);
    return (float)packed;
}

// Значение после записи в half вершины (TerrainVertex.H): мантисса
// обрезается до 10 бит, слишком малое становится нулём.
float TerrainTruncateToHalf(float value)
{
    uint bits = asuint(value);
    int exponent = (int)((bits >> 23) & 0xFFu) - 127 + 15;
    if (exponent <= 0)
    {
        return asfloat(bits & 0x80000000u);
    }

    return asfloat(bits & 0xFFFFE000u);
}

// H(k / 1000f) без деления float: деление на GPU округляется не так, как на
// CPU, а целое — одинаково везде. s — наименьший сдвиг, при котором
// k·2^s ≥ 1024·1000; мантисса m = ⌊k·2^s / 1000⌋ ∈ [1024, 2047]; значение
// m·2^-s. Округление float до 24 бит перед обрезкой не переходит границу
// 10-битной мантиссы (остаток кратен 1/1000 её шага), поэтому равенство
// с H(k / 1000f) точное.
float TerrainThousandthsAsHalf(uint k)
{
    if (k == 0u)
    {
        return 0.0;
    }

    uint shift = 8u;
    [loop]
    while (shift < 20u && (k << shift) < 1024000u)
    {
        shift++;
    }

    uint mantissa = (k << shift) / 1000u;
    return (float)mantissa * asfloat((127u - shift) << 23);
}

// Как TerrainQuadBuilder.HashCell.
uint TerrainCellHash(int gridX, int serverY)
{
    uint seed = asuint(gridX) * 374761397u + asuint(serverY) * 668265263u;
    seed = (seed ^ (seed >> 13)) * 1274126177u;
    return seed ^ (seed >> 16);
}

// Фаза анимации — как TerrainQuadBuilder.ResolveAnimationOffset.
float TerrainAnimationPhase(TerrainTypeRow row, bool hasAtlasRect, int gridX, int serverY)
{
    float palette = (float)TerrainTypeAnimationPalette(row);
    if (!hasAtlasRect)
    {
        return palette;
    }

    TerrainAnimationProfile profile = ResolveTerrainAnimationProfile((int)TerrainTypeAnimationType(row));
    if (profile.hasCellPhase && profile.behavior == KERN_TERRAIN_PROFILE_BASE_ANIMATION)
    {
        return TerrainThousandthsAsHalf(TerrainCellHash(gridX, serverY) % 6283u);
    }

    if (profile.behavior == KERN_TERRAIN_PROFILE_FACETED)
    {
        return TerrainTruncateToHalf(
            (float)(TerrainCellHash(gridX, serverY) & 0xFFFFu) * (1.0 / 65536.0));
    }

    return palette;
}

// Классический джиттер — как RandXd (row 1) и RandYd (row 2). Произведение
// переполняется так же, как int в C#: умножение в uint.

#endif // KERN_TERRAIN_CELL_VARIATION_INCLUDED
