#ifndef KERN_COLOR_GRADING_INCLUDED
#define KERN_COLOR_GRADING_INCLUDED

#define KERN_CURVE_MAX_POINTS 16

float4 _MasterCurve[KERN_CURVE_MAX_POINTS];
float4 _RedCurve[KERN_CURVE_MAX_POINTS];
float4 _GreenCurve[KERN_CURVE_MAX_POINTS];
float4 _BlueCurve[KERN_CURVE_MAX_POINTS];
float4 _HueVsHueCurve[KERN_CURVE_MAX_POINTS];
float4 _HueVsSaturationCurve[KERN_CURVE_MAX_POINTS];
float4 _HueVsLuminanceCurve[KERN_CURVE_MAX_POINTS];
float4 _LuminanceVsSaturationCurve[KERN_CURVE_MAX_POINTS];
float4 _SaturationVsSaturationCurve[KERN_CURVE_MAX_POINTS];
float3 _ContrastControls2;
int _MasterCurvePointCount;
int _RedCurvePointCount;
int _GreenCurvePointCount;
int _BlueCurvePointCount;
int _HueVsHueCurvePointCount;
int _HueVsSaturationCurvePointCount;
int _HueVsLuminanceCurvePointCount;
int _LuminanceVsSaturationCurvePointCount;
int _SaturationVsSaturationCurvePointCount;
int _CurveInterpolation;

// ============================================================================
// Цветовой конвейер: лог-кодирование, творческий грейд, кривая вывода.
// ============================================================================
//
// ЗАЧЕМ ОТДЕЛЬНЫМ ФАЙЛОМ. PostProcess.compute занят кадром: блум, оптика,
// зерно, композит. Цвет — другая работа и другой словарь, и держать их в одном
// файле значило бы, что правку кривой надо искать между ветками анаморфных
// лучей.
//
// ПОРЯДОК ОПЕРАЦИЙ. Он не произволен и повторяет устройство настоящего
// цветового конвейера:
//
//   сцен-линейный кадр
//     -> экспозиция, баланс белого        линейные операции: они физичны,
//                                          это свойства съёмки, а не вкуса
//     -> лог-кодирование
//     -> CDL (slope/offset/power)         творческий грейд: он перцептивен,
//     -> контраст                          и потому живёт в логе
//     -> лог-декодирование
//     -> фотометрическая насыщенность      линейные веса Rec.709 без сдвига оттенка
//     -> кривая вывода (DRT)
//     -> кодирование дисплея
//
// Шаг в логарифме равномерен по восприятию, в линейном — нет: одна и та же
// прибавка контраста в линейном означала бы разное в тенях и в светах.

// ----------------------------------------------------------------------------
// Слой 1: линейные операции
// ----------------------------------------------------------------------------

// Баланс белого через LMS — пространство откликов колбочек глаза. Умножать
// надо именно там: в RGB множитель по каналам меняет и оттенок, а не только
// температуру, потому что каналы RGB не соответствуют ничему в зрении.
//
// Температура и оттенок здесь — не кельвины, а сдвиг в [-100, 100]
// относительно точки съёмки: крутят «холоднее/теплее», а не выставляют
// абсолютное значение.
float3 WhiteBalanceCoefficients(float temperature, float tint)
{
    float3 coefficients = float3(1.0, 1.0, 1.0);
    if (abs(temperature) >= 1e-5 || abs(tint) >= 1e-5)
    {
        float t1 = temperature / 65.0;
        float t2 = tint / 65.0;

    // Отрицательный сдвиг (в холод) уводит по x сильнее положительного:
    // планковский локус несимметричен, и равные шаги в кельвинах дают
    // неравные шаги в цветности.
        float x = 0.31271 - t1 * (t1 < 0.0 ? 0.1 : 0.05);
        float standardY = 2.87 * x - 3.0 * x * x - 0.27509507;
        float y = standardY + t2 * 0.05;

        float divisor = max(y, 1e-5);
        float3 targetXyz = float3(x / divisor, 1.0, (1.0 - x - y) / divisor);
        const float3 sourceXyz = float3(0.95047, 1.0, 1.08883); // D65
        const float3x3 bradford = float3x3(
         0.8951,  0.2664, -0.1614,
        -0.7502,  1.7135,  0.0367,
         0.0389, -0.0685,  1.0296);
        float3 sourceLms = mul(bradford, sourceXyz);
        float3 targetLms = mul(bradford, targetXyz);
        coefficients = targetLms / max(sourceLms, 1e-5);
    }

    return coefficients;
}

