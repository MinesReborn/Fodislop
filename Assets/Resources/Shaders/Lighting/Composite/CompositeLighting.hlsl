#ifndef KERN_COMPOSITE_LIGHTING_HLSL
#define KERN_COMPOSITE_LIGHTING_HLSL

// CompositeLighting: финальная сборка изображения.
//
// READS: _DirectInput, _StaticDirectInput, _BounceInput, _MaterialField, _EmissionField, _BounceFilterWeights
// WRITES: _Result
// MUST NOT: вызывать DDA, трогать каскады, источники

float3 SurfaceReflection(float2 position, float3 albedo)
{
    float2 uv = OutputUv(position);
    float3 incident = (_DebugView == 7) ? 0.0 :
        (_DirectInput.SampleLevel(sampler_LinearClamp, uv, 0).rgb +
         _StaticDirectInput.SampleLevel(sampler_LinearClamp, uv, 0).rgb);
    float2 pixelsPerCell = float2(_FieldSize) * _CellSize / _WorldRect.zw;
    int2 pixel = int2(floor(position));
    static const int2 offsets[4] =
    {
        int2(-1, 0),
        int2(1, 0),
        int2(0, -1),
        int2(0, 1),
    };
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        // Reach stays one cell, but the light is read at the first air texel
        // on this row or column - the face itself. Sampling a fixed cell away
        // gave every texel of a one-cell block the same point in front of the
        // face, so whole blocks lit as flat squares.
        int reach = max(1, (int)ceil(abs(dot(float2(offsets[i]), pixelsPerCell))));
        [loop]
        for (int stepIndex = 1; stepIndex <= reach; stepIndex++)
        {
            int2 neighbor = pixel + offsets[i] * stepIndex;
            if (any(neighbor < 0) || any(neighbor >= _FieldSize))
            {
                break;
            }

            int2 materialNeighbor = neighbor;
            if (_MaterialYFlip != 0)
            {
                materialNeighbor.y = _FieldSize.y - 1 - materialNeighbor.y;
            }

            if (_MaterialField.Load(int3(materialNeighbor, 0)).a >= 0.5)
            {
                continue;
            }

            float2 neighborUv = OutputUv(float2(neighbor) + 0.5);
            float3 directLight = (_DebugView == 7) ? 0.0 :
                (_DirectInput.Load(int3(neighbor, 0)).rgb +
                 _StaticDirectInput.Load(int3(neighbor, 0)).rgb);
            float3 bounceLight = _BounceInput.SampleLevel(sampler_LinearClamp, neighborUv, 0).rgb;
            float3 light = directLight + bounceLight;

            // Incident light reaches the exposed face through half an air cell.
            // Surface reflection is presentation only; it is never transmitted
            // through the wall or used as light on its opposite face.
            incident = max(incident, light * SegmentTransmission(0.0, 0.5));
            break;
        }
    }

    return incident * saturate(albedo) * _BounceStrength;
}

