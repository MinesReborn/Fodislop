#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;

namespace Kern.UI.Settings;

// Применение в меню: живых систем графики нет, выбор только сохраняется в конфиг
internal sealed class ConfigSettingsApply : ISettingsApply
{
    private readonly IClientConfigManager _config;

    public ConfigSettingsApply(IClientConfigManager config)
    {
        _config = config;
    }

    public GraphicsPreset SelectedPreset => _config.SelectedGraphicsPreset;

    public void SelectPreset(GraphicsPreset preset)
    {
        _config.SelectGraphicsPreset(preset);
        _config.Save();
    }

    public void UpdateWorldMaterial(Action<ClientConfig> update) => _config.UpdateAndSave(update);

    public void UpdatePostProcess(Action<ClientConfig> update) => _config.UpdatePostProcessAndSave(update);
}
