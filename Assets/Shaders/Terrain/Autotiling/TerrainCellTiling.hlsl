#ifndef KERN_TERRAIN_CELL_TILING_INCLUDED
#define KERN_TERRAIN_CELL_TILING_INCLUDED
uint TerrainTileDescriptor(uint mask)
{
    return (_TerrainTileDescriptors[mask >> 2] >> ((mask & 3u) * 8u)) & 0xFFu;
}

// Как TerrainCellMaskCalculator.CalculateTilingDescriptor и
// TerrainBackgroundTileResolver: соседи той же тайлгруппы → дескриптор.
// neighbours — признаки соседей того же слоя.
uint TerrainTilingDescriptor(TerrainTypeRow own, TerrainTypeRow neighbours[8])
{
    if (!TerrainTypeHasTileGroup(own))
    {
        return 0u;
    }

    uint group = TerrainTypeTileGroup(own);
    uint mask = 0u;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        TerrainTypeRow other = neighbours[i];
        if (TerrainTypeHasTileGroup(other) && TerrainTypeTileGroup(other) == group)
        {
            mask |= 1u << i;
        }
    }

    return TerrainTileDescriptor(mask);
}

// Как TerrainQuadBuilder.ResolveBuildingWallVariant: колонка по числу
// примыкающих углов пака, отражения — из собственного дескриптора.
uint TerrainBuildingWallVariant(uint descriptor, uint cornerSideMask)
{
    bool hasLeft = (cornerSideMask & 1u) != 0u;
    bool hasRight = (cornerSideMask & 2u) != 0u;
    bool hasTop = (cornerSideMask & 4u) != 0u;
    bool hasBottom = (cornerSideMask & 8u) != 0u;
    uint cornerCount = (hasLeft ? 1u : 0u) + (hasRight ? 1u : 0u) + (hasTop ? 1u : 0u) + (hasBottom ? 1u : 0u);
    uint column = KERN_TERRAIN_WALL_BASE_COLUMN + (cornerCount < 2u ? cornerCount : 2u);
    uint transforms = descriptor & KERN_TERRAIN_TILE_TRANSFORM_MASK;
    if (cornerCount == 1u && (hasRight || hasBottom))
    {
        transforms ^= 1u << KERN_TERRAIN_TILE_FLIP_U_SHIFT;
    }

    if (cornerCount >= 2u && !hasLeft && !hasRight)
    {
        transforms ^= 1u << KERN_TERRAIN_TILE_TURN_SHIFT;
    }

    return transforms | (column & KERN_TERRAIN_TILE_COLUMN_MASK);
}

// Как TerrainCellMaskCalculator.SameRimSurface: соседи одной ненулевой
// массы — одно тело без каймы; масса 0 каймы не имеет.
bool TerrainSameRim(TerrainTypeRow own, TerrainTypeRow other)
{
    uint otherMass = TerrainTypeReliefGroup(other);
    return !TerrainTypeIsBackground(other) && otherMass != 0u && TerrainTypeReliefGroup(own) == otherMass;
}

// Как TerrainForegroundOcclusion.IsOpaqueMassCell (до переноса в шейдер):
// загруженная волнистая клетка, непрозрачная в атласе.
bool TerrainOpaqueMassCell(TerrainTypeRow row)
{
    return TerrainTypeWavy(row) && TerrainTypeOpaqueAny(row);
}

// Углы UV квада после отражений и поворота автотайла — как
// TerrainQuadUvs.Transform: отражения по осям, затем поворот на четверть.
// Биты угла i: (2i) — u, (2i+1) — v.
uint TerrainCornerUvBits(uint descriptor, bool tiling)
{
    uint transforms = tiling ? descriptor : 0u;
    uint flipU = (transforms >> KERN_TERRAIN_TILE_FLIP_U_SHIFT) & 1u;
    uint flipV = (transforms >> KERN_TERRAIN_TILE_FLIP_V_SHIFT) & 1u;
    uint turn = (transforms >> KERN_TERRAIN_TILE_TURN_SHIFT) & 1u;
    uint bits = 0u;
    [unroll]
    for (uint corner = 0u; corner < 4u; corner++)
    {
        uint source = (corner + turn) & 3u;
        uint u = ((source == 1u || source == 2u) ? 1u : 0u) ^ flipU;
        uint v = (source >= 2u ? 1u : 0u) ^ flipV;
        bits |= (u | (v << 1)) << (corner * KERN_TERRAIN_UV_BITS_PER_CORNER);
    }

    return bits;
}

// Декаль — тем же хэшем, что TerrainDecalCatalog.Place.

#endif // KERN_TERRAIN_CELL_TILING_INCLUDED
