#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

// Свёрнутое приложение система может завершить без OnApplicationQuit: всё,
// что игрок успел изменить, к этому моменту обязано лежать на диске. После
// возврата игра продолжает ту же сессию.
[TestFixture]
public sealed class PauseResumePlayModeTests
{
    private const string TestDummyToken = "playmode-pause-token";
    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IClientConfigManager _config = null!;
    private float _originalMasterVolume;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _config = _bootstrap.Container.Resolve<IClientConfigManager>();
        _originalMasterVolume = _config.Config.Audio.MasterVolume;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_config.Config != null)
        {
            _config.UpdateSection(config => config.Audio, audio => audio.MasterVolume = _originalMasterVolume);
            _config.Save();
        }

        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator Pause_WritesPendingConfigEditToDiskImmediately()
    {
        float edited = Mathf.Approximately(_originalMasterVolume, 0.37f) ? 0.41f : 0.37f;

        // Правка уходит в отложенное сохранение (0.25 с); пауза в том же
        // кадре должна записать её, не дожидаясь таймера.
        _config.UpdateSection(config => config.Audio, audio => audio.MasterVolume = edited);
        PlayModeHarness.SendApplicationPause(true);

        ClientConfig onDisk = new ClientConfigRepository(_config.ConfigFilePath).Load().Config;
        Assert.That(onDisk.Audio.MasterVolume, Is.EqualTo(edited).Within(1e-5f));

        PlayModeHarness.SendApplicationPause(false);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Pause_FlushesDirtyWorldChunks()
    {
        IWorldDataStorage storage = PlayModeHarness.RequireInGame<IWorldDataStorage>();
        IWorldPersistence persistence = PlayModeHarness.RequireInGame<IWorldPersistence>();
        Assert.That(storage.IsReady, Is.True);
        CellType original = storage.GetCell(0, 0);

        try
        {
            storage.SetCell(0, 0, original == CellType.Empty ? CellType.DeepObsidianRock : CellType.Empty);
            Assert.That(persistence.HasDirtyChunks, Is.True);

            PlayModeHarness.SendApplicationPause(true);

            Assert.That(persistence.HasDirtyChunks, Is.False, "Pause left world changes only in memory.");
        }
        finally
        {
            storage.SetCell(0, 0, original);
            persistence.Flush(durable: true);
            PlayModeHarness.SendApplicationPause(false);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator Resume_ContinuesTheSameSession()
    {
        IConnectionService connection = _bootstrap.Container.Resolve<IConnectionService>();
        GameManager game = PlayModeHarness.RequireInGame<GameManager>();
        float timeScale = Time.timeScale;

        PlayModeHarness.SendApplicationPause(true);
        yield return new WaitForSecondsRealtime(2f);
        PlayModeHarness.SendApplicationPause(false);

        int pings = 0;
        connection.OnPacketReceived += OnPacket;
        try
        {
            yield return PlayModeHarness.WaitUntil(
                () => pings > 0,
                10f,
                "The offline server stopped sending pings after resume.");
        }
        finally
        {
            connection.OnPacketReceived -= OnPacket;
        }

        Assert.That(connection.IsConnected, Is.True);
        Assert.That(game.IsWorldLoaded, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(timeScale));
        Assert.That(_bootstrap.CurrentSceneName, Is.EqualTo(ProjectRuntimeContracts.SceneNames.MainGame));

        void OnPacket(ServerPacket packet)
        {
            if (packet.Payload is PingPacket)
            {
                pings++;
            }
        }
    }
}
