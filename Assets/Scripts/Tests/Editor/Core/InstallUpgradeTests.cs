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

namespace Kern.Tests.Core;

// Всё, что клиент хранит в persistentDataPath, проверяется вместе на одной
// папке: конфиг, кэш ассетов и карта мира. Старые конфиги мигрируются с
// backup; будущая схема отклоняется без перезаписи. Старые карта и маркер
// кеша обновляются.
[TestFixture]
public sealed class InstallUpgradeTests
{
    private const string WorldCode = "install_test";
    private const int WorldWidth = 64;
    private const int WorldHeight = 32;
    private const string CachedAsset = "Cells/117.png";
    private static readonly CellType _StoredCell = (CellType)123;
    private static readonly byte[] _CachedPayload = [1, 2, 3, 4];

    private string _dataRoot = null!;

    private GraphicsQualityProfile _profile = null!;

    private string ConfigPath => Path.Combine(_dataRoot, "Config", "client_config.json");

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

    [Test]
    public void CleanInstall_CreatesCurrentFormatsWithoutBackups()
    {
        ClientConfigLoader.Result config = LoadConfig();
        _ = new PersistentAssetCache(CachePath);
        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.Not.EqualTo(_StoredCell)));

        Assert.That(config.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.CreatedDefaults));
        Assert.That(new ClientConfigRepository(ConfigPath).Load().Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(ReadMapFormatVersion(), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
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
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
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
    public void PreviousConfigVersion_MigratesDistortionStyleAndKeepsBackup()
    {
        string previousJson = OldVersionConfig(
            schemaVersion: ClientConfig.CurrentSchemaVersion - 1,
            PixelSamplingMode.SmoothFiltered);
        previousJson = Regex.Replace(
            previousJson,
            @"^\s*""DistortionStyle""\s*:\s*0,\r?\n",
            string.Empty,
            RegexOptions.Multiline);
        Assert.That(previousJson, Does.Not.Contain("DistortionStyle"));
        File.WriteAllText(ConfigPath, previousJson);

        ClientConfigLoader.Result result = LoadConfig();

        Assert.That(result.Outcome, Is.EqualTo(ClientConfigLoader.Outcome.Migrated));
        Assert.That(result.SourceSchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion - 1));
        Assert.That(result.Config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(result.Config.Terrain.EnableReliefRim, Is.True);
        Assert.That(result.Config.Terrain.DistortionStyle, Is.EqualTo(TerrainDistortionStyle.Organic));
        Assert.That(result.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Overdrive));
        Assert.That(File.ReadAllText(ConfigPath + ".backup"), Is.EqualTo(previousJson));
    }

    [Test]
    public void OldMapVersion_DropsAndRegenerates()
    {
        WriteMap(formatVersion: 0);

        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.Not.EqualTo(_StoredCell)));
        Assert.That(ReadMapFormatVersion(), Is.EqualTo(WorldLayerFileHeader.CurrentFormatVersion));
        Assert.That(Directory.GetFiles(_dataRoot, "*.v*.backup", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void OldCacheMarker_RestampsAndKeepsPayloads()
    {
        WriteCache(markerVersion: 1);

        _ = new PersistentAssetCache(CachePath);

        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(File.ReadAllBytes(Path.Combine(CachePath, CachedAsset)), Is.EqualTo(_CachedPayload));
    }

    [Test]
    public void MissingCacheMarker_RestampsAndKeepsPayloads()
    {
        WriteCache(markerVersion: null);

        _ = new PersistentAssetCache(CachePath);

        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        Assert.That(File.ReadAllBytes(Path.Combine(CachePath, CachedAsset)), Is.EqualTo(_CachedPayload));
    }

    [Test]
    public void NewerConfigVersion_IsRejectedAndLeftUntouched()
    {
        string newerJson = OldVersionConfig(
            schemaVersion: ClientConfig.CurrentSchemaVersion + 1,
            PixelSamplingMode.SmoothFiltered);
        WriteCache(markerVersion: PersistentAssetCacheFormat.CurrentSchemaVersion + 1);
        WriteMap(formatVersion: WorldLayerFileHeader.CurrentFormatVersion + 1);

        Assert.Throws<InvalidDataException>(() => LoadConfig());
        _ = new PersistentAssetCache(CachePath);

        Assert.That(File.ReadAllText(ConfigPath), Is.EqualTo(newerJson));
        Assert.That(ReadCacheMarker(), Is.EqualTo(PersistentAssetCacheFormat.CurrentSchemaVersion));
        WithMap(storage => Assert.That(storage.GetCell(0, 0), Is.Not.EqualTo(_StoredCell)));
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

    private ClientConfigLoader.Result LoadConfig() =>
        new ClientConfigLoader(new ClientConfigRepository(ConfigPath), _profile).LoadOrCreate();

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
        // её в «Overdrive», а не оставить как есть.
        json = Regex.Replace(json, @"""GraphicsPreset"":\s*\d+", "\"GraphicsPreset\": 5");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, json);
        return json;
    }

    private void WriteCache(int? markerVersion)
    {
        string assetPath = Path.Combine(CachePath, CachedAsset);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllBytes(assetPath, _CachedPayload);
        if (markerVersion.HasValue)
        {
            File.WriteAllText(
                Path.Combine(CachePath, PersistentAssetCacheFormat.MarkerFileName),
                markerVersion.Value + "\n");
        }
    }

    private int ReadCacheMarker() =>
        int.Parse(File.ReadAllText(Path.Combine(CachePath, PersistentAssetCacheFormat.MarkerFileName)).Trim());

    // Карта чужого формата: пишем текущим клиентом и подменяем номер версии.
    // Старая клетка при этом теряется: файл пересоздаётся.
    private void WriteMap(int formatVersion)
    {
        WithMap(storage =>
        {
            storage.SetCell(0, 0, _StoredCell);
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
