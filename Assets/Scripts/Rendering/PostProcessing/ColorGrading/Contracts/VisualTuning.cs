#nullable enable

using UnityEngine;

namespace Kern.Game
{
    // Входные пороги покрытия мировых спрайтов и полей перед композицией.
    public static class WorldRenderConfigHolder
    {
        public const float SpriteAlphaCull = 0.003f;
        public const float EmissiveFieldThreshold = 0.05f;
        public const float SurfaceFieldThreshold = 0.05f;
    }
}

namespace Kern.UI.Backgrounds
{
    // Авторские параметры процедурного меню-фона.
    public static class FractalBackgroundLook
    {
        public const float Speed = 1f;
        public const float FoldFrequency = 32f;
        public const int Iterations = 50;
        public static Color Tint => Color.white;
    }
}

namespace Kern.UI
{
    // Авторские параметры процедурного звёздного фона главного меню.
    public static class MenuStarfieldLook
    {
        public const float Density = 96f;
        public const float Brightness = 1.6f;
        public const float CoreSize = 0.010f;
        public const float GlowSize = 0.06f;
        public const float TwinkleAmount = 0.1f;
        public const float TwinkleSpeed = 0.8f;
        public static Color SkyColor => new(0.012f, 0.018f, 0.032f, 1f);
        public const float NebulaIntensity = 0.65f;
        public static Color NebulaColor1 => new(0.025f, 0.055f, 0.11f, 1f);
        public static Color NebulaColor2 => new(0.09f, 0.045f, 0.025f, 1f);

        public const float NebulaFbmStartAmplitude = 0.5f;
        public const float NebulaFbmFrequencyScale = 2.1f;
        public const float NebulaFbmAmplitudeDecay = 0.5f;
        public const int NebulaFbmOctaves = 3;
        public const float NebulaCoordinateScale = 1.5f;
        public static Vector2 NebulaFbmShift => new(100f, 100f);
        public static Vector2 NebulaWarpOffset => new(5.2f, 1.3f);
        public const float NebulaWarpStrength = 0.7f;
        public const float NebulaDustThresholdStart = 0.34f;
        public const float NebulaDustThresholdEnd = 0.76f;
        public const float NebulaGasThresholdStart = 0.42f;
        public const float NebulaGasThresholdEnd = 0.82f;
        public const float NebulaGasContrast = 1.15f;
        public const float NebulaGasMix = 0.65f;

        public const float StarPresenceThreshold = 0.62f;
        public const float StarMagnitudePower = 6f;
        public const float StarMinimumRadiusScale = 0.55f;
        public const float StarMagnitudeRadiusScale = 1.9f;
        public const float StarWingDistanceScale = 42f;
        public const float StarWingMix = 0.1f;
        public const float StarRateMinimum = 0.5f;
        public const float StarRateRange = 1.5f;
        public const float StarSensorBreathingAmplitude = 0.05f;
        public const float StarMagnitudeFloor = 0.03f;
        public const float StarPresenceHashOffset = 7.13f;
        public const float StarMagnitudeHashOffset = 19.7f;
        public const float StarPhaseHashOffset = 3.77f;
        public const float StarRateHashOffset = 11.3f;
        public const float StarColorHashOffset = 5.19f;
        public const float SecondaryStarDensityScale = 0.43f;
        public const float SecondaryStarSizeScale = 0.22f;
        public const float SecondaryStarSeed = 41.7f;
        public const float SecondaryStarBrightness = 1.6f;

        public static Vector3 StarColorBlue => new(0.72f, 0.80f, 1f);
        public static Vector3 StarColorWhite => new(1f, 0.98f, 0.96f);
        public static Vector3 StarColorYellow => new(1f, 0.93f, 0.78f);
        public static Vector3 StarColorOrange => new(1f, 0.80f, 0.60f);
        public static Vector3 StarColorRed => new(1f, 0.68f, 0.50f);
        public const float StarColorBlueEnd = 0.22f;
        public const float StarColorYellowEnd = 0.48f;
        public const float StarColorOrangeEnd = 0.75f;
    }

