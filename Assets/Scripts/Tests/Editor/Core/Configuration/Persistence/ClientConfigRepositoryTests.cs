#nullable enable

using System.IO;
using Kern.Core;
using Kern.Core.Interfaces;
using NUnit.Framework;

using Kern.Core.Configuration;

namespace Kern.Tests.Core;

public sealed class ClientConfigRepositoryTests
{
    private string _directory = null!;
    private string _configPath = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"kern-config-repository-{System.Guid.NewGuid():N}");
        _configPath = Path.Combine(_directory, "client_config.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void SaveThenLoad_RoundTripsConfigAndRemovesTemporaryFile()
    {
        var repository = new ClientConfigRepository(_configPath);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            Connection = new ConnectionSettings
            {
                ServerHost = "example.test",
                ServerPort = 4242,
            },
        };
        config.Display.HDRSwitchPending = true;

        repository.Save(config);
        ClientConfig loaded = repository.Load().Config;

        Assert.That(loaded.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(loaded.Connection.ServerHost, Is.EqualTo("example.test"));
        Assert.That(loaded.Connection.ServerPort, Is.EqualTo(4242));
        Assert.That(loaded.Display.HDRSwitchPending, Is.True);
        Assert.That(File.Exists(_configPath + ".tmp"), Is.False);
    }

    [Test]
    public void Load_CurrentSchemaWithMissingFields_ThrowsInsteadOfUsingClrDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _configPath,
            $"{{\"SchemaVersion\":{ClientConfig.CurrentSchemaVersion},\"MasterVolume\":1}}");
        var repository = new ClientConfigRepository(_configPath);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => repository.Load())!;

        Assert.That(exception.Message, Does.Contain("missing field(s)"));
        Assert.That(exception.Message, Does.Contain(nameof(AudioSettings.SoundEffectsVolume)));
    }

    [TestCase("FisheyeEnabled")]
    [TestCase("DepthEnabled")]
    [TestCase("TextScale")]
    [TestCase("KeyMoveUp")]
    [TestCase("FisheyeScale")]
    [TestCase("DepthPixels")]
    [TestCase("DepthDarkening")]
    public void Load_CurrentSchemaMissingPresentationField_RemainsAnError(string field)
    {
        var repository = new ClientConfigRepository(_configPath);
        repository.Save(new ClientConfig { SchemaVersion = ClientConfig.CurrentSchemaVersion });
        string original = File.ReadAllText(_configPath);
        string incomplete = System.Text.RegularExpressions.Regex.Replace(
            original, "\\s*\"" + field + "\"\\s*:\\s*[^,}]+,", "");
        Assert.That(incomplete, Is.Not.EqualTo(original));
        File.WriteAllText(_configPath, incomplete);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => repository.Load())!;

        Assert.That(exception.Message, Does.Contain(field));
        Assert.That(File.ReadAllText(_configPath), Is.EqualTo(incomplete));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Load_RenamedHDRSwitchFlag_PreservesSavedValue(bool pending)
    {
        var repository = new ClientConfigRepository(_configPath);
        var config = new ClientConfig { SchemaVersion = ClientConfig.CurrentSchemaVersion };
        config.Display.HDRSwitchPending = pending;
        config.Connection.ServerPort = 4242;
        repository.Save(config);
        string historicalJson = File.ReadAllText(_configPath)
            .Replace("\"HDRSwitchPending\"", "\"HdrSwitchPending\"");
        File.WriteAllText(_configPath, historicalJson);

        ClientConfig loaded = repository.Load().Config;

        Assert.That(loaded.Display.HDRSwitchPending, Is.EqualTo(pending));
        Assert.That(loaded.Connection.ServerPort, Is.EqualTo(4242));
        Assert.That(File.ReadAllText(_configPath), Is.EqualTo(historicalJson));
        repository.Save(loaded);
        Assert.That(File.ReadAllText(_configPath), Does.Contain("\"HDRSwitchPending\""));
        Assert.That(repository.Load().Config.Display.HDRSwitchPending, Is.EqualTo(pending));
    }

    [Test]
    public void Load_MissingHDRSwitchFlag_StillRejectsIncompleteCurrentSchema()
    {
        var repository = new ClientConfigRepository(_configPath);
        var config = new ClientConfig { SchemaVersion = ClientConfig.CurrentSchemaVersion };
        repository.Save(config);
        string json = File.ReadAllText(_configPath);
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"HDRSwitchPending\"\\s*:\\s*(true|false)\\s*,", "");
        Assert.That(json, Does.Not.Contain("\"HDRSwitchPending\""));
        File.WriteAllText(_configPath, json);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => repository.Load())!;

        Assert.That(exception.Message, Does.Contain(nameof(DisplaySettings.HDRSwitchPending)));
        Assert.That(File.ReadAllText(_configPath), Is.EqualTo(json));
    }

    [Test]
    public void Save_WithBackup_ReplacesExistingFileAndPreservesPreviousPayload()
    {
        var repository = new ClientConfigRepository(_configPath);
        var first = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            Connection = new ConnectionSettings { ServerPort = 1001 },
        };
        var second = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            Connection = new ConnectionSettings { ServerPort = 1002 },
        };
        string backupPath = _configPath + ".v14.backup";
        repository.Save(first);

        repository.Save(second, backupPath);

        Assert.That(repository.Load().Config.Connection.ServerPort, Is.EqualTo(1002));
        var backupRepository = new ClientConfigRepository(backupPath);
        Assert.That(backupRepository.Load().Config.Connection.ServerPort, Is.EqualTo(1001));
    }
}
