#ifndef KERN_WORLD_GRID_FINAL_VIGNETTE_INCLUDED
#define KERN_WORLD_GRID_FINAL_VIGNETTE_INCLUDED

// Screen-resolution vignette for the world-grid final blit. With the world
// rendered at 32 pixels/cell, DisplayFinal runs per world pixel; a vignette
// there was a staircase of world texels. It belongs to the display, so it is
// evaluated here per destination pixel, on the scene sample only (before any
// UI composition). Set by Kern's display pass; reset by FinalBlitPass.
float4 _KernFinalVignette;        // intensity, smoothness, center.xy
float4 _KernFinalVignetteColor;   // output units of the scene sample
float _KernFinalVignetteAspect;

float3 KernApplyFinalVignette(float3 color, float2 viewportUv)
{
    if (_KernFinalVignette.x > 0.001)
    {
        float2 centered = (viewportUv - _KernFinalVignette.zw) * float2(_KernFinalVignetteAspect, 1.0);
        float feather = max(_KernFinalVignette.y, 0.001);
        float edgeMask = smoothstep(0.5 - feather * 0.5, 0.5 + feather * 0.5, length(centered));
        color = lerp(color, _KernFinalVignetteColor.rgb, saturate(edgeMask * _KernFinalVignette.x));
    }
    return color;
}

#endif