    // Геометрия и яркость статичного указателя миссии.
    public static class MissionRingLook
    {
        public const float ViewportRadiusFraction = 0.34f;
        public const float CenterFadeDistance = 0.6f;
        public const float FullOpacityDistance = 2.2f;
        public const float MinimumVisibleOpacity = 0.001f;
        public const float Radius = 0.74f;
        public const float VerticalSquash = 0.90f;
        public const float ArcHalfAngle = 0.50f;
        public const float ArcThickness = 0.030f;
        public const float EndTaper = 0.45f;
        public const float CoreWidth = 0.35f;
        public const float ArcEnergy = 1f;
        public const float CoreEnergy = 0.85f;
        public static Color CoreColor => new(1f, 0.95f, 0.86f, 1f);
    }
}

namespace Kern.World.Terrain
{
    // Авторский вид поверхности террейна. Отсюда читает дефолты
    // TerrainSettings: масштаб потока, скорости шиммера и пульсации,
    // цвета и силы эмиссии переходов и дальней поверхности, занятость
    // поверхности. Это величины террейна, поэтому живут они здесь, а не
    // в постобработке.
    public static class TerrainConfigHolder
    {
        // 1. Смещение узлов сетки (шаг = 1/32 клетки). Classic умножает
        // детерминированный джиттер 0..6; Organic центрирует шум в диапазоне
        // -OrganicMaximumOffsetSteps/2 .. +OrganicMaximumOffsetSteps/2.
        public const int ClassicDistortionStrengthSteps = 1;
        public const int OrganicMaximumOffsetSteps = 4;

        // Скорости профилей записаны в legacy единицах данных клетки; шейдер
        // сам переводит их в фазу анимации.
        public const float PrismaticCrystalAnimationSpeed = 50f;
        public const float FacetedCrystalAnimationSpeed = 0.06f;


        // Ключи классического джиттера задают пространственный рисунок.
        // Диапазон хэша нечётный, чтобы свободный узел центрировался точно.
        public const int ClassicJitterRange = 7;
        public const int ClassicXHashA = 5;
        public const int ClassicXHashB = 11;
        public const int ClassicXHashC = 13;
        public const int ClassicXHashD = 7;
        public const int ClassicXHashModulus = 3221;
        public const int ClassicYHashA = 17;
        public const int ClassicYHashB = 19;
        public const int ClassicYHashC = 23;
        public const int ClassicYHashD = 37;
        public const int ClassicYHashModulus = 3469;

        // Три пространственных масштаба органического смещения и их веса.
        // Оба направления используют одинаковые масштабы и веса, но разные
        // ключи шума, чтобы не двигаться по диагонали синхронно.
        public const int OrganicNoiseBroadPeriodCells = 9;
        public const int OrganicNoiseMediumPeriodCells = 4;
        public const int OrganicNoiseFinePeriodCells = 2;
        public const float OrganicNoiseBroadWeight = 0.50f;
        public const float OrganicNoiseMediumWeight = 0.35f;
        public const float OrganicNoiseFineWeight = 0.15f;
        public const float OrganicNoiseContrast = 2f;
        public const float OrganicNoiseCenter = 0.5f;
        public const uint OrganicNoiseBroadXSeed = 0xA53u;
        public const uint OrganicNoiseMediumXSeed = 0xB71u;
        public const uint OrganicNoiseFineXSeed = 0xC25u;
        public const uint OrganicNoiseBroadYSeed = 0xD49u;
        public const uint OrganicNoiseMediumYSeed = 0xE83u;
        public const uint OrganicNoiseFineYSeed = 0xF17u;
        public const uint OrganicEdgeVerticalSeed = 0x6D21u;
        public const uint OrganicEdgeHorizontalSeed = 0x39B7u;

        // Органическое искажение грани: сдвиг вершины на один шаг изгиба, в
        // долях сетки грани. Код изгиба лежит в {-2..2}, поэтому вершина
        // уходит не дальше произведения этого размаха на силу; ту же величину
        // берёт внутренний отступ в TerrainContour, так что менять их надо вместе.
        public const float OrganicBendStrength = 1f;

        // Где по ребру стоит излом изогнутой грани, в долях длины ребра;
        // изгиб противоположного знака берёт дополнение до единицы.
        public const float OrganicBendPivot = 0.35f;

        // 2. Приём геометрии пиксельным проходом: ранний discard.
        public const float AlphaCutoff = 0.05f;

        // 3. Поверхностные координаты и базовая цветовая анимация.
        public static Vector2 FlowScale => new(12f, 10f);
        public const float ShimmerSpeedScale = 0.05f;
        public const float PulseSpeedScale = 0.5f;
        public static Color ShimmerColor => Color.white;
        public const float ShimmerChromaFloor = 0.65f;

