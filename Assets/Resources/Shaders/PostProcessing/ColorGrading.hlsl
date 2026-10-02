#ifndef KERN_COLOR_GRADING_INCLUDED
#define KERN_COLOR_GRADING_INCLUDED

// ============================================================================
// Цветокоррекция кадра — только LUT. Его присылает сервер как эффект; до
// сервера его подгружает окно «LUT» рабочего места.
// ============================================================================
//
// LUT живёт в DisplayFinal, после тонмаппинга URP: .cube из программ
// цветокоррекции описывает отображаемый сигнал, а не сцену.
//
// В HDR кадр после тонмаппинга ярче белой точки — до пика дисплея. Таблица
// же покрывает только свою область, и прежний saturate на входе срезал всё
// ярче белого в плоскую заливку. Поэтому цвет, вышедший за верх области,
// нормируется по самому яркому каналу перед чтением и получает множитель
// обратно после: оттенок и HDR-пики сохраняются, в SDR ничего не меняется.
inline float3 ApplyCubeLut(float3 color)
{
    float3 lutColor = color;
    if (_GradeLutParams.x > 1e-5)
    {
        float3 domainSize = max(_GradeLutDomainMax - _GradeLutDomainMin, 1e-5);
        float domainTop = max(_GradeLutDomainMax.r, max(_GradeLutDomainMax.g, _GradeLutDomainMax.b));
        float peak = max(color.r, max(color.g, color.b));
        float scale = max(peak / max(domainTop, 1e-5), 1.0);
        float3 lookup = saturate((color / scale - _GradeLutDomainMin) / domainSize);
        if (_GradeLutParams.y > 0.5)
        {
            float3 low = lookup * 12.92;
            float3 high = 1.055 * pow(max(lookup, 0.0), 1.0 / 2.4) - 0.055;
            lookup = lerp(high, low, step(lookup, 0.0031308));
        }

        float lutSize = max(_GradeLutParams.z, 2.0);
        float3 position = lookup * (lutSize - 1.0);
        float3 cell = floor(position);
        float3 fraction = position - cell;
        float3 texel = 1.0 / lutSize;
        float3 baseUv = (cell + 0.5) * texel;
        float3 c000 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv, 0).rgb;
        float3 c100 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(texel.x, 0, 0), 0).rgb;
        float3 c010 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(0, texel.y, 0), 0).rgb;
        float3 c001 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(0, 0, texel.z), 0).rgb;
        float3 c110 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(texel.x, texel.y, 0), 0).rgb;
        float3 c101 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(texel.x, 0, texel.z), 0).rgb;
        float3 c011 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + float3(0, texel.y, texel.z), 0).rgb;
        float3 c111 = _GradeLut3D.SampleLevel(sampler_GradeLut3D_LinearClamp, baseUv + texel, 0).rgb;
        if (fraction.x >= fraction.y)
        {
            lutColor = fraction.y >= fraction.z
                ? c000 + (c100 - c000) * fraction.x + (c110 - c100) * fraction.y + (c111 - c110) * fraction.z
                : fraction.x >= fraction.z
                    ? c000 + (c100 - c000) * fraction.x + (c101 - c100) * fraction.z + (c111 - c101) * fraction.y
                    : c000 + (c001 - c000) * fraction.z + (c101 - c001) * fraction.x + (c111 - c101) * fraction.y;
        }
        else
        {
            lutColor = fraction.x >= fraction.z
                ? c000 + (c010 - c000) * fraction.y + (c110 - c010) * fraction.x + (c111 - c110) * fraction.z
                : fraction.y >= fraction.z
                    ? c000 + (c010 - c000) * fraction.y + (c011 - c010) * fraction.z + (c111 - c011) * fraction.x
                    : c000 + (c001 - c000) * fraction.z + (c011 - c001) * fraction.y + (c111 - c011) * fraction.x;
        }

        if (_GradeLutParams.y > 0.5)
        {
            float3 low = lutColor / 12.92;
            float3 high = pow(max((lutColor + 0.055) / 1.055, 0.0), 2.4);
            lutColor = lerp(high, low, step(lutColor, 0.04045));
        }

        lutColor *= scale;
    }

    return lerp(color, lutColor, saturate(_GradeLutParams.x));
}

#endif // KERN_COLOR_GRADING_INCLUDED
