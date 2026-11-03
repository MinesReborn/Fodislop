#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;
[Serializable]
public class ClientConfig
{
    // 32: TerrainSettings.EnableReliefRim.
    // 33: removed terrain style selector; schema number reserved for existing configs.
    // 34: две ступени качества (Классика/Стандарт) вместо шести, без Custom.
    // 35: EffectSettings.BloomVariant.
    // 36: перебиндиваемые клавиши InterfaceSettings.Key* (вкладка «Управление»).
    // 37: reserved schema version.
    // 38: DisplaySettings.HDRSwitchPending для подтверждения смены режима дисплея.
    // 39: DisplaySettings.PeakBrightnessFromDisplay — пик с дисплея до ручной настройки.
    // 40: TerrainSettings EnableReliefRim → EnableRim, *Emission* → *Glow*,
    //     PulseSpeedScale → BlinkingSpeedScale.
    // 41: InterfaceSettings.UIScale — схема под плотностный масштаб на телефоне.
    // 42: 41-я поднимала масштаб до плотности экрана (2.5); на таком холсте
    //     макет рассыпался, значение возвращается к рекомендованному.
    // 43: Android fresh installs and schema-42 saves using the default
    // Classic preset start with the radiance lighting preset enabled.
    // 44: на мобильной платформе UIScale — множитель поверх dp; сброс к 1.0.
    // 45: объединение веток схемы: ApiPort и сохранённая геометрия чата.
    // Поля аддитивные; существующие значения сохраняются.
    // 46: TerrainSettings.DepthPixels и DepthDarkening для стенок тоннелей.
    // 47: EffectSettings.FisheyeScale — сохраняемая сила рыбьего глаза.
    // 48: независимый масштаб текста и полные клавиатурные привязки.
    // 49: отдельные тумблеры рыбьего глаза и глубины, изначально выключены.
    // Схемы 31–48 мигрируются штатным загрузчиком с созданием backup.
    // 50: replace the old default slow-movement Ctrl binding.
    // 51: move the default slow-movement binding to LeftAlt.
    // 52: помощь в движении (обход препятствий), изначально включена.
    public const int CurrentSchemaVersion = 52;

    public int SchemaVersion;
    public AudioSettings Audio = new();
    public DisplaySettings Display = new();
    public InterfaceSettings Interface = new();
    public ConnectionSettings Connection = new();
    public TerrainSettings Terrain = new();
    public EffectSettings Effects = new();

    // По умолчанию — «Стандарт» (со светом) на всех платформах; «Классика» — только явный выбор.
    public GraphicsPreset GraphicsPreset = GraphicsPreset.Standard;
    public GraphicsQualitySettings GraphicsQualitySettings;
}
