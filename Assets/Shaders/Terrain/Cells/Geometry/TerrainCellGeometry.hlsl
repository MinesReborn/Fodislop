#ifndef KERN_TERRAIN_CELL_GEOMETRY_INCLUDED
#define KERN_TERRAIN_CELL_GEOMETRY_INCLUDED
int TerrainClassicJitter(int x, int y, int row)
{
    float4 k = _TerrainDistortion[row];
    int a = (int)k.x * x + (int)k.y * y;
    int b = (int)k.z * x + (int)k.w * y;
    int product = asint(asuint(a) * asuint(b));
    int number = product % (int)_TerrainDistortion[3][row - 1];
    return (number * number) % (int)_TerrainDistortion[0].y;
}

// Узел в долях клетки.
float2 TerrainNodeCells(int2 units)
{
    return float2(units.x, units.y) * (1.0 / 256.0);
}

// Узел сетки — левый нижний угол клетки (nodeX, nodeY) — в единицах 1/256
// клетки по флагам передних типов клеток вокруг узла: как
// TerrainVertexDistortionCalculator.ComputeNode.
int2 TerrainNode(int nodeX, int nodeY, TerrainTypeRow ftl, TerrainTypeRow ftr, TerrainTypeRow fbl, TerrainTypeRow fbr)
{
    int worldX = nodeX - 1;
    int worldY = nodeY - 1;
    if (_TerrainDistortionEnabled == 0)
    {
        return int2(0, 0);
    }

    bool ctl = TerrainTypeWavy(ftl);
    bool ctr = TerrainTypeWavy(ftr);
    bool cbl = TerrainTypeWavy(fbl);
    bool cbr = TerrainTypeWavy(fbr);
    bool allCause = ctl && ctr && cbl && cbr;
    int step = (int)_TerrainDistortion[0].x;
    int rx = TerrainClassicJitter(worldX, worldY, 1) * step;
    int ry = TerrainClassicJitter(worldX, worldY, 2) * step;
    int center = (int)_TerrainDistortion[0].z;

    // Как ComputeOffsetFromJitter.
    if (allCause)
    {
        return int2(rx - center, -(ry - center));
    }

    if (TerrainTypeHolds(ftl) || TerrainTypeHolds(ftr) || TerrainTypeHolds(fbl) || TerrainTypeHolds(fbr) ||
        (ctl && cbr) || (ctr && cbl))
    {
        return int2(0, 0);
    }

    if (ctl && ctr) return int2(0, -ry);
    if (ctl && cbl) return int2(-rx, 0);
    if (ctr && cbr) return int2(rx, 0);
    if (cbl && cbr) return int2(0, ry);
    if (ctl) return int2(-rx, -ry);
    if (ctr) return int2(rx, -ry);
    if (cbl) return int2(-rx, ry);
    if (cbr) return int2(rx, ry);
    return int2(0, 0);
}

// Номер угла квада из меша идентификаторов (0..3, против часовой стрелки
// от левого нижнего) — в угол клетки.
float2 TerrainCornerBase(float corner)
{
    int index = (int)round(corner);
    return float2(index == 1 || index == 2 ? 1.0 : 0.0, index >= 2 ? 1.0 : 0.0);
}


#endif // KERN_TERRAIN_CELL_GEOMETRY_INCLUDED