        // Скорости цветовых фаз и параметры профилей.
        public const float PrismaticPhaseSpeed = 0.05f;
        public const float RainbowHueDivisor = 255f;

        public static Color PrismaticTintA => new(0.2f, 1f, 0.2f, 1f);
        public static Color PrismaticTintB => new(0.2f, 0.2f, 1f, 1f);
        public static Color PrismaticTintC => Color.white;
        public static Color PrismaticTintD => new(0.1f, 1f, 1f, 1f);
        public static Color PrismaticTintE => new(1f, 0f, 0f, 1f);

        // Глинт — ещё одна модификация анимированного цвета до освещения.
        public static Vector2 FacetedGlintDirection => new(0.62f, 0.38f);
        public const float FacetedGlintSweepStart = -0.12f;
        public const float FacetedGlintSweepEnd = 1.12f;
        public const float FacetedGlintBandStart = 0.035f;
        public const float FacetedGlintBandEnd = 0.13f;
        public const float FacetedGlintMaskStart = 0.2f;
        public const float FacetedGlintMaskEnd = 0.75f;
        public const float FacetedGlintStrength = 0.45f;
        public const float FacetedGlintMix = 0.72f;
        public const float FacetedGlintRiseEnd = 0.04f;
        public const float FacetedGlintFallStart = 0.28f;
        public const float FacetedGlintFallEnd = 0.40f;
        public const float FacetedGlintSweepDuration = 0.40f;

        // 4. Декали подмешиваются после базовой цветовой анимации.
        public const float GroundDecalStrength = 0.35f;
        public const float StoneDecalStrength = 0.7f;
        public const float DecalPlacementOffset = 0.5f;
        public const uint GroundDecalPlacementPercent = 24u;

        // Усиление signed-разницы в диагностике вклада декали и призматического
        // тинта. На сам рендер поверхности этот коэффициент не влияет.
        public const float TerrainDebugDeltaContrast = 128f;

        // 5. Параметры world-surface field публикуются в материал и поле света.
        public const float SurfaceOccupancy = 1f;
        public static Color TransitEmissionColor => Color.white;
        public const float TransitEmissionStrength = 0.35f;
        public static Color PerspectiveEmissionColor => Color.white;
        public const float PerspectiveEmissionStrength = 0.12f;

        // 6. Контактное затенение: сила контраста и самый тёмный уровень,
        // до которого оно опускает поверхность.
        //
        // Сила слегка усиливает среднюю часть контактной маски; насыщение
        // остаётся ограничено единицей в KernSampleTerrainAmbientOcclusion.
        // Пол задаёт нижний уровень яркости поверхности.
        //
        // 0.51 — не подбор на глаз. В оригинале тень на полу это 1 - z² при
        // z = 0.7, то есть ровно 0.51, и глубже пол там не темнеет никогда.
        public const float AmbientOcclusionStrength = 1.1f;
        public const float AmbientOcclusionFloor = 0.51f;
        // Максимальная ширина поля контактного затенения за геометрией, в клетках.
        public const float AmbientOcclusionDistanceCells = 0.75f;

        // Радиус скруглённого силуэта loose-террейна, в клетках.
        public const float RoundableCornerRadiusCells = 0.51f;

        // Фаска открытых сторон клетки: масштаб расстояния от геометрического
        // ребра и максимальная доля затемнения у самого ребра.
        public const bool ReliefRimQuantizationEnabled = true;
        public const float ReliefRimDistanceScale = 8f;
        public const float ReliefRimFalloff = 0.5f;

        // 7. Выход: premultiplied alpha корректируется после умножения света.
        public const float PremultiplyAlphaFloor = 0.15f;
    }
}

namespace Kern.World.Lighting
{
    [System.Flags]
    public enum LightingFeatureFlags
    {
        None = 0,
        StaticRC = 1 << 0,
        DynamicLights = 1 << 1,
    }

    public static class LightingConfigHolder
    {
        public static LightingFeatureFlags EnabledFeatures { get; set; } =
            LightingFeatureFlags.StaticRC |
            LightingFeatureFlags.DynamicLights;

        // 1. Геометрическая классификация входного материала для транспорта.
        public const float SolidOccupancyThreshold = 0.5f;
        public const float TransportSolidThreshold = 0.4f;

