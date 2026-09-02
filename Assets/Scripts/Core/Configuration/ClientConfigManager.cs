#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using UnityEngine;
using VContainer;

namespace Fodinae.Core
{
    /// <summary>
    /// Клиентский локальный конфиг: survives перезапусков, живёт в Application.persistentDataPath.
    /// Initial values приходят только из injected ProjectDefaults. Повреждённый
    /// persisted config не исправляется тихо и останавливает startup.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public class ClientConfigManager : MonoBehaviour, IClientConfigManager
    {
        private const string ConfigFileName = "client_config.json";
        private const string ConfigDirectory = "Config";

        public ClientConfig Config { get; private set; } = null!;
        public string ConfigFilePath => Repository.ConfigPath;
        public GraphicsPreset SelectedGraphicsPreset => Config.GraphicsPreset;
        private bool _initialized;
        private ClientConfigRepository? _repository;
        private ClientConfigMigration? _migration;
        private ClientConfigValidator? _validator;

        [Inject]
        private IProjectDefaults _projectDefaults = null!;
        [Inject]
        private GraphicsQualityProfile _graphicsQualityProfile = null!;

        private string GetConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, ConfigDirectory, ConfigFileName);
        }

        private ClientConfigRepository Repository =>
            _repository ??= new ClientConfigRepository(GetConfigPath());

        private ClientConfigMigration Migration =>
            _migration ??= new ClientConfigMigration(_projectDefaults, _graphicsQualityProfile);

        private ClientConfigValidator Validator =>
            _validator ??= new ClientConfigValidator(_projectDefaults, _graphicsQualityProfile);

        private void Awake()
        {
        }

        private void Start()
        {
            if (DependenciesReady)
            {
                TryInitialize();
            }
        }

        private void Update()
        {
            if (!_initialized && DependenciesReady)
            {
                TryInitialize();
            }
        }

        private bool DependenciesReady =>
            _projectDefaults != null &&
            _graphicsQualityProfile != null;

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_projectDefaults == null)
            {
                throw new InvalidOperationException(
                    "[ClientConfigManager] ProjectDefaults must be injected before loading client config.");
            }

            Load();
            _initialized = true;
        }

        /// <summary>
        /// Forces config load synchronously, without waiting for the next
        /// Start/Update cycle. This manager is an authored Bootstrap-tier
        /// singleton authored under BootstrapLifetimeScope:
        /// its Start() runs a frame later — too late for GameStartupPipeline,
        /// which reads Config in the same frame the manager is created.
        /// EnsureInitialized is called at Bootstrap startup (BootstrapLifetimeScope.Awake)
        /// before any game scope is built.
        /// </summary>
        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            TryInitialize();
        }

        public void Load()
        {
            ClientConfigRepository repository = Repository;
            if (!repository.Exists)
            {
                ApplyDefaults();
                Save();
                return;
            }

            ClientConfig loaded = repository.Load();
            int sourceSchemaVersion = loaded.SchemaVersion;
            bool migrated = Migration.Migrate(loaded);
            Validator.Validate(loaded);
            Config = loaded;
            if (migrated)
            {
                repository.Save(
                    Config,
                    GetMigrationBackupPath(repository.ConfigPath, sourceSchemaVersion));
            }

            Debug.Log(
                $"[ClientConfigManager] Config loaded and validated from {repository.ConfigPath}; " +
                $"GraphicsPreset={Config.GraphicsPreset}; rendering pipeline is always enabled");
        }

        public void ApplyDefaults()
        {
            Config = ClientConfigDefaults.Create(_projectDefaults, _graphicsQualityProfile);
            Debug.Log("[ClientConfigManager] Applied explicit ProjectDefaults config values.");
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
            ClientConfigDefaults.ApplyLightingDefaults(Config, _projectDefaults.Lighting);
            ClientConfigDefaults.ApplyShaderDefaults(Config, _projectDefaults.Shaders);
        }

        public void SetCustomGraphicsSettings(GraphicsQualitySettings settings)
        {
            MarkGraphicsAsCustom();
            GraphicsQualityProfile.ValidateSettings(settings, "Custom");
            Config.GraphicsQualitySettings = settings;
        }

        public void UpdateAndSave(Action<ClientConfig> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            update(Config);
            Save();
        }

        public void UpdateAudio(Action<AudioSettings> update)
        {
            UpdateSection(Config.Audio, update);
        }

        public void UpdateDisplay(Action<DisplaySettings> update)
        {
            UpdateSection(Config.Display, update);
        }

        public void UpdateInterface(Action<InterfaceSettings> update)
        {
            UpdateSection(Config.Interface, update);
        }

        public void UpdateAccessibility(Action<AccessibilitySettings> update)
        {
            UpdateSection(Config.Accessibility, update);
        }

        public void UpdateConnection(Action<ConnectionSettings> update)
        {
            UpdateSection(Config.Connection, update);
        }

        private void UpdateSection<TSettings>(TSettings settings, Action<TSettings> update)
            where TSettings : class
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            update(settings);
            Save();
        }

        public void UpdatePostProcessAndSave(Action<ClientConfig> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            MarkGraphicsAsCustom();
            update(Config);
            PromotePostProcessQualityForEnabledEffects(Config);
            Save();
        }

        private static void PromotePostProcessQualityForEnabledEffects(ClientConfig config)
        {
            // Пирамида блума нужна не только самому блуму: грязь на линзе,
            // анаморфные лучи и дифракция берут из неё яркий проход.
            bool requiresFull = config.BloomEnabled ||
                config.MotionBlurEnabled ||
                config.LensEffectsEnabled;

            // Ветки «поднять до Essential» больше нет: тира ниже Essential не
            // существует, поэтому поднимать неоткуда. Остаётся только подъём до
            // Full, когда включён эффект, которому нужна пирамида блума.
            GraphicsQualitySettings quality = config.GraphicsQualitySettings;
            if (requiresFull)
            {
                quality.PostProcessQuality = PostProcessQualityMode.Full;
            }

            config.GraphicsQualitySettings = quality;
        }

        public void Save()
        {
            Validator.Validate(Config);
            Repository.Save(Config);
        }

        private static string GetMigrationBackupPath(string configPath, int sourceSchemaVersion)
        {
            return $"{configPath}.v{sourceSchemaVersion}.backup";
        }

    }
}
