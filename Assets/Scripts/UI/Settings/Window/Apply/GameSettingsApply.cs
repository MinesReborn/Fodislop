#nullable enable

using System;
using Kern.Core;
using Kern.Rendering;
using Kern.Rendering.Display;

namespace Kern.UI.Settings;

// Применение в игре: через GraphicsSettingsController на живые системы
internal sealed class GameSettingsApply : ISettingsApply
{
    private readonly GraphicsSettingsController _graphics;

    public GameSettingsApply(GraphicsSettingsController graphics)
    {
        _graphics = graphics;
    }

    public GraphicsPreset SelectedPreset => _graphics.SelectedPreset;

    public void SelectPreset(GraphicsPreset preset) => _graphics.SelectPreset(preset);

    public void UpdateWorldMaterial(Action<ClientConfig> update) => _graphics.UpdateWorldMaterialSettings(update);

    public void UpdatePostProcess(Action<ClientConfig> update) => _graphics.UpdatePostProcessSettings(update);
}
