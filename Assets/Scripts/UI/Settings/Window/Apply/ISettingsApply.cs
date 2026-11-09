#nullable enable

using System;
using Kern.Core;
using Kern.Rendering;

namespace Kern.UI.Settings;

// Как окно настроек применяет графику: в игре — сразу на живые системы (свет, террейн, постобработка),
// в меню этих систем нет — только сохранение в конфиг, игра применит его при входе в мир.
internal interface ISettingsApply
{
    GraphicsPreset SelectedPreset { get; }

    void SelectPreset(GraphicsPreset preset);

    void UpdateWorldMaterial(Action<ClientConfig> update);

    void UpdatePostProcess(Action<ClientConfig> update);
}
