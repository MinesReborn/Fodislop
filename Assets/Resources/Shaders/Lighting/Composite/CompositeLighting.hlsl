#ifndef KERN_COMPOSITE_LIGHTING_HLSL
#define KERN_COMPOSITE_LIGHTING_HLSL

// CompositeLighting: финальная сборка изображения.
//
// READS: _DirectInput, _StaticDirectInput, _MaterialField, _EmissionField, _SurfaceAirCache
// WRITES: _Result
// MUST NOT: вызывать DDA, трогать каскады, источники

float3 SurfaceIncidentLighting(int2 pixel, float3 centerIncident)
{
    float3 incident = centerIncident;
    float4 firstAir = _SurfaceAirCache.Load(int3(pixel, 0));
    float2 cellsPerPixel = (_WorldRect.zw / _CellSize) / float2(_LightSize);
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
        int stepIndex = (int)firstAir[i];
        if (stepIndex > 0)
        {
            int2 neighbor = pixel + offsets[i] * stepIndex;
            float3 light =
                _DirectInput.Load(int3(neighbor, 0)).rgb +
                _StaticDirectInput.Load(int3(neighbor, 0)).rgb;

            // A surface texel receives light from an exposed face according
            // to its own depth, in cells. Applying one fixed coefficient to
            // every texel copied a bright air sample across an entire wall cell.
            // Surface reflection is presentation only; it is never transmitted
            // through the wall or used as light on its opposite face.
            //
            // Depth is measured from the face to this texel's centre: the face
            // lies half a texel before the first air centre. Measuring to the
            // air centre made the weight depend on lattice density; at two
            // texels per cell every face texel sat exactly at the reach and
            // received zero surface light.
            float depthCells = length(float2(offsets[i]) * (float(stepIndex) - 0.5) * cellsPerPixel);
            float faceWeight = 1.0 - smoothstep(0.0, _SurfaceReflectionReachCells, depthCells);
            incident = max(incident, light * faceWeight * SegmentTransmission(0.0, depthCells));
        }
    }

    // The published field contains incident light. The visible material pass
    // applies receiver albedo once; coloring this field by that same albedo
    // would square the material color and tint other receivers sampling it.
    return incident;
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
        dispatchSize = _LightSize;
    }

    if (any(int2(dispatchId.xy) >= dispatchSize))
    {
        return;
    }

    int2 pixel = dispatchOrigin + int2(dispatchId.xy);
    if (any(pixel < 0) || any(pixel >= _LightSize))
    {
        return;
    }
    // Output is on the light lattice; material is read at the transport
    // texel holding this receiver's centre.
    int2 materialPixel = MaterialPixel(LightPxToFieldTexel(pixel));

    float4 material = _MaterialField.Load(int3(materialPixel.x, materialPixel.y, 0)).rgba;
    float4 emission = _EmissionField.Load(int3(materialPixel.x, materialPixel.y, 0)).rgba;
    if (_DebugView == 1) // Occupancy
    {
        _Result[pixel] = float4(material.aaa, 1.0);
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

    float solid = saturate(material.a);

    float3 directAndSurface = combinedDirect.rgb;
    if (solid > 0.0)
    {
        float3 surfaceIncident = SurfaceIncidentLighting(pixel, combinedDirect.rgb);
        // Surface presentation replaces the attenuated interior sample with
        // its exposed-face incident estimate. It does not add the center
        // incident light to itself a second time.
        directAndSurface = lerp(combinedDirect.rgb, surfaceIncident, solid);
    }

    float3 ambient = _AmbientColor.rgb;

    if (_DebugView == 8) // Exposure (false-color zebras)
    {
        // Шкала в стопах от белого, а не от единицы: контент HDR by design
        // (эмиссия до EmissionScale), а URP Neutral гасит света плавно.
        // Красный — только то что сгорит и после тонмаппа (выше потолка
        // _MaximumLightMultiplier, +3 стопа), жёлтое — рабочий HDR-запас.
        float ceiling = max(_MaximumLightMultiplier, 1.0);
        float3 result = ambient + directAndSurface;
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

    float3 output = max(directAndSurface, 0.0);
    _Result[pixel] = float4(ambient + output, 1.0);
}

#endif // KERN_COMPOSITE_LIGHTING_HLSL
