#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Audio.Core;
using Kern.Core;
using Kern.Rendering;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.Information;
using MinesServer.Networking.Contracts.Packets;
using NUnit.Framework;
using UnityEngine;

using Kern.Networking.Processing;

namespace Kern.Tests.Networking;

[TestFixture]
public class ClientConfigProcessorTests
{
    private StubTextureStorageService _textureStorage = null!;
    private StubAsyncOperationSupervisor _supervisor = null!;
    private StubAudioSystem _audio = null!;
    private ClientConfigProcessor _processor = null!;
    private ConfigFixture _config = null!;

    [SetUp]
    public void SetUp()
    {
        _textureStorage = new StubTextureStorageService();
        _supervisor = new StubAsyncOperationSupervisor();
        _audio = new StubAudioSystem();
        _config = new ConfigFixture();
        _config.Config.Audio.MasterVolume = 0.25f;
        _config.Config.Audio.SoundEffectsVolume = 0.5f;
        _config.Config.Audio.MusicVolume = 0.75f;
        _processor = new ClientConfigProcessor(_textureStorage, _supervisor, _audio, _config);
    }

    [Test]
    public void Process_UsesLocalBusVolumes()
    {
        var packet = new ClientConfigPacket(
            Array.Empty<StringPairPacket>(),
            Array.Empty<string>());

        _processor.Process(packet);

        Assert.That(_audio.BusVolumes[AudioBusType.Master], Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(_audio.BusVolumes[AudioBusType.SoundEffects], Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(_audio.BusVolumes[AudioBusType.Music], Is.EqualTo(0.75f).Within(0.001f));
    }

    [Test]
    public void Process_UnrenderedTextures_RunsPreloadOperation()
    {
        var packet = new ClientConfigPacket(
            Array.Empty<StringPairPacket>(),
            new[] { "Visual/Cells/1.png", "Visual/Cells/2.png", "Visual/Skin/bee.png" });

        _processor.Process(packet);

        Assert.That(_supervisor.LastOperationName, Is.EqualTo("preload_textures"));
        Assert.That(_textureStorage.PreloadedLists.Count, Is.EqualTo(1));
        Assert.That(_textureStorage.PreloadedLists[0], Is.EquivalentTo(new[] { "Visual/Cells/1.png", "Visual/Cells/2.png", "Visual/Skin/bee.png" }));
    }

    [Test]
    public void Process_EmptyTextures_DoesNotRunPreload()
    {
        var packet = new ClientConfigPacket(
            Array.Empty<StringPairPacket>(),
            Array.Empty<string>());

        _processor.Process(packet);

        Assert.That(_supervisor.LastOperationName, Is.Null);
        Assert.That(_textureStorage.PreloadedLists.Count, Is.EqualTo(0));
    }

    private sealed class ConfigFixture : IClientConfigManager
    {
        public ClientConfig Config { get; } = new();
        public string ConfigFilePath => string.Empty;
        public GraphicsPreset SelectedGraphicsPreset => GraphicsPreset.Standard;
        public void EnsureInitialized()
        {
        }
        public void SelectGraphicsPreset(GraphicsPreset preset) => throw new NotSupportedException();
        public void UpdateSection<TSection>(Func<ClientConfig, TSection> select, Action<TSection> update)
            where TSection : class, new() => update(select(Config));
        public void UpdatePostProcessAndSave(Action<ClientConfig> update) => update(Config);
        public void UpdateAndSave(Action<ClientConfig> update) => update(Config);
        public void Load() => throw new NotSupportedException();
        public void Save() => throw new NotSupportedException();
        public void SaveDeferred() => throw new NotSupportedException();
    }

    private sealed class StubTextureStorageService : ITextureStorageService
    {
        public List<IReadOnlyList<string>> PreloadedLists { get; } = new();

        public event Action<string> OnTextureLoaded
        {
            add { }
            remove { }
        }

        public bool HasTexture(string filename) => false;

        public UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<Texture2D?>(null);

        public UniTask<byte[]?> GetTextureData(string filename, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<byte[]?>(null);

        public UniTask PreloadTexturesAsync(IReadOnlyList<string> filenames, CancellationToken cancellationToken = default)
        {
            PreloadedLists.Add(filenames);
            return UniTask.CompletedTask;
        }
    }

    private sealed class StubAsyncOperationSupervisor : IAsyncOperationSupervisor
    {
        public string? LastOperationName { get; private set; }
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
            LastOperationName = operationName;
            operation(CancellationToken.None).Forget();
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
    }

    private sealed class StubAudioSystem : IAudioSystem
    {
        public Dictionary<AudioBusType, float> BusVolumes { get; } = new();

        public bool IsInitialized => true;
        public bool IsDegraded => false;
        public bool IsBackgroundMuted => false;

        public IAudioPlaybackHandle? Play(string eventName, Vector3? worldPosition = null, AudioLayer? overrideLayer = null, float? overrideVolume = null) => null;
        public IAudioPlaybackHandle? PlayAttached(string eventName, GameObject targetGameObject, AudioLayer? overrideLayer = null, float? overrideVolume = null) => null;
        public IAudioPlaybackHandle? PlayAt(string eventName, Vector3 worldPosition, AudioLayer? layer = null, float? volume = null) => null;
        public IAudioPlaybackHandle? Play2D(string eventName, AudioLayer? layer = null, float? volume = null) => null;

        public float GetBusVolume(AudioBusType type) =>
            BusVolumes.TryGetValue(type, out float vol) ? vol : 1f;

        public void SetBusVolume(AudioBusType type, float volume)
        {
            BusVolumes[type] = volume;
        }

        public void StopBus(AudioBusType type, float fadeOut = 0f)
        {
        }

        public UniTask WaitUntilBanksReadyAsync(CancellationToken cancellationToken = default) =>
            UniTask.CompletedTask;
    }
}