float3 ApplyWhiteBalance(float3 color, float3 lmsCoefficients)
{
    // The linter names the two reciprocal adaptation-basis matrices toLms and
    // fromLms. They contain the RGB<->XYZ basis used immediately around the
    // Bradford LMS multiplication below.
    const float3x3 toLms = float3x3(
        0.4124564, 0.3575761, 0.1804375,
        0.2126729, 0.7151522, 0.0721750,
        0.0193339, 0.1191920, 0.9503041);
    const float3x3 fromLms = float3x3(
         3.2404542, -1.5371385, -0.4985314,
        -0.9692660,  1.8760108,  0.0415560,
         0.0556434, -0.2040259,  1.0572252);
    const float3x3 bradford = float3x3(
         0.8951,  0.2664, -0.1614,
        -0.7502,  1.7135,  0.0367,
         0.0389, -0.0685,  1.0296);
    const float3x3 bradfordInverse = float3x3(
         0.9869929, -0.1470543,  0.1599627,
         0.4323053,  0.5183603,  0.0492912,
        -0.0085287,  0.0400428,  0.9684867);

    float3 xyz = mul(toLms, color);
    float3 lms = mul(bradford, xyz);
    lms *= lmsCoefficients;
    return mul(fromLms, mul(bradfordInverse, lms));
}

// ----------------------------------------------------------------------------
// Слой 2: лог-кодирование
// ----------------------------------------------------------------------------
//
// Своё, а не ACEScct и не лог AgX: обе чужие шкалы привязаны к чужим белым
// точкам и чужим примарам, и брать из них только кодирование значит тащить
// привязку, которой в проекте нет. Здесь шкала объявлена явно — стопы
// относительно средне-серого, нормированные в [0, 1].
//
// KernSceneToeStops стопов ниже серого и KernSceneHeadStops выше него.
// Диапазон выбран под сцену, а не под кинонегатив: 10 стопов вниз это
// серый/1024, 6.5 вверх — серый*90, чего хватает и на неон, и на тени.

static const float KernMidGrey = 0.18;
static const float KernSceneToeStops = 10.0;
static const float KernSceneHeadStops = 6.5;
static const float KernSceneStops = KernSceneToeStops + KernSceneHeadStops;

float3 KernLogEncode(float3 linearColor)
{
    // Encode magnitude and carry the sign separately. `max(color, 1e-7)`
    // looked safe but silently turned every negative HDR intermediate into a
    // positive value, so even a neutral CDL was not an identity operation.
    // The signed representation keeps values outside the nominal range until
    // the display stage, where gamut/tone mapping is finally allowed.
    //
    // Величина обязана кодироваться в неотрицательное число, иначе знак
    // теряется. Прежний log2(|c| / grey) + toe уходил ниже нуля для всего,
    // что темнее grey * 2^-toe (~1.8e-4), а decode брал abs() и зеркалил эти
    // значения обратно вверх: чем темнее канал, тем ярче он выходил. В темноте
    // каналы разной малости разъезжались в яркий фиолет, а точный ноль
    // (sign == 0) оставался чёрным. Сдвиг под логарифмом на 2^-toe даёт
    // encode(0) = 0, монотонность и точную обратимость; для средних и светлых
    // тонов он пренебрежимо мал (1/1024 серого).
    float toeFloor = exp2(-KernSceneToeStops);
    float3 magnitudeStops = log2(abs(linearColor) / KernMidGrey + toeFloor);
    float3 encodedMagnitude =
        (magnitudeStops + KernSceneToeStops) / KernSceneStops;
    return sign(linearColor) * encodedMagnitude;
}

float3 KernLogDecode(float3 logColor)
{
    float toeFloor = exp2(-KernSceneToeStops);
    float3 magnitude = abs(logColor);
    float3 stops = magnitude * KernSceneStops - KernSceneToeStops;
    return sign(logColor) * max(exp2(stops) - toeFloor, 0.0) * KernMidGrey;
}

