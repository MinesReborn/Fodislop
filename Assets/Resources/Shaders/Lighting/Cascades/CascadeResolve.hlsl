#ifndef KERN_CASCADE_RESOLVE_HLSL
#define KERN_CASCADE_RESOLVE_HLSL

// ResolveDirect: чтение из атласа и запись прямого света.
//
// READS: _RadianceAtlas
// WRITES: _DirectTexture
// MUST NOT: выполнять geometry traversal, трогать DynamicLight buffers

[numthreads(8, 8, 1)]
void ResolveDirect(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_LightSize))
    {
        return;
    }

    // Probes live on the transport lattice; this receiver texel is on the
    // light lattice. Reconstruct at the receiver centre in field pixels.
    int2 pixel = int2(dispatchId.xy);
    float2 probePosition = LightPxCenterToFieldPx(pixel) / float(_CascadeProbeSpacing) - 0.5;
    int2 probeBase = int2(floor(probePosition));
    float2 blend = frac(probePosition);
    float3 radiance = 0.0;

    [unroll]
    for (int direction = 0; direction < 4; direction++)
    {
        [unroll]
        for (int y = 0; y < 2; y++)
        {
            [unroll]
            for (int x = 0; x < 2; x++)
            {
                int2 probe = clamp(probeBase + int2(x, y), int2(0, 0), _CascadeProbeSize - 1);
                int atlasIndex = _CascadeOffset + (probe.y * _CascadeProbeSize.x + probe.x) * 4 + direction;
                float weight = (x == 0 ? 1.0 - blend.x : blend.x) * (y == 0 ? 1.0 - blend.y : blend.y);
                radiance += UnpackRadiance(_RadianceAtlas[atlasIndex].xy) * weight;
            }
        }
    }

    float3 directOutput = radiance * 0.25;
    _DirectTexture[pixel] = float4(directOutput, 1.0);
}

#endif // KERN_CASCADE_RESOLVE_HLSL
