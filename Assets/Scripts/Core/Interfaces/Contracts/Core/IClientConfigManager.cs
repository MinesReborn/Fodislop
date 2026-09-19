#nullable enable

using System;
using Kern.Core;
using Kern.Rendering;

namespace Kern.Core.Interfaces;
public interface IClientConfigManager
{
    ClientConfig Config { get; }
    string ConfigFilePath { get; }
    GraphicsPreset SelectedGraphicsPreset { get; }
    void MarkGraphicsAsCustom();
    void SelectGraphicsPreset(GraphicsPreset preset);
    void SetCustomGraphicsSettings(GraphicsQualitySettings settings);

    /// <example>
    /// <c>UpdateSection(config =&gt; config.Audio, audio =&gt; audio.MasterVolume = value);</c>
    /// </example>
    void UpdateSection<TSection>(Func<ClientConfig, TSection> select, Action<TSection> update)
        where TSection : class, new();

    void UpdatePostProcessAndSave(Action<ClientConfig> update);
    void UpdateAndSave(Action<ClientConfig> update);
    void Load();

    void Save();

    void SaveDeferred();

    void EnsureInitialized();
}