        // Общая экспозиция сцены. Одно число, на которое умножается весь свет:
        // и заполняющий, и прямой, и эмиссия. Соотношения между ними не
        // меняются — меняется только то, где вся картина стоит относительно
        // белой точки дисплея.
        //
        // Яркость нельзя чинить эмиссией. Эмиссия поднимает только источники,
        // то есть ровно то, что и так лежит выше белого и всё равно будет
        // сжато выводом: кадр от неё не светлеет, а источники выжигаются.
        // Темноту двигает экспозиция, и двигать её надо здесь, у источника
        // величин, а не грейдом на выводе: грейд стоит полноэкранного прохода,
        // а здесь это тот же умножитель, что уже уходит в шейдер.
        //
        // Единица — исходная авторская калибровка. При ней даже полностью
        // освещённая поверхность не доходила до белой точки, выше единицы
        // жили только сами источники, и вся работа вывода — плавное сжатие
        // пересвета — не начиналась вовсе: сжимать было нечего. Штатные два
        // поднимают сцену на стоп, после чего освещённое доходит до белого,
        // а пересвет попадает туда, где SDR его свернёт, а HDR покажет.
        // Это единственная ручка общей яркости; крутить её и только её.
        //
        // Свойство, а не константа: ручка вынесена в инструменты (F1, окно
        // «Цвет и вывод»), и подбирать экспозицию надо глазом на живой сцене.
        // Значение здесь — штатное; инструмент меняет его на сессию, файл
        // остаётся авторским источником правды.
        public const float DefaultSceneExposureScale = 2.0f;

        public static float SceneExposureScale { get; set; } = DefaultSceneExposureScale;

        // 2. Экспозиция и интенсивности источников/заполняющего света.
        // Базовые величины — авторская калибровка при экспозиции 1. Наружу
        // отдаются уже помноженными: потребители читают их каждый кадр, и
        // поворот ручки виден сразу, без пересборки.
        public const float BaseAmbientIntensity = 0.25f;
        public const float BaseEmissionScale = 10.0f;
        public const float BaseDynamicLightIntensity = 1.0f;
        public const float BaseMaximumLightMultiplier = 8.0f;

        public static float AmbientIntensity => BaseAmbientIntensity * SceneExposureScale;
        public static float EmissionScale => BaseEmissionScale * SceneExposureScale;
        public static readonly Color AmbientColor = Color.white;
        public static bool DynamicLightEnabled => (EnabledFeatures & LightingFeatureFlags.DynamicLights) != 0;
        public static float DynamicLightIntensity =>
            BaseDynamicLightIntensity * SceneExposureScale;
        public static readonly Color DynamicLightColor = Color.white;

        // Пропускание однородной среды применяется трассировщиком вдоль пути.
        // Per RGB channel: sigma = ExtinctionRGB * ExtinctionMultiplier.
        // Transmission after d cells = exp(-sigma * d); multiply incoming light by it.
        // sigma: 0 = transparent; 0.2 = 81.87% per cell; 4.60517 = 1% per cell.
        // Solid affects transmission through the wall, not illumination of its front surface.
        public static readonly Color EmptyExtinctionRGB = Color.white;
        public static readonly Color SolidExtinctionRGB = Color.white;
        public const float EmptyExtinctionMultiplier = 0.20f;
        public const float SolidExtinctionMultiplier = 1.0f;

        // 3. Пространственный бюджет и фильтрация трассировки.
        // Плечо отражения поверхности: путь до лицевой грани, в клетках.
        public const float SurfaceReflectionReachCells = 0.5f;

        // Ближняя зона динамики использует прямой DDA, дальше — полярный веер.
        public const float DynamicNearCells = 6.0f;

        // Запас дальности до отсечения по затуханию; это slack, не радиус.
        public const float DynamicReachSlackTexels = 2f;
        public const float DynamicReachSlackCells = 1.5f;

        // Маржа входа источника и границы невидимости в полярном веере.
        public const float DynamicPolarMargin = 1.5f;

        // Угловая плотность сэмплов на источник, стоимость растёт линейно.
        public const int DynamicAngularSampleCount = 8;

        // Плотность сэмплирования эмиттера по оси. Зеркалится в
        // LightingComputeBinder.DynamicEmitterPointCount — менять только парой.
        public const int DynamicEmitterPointsPerAxis = 3;

        // Билинейный фикс при чтении соседних записей атласа каскада.
        public const bool EnableBilinearFix = true;

