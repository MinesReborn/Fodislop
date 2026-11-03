#ifndef KERN_TERRAIN_CELL_DATA_INCLUDED
#define KERN_TERRAIN_CELL_DATA_INCLUDED

#include "Assets/Shaders/Terrain/Geometry/TerrainGeometryContract.hlsl"
#include "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl"
#include "Assets/Shaders/Terrain/Textures/TerrainAtlasSampling.hlsl"
#include "Assets/Shaders/Terrain/Materials/TerrainMaterialCBuffer.hlsl"
#include "Assets/Shaders/Terrain/Animation/TerrainAnimationProfile.hlsl"

// Квад террейна из данных клетки — только функции. Данные (раскладка,
// буферы, глобалы) — в TerrainCellFormat.hlsl.
//
// Меш идентификаторов (TerrainCellIdMesh) несёт в POSITION адрес квада
// (x, y, слой: 0 фон, 1 передний план, 2 накладка дверей) и угол. Из
// клетки, её восьми соседей и строк их типов восстанавливаются ровно те
// атрибуты, что TerrainQuadBuilder пишет в вершину.

#include "Assets/Shaders/Terrain/Cells/Decoding/TerrainCellDecoding.hlsl"
#include "Assets/Shaders/Terrain/Autotiling/TerrainCellTiling.hlsl"
#include "Assets/Shaders/Terrain/Cells/Variation/TerrainCellVariation.hlsl"
#include "Assets/Shaders/Terrain/Cells/Geometry/TerrainCellGeometry.hlsl"
#include "Assets/Shaders/Terrain/Cells/Vertices/TerrainCellVertexLoader.hlsl"

#endif
