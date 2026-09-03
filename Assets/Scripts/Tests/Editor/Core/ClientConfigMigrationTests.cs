#nullable enable

using System.IO;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
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

    [Test]
    public void Migrate_V14CustomConfig_SeedsPostProcessDefaults()
    {
        // Ступень 19 удаляет старые подробные параметры эффектов и сеет
        // тумблеры из проекта. Ступень 20 добавляет только компактную
        // калибровку вывода из текущего PostProcessLook.
        var migration = new ClientConfigMigration(new StubProjectDefaults("defaults-v2"), _profile);
        var config = new ClientConfig
        {
            SchemaVersion = 14,
            ProjectDefaultsHash = "defaults-v1",
            GraphicsPreset = GraphicsPreset.Custom,
            BloomEnabled = true,
            MotionBlurEnabled = true,
            LensEffectsEnabled = true,
        };

        bool migrated = migration.Migrate(config);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(config.ProjectDefaultsHash, Is.EqualTo("defaults-v2"));

        // Стаб отдаёт default-снимок, то есть все тумблеры выключены.
        Assert.That(config.BloomEnabled, Is.False);
        Assert.That(config.MotionBlurEnabled, Is.False);
        Assert.That(config.LensEffectsEnabled, Is.False);
        Assert.That(
            config.PostProcess.ToneMappingWhitePoint,
            Is.EqualTo(PostProcessLook.ColorGrading.ToneMappingWhitePoint));
    }

    [Test]
    public void Migrate_V20Config_SeedsDisplayCalibrationDefaults()
    {
        var migration = new ClientConfigMigration(new StubProjectDefaults("defaults-v2"), _profile);
        var config = new ClientConfig
        {
            SchemaVersion = 20,
            ProjectDefaultsHash = "defaults-v2",
            Display = new DisplaySettings
            {
                Gamma = 0f,
                PaperWhiteNits = 0f,
                PeakBrightnessNits = 0f,
            },
        };

        bool migrated = migration.Migrate(config);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(config.Display.Gamma, Is.EqualTo(DisplaySettings.DefaultGamma));
        Assert.That(config.Display.PaperWhiteNits, Is.EqualTo(DisplaySettings.DefaultPaperWhite));
        Assert.That(config.Display.PeakBrightnessNits, Is.EqualTo(DisplaySettings.DefaultPeakBrightness));
    }

    [Test]
    public void Migrate_CurrentCustomConfigWithMatchingHash_IsIdempotent()
    {
        var migration = new ClientConfigMigration(new StubProjectDefaults("defaults-v1"), _profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = "defaults-v1",
            GraphicsPreset = GraphicsPreset.Custom,
        };

        bool migrated = migration.Migrate(config);

        Assert.That(migrated, Is.False);
    }

    [Test]
    public void Validator_RejectsNonFiniteRuntimeSetting()
    {
        var validator = new ClientConfigValidator(
            new StubProjectDefaults("defaults-v1"),
            _profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = "defaults-v1",
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
    public void Validator_RejectsInvalidToneMappingWhitePoint()
    {
        var validator = new ClientConfigValidator(
            new StubProjectDefaults("defaults-v1"),
            _profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = "defaults-v1",
            GraphicsPreset = GraphicsPreset.Custom,
        };
        config.PostProcess.ToneMappingWhitePoint = 0f;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => validator.Validate(config))!;

        Assert.That(
            exception.Message,
            Does.Contain(nameof(config.PostProcess.ToneMappingWhitePoint)));
    }

    [Test]
    public void Validator_RejectsUnsupportedGeneralSettings()
    {
        var validator = new ClientConfigValidator(
            new StubProjectDefaults("defaults-v1"),
            _profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = "defaults-v1",
            Audio = new AudioSettings
            {
                MasterVolume = 1f,
                SfxVolume = 1f,
                MusicVolume = 1f,
                AmbienceVolume = 1f,
                VoiceVolume = 1f,
                UIVolume = 1f,
            },
            Interface = new InterfaceSettings
            {
                UIScale = 1f,
                Language = "unsupported",
            },
            GraphicsPreset = GraphicsPreset.Custom,
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => validator.Validate(config))!;

        Assert.That(exception.Message, Does.Contain(nameof(config.Interface.Language)));
    }

    private sealed class StubProjectDefaults(string contentHash) : IProjectDefaults
    {
        public int SchemaVersion => 1;

        public string ContentHash => contentHash;

        public ClientDefaultsSnapshot Client => default;

        public LightingDefaultsSnapshot Lighting => default;

        public ShaderDefaultsSnapshot Shaders => default;
    }
}
