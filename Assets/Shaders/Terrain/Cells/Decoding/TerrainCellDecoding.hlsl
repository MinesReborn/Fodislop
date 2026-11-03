#ifndef KERN_TERRAIN_CELL_DECODING_INCLUDED
#define KERN_TERRAIN_CELL_DECODING_INCLUDED
int TerrainRing(int value, int size)
{
    int quotient = value / size;
    int remainder = value - quotient * size;
    return remainder < 0 ? remainder + size : remainder;
}

uint TerrainLoadCell(int gridX, int unityY)
{
    if (_TerrainForceUnloaded > 0.5)
    {
        return 0u;
    }

    int width = (int)round(_TerrainCellGridSize.x);
    int height = (int)round(_TerrainCellGridSize.y);
    uint index = (uint)(TerrainRing(unityY, height) * width + TerrainRing(gridX, width));
    return (_TerrainCells[index / KERN_TERRAIN_CELLS_PER_WORD] >>
        ((index % KERN_TERRAIN_CELLS_PER_WORD) * KERN_TERRAIN_CELL_BITS)) & KERN_TERRAIN_CELL_TYPE_MASK;
}

// ═══ ФОРМАТ: доступ к полям ═════════════════════════════════════════════
// Только здесь поле клетки или строки типа достаётся по битам; остальной
// код зовёт эти функции. Имя функции — TerrainType + имя поля строки.

// Клетка: её тип.
uint TerrainCellType(uint cell) { return cell & KERN_TERRAIN_CELL_TYPE_MASK; }

// Строка типа читается целиком — и своя, и соседей.
TerrainTypeRow TerrainTypeRowOf(uint type) { return _TerrainTypes[type]; }

uint TerrainField(uint word, uint shift, uint mask) { return (word >> shift) & mask; }

// atlasXY, atlasWH
float4 TerrainTypeAtlasPixels(TerrainTypeRow row)
{
    return float4(
        row.atlasXY & KERN_TERRAIN_TYPE_PIXEL_MASK,
        (row.atlasXY >> KERN_TERRAIN_TYPE_PIXEL_HIGH_SHIFT) & KERN_TERRAIN_TYPE_PIXEL_MASK,
        row.atlasWH & KERN_TERRAIN_TYPE_PIXEL_MASK,
        (row.atlasWH >> KERN_TERRAIN_TYPE_PIXEL_HIGH_SHIFT) & KERN_TERRAIN_TYPE_PIXEL_MASK);
}
float TerrainTypeFrameCount(TerrainTypeRow row) { return (float)(row.atlasXY >> KERN_TERRAIN_TYPE_BYTE_SHIFT); }
uint TerrainTypeReliefGroup(TerrainTypeRow row) { return row.atlasWH >> KERN_TERRAIN_TYPE_BYTE_SHIFT; }

// speedGlowTile
uint TerrainTypeTileGroupCode(TerrainTypeRow row) { return row.speedGlowTile >> KERN_TERRAIN_TYPE_BYTE_SHIFT; }
bool TerrainTypeHasTileGroup(TerrainTypeRow row) { return TerrainTypeTileGroupCode(row) != 0u; }
uint TerrainTypeTileGroup(TerrainTypeRow row) { return TerrainTypeTileGroupCode(row) - 1u; }

