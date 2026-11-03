#nullable enable

using System;
using System.IO;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using UnityEngine;

using Kern.Core.UserInterface;

namespace Kern.Core.Configuration;

// Путь конфига на старте: чистая установка или обычный запуск.
//
// Старые схемы накладываются на текущие defaults, чтобы отсутствующие поля
// получили актуальные значения, а существующие пользовательские настройки
// сохранились. Известные изменения формата применяются последовательно.
// Сверка стандартных пресетов — текущее поведение, не миграция:
// выполняется при каждой загрузке.
//
// Отделён от ClientConfigManager, чтобы установку, миграцию и сброс можно было
// проверить на временной папке, без MonoBehaviour и persistentDataPath.
internal sealed class ClientConfigLoader
{
    private readonly ClientConfigRepository _repository;
    private readonly ClientConfigValidator _validator;
    private readonly GraphicsQualityProfile _graphicsQualityProfile;
    private readonly bool _isAndroidPlayer;

    public ClientConfigLoader(
        ClientConfigRepository repository,
        GraphicsQualityProfile graphicsQualityProfile,
        bool isAndroidPlayer = false)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _graphicsQualityProfile = graphicsQualityProfile ??
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        _isAndroidPlayer = isAndroidPlayer;
        _validator = new ClientConfigValidator(graphicsQualityProfile);
    }

    public enum Outcome
    {
        CreatedDefaults,
        Loaded,
        Migrated,
        ResetToDefaults,
    }

    public readonly record struct Result(ClientConfig Config, Outcome Outcome, int SourceSchemaVersion);

    // Файл, который нельзя прочитать или провалидировать, не переписывается:
    // исключение уходит наверх, диск не тронут.
    public Result LoadOrCreate()
    {
        if (!_repository.Exists)
        {
            ClientConfig defaults = ClientConfigDefaults.Create(_graphicsQualityProfile);
            _validator.Validate(defaults);
            _repository.Save(defaults, _repository.BackupPath);
            return new Result(defaults, Outcome.CreatedDefaults, ClientConfig.CurrentSchemaVersion);
        }

        ClientConfigRepository.LoadedConfig loaded = _repository.Load();
        int sourceSchemaVersion = loaded.Config.SchemaVersion;
        if (sourceSchemaVersion < 1)
        {
            throw new InvalidDataException(
                $"Client config schema {sourceSchemaVersion} is invalid; refusing to overwrite it.");
        }

        if (sourceSchemaVersion > ClientConfig.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Client config schema {sourceSchemaVersion} is newer than supported " +
                $"schema {ClientConfig.CurrentSchemaVersion}; refusing to overwrite it.");
        }

        if (sourceSchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            ClientConfig migrated = ClientConfigDefaults.Create(_graphicsQualityProfile);
            JsonUtility.FromJsonOverwrite(loaded.Json, migrated);
            ClientConfigMigrations.Apply(
                migrated, loaded.Json, sourceSchemaVersion,
                _isAndroidPlayer && migrated.GraphicsPreset == GraphicsPreset.Classic,
                preset => migrated.GraphicsPreset = preset);
            if (!Enum.IsDefined(typeof(GraphicsPreset), migrated.GraphicsPreset))
            {
                // Older releases persisted the retired multi-step preset enum.
                // Normalize values that no longer exist before rebuilding the profile.
                migrated.GraphicsPreset = GraphicsPreset.Standard;
            }

            ReconcileStandardPreset(migrated);
            _validator.Validate(migrated);
            _repository.Save(migrated, _repository.BackupPath);
            return new Result(migrated, Outcome.Migrated, sourceSchemaVersion);
        }

        GraphicsPreset presetBefore = loaded.Config.GraphicsPreset;
        GraphicsQualitySettings qualityBefore = loaded.Config.GraphicsQualitySettings;

        ReconcileStandardPreset(loaded.Config);
        _validator.Validate(loaded.Config);
        if (loaded.Config.GraphicsPreset != presetBefore ||
            loaded.Config.GraphicsQualitySettings != qualityBefore)
        {
            _repository.Save(loaded.Config, _repository.BackupPath);
        }

        return new Result(loaded.Config, Outcome.Loaded, sourceSchemaVersion);
    }

    private void ReconcileStandardPreset(ClientConfig config)
    {
        // Ступени неизменяемы, поэтому снимок настроек всегда авторский: правка
        // конфига руками не переживает загрузку. Раньше здесь была ветка
        // «настройки разошлись — перевести в Custom», но Custom больше нет.
        config.GraphicsQualitySettings = _graphicsQualityProfile.Get(config.GraphicsPreset);
    }
}
