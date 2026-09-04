#nullable enable

using System;
using System.IO;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using UnityEngine;
using VContainer;

namespace Fodinae.Core
{
    /// <summary>
    /// Клиентский локальный конфиг: переживает перезапуск, живёт в
    /// Application.persistentDataPath. Повреждённый файл не исправляется тихо и
    /// останавливает startup.
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
        private ConfigSaveScheduler? _saveScheduler;
        private ClientConfigRepository? _repository;
        private ClientConfigMigration? _migration;
        private ClientConfigValidator? _validator;

        [Inject]
        private GraphicsQualityProfile _graphicsQualityProfile = null!;

        private string GetConfigPath()
        {
            return Path.Combine(Application.persistentDataPath, ConfigDirectory, ConfigFileName);
        }

        private ClientConfigRepository Repository =>
            _repository ??= new ClientConfigRepository(GetConfigPath());

        private ClientConfigMigration Migration =>
            _migration ??= new ClientConfigMigration(_graphicsQualityProfile);

        private ClientConfigValidator Validator =>
            _validator ??= new ClientConfigValidator(_graphicsQualityProfile);

        private ConfigSaveScheduler SaveScheduler =>
            _saveScheduler ??= new ConfigSaveScheduler(this);

        /// <summary>
        /// Загружает конфиг синхронно, не дожидаясь Start.
        /// </summary>
        /// <remarks>
        /// Вызывается из <c>BootstrapLifetimeScope.Awake</c> до сборки игровых
        /// скоупов: <c>GameStartupPipeline</c> читает Config в том же кадре, в
        /// котором менеджер создан, а Start у него наступит только на
        /// следующем.
        ///
        /// Раньше вдобавок к этому в <c>Update</c> висел опрос «зависимости уже
        /// приехали?» — костыль поверх уже существующего явного вызова, из-за
        /// которого момент загрузки конфига зависел от порядка кадров. Опроса
        /// нет: если зависимость не внедрена к моменту вызова, это ошибка
        /// сборки контейнера, и она обязана быть видна, а не разойтись через
        /// кадр.
        /// </remarks>
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
            SaveScheduler.TryFlush(Time.unscaledTime);
        }

        private void OnApplicationQuit()
        {
            SaveScheduler.Flush();
        }

        private void OnDisable()
        {
            // Выход из Play Mode в редакторе OnApplicationQuit не вызывает.
            // Без этого правка, сделанная в последнюю четверть секунды, не
            // доехала бы до диска.
            SaveScheduler.Flush();
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

            ClientConfigRepository.LoadedConfig loaded = repository.Load();
            int sourceSchemaVersion = loaded.Config.SchemaVersion;
            bool migrated = Migration.Migrate(loaded.Config, loaded.Json);
            Validator.Validate(loaded.Config);
            Config = loaded.Config;
            if (migrated)
            {
                repository.Save(
                    Config,
                    GetMigrationBackupPath(repository.ConfigPath, sourceSchemaVersion));
            }

            Debug.Log(
                $"[ClientConfigManager] Config loaded and validated from {repository.ConfigPath}; " +
                $"GraphicsPreset={Config.GraphicsPreset}");
        }

        public void ApplyDefaults()
        {
            Config = ClientConfigDefaults.Create(_graphicsQualityProfile);
            Debug.Log("[ClientConfigManager] Applied authored default config values.");
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
            Config.Lighting = new WorldLightingSettings();
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

        /// <summary>
        /// Правит одну секцию и сохраняет.
        /// </summary>
        /// <remarks>
        /// Раньше на каждую секцию была своя обёртка — <c>UpdateAudio</c>,
        /// <c>UpdateDisplay</c>, <c>UpdateInterface</c>,
        /// <c>UpdateAccessibility</c>, <c>UpdateConnection</c>, — пять
        /// одинаковых методов, отличавшихся только именем поля, и каждая новая
        /// секция требовала шестой.
        /// </remarks>
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

        /// <summary>
        /// Пишет конфиг немедленно: валидация плюс запись файла.
        /// </summary>
        public void Save()
        {
            Validator.Validate(Config);
            Repository.Save(Config);
            Debug.Log($"[ClientConfigManager] Saved config directly to {Repository.ConfigPath}");
        }

        /// <summary>
        /// Откладывает запись на окно дебаунса.
        /// </summary>
        /// <remarks>
        /// ЗАЧЕМ. <see cref="Save"/> валидирует конфиг целиком и пишет файл с
        /// fsync и подменой через временный. Раньше каждое движение ползунка
        /// вызывало его напрямую — то есть на 120-герцовом экране до ста
        /// двадцати полных записей в секунду за одно перетаскивание.
        ///
        /// Дебаунс был, но только у света и жил внутри его холдера, а тик к
        /// нему приходилось пробрасывать через LightingEngine.Update. Теперь он
        /// один на весь конфиг и крутится там, где и должен — у владельца
        /// файла. Запись гарантирована выходом из игры и из Play Mode.
        ///
        /// Откладывается именно запись: валидация выполняется сразу, чтобы
        /// ошибка называлась в момент правки, а не при выходе.
        /// </remarks>
        public void SaveDeferred()
        {
            // Проверка немедленная, откладывается только диск. Иначе неверное
            // значение всплывало бы исключением на выходе из игры — позже
            // правки, которая его внесла, и без всякой связи с ней.
            Validator.Validate(Config);
            SaveScheduler.Queue();
            Debug.Log("[ClientConfigManager] Queued deferred config save");
        }

        private static string GetMigrationBackupPath(string configPath, int sourceSchemaVersion)
        {
            return $"{configPath}.v{sourceSchemaVersion}.backup";
        }
    }
}
