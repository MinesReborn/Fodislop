#ifndef KERN_LIGHTING_FIELD_RASTER_INCLUDED
#define KERN_LIGHTING_FIELD_RASTER_INCLUDED

// Raster transform of world-space lighting fields. Bound per pass by
// LightingFieldOrientation.BindRaster; the same C# owner publishes the row
// order every field reader uses. Camera matrices are never read here.
float4x4 _KernLightingFieldView;
float4x4 _KernLightingFieldProjection;
float4x4 _KernLightingFieldObjectToWorld;

// For draws whose object matrix comes from the draw call (field contributors).
float4 KernLightingFieldClipPositionWorld(float3 positionWS)
{
    float4 fieldView = mul(_KernLightingFieldView, float4(positionWS, 1.0));
    return mul(_KernLightingFieldProjection, fieldView);
}

// For the terrain cell mesh, whose object matrix is bound with the field pass.
float4 KernLightingFieldClipPosition(float3 positionOS)
{
    float4 world = mul(_KernLightingFieldObjectToWorld, float4(positionOS, 1.0));
    return KernLightingFieldClipPositionWorld(world.xyz);
}

#endif
