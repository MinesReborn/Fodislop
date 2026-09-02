#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Rendering;

namespace Fodinae.Core.Interfaces
{
    public interface IClientConfigManager
    {
        ClientConfig Config { get; }
        string ConfigFilePath { get; }
        GraphicsPreset SelectedGraphicsPreset { get; }
        void MarkGraphicsAsCustom();
        void SelectGraphicsPreset(GraphicsPreset preset);
        void SetCustomGraphicsSettings(GraphicsQualitySettings settings);
        void UpdateAudio(Action<AudioSettings> update);
        void UpdateDisplay(Action<DisplaySettings> update);
        void UpdateInterface(Action<InterfaceSettings> update);
        void UpdateAccessibility(Action<AccessibilitySettings> update);
        void UpdateConnection(Action<ConnectionSettings> update);
        void UpdatePostProcessAndSave(Action<ClientConfig> update);
        void UpdateAndSave(Action<ClientConfig> update);
        void Load();
        void Save();

        /// <summary>
        /// Forces the config to load synchronously if it has not already.
        /// Safe to call immediately after Resolve, before Start() would have run.
        /// </summary>
        void EnsureInitialized();
    }
}
