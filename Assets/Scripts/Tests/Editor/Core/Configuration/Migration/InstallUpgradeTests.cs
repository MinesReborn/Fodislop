#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.Persistence;
using Kern.Rendering;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

using Kern.AssetPipeline.Cache;
using Kern.Core.Configuration;

namespace Kern.Tests.Core;

// Всё, что клиент хранит в persistentDataPath, проверяется вместе на одной
// папке: конфиг, кэш ассетов и карта мира. Старые конфиги мигрируются с
// backup; будущая схема отклоняется без перезаписи. Старая карта
// пересоздаётся текущим форматом.
[TestFixture]
public sealed class InstallUpgradeTests
{
    private const string WorldCode = "install_test";
    private const int WorldWidth = 64;
    private const int WorldHeight = 32;
    private static readonly CellType s_storedCell = (CellType)123;

    private string _dataRoot = null!;

    private GraphicsQualityProfile _profile = null!;

    private string ConfigPath => Path.Combine(_dataRoot, "Config/client_config.json");

    private string CachePath => Path.Combine(_dataRoot, "AssetCache");

    private string MapPath => Path.Combine(_dataRoot, WorldCode + ".map");

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"kern_install_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
        _profile = GraphicsQualityProfile.CreateDefault();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [TestCase("LeftCtrl", "LeftAlt")]
    [TestCase("B", "B")]
    public void Schema49_SlowMovementBindingMigratesDefaultAndPreservesCustom(string oldKey, string expectedKey)
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 49;
        legacy.Interface.KeyFastMove = oldKey;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonUtility.ToJson(legacy));

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Interface.KeyFastMove, Is.EqualTo(expectedKey));
    }

    [TestCase(44)]
    [TestCase(45)]
    public void PreDepthConfig_MigratesMissingDepthSettingsAndPreservesSavedValues(int schema)
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = schema;
        legacy.Audio.MasterVolume = 0.37f;
        legacy.Connection.ServerHost = "depth-migration.test";
        legacy.Connection.ApiPort = 4321;
        legacy.Terrain.ShimmerSpeedScale = 0.23f;
        legacy.Terrain.EnableRim = false;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string original = JsonUtility.ToJson(legacy, prettyPrint: true);
        original = Regex.Replace(original, """\s*"DepthPixels"\s*:\s*[^,}]+,""", "");
        original = Regex.Replace(original, """\s*"DepthDarkening"\s*:\s*[^,}]+,""", "");
        File.WriteAllText(ConfigPath, original);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.SourceSchemaVersion, Is.EqualTo(schema));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Terrain.DepthPixels, Is.EqualTo(16f));
        Assert.That(result.Config.Terrain.DepthDarkening, Is.EqualTo(0.35f));
        Assert.That(result.Config.Audio.MasterVolume, Is.EqualTo(0.37f));
        Assert.That(result.Config.Connection.ServerHost, Is.EqualTo("depth-migration.test"));
        Assert.That(result.Config.Connection.ApiPort, Is.EqualTo(4321));
        Assert.That(result.Config.Terrain.ShimmerSpeedScale, Is.EqualTo(0.23f));
        Assert.That(result.Config.Terrain.EnableRim, Is.False);
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(original));
        ClientConfig reloaded = new ClientConfigRepository(ConfigPath).Load().Config;
        Assert.That(reloaded.Terrain.DepthPixels, Is.EqualTo(16f));
        Assert.That(reloaded.Terrain.DepthDarkening, Is.EqualTo(0.35f));
        Assert.That(LoadConfig().Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
    }

    [Test]
    public void Schema45WithSavedDepthValues_PreservesDisabledEffect()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 45;
        legacy.Terrain.DepthPixels = 0f;
        legacy.Terrain.DepthDarkening = 0.18f;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonUtility.ToJson(legacy, prettyPrint: true));

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.Config.Terrain.DepthPixels, Is.Zero);
        Assert.That(result.Config.Terrain.DepthDarkening, Is.EqualTo(0.18f));
    }

    [TestCase(44)]
    [TestCase(45)]
    [TestCase(46)]
    public void PreFisheyeConfig_MigratesAndPreservesDepthAndVolume(int schema)
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = schema;
        legacy.Terrain.DepthPixels = 0f;
        legacy.Terrain.DepthDarkening = 0.18f;
        legacy.Audio.MasterVolume = 0.37f;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        string original = JsonUtility.ToJson(legacy, prettyPrint: true);
        File.WriteAllText(ConfigPath, original);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Terrain.DepthPixels, Is.Zero);
        Assert.That(result.Config.Terrain.DepthDarkening, Is.EqualTo(0.18f));
        Assert.That(result.Config.Audio.MasterVolume, Is.EqualTo(0.37f));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(original));
        Assert.That(new ClientConfigRepository(ConfigPath).Load().Config.Audio.MasterVolume, Is.EqualTo(0.37f));
        Assert.That(LoadConfig().Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
    }

    [TestCase(45)]
    [TestCase(46)]
    [TestCase(47)]
    public void PreTextAndMovementConfig_PreservesScaleAndCustomActions(int schema)
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = schema;
        legacy.Interface.UIScale = 1.2f;
        legacy.Interface.KeyDig = "W";
        string original = JsonUtility.ToJson(legacy, prettyPrint: true);
        foreach (string field in new[] { "TextScale", "KeyMoveUp", "KeyMoveDown", "KeyMoveLeft", "KeyMoveRight",
            "KeyMoveUpAlternate", "KeyMoveDownAlternate", "KeyMoveLeftAlternate", "KeyMoveRightAlternate",
            "KeyTurnOnly", "KeyTurnOnlyAlternate", "KeyFastMove" })
        {
            original = Regex.Replace(original, "\\s*\"" + field + "\"\\s*:\\s*[^,}]+,", "");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, original);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.Config.Interface.TextScale, Is.EqualTo(1f));
        Assert.That(result.Config.Interface.UIScale, Is.EqualTo(1.2f));
        Assert.That(result.Config.Interface.KeyDig, Is.EqualTo("W"));
        Assert.That(result.Config.Interface.KeyMoveUp, Is.EqualTo("None"));
        Assert.That(result.Config.Interface.KeyMoveUpAlternate, Is.EqualTo("UpArrow"));
        Assert.That(result.Config.Interface.KeyTurnOnly, Is.EqualTo("LeftShift"));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(original));
        Assert.That(LoadConfig().Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
    }

    [Test]
    public void Schema48_DisablesDepthSwitchWithoutLosingStrengths()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 48;
        legacy.Terrain.DepthPixels = 12f;
        legacy.Terrain.DepthDarkening = 0.42f;
        string original = JsonUtility.ToJson(legacy, prettyPrint: true);
        original = Regex.Replace(original, "\\s*\"DepthEnabled\"\\s*:\\s*[^,}]+,", "");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, original);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Config.Terrain.DepthEnabled, Is.False);
        Assert.That(result.Config.Terrain.DepthPixels, Is.EqualTo(12f));
        Assert.That(result.Config.Terrain.DepthDarkening, Is.EqualTo(0.42f));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(original));
        Assert.That(LoadConfig().Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
    }

    [Test]
    public void CleanInstall_CreatesCurrentFormatsWithoutBackups()
    {
        ClientConfigLoader.Result config = LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.Not.EqualTo(s_storedCell)));

        Assert.That(config.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.CreatedDefaults));
        Assert.That(new ClientConfigRepository(ConfigPath).Load().Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(ReadMapFormatVersion(), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void DesktopCleanInstall_SelectsStandardPreset()
    {
        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Standard));
        Assert.That(
            result.Config.GraphicsQualitySettings.LightingQuality,
            Is.EqualTo(Kern.World.Lighting.Quality.LightingQualityMode.PerPixel));
    }

    [Test]
    public void AndroidCleanInstall_SelectsLightingPreset()
    {
        ClientConfigLoader.Result result = LoadConfig(isAndroidPlayer: true);

        Assert.That(result.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Standard));
        Assert.That(
            result.Config.GraphicsQualitySettings.LightingQuality,
            Is.EqualTo(Kern.World.Lighting.Quality.LightingQualityMode.PerPixel));
    }

    [Test]
    public void AndroidSchema42ClassicConfig_MigratesToLightingPreset()
    {
        ClientConfig config = ClientConfigDefaults.Create(_profile);
        config.SchemaVersion = 42;
        config.GraphicsPreset = GraphicsPreset.Classic;
        config.GraphicsQualitySettings = _profile.Get(GraphicsPreset.Classic);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, prettyPrint: true));

        ClientConfigLoader.Result result = LoadConfig(isAndroidPlayer: true);

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Standard));
        Assert.That(
            result.Config.GraphicsQualitySettings.LightingQuality,
            Is.EqualTo(Kern.World.Lighting.Quality.LightingQualityMode.PerPixel));
        Assert.That(File.Exists(ConfigPath + ".backup"), Is.True);
    }

    [Test]
    public void DesktopSchema42ClassicConfig_RemainsClassic()
    {
        ClientConfig config = ClientConfigDefaults.Create(_profile);
        config.SchemaVersion = 42;
        config.GraphicsPreset = GraphicsPreset.Classic;
        config.GraphicsQualitySettings = _profile.Get(GraphicsPreset.Classic);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, prettyPrint: true));

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Classic));
        Assert.That(
            result.Config.GraphicsQualitySettings.LightingQuality,
            Is.EqualTo(Kern.World.Lighting.Quality.LightingQualityMode.Off));
    }

    [Test]
    public void SecondLaunch_AfterCleanInstall_RewritesNothing()
    {
        LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        byte[] configBytes = File.ReadAllBytes(ConfigPath);

        ClientConfigLoader.Result second = LoadConfig();
        _ = new PersistentAssetCache(CachePath);

        Assert.That(second.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Loaded));
        Assert.That(File.ReadAllBytes(ConfigPath), Is.EqualTo(configBytes));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void OldConfigVersion_MigratesSettingsAndKeepsBackup()
    {
        string oldJson = OldVersionConfig(
            schemaVersion: ClientConfig.CurrentSchemaVersion - 3,
            PixelSamplingMode.PixelPerfect);

        ClientConfigLoader.Result config = LoadConfig();

        Assert.That(config.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(config.SourceSchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion - 3));
        Assert.That(config.Config.Display.PixelSampling, Is.EqualTo(PixelSamplingMode.PixelPerfect));
        Assert.That(
            new ClientConfigRepository(ConfigPath).Load().Config.SchemaVersion,
            Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(oldJson));
    }

    [Test]
    public void Schema15Config_MigratesToCurrentSchemaAndKeepsUserSettings()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 15;
        legacy.Audio.MasterVolume = 0.37f;
        string legacyJson = JsonUtility.ToJson(legacy, prettyPrint: true);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, legacyJson);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.SourceSchemaVersion, Is.EqualTo(15));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Audio.MasterVolume, Is.EqualTo(0.37f));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(legacyJson));
    }

    [Test]
    public void PreviousConfigVersion_MigratesAndKeepsBackup()
    {
        string previousJson = OldVersionConfig(
            schemaVersion: ClientConfig.CurrentSchemaVersion - 1,
            PixelSamplingMode.SmoothFiltered);
        // A saved value of the removed selector must not resurrect the feature.
        previousJson = previousJson.Replace(
            "\"EnableDistortion\": true", "\"EnableDistortion\": true, \"DistortionStyle\": 1");
        Assert.That(previousJson, Does.Contain("\"DistortionStyle\": 1"));
        File.WriteAllText(ConfigPath, previousJson);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.SourceSchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion - 1));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Terrain.EnableRim, Is.True);
        Assert.That(result.Config.Terrain.EnableDistortion, Is.True);
        Assert.That(JsonUtility.ToJson(result.Config.Terrain), Does.Not.Contain("DistortionStyle"));
        Assert.That(result.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Standard));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(previousJson));
    }

    [Test]
    public void Schema37WithoutHdrSwitchPending_MigratesAndKeepsBackup()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 37;
        legacy.Display.HDRSwitchPending = true;
        string sourceJson = JsonUtility.ToJson(legacy, prettyPrint: true);
        string legacyJson = Regex.Replace(
            sourceJson,
            @"^[ \t]*""HDRSwitchPending""[ \t]*:[ \t]*(?:true|false),?\r?\n",
            string.Empty,
            RegexOptions.Multiline);
        Assert.That(legacyJson, Does.Not.Contain("HDRSwitchPending"));
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, legacyJson);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.SourceSchemaVersion, Is.EqualTo(37));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Display.HDRSwitchPending, Is.False);
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(legacyJson));
        Assert.That(
            new ClientConfigRepository(ConfigPath).Load().Config.Display.HDRSwitchPending,
            Is.False);
    }

    [Test]
    public void Schema38WithoutPeakBrightnessFromDisplay_MigratesToDisplayPeak()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 38;
        legacy.Display.PeakBrightnessFromDisplay = false;
        string sourceJson = JsonUtility.ToJson(legacy, prettyPrint: true);
        string legacyJson = Regex.Replace(
            sourceJson,
            @",?\r?\n[ \t]*""PeakBrightnessFromDisplay""[ \t]*:[ \t]*(?:true|false)",
            string.Empty);
        Assert.That(legacyJson, Does.Not.Contain("PeakBrightnessFromDisplay"));
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, legacyJson);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.SourceSchemaVersion, Is.EqualTo(38));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Display.PeakBrightnessFromDisplay, Is.True);
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(legacyJson));
    }

    [Test]
    public void Schema39TerrainNames_MigrateToRimGlowAndBlinking()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 39;
        legacy.Terrain.EnableRim = false;
        legacy.Terrain.BlinkingSpeedScale = 3.25f;
        legacy.Terrain.TransitGlowColor = new Color(0.25f, 0.5f, 0.75f, 1f);
        legacy.Terrain.TransitGlowStrength = 2.5f;
        legacy.Terrain.PerspectiveGlowColor = new Color(0.5f, 0.25f, 0.125f, 1f);
        legacy.Terrain.PerspectiveGlowStrength = 1.5f;
        string legacyJson = JsonUtility.ToJson(legacy, prettyPrint: true)
            .Replace("\"EnableRim\"", "\"EnableReliefRim\"")
            .Replace("\"BlinkingSpeedScale\"", "\"PulseSpeedScale\"")
            .Replace("GlowColor\"", "EmissionColor\"")
            .Replace("GlowStrength\"", "EmissionStrength\"");
        Assert.That(legacyJson, Does.Contain("\"EnableReliefRim\""));
        Assert.That(legacyJson, Does.Contain("\"PerspectiveEmissionStrength\""));
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, legacyJson);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Terrain.EnableRim, Is.False);
        Assert.That(result.Config.Terrain.BlinkingSpeedScale, Is.EqualTo(3.25f));
        Assert.That(result.Config.Terrain.TransitGlowColor, Is.EqualTo(new Color(0.25f, 0.5f, 0.75f, 1f)));
        Assert.That(result.Config.Terrain.TransitGlowStrength, Is.EqualTo(2.5f));
        Assert.That(result.Config.Terrain.PerspectiveGlowColor, Is.EqualTo(new Color(0.5f, 0.25f, 0.125f, 1f)));
        Assert.That(result.Config.Terrain.PerspectiveGlowStrength, Is.EqualTo(1.5f));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(legacyJson));
    }

    [TestCase(41)]
    [TestCase(42)]
    [TestCase(43)]
    [TestCase(44)]
    public void DivergedSchema_PreservesPortAndDesktopScale(int schema)
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = schema;
        legacy.Connection.ApiPort = 6123;
        legacy.Interface.UIScale = 1.2f;
        string json = JsonUtility.ToJson(legacy, prettyPrint: true);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, json);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Connection.ApiPort, Is.EqualTo(6123));
        Assert.That(result.Config.Interface.UIScale, Is.EqualTo(1.2f));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(json));
    }

    [Test]
    public void LocalSchema44WithoutUpstreamFields_GainsApiPort()
    {
        ClientConfig legacy = ClientConfigDefaults.Create(_profile);
        legacy.SchemaVersion = 44;
        string json = Regex.Replace(JsonUtility.ToJson(legacy, prettyPrint: true),
            @",?\r?\n[ \t]*""ApiPort""[ \t]*:[ \t]*[-0-9.]+",
            string.Empty);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, json);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Config.Connection.ApiPort,
            Is.EqualTo(ClientConfigurationDefaults.DefaultApiPort));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(json));
    }

    [Test]
    public void OldMapVersion_DropsAndRegenerates()
    {
        WriteMap(formatVersion: 0);

        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.Not.EqualTo(s_storedCell)));
        Assert.That(ReadMapFormatVersion(), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Not.Empty,
            "Map migration keeps the old file as a versioned backup.");
    }

    [Test]
    public void NewerConfigVersion_IsRejectedAndLeftUntouched()
    {
        string newerJson = OldVersionConfig(
            schemaVersion: ClientConfig.CurrentSchemaVersion + 1,
            PixelSamplingMode.SmoothFiltered);
        WriteMap(formatVersion: WorldLayerFileHeader.CurrentFormatVersion);

        Assert.Throws<InvalidDataException>(() => LoadConfig());
        Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(newerJson));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void CorruptConfig_IsRejectedAndLeftUntouched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ \"SchemaVersion\": 27, ");

        Assert.Throws<InvalidDataException>(() => LoadConfig());

        Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo("{ \"SchemaVersion\": 27, "));
    }

    private ClientConfigLoader.Result LoadConfig(bool isAndroidPlayer = false) =>
        new ClientConfigLoader(
            new ClientConfigRepository(ConfigPath),
            _profile,
            isAndroidPlayer).LoadOrCreate();

    // Файл чужой версии: те же секции, что пишет текущий клиент, но с чужим
    // номером схемы. Для старых неподдерживаемых версий содержимое не переносится;
    // отдельный тест выше проверяет миграцию предыдущей версии.
    private string OldVersionConfig(int schemaVersion, PixelSamplingMode pixelSampling)
    {
        ClientConfig config = ClientConfigDefaults.Create(_profile);
        config.Display.PixelSampling = pixelSampling;
        config.SchemaVersion = schemaVersion;
        string json = JsonUtility.ToJson(config, prettyPrint: true);
        // Ступень пишется числом старой схемы (5 — прежнее «Ultra»): нынешнее
        // перечисление такой ступени уже не знает, и миграция обязана перевести
        // её в «Стандарт», а не оставить как есть.
        json = Regex.Replace(json, @"""GraphicsPreset"":\s*\d+", "\"GraphicsPreset\": 5");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, json);
        return json;
    }

    // Карта чужого формата: пишем текущим клиентом и подменяем номер версии.
    // Старая клетка при этом теряется: файл пересоздаётся.
    private void WriteMap(int formatVersion)
    {
        WithMap(storage =>
        {
            storage.SetCell(0, 0, s_storedCell);
            storage.Flush(durable: true);
        });

        using (var stream = new FileStream(MapPath, FileMode.Open, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Seek(WorldLayerFileHeader.FormatVersionOffset, SeekOrigin.Begin);
            writer.Write(formatVersion);
        }

        foreach (string backup in Directory.GetFiles(_dataRoot, WorldCode + ".backup.map"))
        {
            File.Delete(backup);
        }
    }

    private int ReadMapFormatVersion()
    {
        using var stream = new FileStream(MapPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream);
        stream.Seek(WorldLayerFileHeader.FormatVersionOffset, SeekOrigin.Begin);
        return reader.ReadInt32();
    }

    private void WithMap(Action<MapStorage> use)
    {
        using var operations = new AsyncOperationSupervisor();
        var storage = new MapStorage(operations, _dataRoot);
        try
        {
            storage.InitWorld(WorldCode, WorldWidth, WorldHeight);
            use(storage);
        }
        finally
        {
            storage.Dispose();
        }
    }
}