// ----------------------------------------------------------------------------
// Слой 3: ASC CDL — стандарт обмена грейдом
// ----------------------------------------------------------------------------
//
// out = (in * slope + offset) ^ power, по каналам. Формулу описал American
// Society of Cinematographers, её понимает любой инструмент цветокоррекции, и
// грейд в этом виде вывозится файлом .cdl.
//
// Соответствие привычным словам: slope — усиление (gain), offset — подъём
// (lift), power — гамма средних тонов.
float3 ApplyCdl(float3 color, float3 slope, float3 offset, float3 power)
{
    float3 graded = color * slope + offset;
    // Отрицательное основание в дробной степени даёт NaN, и один такой пиксель
    // расползается по кадру временным накоплением. Но при power == 1 clamp не
    // нужен и вреден: нейтральный CDL обязан сохранить лог-значения ниже нуля,
    // иначе глубокие тени прижимаются к нижней границе рабочего диапазона.
    float3 powered = pow(max(graded, 0.0), max(power, 1e-3));
    float3 unitPower = 1.0 - step(1e-5, abs(power - 1.0));
    return lerp(powered, graded, unitPower);
}

float3 ApplyPrimaryWheels(
    float3 color,
    float3 lift,
    float3 gamma,
    float3 gain,
    float3 offset,
    float4 master)
{
    lift += master.xxx;
    gamma *= master.yyy;
    gain *= master.zzz;
    offset += master.www;
    float3 adjusted = color * gain + lift + offset;
    float3 powered = pow(max(adjusted, 0.0), max(gamma, 1e-3));
    float3 unitGamma = 1.0 - step(1e-5, abs(gamma - 1.0));
    return lerp(powered, adjusted, unitGamma);
}

float3 ApplySaturation(float3 color, float saturation, float3 lumaWeights)
{
    float luma = dot(color, lumaWeights);
    float amount = saturation;

    // Усиление упирается в границу гамута. Без предела самый слабый канал
    // насыщенного цвета уходил ниже нуля, дальше обрезался, и цвет менял
    // оттенок. Предел — множитель, при котором слабейший канал ровно
    // достигает нуля; яркость (luma) сохраняется при любом множителе.
    // Цвет, уже вышедший за гамут на входе, не трогается: его судьба решается
    // на выводе.
    float minimum = min(color.r, min(color.g, color.b));
    if (saturation > 1.0 && luma > 0.0 && minimum >= 0.0 && minimum < luma)
    {
        amount = min(saturation, luma / (luma - minimum));
    }

    return lerp(float3(luma, luma, luma), color, amount);
}

float3 ApplyVibrance(float3 color, float vibrance, float3 lumaWeights)
{
    float luma = dot(color, lumaWeights);
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float normalizedChroma = chroma / max(maximum, 1e-5);
    float strength = 1.0 - saturate(normalizedChroma);
    // Positive vibrance boosts weak colors; negative vibrance gently reduces
    // them while leaving already saturated colors close to their source.
    float factor = 1.0 + vibrance * strength;
    float3 adjusted = lerp(float3(luma, luma, luma), color, factor);
    float active = step(1e-5, abs(vibrance));
    return lerp(color, adjusted, active);
}

float3 ApplyContrast(float3 color, float contrast, float pivot)
{
    return (color - pivot) * (1.0 + contrast) + pivot;
}

float3 ApplyContrastControls(float3 color, float contrast, float4 controls, float3 controls2)
{
    float pivot = controls.x;
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    float shadowWeight = 1.0 - smoothstep(0.0, 0.5, luma);
    float highlightWeight = smoothstep(0.5, 1.0, luma);
    float3 adjusted = ApplyContrast(color, contrast, pivot);
    adjusted += controls.y * shadowWeight + controls.z * highlightWeight;
    adjusted += controls.w * shadowWeight + controls2.x * highlightWeight;

    float3 belowBlack = min(adjusted, 0.0);
    adjusted = max(adjusted, belowBlack / (1.0 + controls2.y * abs(belowBlack)));
    float3 aboveWhite = max(adjusted - 1.0, 0.0);
    adjusted = min(adjusted, 1.0) + aboveWhite / (1.0 + controls2.z * aboveWhite);
    return adjusted;
}