// look
uint TerrainTypeSlot(TerrainTypeRow row) { return row.look & KERN_TERRAIN_TYPE_SLOT_MASK; }
bool TerrainTypeOpaqueOwn(TerrainTypeRow row) { return (row.look & KERN_TERRAIN_TYPE_OPAQUE_OWN) != 0u; }
bool TerrainTypeOpaqueAny(TerrainTypeRow row) { return (row.look & KERN_TERRAIN_TYPE_OPAQUE_ANY) != 0u; }
uint TerrainTypeTextureAnchor(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_SHIFT, KERN_TERRAIN_TYPE_TEXTURE_ANCHOR_MASK); }
uint TerrainTypeOutline(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_OUTLINE_SHIFT, KERN_TERRAIN_TYPE_OUTLINE_MASK); }
uint TerrainTypeAnimationType(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_ANIMATION_TYPE_SHIFT, KERN_TERRAIN_TYPE_ANIMATION_TYPE_MASK); }
uint TerrainTypeAnimationPalette(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_ANIMATION_PALETTE_SHIFT, KERN_TERRAIN_TYPE_ANIMATION_PALETTE_MASK); }
uint TerrainTypeDecalTextureIndex(TerrainTypeRow row) { return TerrainField(row.look, KERN_TERRAIN_TYPE_DECAL_TEXTURE_INDEX_SHIFT, KERN_TERRAIN_TYPE_DECAL_TEXTURE_INDEX_MASK); }

float TerrainTypeFrameSpeed(TerrainTypeRow row) { return f16tof32(row.speedGlowTile & KERN_TERRAIN_TYPE_FRAME_SPEED_MASK); }
uint TerrainTypeGlowByte(TerrainTypeRow row) { return TerrainField(row.speedGlowTile, KERN_TERRAIN_TYPE_GLOW_SHIFT, KERN_TERRAIN_TYPE_GLOW_MASK); }
float TerrainTypeGlow(TerrainTypeRow row) { return (float)TerrainTypeGlowByte(row) * (1.0 / 255.0); }

// Сравнение одного поля со значением.
// Фон (drawLayer Background или Underlay) лежит сам на себе; под передним
// планом — подложка.
bool TerrainTypeIsBackground(TerrainTypeRow row) { return (row.look & KERN_TERRAIN_TYPE_BACKGROUND) != 0u; }
uint TerrainTypeUnder(uint type, TerrainTypeRow row) { return TerrainTypeIsBackground(row) ? type : (uint)_TerrainUnderlayType; }
bool TerrainTypeGlows(TerrainTypeRow row) { return TerrainTypeGlowByte(row) != 0u; }
// Только передний план может быть источником искажения.
bool TerrainTypeWavy(TerrainTypeRow row)
{
    return !TerrainTypeIsBackground(row) && TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_WAVY;
}
bool TerrainTypeHolds(TerrainTypeRow row)
{
    uint outline = TerrainTypeOutline(row);
    return !TerrainTypeIsBackground(row) &&
        outline != KERN_TERRAIN_OUTLINE_PLIANT && outline != KERN_TERRAIN_OUTLINE_WAVY;
}
bool TerrainTypeRound(TerrainTypeRow row) { return TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_ROUND; }
bool TerrainTypeWall(TerrainTypeRow row) { return TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_WALL; }
bool TerrainTypeCorner(TerrainTypeRow row)
{
    return !TerrainTypeIsBackground(row) && TerrainTypeOutline(row) == KERN_TERRAIN_OUTLINE_CORNER;
}

// Факты текстуры: прямоугольник, размер тайла, высота кадра.
float4 TerrainTypeAtlasRect(TerrainTypeRow row)
{
    float4 texel = TerrainMaterialAtlasTexelSize((int)TerrainTypeSlot(row));
    return TerrainTypeAtlasPixels(row) * texel.xyxy;
}
bool TerrainTypeHasAtlasRect(TerrainTypeRow row) { return (row.atlasWH & KERN_TERRAIN_TYPE_PIXEL_MASK) != 0u; }
float TerrainTypeTileSize(TerrainTypeRow row)
{
    return KERN_TERRAIN_CELL_TEXELS * TerrainMaterialAtlasTexelSize((int)TerrainTypeSlot(row)).x;
}
float TerrainTypeFrameHeight(TerrainTypeRow row)
{
    float height = TerrainTypeAtlasRect(row).w;
    return height > 0.0 ? height / TerrainTypeTileSize(row) : 1.0;
}


#endif // KERN_TERRAIN_CELL_DECODING_INCLUDED
