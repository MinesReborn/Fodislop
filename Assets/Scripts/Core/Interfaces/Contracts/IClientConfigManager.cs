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

        /// <summary>
        /// Правит одну секцию конфига и сохраняет.
        /// </summary>
        /// <example>
        /// <c>UpdateSection(config =&gt; config.Audio, audio =&gt; audio.MasterVolume = value);</c>
        /// </example>
        void UpdateSection<TSection>(Func<ClientConfig, TSection> select, Action<TSection> update)
            where TSection : class, new();

        void UpdatePostProcessAndSave(Action<ClientConfig> update);
        void UpdateAndSave(Action<ClientConfig> update);
        void Load();

        /// <summary>Пишет конфиг немедленно.</summary>
        void Save();

        /// <summary>
        /// Откладывает запись на окно дебаунса. Путь для ползунков: полная
        /// валидация и запись файла с fsync на каждый кадр перетаскивания —
        /// это десятки записей в секунду.
        /// </summary>
        void SaveDeferred();

        /// <summary>
        /// Загружает конфиг синхронно, если он ещё не загружен.
        /// Безопасно вызывать сразу после Resolve, до того как отработал Start.
        /// </summary>
        void EnsureInitialized();
    }
}
