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
        public string ConfigFilePath => Repository.ConfigPath;
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

        private ClientConfigRepository Repository =>
            _repository ??= new ClientConfigRepository(GetConfigPath());

        private ClientConfigValidator Validator =>
            _validator ??= new ClientConfigValidator(_graphicsQualityProfile);

        private ConfigSaveScheduler SaveScheduler =>
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
            SaveScheduler.TryFlush(Time.unscaledTime);
        }

        private void OnApplicationQuit()
        {
            SaveScheduler.Flush();
        }

        // Свёрнутое приложение система вправе завершить без OnApplicationQuit.
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                SaveScheduler.Flush();
            }
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
            ClientConfigLoader.Result result =
                new ClientConfigLoader(Repository, _graphicsQualityProfile).LoadOrCreate();
            Config = result.Config;
            Debug.Log(
                $"[ClientConfigManager] Config {result.Outcome} (schema {result.SourceSchemaVersion}) " +
                $"at {Repository.ConfigPath}; GraphicsPreset={Config.GraphicsPreset}");
        }

        public void SelectGraphicsPreset(GraphicsPreset preset)
        {
            // Неизвестное значение отвергает сам профиль: ступеней две, и
            // «промежуточной» ступени, куда можно было бы перевести конфиг,
            // больше нет.
            Config.GraphicsPreset = preset;
            Config.GraphicsQualitySettings = _graphicsQualityProfile.Get(preset);
            Debug.Log($"[ClientConfigManager] Selected graphics preset: {preset}");
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

            update(Config);
            Debug.Log("[ClientConfigManager] Updated post-process settings");
            SaveDeferred();
        }

        public void Save()
        {
            Validator.Validate(Config);
            Repository.Save(Config, Repository.BackupPath);
            Debug.Log($"[ClientConfigManager] Saved config directly to {Repository.ConfigPath}");
        }

        public void SaveDeferred()
        {
            // Проверка немедленная, откладывается только диск. Иначе неверное
            // значение всплывало бы исключением на выходе из игры — позже
            // правки, которая его внесла, и без всякой связи с ней.
            Validator.Validate(Config);
            SaveScheduler.Queue();
            Debug.Log("[ClientConfigManager] Queued deferred config save");
        }
    }
}