        // 4. Производная величина для exposure-зебры после расчёта света.
        // Стеля exposure-зебры (вид 9): всё выше — згорить і після тонмаппа.
        // Шкала в стопах від білого: 8.0 = +3 стопи. Контент HDR by design
        // (емісія до EmissionScale), тому стеля 1.0 фарбувала червоним весь
        // робочий HDR-запас.
        // Масштабируется вместе со сценой: это потолок в тех же величинах,
        // и без множителя ложная раскраска показывала бы пересвет там, где
        // его нет.
        public static float MaximumLightMultiplier =>
            BaseMaximumLightMultiplier * SceneExposureScale;

    }
}

namespace Kern.Rendering.PostProcessing
{
    public static class PostProcessLook
    {
        // Переключатели включают соответствующие этапы графа.
        public static class Effects
        {
            public const bool Bloom = false;
            public const bool Vignette = false;
            public const bool Eigengrau = false;
        }

        // 1. Пирамида блум собирается из HDR-входа до финального композита.
        public static class Bloom
        {
            public const float Intensity = 0.35f;
            public const float Threshold = 0.5f;
            public const float SoftKnee = 0.5f;
            public const float Radius = 1.5f;
            public const float Scatter = 0.35f;

            public static Color Tint => Color.white;
        }

        // 2. Творческий грейд выполняется в композитном проходе.
        public static class ColorGrading
        {
            public const float Exposure = 0f;
            public const float Contrast = 0f;
            public const float Saturation = 1f;

            public static Color Filter => Color.white;

            public const float ContrastPivot = 0.5f;
            public const float TonalAdjustment = 0f;
            public const float CdlSaturation = 1f;
            public const float Vibrance = 0f;
            public const float Hue = 0f;
            public static Vector3 CdlMaster => new(1f, 0f, 1f);
        }

        // 3. Nits профиля задают нормализацию входа и предел вывода дисплея.
        public static class DisplayCalibration
        {
            public const float PaperWhiteNits = 350f;
            public const float PeakBrightnessNits = 1300f;
        }

        // 4. Display transform, LUT и кривые выводят кадр в сигнал дисплея.
        public static class Grade
        {
            public const DisplayTransform Transform = DisplayTransform.None;
            public const float WhitePoint = 1f;
            public const float Temperature = 0f;
            public const float Tint = 0f;

            public static Vector3 Slope => Vector3.one;

            public static Vector3 Offset => Vector3.zero;

            public static Vector3 Power => Vector3.one;

            public const float GreyOut = 0.18f;
            public const float ShoulderPower = 4f;
            public const float ToePower = 1.6f;
            public const float ToeStops = 12f;

            // Настройки сжатия гамута применяются в display pass.
            public const bool GamutCompressionEnabled = false;
            public const float GamutCompressionStrength = 1f;

            public static Vector3 PrimaryLift => Vector3.zero;
            public static Vector3 PrimaryGamma => Vector3.one;
            public static Vector3 PrimaryGain => Vector3.one;
            public static Vector3 PrimaryOffset => Vector3.zero;
            public static Vector4 PrimaryMaster => new(0f, 1f, 1f, 0f);
        }

        // Начальные настройки квалификатора оттенка, насыщенности и яркости.
        public static class Qualifier
        {
            public const float HueCenter = 120f;
            public const float HueWidth = 30f;
            public const float HueSoftness = 15f;
            public const float SaturationCenter = 0.5f;
            public const float SaturationWidth = 0.5f;
            public const float SaturationSoftness = 0.1f;
            public const float LuminanceCenter = 0.5f;
            public const float LuminanceWidth = 0.5f;
            public const float LuminanceSoftness = 0.1f;
        }

        // 5. Виньетка применяется к уже преобразованному сигналу дисплея.
        public static class Vignette
        {
            public const float Intensity = 0.4f;
            public const float Smoothness = 0.6f;

            public static Color Color => new(0f, 0f, 0f, 1f);

            public static Vector2 Center => new(0.5f, 0.5f);
        }

        // 6. Eigengrau добавляется после виньетки в DisplayFinal.
        public static class FilmGrain
        {
            public const float Intensity = 1f;
            public const float DarknessThreshold = 0.22f;
            public const float NoiseScale = 0.75f;
            public const float EigengrauNoiseAmplitude = 0.35f;
            public static Color Color => new(0.0863f, 0.0863f, 0.1137f, 1f);
        }

    }
}
