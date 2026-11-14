#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.AssetPipeline.Loading;
using Kern.Networking.Processing;
using Kern.UI.ServerWindows;

namespace Kern.Tests.UI;

/// <summary>
/// Слайдер серверных окон с нулевым диапазоном (кристаллов нет): ручка
/// обязана исчезнуть и не отвечать указателю. Раньше прозрачный бокс ручки
/// оставался зоной захвата, тема подсвечивала его при наведении квадратом,
/// а сам бокс прибивало к правому концу линии (репорт про маркет).
/// </summary>
[TestFixture]
public class SliderPacketZeroRangeTests
{
    private sealed class NullAssetLoader : IAssetLoader
    {
        public int PendingAssetCount => 0;

        public int QueuedAssetCount => 0;

        public bool IsKnownMissing(string filename) => false;

        public event System.Action<System.Collections.Generic.IReadOnlyCollection<string>>? AssetsChanged
        {
            add { }
            remove { }
        }

        public void ReplaceAssets(System.Collections.Generic.IReadOnlyCollection<string> filenames) { }

        public UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            CancellationToken cancellationToken = default) =>
            UniTask.FromResult<byte[]?>(null);

        public UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            string expectedETag,
            CancellationToken cancellationToken = default) =>
            UniTask.FromResult<byte[]?>(null);

        public UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default) =>
            UniTask.FromResult(string.Empty);

        public UniTask<Texture2D?> GetTextureAsync(string filename, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<Texture2D?>(null);

        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(
            string filename,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Slider build must not load animated sprites.");
    }

    private sealed class NullOperations : IAsyncOperationSupervisor
    {
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation) { }

        public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
    }

    private sealed class NullWebLoader : IWebAssetLoader
    {
        public UniTask<Texture2D?> GetTextureAsync(string url, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<Texture2D?>(null);
    }

    private static VisualElement Build(SliderPacket packet) =>
        new PacketUIBuilder(new NullAssetLoader(), new NullOperations(), new NullWebLoader())
            .Build(packet);

    [Test]
    public void ZeroRange_DraggerIsHiddenAndDeadToPointer()
    {
        // Пакет строит PacketTestFactory: init-сеттеры записей несут modreq
        // IsExternalInit, на которую тестовая asmdef автоссылки не имеет.
        SliderPacket packet = PacketTestFactory.Slider("crystal_green", 0f, 0f, 0f);

        var slider = (Slider)Build(packet);

        Assert.That(slider.ClassListContains("packet-slider--filled"), Is.False);

        VisualElement dragger = slider.Q(className: "unity-base-slider__dragger")!;
        Assert.That(dragger, Is.Not.Null);
        Assert.That(dragger.style.display.value, Is.EqualTo(DisplayStyle.None));
        Assert.That(dragger.pickingMode, Is.EqualTo(PickingMode.Ignore));

        // Дорожка — весь вид нулевой строки; ранний выход сборщика не должен
        // её пропускать.
        Assert.That(slider.Q(className: "packet-slider__track"), Is.Not.Null);

        // Имя живёт до раннего выхода: значение уходит с кликом «Изм».
        Assert.That(slider.name, Is.EqualTo("crystal_green"));
    }

    [Test]
    public void LiveRange_KeepsFilledClassAndVisibleDragger()
    {
        SliderPacket packet = PacketTestFactory.Slider("crystal_green", 0f, 10f, 3f);

        var slider = (Slider)Build(packet);

        Assert.That(slider.ClassListContains("packet-slider--filled"), Is.True);
        VisualElement dragger = slider.Q(className: "unity-base-slider__dragger")!;
        Assert.That(dragger, Is.Not.Null);
        Assert.That(dragger.style.display.value, Is.Not.EqualTo(DisplayStyle.None));
        Assert.That(dragger.pickingMode, Is.EqualTo(PickingMode.Position));
    }
}