float HueDegrees(float3 color)
{
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float delta = maximum - minimum;

    float hue = 0.0;
    if (maximum == color.r)
    {
        hue = (color.g - color.b) / max(delta, 1e-6);
    }
    else if (maximum == color.g)
    {
        hue = (color.b - color.r) / max(delta, 1e-6) + 2.0;
    }
    else
    {
        hue = (color.r - color.g) / max(delta, 1e-6) + 4.0;
    }
    return frac(hue / 6.0) * 360.0 * step(1e-6, delta);
}

float HueDistanceDegrees(float a, float b)
{
    float distance = abs(a - b);
    return min(distance, 360.0 - distance);
}

float HueRangeWeight(float hue, float4 parameters)
{
    float distance = HueDistanceDegrees(hue, parameters.x);
    float feather = max(parameters.z, 0.0);
    float weight = step(distance, parameters.y);
    if (feather > 1e-5)
    {
        weight = 1.0 - smoothstep(parameters.y, parameters.y + feather, distance);
    }

    // A zero-width range with non-zero feather is still a valid soft
    // qualifier edge. Checking only the hard width made the control appear
    // enabled in the UI while producing an empty matte on the GPU.
    return weight * step(1e-5, parameters.y + feather);
}

float3 KernHSVToRGB(float3 hsv)
{
    const float4 constants = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 permutation = abs(frac(hsv.xxx + constants.xyz) * 6.0 - constants.www);
    return hsv.z * lerp(constants.xxx, saturate(permutation - constants.xxx), hsv.y);
}

float3 ApplyGlobalHue(float3 color, float shiftDegrees)
{
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = chroma / max(maximum, 1e-5);
    float3 hsv = float3(HueDegrees(color) / 360.0, saturation, maximum);
    hsv.x = frac(hsv.x + shiftDegrees / 360.0);
    float3 adjusted = KernHSVToRGB(hsv);
    return lerp(color, adjusted, step(1e-5, abs(shiftDegrees)) * step(1e-6, chroma));
}

inline float EvaluateColorCurve(float value, float4 points[KERN_CURVE_MAX_POINTS], int pointCount)
{
    value = saturate(value);
    float result = value;
    bool authored = pointCount >= 2;
    bool identity = pointCount == 2 &&
        all(abs(points[0].xy - float2(0.0, 0.0)) < 1e-5) &&
        all(abs(points[1].xy - float2(1.0, 1.0)) < 1e-5);
    if (authored && !identity)
    {
        result = points[pointCount - 1].y;
        for (int index = 1; index < KERN_CURVE_MAX_POINTS; index++)
        {
            if (index >= pointCount)
            {
                break;
            }

            float2 left = points[index - 1].xy;
            float2 right = points[index].xy;
            if (value <= right.x)
            {
                float t = saturate((value - left.x) / max(right.x - left.x, 1e-5));
                if (_CurveInterpolation == 1)
                {
                    t = t * t * (3.0 - 2.0 * t);
                }

                result = lerp(left.y, right.y, t);
                break;
            }
        }
    }

    return saturate(result);
}

inline bool IsIdentityColorCurve(float4 points[KERN_CURVE_MAX_POINTS], int pointCount)
{
    bool identity = pointCount == 2;
    if (identity)
    {
        identity = all(abs(points[0].xy - float2(0.0, 0.0)) < 1e-5) &&
            all(abs(points[1].xy - float2(1.0, 1.0)) < 1e-5);
    }

    return identity;
}

// Нейтраль кривых «X против Y» — горизонталь на 0.5 (ColorGradeCurveKind.Hue/Range).
inline bool IsNeutralSelectiveCurve(float4 points[KERN_CURVE_MAX_POINTS], int pointCount)
{
    bool neutral = true;
    for (int index = 0; index < KERN_CURVE_MAX_POINTS; index++)
    {
        if (index >= pointCount)
        {
            break;
        }

        neutral = neutral && abs(points[index].y - 0.5) < 1e-5;
    }

    return neutral;
}

