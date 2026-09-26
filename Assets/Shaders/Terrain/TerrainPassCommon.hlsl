#ifndef KERN_TERRAIN_PASS_COMMON_INCLUDED
#define KERN_TERRAIN_PASS_COMMON_INCLUDED

// Геометрический контракт вершин, общий между экранным, material/emission и
// AO-проходами. Выборка анимационных flow-текстур живёт отдельно.

// Атлас у клетки может отсутствовать (клетка за миром, не загружена или слой
// пуст). Такой квад не отбрасывается на CPU, он выталкивается за плоскость
// отсечения: это дешевле, чем менять состав меша каждый кадр.
float4 TerrainVertexClipPosition(float atlasIndex, float3 positionOS)
{
    return atlasIndex >= 0.0
        ? TransformObjectToHClip(positionOS)
        : TerrainCulledPosition();
}

// Вход вершины у обоих проходов один. В режиме клеток из него читаются только
// positionOS (адрес клетки) и uv (угол квада), остальное приходит из текстур
// данных клетки; вне режима клеток — всё.
struct TerrainVertexInput
{
    float4 positionOS   : POSITION;
    float2 uv           : TEXCOORD0;
#if !defined(KERN_TERRAIN_CELLS)
    // The cell-ID mesh carries only POSITION and TEXCOORD0; all remaining
    // vertex data is fetched from the cell-data textures in this variant.
    // Keep these inputs out of its Metal vertex signature so Unity does not
    // bind dummy attributes for channels the mesh intentionally omits.
    float4 color        : COLOR;
    float4 subAtlasRect : TEXCOORD1;
    float4 tileSizeUV   : TEXCOORD2;
    float4 worldPosAttr : TEXCOORD3;
    float4 animData     : TEXCOORD4;
    float4 packedData   : TEXCOORD5;
    float4 glowAttr     : TEXCOORD6;
#endif
};

// Разбор вершины в режиме клеток. Объявляет `cell` — проходу она нужна и
// после макроса.
#define TERRAIN_RESOLVE_CELL_VERTEX(input, output) \
    TerrainCellVertex cell = LoadTerrainCellVertex( \
        input.positionOS.xyz, input.uv); \
    output.positionCS = TerrainVertexClipPosition(cell.atlasIndex, cell.positionOS); \
    output.uv = cell.uv; \
    output.color = cell.color; \
    output.subAtlasRect = cell.subAtlasRect; \
    output.tileSizeUV = cell.tileSizeUV; \
    output.worldPos = cell.worldPos; \
    output.animData = cell.animData; \
    output.packedData = cell.packedData; \
    output.glowData = cell.glowData; \
    output.geometryCornersX = cell.geometryCornersX; \
    output.geometryCornersY = cell.geometryCornersY; \
    output.uvBits = cell.uvBits; \
    output.atlasIndex = cell.atlasIndex; \
    output.isForeground = cell.layer > 0.5 ? 1.0 : 0.0;

// Разбор вершины вне режима клеток — накладка дверей.
//
// Углы клетки КАНОНИЧЕСКИЕ, а не нули. Этот путь вершин несёт уже смещённый
// полигон в POSITION, поэтому вырезание по геометрии ему не нужно
// (applyGeometry здесь ноль), но кайма нормирует выборку по размаху этих
// углов. При нулях размах нулевой, зажимается в 0.0001, и клеточная координата
// становится (1,1) на каждом фрагменте: оверлей дверей рисовался плоским
// прямоугольником в 1/8 яркости.
#define TERRAIN_RESOLVE_ATTRIBUTE_VERTEX(input, output) \
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz); \
    output.uv = input.uv; \
    output.color = input.color; \
    output.subAtlasRect = input.subAtlasRect; \
    output.tileSizeUV = input.tileSizeUV; \
    output.worldPos = input.worldPosAttr; \
    output.animData = input.animData; \
    output.packedData = input.packedData; \
    output.glowData = input.glowAttr; \
    output.geometryCornersX = float4(0.0, 1.0, 1.0, 0.0); \
    output.geometryCornersY = float4(0.0, 0.0, 1.0, 1.0); \
    output.uvBits = -1.0; \
    output.atlasIndex = 0.0; \
    output.isForeground = input.positionOS.z < 0.05 ? 1.0 : 0.0;

#endif
