#nullable enable

using System.IO;
using Fodinae.Core;
using Fodinae.Rendering;
using NUnit.Framework;
using UnityEngine;
using AudioSettings = Fodinae.Core.AudioSettings;

namespace Fodinae.Tests.Core;

public sealed class ClientConfigMigrationTests
{
    private GraphicsQualityProfile _profile = null!;

    [SetUp]
    public void SetUp()
    {
        _profile = ScriptableObject.CreateInstance<GraphicsQualityProfile>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_profile);
    }

    /// <summary>
    /// Файл схемы 21 хранит поля вида плоско в корне. После миграции они
    /// обязаны оказаться в секциях с теми же значениями: настроенный игроком
    /// свет — это его работа, и терять её при смене формы файла нельзя.
    /// </summary>
    [Test]
    public void Migrate_FlatV21Config_MovesPlayerValuesIntoSections()
    {
        var migration = new ClientConfigMigration(_profile);
        string json = @"{
            ""SchemaVersion"": 21,
            ""GraphicsPreset"": 6,
            ""AmbientIntensity"": 0.42,
            ""EmissionScale"": 3.5,
            ""TerrainShimmerSpeedScale"": 0.25,
            ""BloomEnabled"": false,
            ""MotionBlurEnabled"": true
        }";
        ClientConfig config = JsonUtility.FromJson<ClientConfig>(json);

        bool migrated = migration.Migrate(config, json);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(config.Lighting.AmbientIntensity, Is.EqualTo(0.42f).Within(1e-5f));
        Assert.That(config.Lighting.EmissionScale, Is.EqualTo(3.5f).Within(1e-5f));
        Assert.That(config.Terrain.ShimmerSpeedScale, Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(config.Effects.BloomEnabled, Is.False);
        Assert.That(config.Effects.MotionBlurEnabled, Is.True);
    }

    /// <summary>
    /// Файл старее девятнадцатой схемы плоского хвоста с осмысленными
    /// величинами уже не содержит: их удалили вместе с тридцатью пятью
    /// ползунками постпроцесса. Секции получают авторские значения.
    /// </summary>
    [Test]
    public void Migrate_PreV19Config_RestoresAuthoredSections()
    {
        var migration = new ClientConfigMigration(_profile);
        string json = @"{ ""SchemaVersion"": 14, ""GraphicsPreset"": 6 }";
        ClientConfig config = JsonUtility.FromJson<ClientConfig>(json);

        bool migrated = migration.Migrate(config, json);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(SettingSchema.MatchesDefaults(config.Lighting), Is.True);
        Assert.That(SettingSchema.MatchesDefaults(config.Terrain), Is.True);
        Assert.That(SettingSchema.MatchesDefaults(config.Effects), Is.True);
        Assert.That(SettingSchema.MatchesDefaults(config.PostProcess), Is.True);
    }

    /// <summary>
    /// Значение вне нынешних границ загоняется в диапазон при переносе, а не
    /// роняет валидатор сразу после миграции.
    /// </summary>
    [Test]
    public void Migrate_FlatValueOutsideRange_IsClampedOnTransfer()
    {
        var migration = new ClientConfigMigration(_profile);
        string json = @"{
            ""SchemaVersion"": 21,
            ""GraphicsPreset"": 6,
            ""AmbientIntensity"": 9.0
        }";
        ClientConfig config = JsonUtility.FromJson<ClientConfig>(json);

        migration.Migrate(config, json);

        Assert.That(config.Lighting.AmbientIntensity, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void Migrate_V22DegradedGamma_ResetsToDefault()
    {
        var migration = new ClientConfigMigration(_profile);
        string json = @"{
            ""SchemaVersion"": 22,
            ""GraphicsPreset"": 6,
            ""Display"": { ""Gamma"": 1.8 }
        }";
        ClientConfig config = JsonUtility.FromJson<ClientConfig>(json);

        bool migrated = migration.Migrate(config, json);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(config.Display.Gamma, Is.EqualTo(DisplaySettings.DefaultGamma).Within(1e-5f));
    }

    [Test]
    public void Migrate_V24Config_ReachesCurrentSchema()
    {
        var migration = new ClientConfigMigration(_profile);
        string json = @"{
            ""SchemaVersion"": 24,
            ""GraphicsPreset"": 6,
            ""Effects"": { ""BloomEnabled"": false }
        }";
        ClientConfig config = JsonUtility.FromJson<ClientConfig>(json);

        bool migrated = migration.Migrate(config, json);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(config.Effects.BloomEnabled, Is.False);
    }

    [Test]
    public void Migrate_CurrentCustomConfig_IsIdempotent()
    {
        var migration = new ClientConfigMigration(_profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            GraphicsPreset = GraphicsPreset.Custom,
        };

        bool migrated = migration.Migrate(config, "{}");

        Assert.That(migrated, Is.False);
    }

    [Test]
    public void Validator_RejectsNonFiniteRuntimeSetting()
    {
        var validator = new ClientConfigValidator(_profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            GraphicsPreset = GraphicsPreset.Custom,
            Interface = new InterfaceSettings
            {
                UIScale = float.NaN,
            },
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => validator.Validate(config))!;

        Assert.That(exception.Message, Does.Contain(nameof(config.Interface.UIScale)));
    }

    [Test]
    public void Validator_RejectsUnsupportedLanguage()
    {
        var validator = new ClientConfigValidator(_profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            GraphicsPreset = GraphicsPreset.Custom,
            Audio = new AudioSettings(),
            Interface = new InterfaceSettings
            {
                Language = "unsupported",
            },
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => validator.Validate(config))!;

        Assert.That(exception.Message, Does.Contain(nameof(config.Interface.Language)));
    }

    /// <summary>
    /// Инвариант «стандартный пресет не тронут» раньше был цепочкой из сорока
    /// сравнений, которую забывали дополнять. Проверка, что он действует и
    /// теперь, когда список полей берётся из объявления секции.
    /// </summary>
    [Test]
    public void Validator_RejectsCustomizedVisualsUnderStandardPreset()
    {
        var validator = new ClientConfigValidator(_profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            GraphicsPreset = GraphicsPreset.High,
        };
        config.Lighting.AmbientIntensity = 0.5f;

        Assert.Throws<InvalidDataException>(() => validator.Validate(config));
    }
}
