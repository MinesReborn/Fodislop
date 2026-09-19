#ifndef KERN_LIGHTING_COORDINATES_HLSL
#define KERN_LIGHTING_COORDINATES_HLSL

// ============================================================================
// Coordinate Spaces & Conversions
// ============================================================================
//
// Spaces:
//   worldPos   - World coordinates in meters (float2). _WorldRect = (minX, minY, width, height)
//   cellCoord  - Integer world grid cell coordinate (int2) [0..N-1]
//   fieldPx    - Continuous or discrete pixel inside lighting field (float2/int2) [0.._FieldSize-1]
//   fieldUv    - Normalized texture coordinate [0..1]
//   probeCoord - Probe grid coordinate inside a cascade [0.._CascadeProbeSize-1]
//
// RULE: Never write ad-hoc inline coordinate math inside kernels.
// Use these explicit functions.
// ============================================================================

// COST: O(1) ALU
float2 WorldToFieldPx(float2 worldPos)
{
    return (worldPos - _WorldRect.xy) / _WorldRect.zw * float2(_FieldSize);
}

// COST: O(1) ALU
float2 FieldPxToWorld(float2 fieldPx)
{
    return _WorldRect.xy + (fieldPx / float2(_FieldSize)) * _WorldRect.zw;
}

// COST: O(1) ALU
int2 FieldPxToCellCoord(float2 fieldPx)
{
    float2 worldPos = FieldPxToWorld(fieldPx);
    return int2(floor(worldPos / max(_CellSize, 0.0001)));
}

// COST: O(1) ALU
float2 CellCoordToWorld(int2 cellCoord)
{
    return float2(cellCoord) * _CellSize;
}

// COST: O(1) ALU
float2 CellCoordToFieldPx(int2 cellCoord)
{
    return WorldToFieldPx(CellCoordToWorld(cellCoord));
}

// COST: O(1) ALU
float2 FieldPxToUv(float2 fieldPx)
{
    return saturate(fieldPx / float2(_FieldSize));
}

// COST: O(1) ALU
float2 ProbeToFieldPx(int2 probeCoord, int probeSpacing)
{
    return (float2(probeCoord) + 0.5) * float(probeSpacing);
}

// COST: O(1) ALU
float2 FieldPxToProbeCoord(float2 fieldPx, int probeSpacing)
{
    return fieldPx / float(probeSpacing) - 0.5;
}

#endif // KERN_LIGHTING_COORDINATES_HLSL