// Кривые «X против Y» по индустриальному соглашению: значение кривой — сдвиг
// или множитель вокруг нейтрали 0.5, а не абсолютный результат.
//   Hue vs Hue          — сдвиг оттенка, (y - 0.5) оборота: ±180°.
//   Hue vs Saturation   — множитель насыщенности 2y: ×0…×2.
//   Hue vs Luminance    — множитель яркости 2y: ×0…×2.
//   Luminance/Saturation vs Saturation — множитель насыщенности 2y.
// Прежний абсолютный смысл делил цель на текущее значение: тёмный пиксель с
// яркостью 1e-4 умножался в тысячи раз, почти серый шум — в десятки.
//
// Ключи (оттенок, насыщенность, яркость) берутся со входа, как у маски: одна
// кривая не должна менять то, по чему выбирает следующая.
float3 ApplySelectiveCurves(float3 color)
{
    float3 lumaWeights = float3(0.2126, 0.7152, 0.0722);
    float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = saturate(chroma / max(maximum, 1e-5));
    float hue = HueDegrees(color) / 360.0;
    float inputLuma = dot(color, lumaWeights);

    // У серого и почти чёрного оттенок — шум, а не цвет. Кривые по оттенку
    // вступают плавно по мере того, как цвет становится различимым.
    float hueConfidence = smoothstep(0.02, 0.15, saturation) * step(1e-6, maximum);

    if (!IsNeutralSelectiveCurve(_HueVsHueCurve, _HueVsHueCurvePointCount))
    {
        float shift = EvaluateColorCurve(hue, _HueVsHueCurve, _HueVsHueCurvePointCount) - 0.5;
        float3 hsv = float3(frac(hue + shift * hueConfidence), saturation, maximum);
        color = lerp(color, KernHSVToRGB(hsv), step(1e-6, chroma) * step(1e-6, maximum));
    }

    if (!IsNeutralSelectiveCurve(_HueVsSaturationCurve, _HueVsSaturationCurvePointCount))
    {
        float multiplier = 2.0 * EvaluateColorCurve(hue, _HueVsSaturationCurve, _HueVsSaturationCurvePointCount);
        color = ApplySaturation(color, lerp(1.0, multiplier, hueConfidence), lumaWeights);
    }

    if (!IsNeutralSelectiveCurve(_HueVsLuminanceCurve, _HueVsLuminanceCurvePointCount))
    {
        float gain = 2.0 * EvaluateColorCurve(hue, _HueVsLuminanceCurve, _HueVsLuminanceCurvePointCount);
        color *= lerp(1.0, gain, hueConfidence);
    }

    if (!IsNeutralSelectiveCurve(_LuminanceVsSaturationCurve, _LuminanceVsSaturationCurvePointCount))
    {
        float input = saturate(inputLuma / (1.0 + max(inputLuma, 0.0)));
        float multiplier = 2.0 * EvaluateColorCurve(
            input,
            _LuminanceVsSaturationCurve,
            _LuminanceVsSaturationCurvePointCount);
        color = ApplySaturation(color, multiplier, lumaWeights);
    }

    if (!IsNeutralSelectiveCurve(_SaturationVsSaturationCurve, _SaturationVsSaturationCurvePointCount))
    {
        float multiplier = 2.0 * EvaluateColorCurve(
            saturation,
            _SaturationVsSaturationCurve,
            _SaturationVsSaturationCurvePointCount);
        color = ApplySaturation(color, multiplier, lumaWeights);
    }

    return color;
}

inline float3 ApplyDisplayCurves(float3 color)
{
    // The identity curve must remain a true no-op for HDR and negative
    // intermediate values. EvaluateColorCurve is display-domain bounded, so
    // avoid entering it until a user-authored curve is present.
    bool identity = IsIdentityColorCurve(_MasterCurve, _MasterCurvePointCount) &&
        IsIdentityColorCurve(_RedCurve, _RedCurvePointCount) &&
        IsIdentityColorCurve(_GreenCurve, _GreenCurvePointCount) &&
        IsIdentityColorCurve(_BlueCurve, _BlueCurvePointCount);
    float3 result = color;
    if (!identity)
    {
        float3 nonNegative = max(color, 0.0);
        float luma = dot(nonNegative, float3(0.2126, 0.7152, 0.0722));
        if (luma > 1e-5)
        {
            float mappedLuma = EvaluateColorCurve(luma, _MasterCurve, _MasterCurvePointCount);
            result = nonNegative * (mappedLuma / luma);
            result = float3(
                EvaluateColorCurve(result.r, _RedCurve, _RedCurvePointCount),
                EvaluateColorCurve(result.g, _GreenCurve, _GreenCurvePointCount),
                EvaluateColorCurve(result.b, _BlueCurve, _BlueCurvePointCount));
        }
    }

    return result;
}