[numthreads(8, 8, 1)]
void CompositeLighting(uint3 dispatchId : SV_DispatchThreadID)
{
    // Partial dispatch for dynamic-only frames: the host sets a field-space
    // origin/size covering the dynamic rect union plus neighbor margin. A
    // non-positive size keeps the legacy full-field behavior (native test
    // harness and any path that did not set the uniforms).
    int2 dispatchOrigin = _CompositeDispatchOrigin;
    int2 dispatchSize = _CompositeDispatchSize;
    if (dispatchSize.x <= 0 || dispatchSize.y <= 0)
    {
        dispatchOrigin = int2(0, 0);
        dispatchSize = _FieldSize;
    }

    if (any(int2(dispatchId.xy) >= dispatchSize))
    {
        return;
    }

    int2 pixel = dispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _FieldSize))
    {
        return;
    }
    float2 uv = (float2(pixel) + 0.5) / float2(_FieldSize);
    int2 materialPixel = pixel;
    if (_MaterialYFlip != 0)
    {
        materialPixel.y = _FieldSize.y - 1 - materialPixel.y;
    }

    float4 material = _MaterialField.Load(int3(materialPixel.x, materialPixel.y, 0)).rgba;
    float4 emission = _EmissionField.Load(int3(materialPixel.x, materialPixel.y, 0)).rgba;
    if (_DebugView == 1) // Occupancy
    {
        _Result[pixel] = float4(material.aaa, 1.0);
        return;
    }

    // Тот же мип занятости, из которого террейн строит AO вокруг блоков.
    // Радиус держится здесь и в Terrain.shader порознь: там он в пикселях
    // экрана, тут в текселях поля, и сводить их в одну константу нечем.
    // Вид показывает источник, а не готовую тень.
    if (_DebugView == 9) // AmbientOcclusion
    {
        float nearby = SampleOccupancy(float2(pixel) + 0.5, _TerrainAmbientOcclusionMip);
        float occlusion = saturate(sqrt(nearby) * _TerrainAmbientOcclusionStrength);
        _Result[pixel] = float4(occlusion, occlusion, occlusion, 1.0);
        return;
    }

    if (_DebugView == 2) // Albedo
    {
        _Result[pixel] = float4(material.rgb, 1.0);
        return;
    }

    if (_DebugView == 3) // Emission
    {
        _Result[pixel] = float4(emission.rgb, 1.0);
        return;
    }

    float4 dynamicDirect = _DirectInput.Load(int3(pixel.x, pixel.y, 0)).rgba;
    float4 staticDirect = _StaticDirectInput.Load(int3(pixel.x, pixel.y, 0)).rgba;

    // Прозрачность среды берётся из статической половины, а не из
    // динамической.
    //
    // ЗАЧЕМ. В этом отладочном виде ResolveDirect выходит раньше и пишет одну
    // прозрачность, без всякой эмиссии, — обе половины содержат одно и то же,
    // и опасаться загрязнения статикой здесь не от чего. Зато динамическая
    // половина решается только когда в кадре есть хоть один динамический
    // источник, а иначе её текстуру просто обнуляют (ClearDynamicDirect). Вид
    // выходил чёрным ровно там, где рядом нет ни одного источника, — то есть почти
    // всегда. Статическая половина пересчитывается при каждой смене
    // отладочного вида и потому заполнена всегда.
    if (_DebugView == 4) // Transmission
    {
        // Берётся та половина, которая в этом кадре решалась.
        //
        // В этом отладочном виде ResolveDirect выходит раньше и пишет одну
        // прозрачность, без эмиссии, — обе половины содержат одно и то же,
        // и выбирать между ними по смыслу не из чего. Зато пропущена может
        // быть любая: динамическая не решается, когда в кадре нет ни одного
        // динамического источника (её текстуру тогда обнуляют), а статическая
        // — когда геометрия не менялась. Максимум переживает пропуск любой из
        // них, тогда как жёсткая привязка к одной давала чёрный экран.
        _Result[pixel] = float4(max(staticDirect.rgb, dynamicDirect.rgb), 1.0);
        return;
    }

    if (_DebugView == 5) // StaticDirect
    {
        _Result[pixel] = float4(staticDirect.rgb, 1.0);
        return;
    }

    if (_DebugView == 6) // DynamicDirect
    {
        _Result[pixel] = float4(dynamicDirect.rgb, 1.0);
        return;
    }

    float4 combinedDirect = dynamicDirect + staticDirect;

    if (_BlockAveraged != 0 && _DebugView == 0)
    {
        _Result[pixel] = float4(max(combinedDirect.rgb, 0.0), 1.0);
        return;
    }

    float solid = saturate(material.a);
    // The bounce term reaches the image only when bounce is on, or in its own
    // debug view. Otherwise it was evaluated per pixel and then discarded.
    float3 bounce = 0.0;
    if (_EnableDiffuseBounce != 0 || _DebugView == 7)
    {
        bounce = (1.0 - solid) * SampleBounceFiltered(pixel, uv);
        if (solid > 0.0)
        {
            bounce += solid * SurfaceReflection(float2(pixel) + 0.5, material.rgb);
        }
    }

    if (_DebugView == 7) // DiffuseBounce
    {
        _Result[pixel] = float4(bounce, 1.0);
        return;
    }

    float3 bounceTerm = _EnableDiffuseBounce != 0 ? bounce : 0.0;

    float3 directAndBounce = bounceTerm + combinedDirect.rgb;
    float3 ambient = _AmbientColor.rgb;

    if (_DebugView == 8) // Exposure (false-color zebras)
    {
        // Шкала в стопах от белого, а не от единицы: контент HDR by design
        // (эмиссия до EmissionScale), а URP Neutral гасит света плавно.
        // Красный — только то что сгорит и после тонмаппа (выше потолка
        // _MaximumLightMultiplier, +3 стопа), жёлтое — рабочий HDR-запас.
        float ceiling = max(_MaximumLightMultiplier, 1.0);
        float3 result = ambient + directAndBounce;
        float peak = Max3(result);
        float stops = log2(max(peak, 1e-4));
        float ceilingStops = log2(ceiling);
        float3 falseColor = float3(0.0, 0.0, 0.0);
        if (peak > ceiling)
        {
            // Blown even after tonemap -> bright red
            falseColor = float3(1.0, 0.1, 0.1);
        }
        else if (peak > 1.0)
        {
            // HDR headroom, 0..+3 stops -> green-yellow-orange
            float t = saturate(stops / max(ceilingStops, 1e-4));
            falseColor = lerp(float3(0.3, 0.9, 0.2), float3(1.0, 0.7, 0.0), t);
        }
        else if (peak > 0.05)
        {
            // Well-exposed (< 1.0) -> green gradient
            float t = (peak - 0.05) / 0.95;
            falseColor = lerp(float3(0.05, 0.3, 0.1), float3(0.3, 0.9, 0.2), saturate(t));
        }
        else
        {
            // Deep shadow (< 0.05) -> dark blue
            float t = peak / 0.05;
            falseColor = lerp(float3(0.02, 0.04, 0.15), float3(0.05, 0.3, 0.1), saturate(t));
        }

        _Result[pixel] = float4(falseColor, 1.0);
        return;
    }

    float3 output = max(directAndBounce, 0.0);
    _Result[pixel] = float4(ambient + output, 1.0);
}

#endif // KERN_COMPOSITE_LIGHTING_HLSL
