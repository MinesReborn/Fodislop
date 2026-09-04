#nullable enable

using System;

namespace Fodinae.Core;

/// <summary>
/// Допустимый диапазон значения настройки.
/// </summary>
/// <remarks>
/// ЗАЧЕМ. Раньше диапазон одной настройки был записан литералами в четырёх
/// независимых местах: в <c>Validate</c> у ProjectDefaults, в
/// <c>ClientConfigValidator</c>, в <c>LightingRuntimeConfig.Validate</c> и в
/// билдере ползунка. Ничто не заставляло их совпадать, и они расходились
/// молча: конфиг мог пройти валидацию значением, которое ползунок показать не
/// в состоянии.
///
/// Теперь диапазон объявлен один раз — здесь, над самим полем. Валидация,
/// клампинг и границы ползунка читают его отсюда.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingRangeAttribute : Attribute
{
    public SettingRangeAttribute(float minimum, float maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentException(
                $"Setting range is inverted: [{minimum}, {maximum}].",
                nameof(minimum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public float Minimum { get; }

    public float Maximum { get; }
}

/// <summary>
/// Ключ локализации подписи настройки.
/// </summary>
/// <remarks>
/// Нужен там, где настройка показывается игроку. Отсутствие атрибута означает,
/// что настройка в меню не выводится, — это законно (например,
/// <c>TerrainDebugMode</c>), поэтому линтер его не требует.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingLabelAttribute(string localizationKey) : Attribute
{
    public string LocalizationKey { get; } = string.IsNullOrWhiteSpace(localizationKey)
        ? throw new ArgumentException("Localization key must not be empty.", nameof(localizationKey))
        : localizationKey;
}

/// <summary>
/// Поле не имеет диапазона по существу, и это решение, а не упущение.
/// </summary>
/// <remarks>
/// Диапазона не бывает у <c>bool</c>, у строки, у перечисления и у величины,
/// смысл которой не в отрезке (строковый идентификатор сервера, признак
/// отладки). Линтер требует над каждым публичным полем секции либо
/// <see cref="SettingRangeAttribute"/>, либо этот атрибут с причиной —
/// чтобы «забыл диапазон» и «диапазона нет» нельзя было перепутать.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingUnboundedAttribute(string reason) : Attribute
{
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason)
        ? throw new ArgumentException("Reason must not be empty.", nameof(reason))
        : reason;
}

/// <summary>
/// Подсистема-получатель настройки.
/// </summary>
public enum SettingConsumerTarget
{
    LightingEngine,
    PostProcessController,
    TerrainRenderer,
    SurfaceRenderer,
    DisplayManager,
    AudioSystem,
    LocalizationService,
    NetworkService,
    UserInterface,
    Gameplay,
}

/// <summary>
/// Декларирует подсистему-владельца настройки и механизм её применения.
/// </summary>
/// <remarks>
/// Без этого атрибута настройка считается брошенной: линтер и SettingsProbe
/// требуют, чтобы каждое поле конфига имело зарегистрированного потребителя.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingConsumerAttribute(SettingConsumerTarget target, string mechanism) : Attribute
{
    public SettingConsumerTarget Target { get; } = target;

    public string Mechanism { get; } = string.IsNullOrWhiteSpace(mechanism)
        ? throw new ArgumentException("Mechanism must not be empty.", nameof(mechanism))
        : mechanism;
}