inline float3 ApplyDisplayTransform(float3 color)
{
    // _DisplayGrade0.w: 0 = None (exact bypass for curve before/after checks),
    // 1 = SDR (Khronos PBR Neutral), 2 = HDR PQ 1300 (BT.2390-style EETF in
    // paper-relative units, peak ~= 1300/203 ~= 6.4).
    //
    // Single exit, no early returns: Metal treats a function with early exits
    // as possibly-uninitialized and warns at the kernel (see ApplyCubeLut,
    // CompressDisplayGamut for the same shape).
    float3 result = color;
    int mode = (int)_DisplayGrade0.w;

    float whitePoint = max(_DisplayGrade0.x, 1e-4);
    float greyOut = clamp(_DisplayGrade0.y, 0.05, 0.5);
    float shoulderPower = max(_DisplayGrade1.x, 1e-3);
    float toePower = max(_DisplayGrade1.y, 1e-3);
    float toeStops = clamp(_DisplayGrade1.z, 0.0, 32.0);

    if (mode == 2)
    {
        // HDR EETF: identity at and below paper white, smooth exponential
        // shoulder above it asymptoting to the display peak. Input is
        // paper-relative (DisplayFinal divides absolute nits by paperWhite),
        // output stays paper-relative for ToDisplayOutput + URP PQ encode.
        float peakRelative = max(_DisplayPeakRelative, 1.0);
        float3 x = max(color, 0.0);
        float toeFloor = exp2(-toeStops);
        float3 lifted = max(x - toeFloor, 0.0) / max(1.0 - toeFloor, 1e-4);
        float3 toe = pow(lifted / (1.0 + lifted), toePower / max(shoulderPower, 1e-3));
        float midIn = max((0.18 / whitePoint - toeFloor) / max(1.0 - toeFloor, 1e-4), 0.0);
        float midToe = pow(midIn / (1.0 + midIn), toePower / max(shoulderPower, 1e-3));
        toe *= greyOut / max(midToe, 1e-4);
        float3 over = max(x - 1.0, 0.0);
        float3 shoulder = 1.0 + (peakRelative - 1.0) * (1.0 - exp(-over / max(peakRelative - 1.0, 1e-3)));
        result = max(lerp(toe, shoulder, step(1.0, x)), 0.0);
    }
    else if (mode == 1)
    {
        // SDR: Khronos PBR Neutral. Industry standard for "no look" HDR
        // handling: everything below white passes through bit-identical
        // (no washed mids, no hue shift), only over-white compresses smoothly
        // into display range. whitePoint sets what scene value is white.
        float3 neutral = max(color, 0.0) / whitePoint;
        float startCompression = 0.8 - 0.04;
        float desaturation = 0.15;
        float x = min(neutral.r, min(neutral.g, neutral.b));
        float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
        neutral -= offset;
        float peak = max(neutral.r, max(neutral.g, neutral.b));
        if (peak >= startCompression)
        {
            float d = 1.0 - startCompression;
            float newPeak = 1.0 - d * d / (peak + d - startCompression);
            neutral *= newPeak / max(peak, 1e-6);
            float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
            neutral = lerp(neutral, newPeak.xxx, g);
        }

        result = max(neutral, 0.0);
    }

    return result;
}

float ScalarQualifierWeight(float value, float center, float width, float softness)
{
    float distance = abs(value - center);
    float result = step(distance, width);
    if (softness > 1e-5)
    {
        result = 1.0 - smoothstep(width, width + softness, distance);
    }

    return result * step(1e-5, width + softness);
}

