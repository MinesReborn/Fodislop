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
    if (any(dispatchId.xy >= (uint2)_FieldSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    int2 samplePixel = pixel;
    GetBlockSnappedPixel(pixel, samplePixel);
    int probeIndex = samplePixel.y * _FieldSize.x + samplePixel.x;
    float3 radiance = 0.0;

    [unroll]
    for (int direction = 0; direction < 4; direction++)
    {
        int atlasIndex = _CascadeOffset + probeIndex * 4 + direction;
        uint3 packedInterval = _RadianceAtlas[atlasIndex];
        radiance += UnpackRadiance(packedInterval.xy);
    }

    float3 directOutput = radiance * 0.25;
    _DirectTexture[pixel] = float4(directOutput, 1.0);
}

#endif // KERN_CASCADE_RESOLVE_HLSL
