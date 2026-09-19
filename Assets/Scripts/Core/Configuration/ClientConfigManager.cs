#nullable enable

using System;
using System.IO;
using Kern.Core.Interfaces;
using Kern.Rendering;
using UnityEngine;
using VContainer;

namespace Kern.Core
{
    [DefaultExecutionOrder(-9000)]
    public class ClientConfigManager : MonoBehaviour, IClientConfigManager
    {
        private const string ConfigFileName = "client_config.json";
        private const string ConfigDirectory = "Config";

        public ClientConfig Config { get; private set; } = null!;
        public string ConfigFilePath => _Repository.ConfigPath;
        public GraphicsPreset SelectedGraphicsPreset => Config.GraphicsPreset;

        private bool _initialized;
        private ConfigSaveScheduler? _saveScheduler;
        private ClientConfigRepository? _repository;
        private ClientConfigValidator? _validator;

        [Inject]
        private GraphicsQualityProfile _graphicsQualityProfile = null!;

        private string GetConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, ConfigDirectory, ConfigFileName);
        }

        private ClientConfigRepository _Repository =>
            _repository ??= new ClientConfigRepository(GetConfigPath());

        private ClientConfigValidator _Validator =>
            _validator ??= new ClientConfigValidator(_graphicsQualityProfile);

        private ConfigSaveScheduler _SaveScheduler =>
            _saveScheduler ??= new ConfigSaveScheduler(this);

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (_graphicsQualityProfile == null)
            {
                throw new InvalidOperationException(
                    "[ClientConfigManager] GraphicsQualityProfile must be injected before loading client config.");
            }

            Load();
            _initialized = true;
        }

        private void Start()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            _SaveScheduler.TryFlush(Time.unscaledTime);
        }

        private void OnApplicationQuit()
        {
            _SaveScheduler.Flush();
        }

        // Свёрнутое приложение система вправе завершить без OnApplicationQuit.
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _SaveScheduler.Flush();
            }
        }

        private void OnDisable()
        {
            // Выход из Play Mode в редакторе OnApplicationQuit не вызывает.
            // Без этого правка, сделанная в последнюю четверть секунды, не
            // доехала бы до диска.
            _SaveScheduler.Flush();
        }

        public void Load()
        {
            ClientConfigLoader.Result result =
                new ClientConfigLoader(_Repository, _graphicsQualityProfile).LoadOrCreate();
            Config = result.Config;
            Debug.Log(
                $"[ClientConfigManager] Config {result.Outcome} (schema {result.SourceSchemaVersion}) " +
                $"at {_Repository.ConfigPath}; GraphicsPreset={Config.GraphicsPreset}");
        }

        public void MarkGraphicsAsCustom()
        {
            if (Config.GraphicsPreset == GraphicsPreset.Custom)
            {
                return;
            }

            if (!GraphicsQualityProfile.IsStandard(Config.GraphicsPreset))
            {
                throw new InvalidOperationException(
                    $"Cannot promote unknown graphics preset '{Config.GraphicsPreset}' to Custom.");
            }

            Config.GraphicsQualitySettings = _graphicsQualityProfile.Get(Config.GraphicsPreset);
            Config.GraphicsPreset = GraphicsPreset.Custom;
            Debug.Log("[ClientConfigManager] Marked graphics preset as Custom");
        }

        public void SelectGraphicsPreset(GraphicsPreset preset)
        {
            if (!GraphicsQualityProfile.IsStandard(preset))
            {
                throw new ArgumentException(
                    "Only one of the six immutable standard presets can be selected directly.",
                    nameof(preset));
            }

            Config.GraphicsPreset = preset;
            Config.GraphicsQualitySettings = _graphicsQualityProfile.Get(preset);

            // Стандартный пресет обязан совпадать с авторскими значениями во
            // всех секциях вида — этого требует инвариант валидатора. Раньше
            // здесь было два вызова, копировавших сорок полей из снимка;
            // теперь авторское значение и есть новый экземпляр секции.
            Config.Terrain = new TerrainSettings();
            Config.Effects = new EffectSettings();
            Config.PostProcess = new PostProcessSettings();
            Debug.Log($"[ClientConfigManager] Selected graphics preset: {preset}");
        }

        public void SetCustomGraphicsSettings(GraphicsQualitySettings settings)
        {
            MarkGraphicsAsCustom();
            GraphicsQualityProfile.ValidateSettings(settings, "Custom");
            Config.GraphicsQualitySettings = settings;
            Debug.Log($"[ClientConfigManager] Set custom graphics settings (Lighting={settings.LightingQuality}, AA={settings.AntiAliasing}, RenderScale={settings.RenderScale})");
        }

        public void UpdateSection<TSection>(
            Func<ClientConfig, TSection> select,
            Action<TSection> update)
            where TSection : class, new()
        {
            if (select == null)
            {
                throw new ArgumentNullException(nameof(select));
            }

            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            update(select(Config));
            Debug.Log($"[ClientConfigManager] Updated section {typeof(TSection).Name}");
            SaveDeferred();
        }

        public void UpdateAndSave(Action<ClientConfig> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            update(Config);
            Debug.Log("[ClientConfigManager] Updated config");
            SaveDeferred();
        }

        public void UpdatePostProcessAndSave(Action<ClientConfig> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            MarkGraphicsAsCustom();
            update(Config);
            Debug.Log("[ClientConfigManager] Updated post-process settings");
            SaveDeferred();
        }

        public void Save()
        {
            _Validator.Validate(Config);
            _Repository.Save(Config);
            Debug.Log($"[ClientConfigManager] Saved config directly to {_Repository.ConfigPath}");
        }

        public void SaveDeferred()
        {
            // Проверка немедленная, откладывается только диск. Иначе неверное
            // значение всплывало бы исключением на выходе из игры — позже
            // правки, которая его внесла, и без всякой связи с ней.
            _Validator.Validate(Config);
            _SaveScheduler.Queue();
            Debug.Log("[ClientConfigManager] Queued deferred config save");
        }
    }
}
