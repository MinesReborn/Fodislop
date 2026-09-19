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
Texture2D<float4> _TerrainGridOffsets;

// x, y — размер сетки в клетках; z — размер клетки в мире; w — искажение
// включено (1) или нет (0): без него смещения узлов не читаются вовсе.
float4 _TerrainCellGridSize;

// Мировая клетка локального (0, 0) окна. Тексели лежат по кольцевому
// адресу: мировая координата по модулю размера сетки.
float4 _TerrainCellOrigin;

// Начало рисуемого окна внутри сетки. Экран рисует меш размером с видимое
// окно, поле материалов — меш всей сетки со смещением ноль.
float4 _TerrainCellViewOffset;

int TerrainRing(int value, int size)
{
    int remainder = value % size;
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
    float atlasIndex;
    float layer;
};

// node — локальный узел окна.
float3 TerrainGridOffset(int2 node)
{
    int2 origin = (int2)round(_TerrainCellOrigin.xy);
    int nodesWide = (int)round(_TerrainCellGridSize.x) + 1;
    int nodesHigh = (int)round(_TerrainCellGridSize.y) + 1;
    int2 ring = int2(
        TerrainRing(origin.x + node.x, nodesWide),
        TerrainRing(origin.y + node.y, nodesHigh));
    return _TerrainGridOffsets.Load(int3(ring, 0)).xyz;
}

TerrainCellVertex LoadTerrainCellVertex(float3 address, float2 cornerBase)
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
    v.uv = float2((uvBits >> (corner * 2)) & 1u, (uvBits >> (corner * 2 + 1)) & 1u);
    v.atlasIndex = round(meta.r * 255.0) - 1.0;
    v.layer = layer;

    // Фон под сплошным передним планом (флаг в синем канале meta) не виден
    // ни в одном пикселе: отбрасывается так же, как незаполненный квад.
    if (layer == 0 && meta.b > 0.5)
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

    // Якорь: флаг ставится, если сдвинут хоть один из четырёх узлов квада,
    // а сам якорь угла — это угол плюс смещение его узла.
    bool anchored = false;
    float3 offset = 0.0;
    if (_TerrainCellGridSize.w > 0.5)
    {
        float3 offset00 = TerrainGridOffset(int2(x, y));
        float3 offset10 = TerrainGridOffset(int2(x + 1, y));
        float3 offset11 = TerrainGridOffset(int2(x + 1, y + 1));
        float3 offset01 = TerrainGridOffset(int2(x, y + 1));
        anchored = any(offset00 != 0.0) || any(offset10 != 0.0) ||
            any(offset11 != 0.0) || any(offset01 != 0.0);
        offset = TerrainGridOffset(int2(x, y) + cornerStep);
    }
    v.packedData = float4(anchored ? 1.0 : 0.0, cornerBase + offset.xy, 0.0);

    float cellSize = _TerrainCellGridSize.z;
    v.positionOS = float3(
        (x + cornerBase.x) * cellSize,
        (y + cornerBase.y) * cellSize,
        layer == 0 ? 0.1 : 0.0) + offset;
    return v;
}

// Клип-позиция, которую растеризатор отбрасывает целиком.
float4 TerrainCulledPosition()
{
    return float4(2.0, 2.0, 2.0, 1.0);
}

#endif
