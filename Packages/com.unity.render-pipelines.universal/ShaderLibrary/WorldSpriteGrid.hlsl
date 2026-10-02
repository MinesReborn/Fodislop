#ifndef KERN_WORLD_SPRITE_GRID_INCLUDED
#define KERN_WORLD_SPRITE_GRID_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/WorldRenderGrid.hlsl"

float4 KernWorldGridObjectClipPosition(float3 positionOS)
{
    if (_KernWorldGridDensity > 0.0)
    {
        return KernWorldGridClipPosition(KernWorldGridVertex(TransformObjectToWorld(positionOS)));
    }
    return TransformObjectToHClip(positionOS);
}

#endif
