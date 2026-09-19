#nullable enable

using System;
using Kern.Rendering;

namespace Kern.Core;

// Путь конфига на старте: чистая установка или обычный запуск.
//
// Легаси запрещено: никаких пошаговых миграций старых схем, никаких
// LegacySchema-классов, никаких бэкапов версий. Файл чужой версии —
// это чужой файл: сбрасывается на дефолты и перезаписывается.
// Сверка стандартных пресетов — текущее поведение, не миграция:
// выполняется при каждой загрузке.
//
// Отделён от ClientConfigManager, чтобы установку и сброс можно было
// проверить на временной папке, без MonoBehaviour и persistentDataPath.
internal sealed class ClientConfigLoader
{
    private readonly ClientConfigRepository _repository;
    private readonly ClientConfigValidator _validator;
    private readonly GraphicsQualityProfile _graphicsQualityProfile;

    public ClientConfigLoader(ClientConfigRepository repository, GraphicsQualityProfile graphicsQualityProfile)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _graphicsQualityProfile = graphicsQualityProfile ??
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        _validator = new ClientConfigValidator(graphicsQualityProfile);
    }

    public enum Outcome
    {
        CreatedDefaults,
        Loaded,
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
            _repository.Save(defaults);
            return new Result(defaults, Outcome.CreatedDefaults, ClientConfig.CurrentSchemaVersion);
        }

        ClientConfigRepository.LoadedConfig loaded = _repository.Load();
        int sourceSchemaVersion = loaded.Config.SchemaVersion;
        if (sourceSchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            ClientConfig defaults = ClientConfigDefaults.Create(_graphicsQualityProfile);
            _validator.Validate(defaults);
            _repository.Save(defaults);
            return new Result(defaults, Outcome.ResetToDefaults, sourceSchemaVersion);
        }

        GraphicsPreset presetBefore = loaded.Config.GraphicsPreset;
        GraphicsQualitySettings qualityBefore = loaded.Config.GraphicsQualitySettings;

        ReconcileStandardPreset(loaded.Config);
        _validator.Validate(loaded.Config);
        if (loaded.Config.GraphicsPreset != presetBefore ||
            loaded.Config.GraphicsQualitySettings != qualityBefore)
        {
            _repository.Save(loaded.Config);
        }

        return new Result(loaded.Config, Outcome.Loaded, sourceSchemaVersion);
    }

    private void ReconcileStandardPreset(ClientConfig config)
    {
        if (!GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            return;
        }

        GraphicsQualitySettings standardSettings =
            _graphicsQualityProfile.Get(config.GraphicsPreset);
        if (config.GraphicsQualitySettings != standardSettings)
        {
            config.GraphicsQualitySettings = standardSettings;
        }

        if (!SettingSchema.MatchesDefaults(config.Terrain) ||
            !SettingSchema.MatchesDefaults(config.Effects) ||
            !SettingSchema.MatchesDefaults(config.PostProcess))
        {
            config.GraphicsPreset = GraphicsPreset.Custom;
        }
    }
}