inline float QualifierMask(float3 color)
{
    float result = 0.0;
    if (abs(_Qualifier0.w) >= 0.5)
    {
      float maximum = max(color.r, max(color.g, color.b));
    float minimum = min(color.r, min(color.g, color.b));
    float chroma = max(maximum - minimum, 0.0);
    float saturation = chroma / max(maximum, 1e-5);
    float luma = dot(max(color, 0.0), float3(0.2126, 0.7152, 0.0722));
    float hue = HueDegrees(color);
    float mask = HueRangeWeight(hue, _Qualifier0);
    for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
    {
        if (sampleIndex >= _QualifierHueSampleCount)
        {
            break;
        }

        mask = max(mask, HueRangeWeight(hue, _QualifierHueSamples[sampleIndex]));
    }
    mask *= ScalarQualifierWeight(
        saturation, _Qualifier1.x, _Qualifier1.y, _Qualifier1.z);
    mask *= ScalarQualifierWeight(
        saturate(luma), _Qualifier1.w, _Qualifier2.x, _Qualifier2.y);
    mask = _Qualifier0.w < 0.0 ? 1.0 - mask : mask;
      result = mask;
    }

    return result;
}

float3 ApplyQualifier(float3 color)
{
    float mask = QualifierMask(color);
    float3 result = color;
    if (mask > 1e-5)
    {
        result = color * exp2(_Qualifier3.x * mask);
    if (abs(_Qualifier3.y) > 0.001 || abs(_Qualifier3.z) > 0.001)
    {
        float3 whiteBalanced = ApplyWhiteBalance(
            result,
            WhiteBalanceCoefficients(_Qualifier3.y, _Qualifier3.z));
        result = lerp(result, whiteBalanced, mask);
    }
    result *= lerp(float3(1.0, 1.0, 1.0), _Qualifier6.rgb, mask);
    result += _Qualifier4.rgb * mask;
    float3 gamma = lerp(float3(1.0, 1.0, 1.0), _Qualifier5.rgb, mask);
    float3 powered = pow(max(result, 0.0), max(gamma, 1e-3));
    float3 unitGamma = 1.0 - step(1e-5, abs(gamma - 1.0));
    result = lerp(powered, result, unitGamma);
    result = ApplySaturation(
        result,
        lerp(1.0, _Qualifier2.w, mask),
        float3(0.2126, 0.7152, 0.0722));
        result = ApplyGlobalHue(result, _Qualifier2.z * mask);
    }

    return result;
}

inline float3 ApplyCubeLut(float3 color)
{
    float3 lutColor = color;
    if (_GradeLutParams.x > 1e-5)
    {
        float3 lookup = saturate(
            (color - _GradeLutDomainMin) /
            max(_GradeLutDomainMax - _GradeLutDomainMin, 1e-5));
        if (_GradeLutParams.z > 0.5)
        {
            float3 low = lookup * 12.92;
            float3 high = 1.055 * pow(max(lookup, 0.0), 1.0 / 2.4) - 0.055;
            lookup = lerp(high, low, step(lookup, 0.0031308));
        }

    // Initialize before the 1D/3D branch so Metal's definite-assignment
    // analysis cannot produce an undefined value if a future LUT mode is
    // added without a matching branch.
    if (_GradeLutParams.y < 1.5)
    {
        lutColor = _GradeLut1D.SampleLevel(sampler_GradeLut1D_LinearClamp, float2(lookup.r, 0.5), 0).rgb;
        lutColor.g = _GradeLut1D.SampleLevel(sampler_GradeLut1D_LinearClamp, float2(lookup.g, 0.5), 0).g;
        lutColor.b = _GradeLut1D.SampleLevel(sampler_GradeLut1D_LinearClamp, float2(lookup.b, 0.5), 0).b;
    }
    else
    {
        float lutSize = max(_GradeLutParams.w, 2.0);
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
    }

    if (_GradeLutParams.z > 0.5)
    {
        float3 low = lutColor / 12.92;
        float3 high = pow(max((lutColor + 0.055) / 1.055, 0.0), 2.4);
        lutColor = lerp(high, low, step(lutColor, 0.04045));
    }

    }

    return lerp(color, lutColor, saturate(_GradeLutParams.x));
}

#endif // KERN_COLOR_GRADING_INCLUDED
