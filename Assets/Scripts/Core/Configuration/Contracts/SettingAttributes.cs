#nullable enable

using System;

namespace Kern.Core;

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

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingLabelAttribute(string localizationKey) : Attribute
{
    public string LocalizationKey { get; } = string.IsNullOrWhiteSpace(localizationKey)
        ? throw new ArgumentException("Localization key must not be empty.", nameof(localizationKey))
        : localizationKey;
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingUnboundedAttribute(string reason) : Attribute
{
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason)
        ? throw new ArgumentException("Reason must not be empty.", nameof(reason))
        : reason;
}

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

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingConsumerAttribute(SettingConsumerTarget target, string mechanism) : Attribute
{
    public SettingConsumerTarget Target { get; } = target;

    public string Mechanism { get; } = string.IsNullOrWhiteSpace(mechanism)
        ? throw new ArgumentException("Mechanism must not be empty.", nameof(mechanism))
        : mechanism;
}
