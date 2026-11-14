#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Contracts.Packets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.AssetPipeline.Loading;
using Kern.Networking.Processing;
using Kern.Networking.Protocol;
using Kern.UI.ServerWindows;

namespace Kern.Tests.UI;

/// <summary>
/// Контракт сборки серверных окон: карточки инвентаря сервер
/// присылает контейнерами [иконка / статус], ряды — DockPanel с детьми
/// влево, колонки — детьми без стыковки; закрывающая кнопка (метка
/// ServerWindowActionMetadata) не рисуется, оставаясь в кликабельном порядке.
/// </summary>
[TestFixture]
public class PacketInventoryCardTests
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

        public UniTask<Texture2D?> GetTextureAsync(
            string filename,
            CancellationToken cancellationToken = default) =>
            UniTask.FromResult<Texture2D?>(null);

        public UniTask<AnimatedSpriteData> GetAnimatedSpritesAsync(
            string filename,
            CancellationToken cancellationToken = default) =>
            throw new System.NotSupportedException("Card build must not load animated sprites.");
    }

    private sealed class NoOpOperations : IAsyncOperationSupervisor
    {
        public int ActiveCount => 0;

        public void Run(string operationName, Func<CancellationToken, UniTask> operation)
        {
        }

        public UniTask StopAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
    }

    private sealed class NullWebLoader : IWebAssetLoader
    {
        public UniTask<Texture2D?> GetTextureAsync(string url, CancellationToken cancellationToken = default) =>
            UniTask.FromResult<Texture2D?>(null);
    }

    private static PacketUIBuilder Builder() =>
        new(new NullAssetLoader(), new NoOpOperations(), new NullWebLoader());

    [Test]
    public void ServerCard_WithIconAndStatus_BuildsIconOverStatus()
    {
        // Карточка инвентаря в исполнении моста: контейнер с иконкой (URI
        // серверного ассета) и статусом, пристыкованными вверх.
        VisualElement card = Builder().Build(PacketTestFactory.DockPanel(
            PacketTestFactory.Image("Visual/Items/geoRed.png", 44, 44, "", PacketTestFactory.Pair("DockPanel.Dock", "Top")),
            PacketTestFactory.Text("1.2KK\nНет спроса", "", PacketTestFactory.Pair("DockPanel.Dock", "Top"))));

        Assert.That(card.childCount, Is.EqualTo(2));
        VisualElement icon = card[0];
        Assert.That(icon.style.width.value.value, Is.EqualTo(44f));
        Assert.That(icon.style.height.value.value, Is.EqualTo(44f));
        Assert.That(icon.pickingMode, Is.EqualTo(PickingMode.Ignore));
        var status = (Label)card[1];
        Assert.That(status.text, Is.EqualTo("1.2KK\nНет спроса"));
        // Некликабельный текст не перехватывает мышь у соседних кликабельных
        // элементов окна.
        Assert.That(status.pickingMode, Is.EqualTo(PickingMode.Ignore));
    }

    [Test]
    public void ExitMarkedButton_IsBuiltAsRegularAction()
    {
        // Закрывающая кнопка — обычная видимая кнопка ряда (крестик рамки и
        // ESC шлют клик по её индексу), без клиентских спец-веток.
        VisualElement exit = Builder().Build(PacketTestFactory.Text(
            "Закрыть",
            ".",
            PacketTestFactory.Pair(
                ServerWindowActionMetadata.ActionKey,
                ServerWindowActionMetadata.CloseActionValue)));

        Assert.That(exit, Is.InstanceOf<Button>());
        Assert.That(exit.ClassListContains("packet-window-action"), Is.True);
        Assert.That(exit.style.display.value, Is.EqualTo(DisplayStyle.Flex));
    }

    [Test]
    public void AllLeftDockedPanel_BuildsCenteredRowStrip()
    {
        // Ряд вкладок/кнопок/строк форм от сервера: все дети Dock=Left —
        // плоский центрированный ряд, без вложенных док-обёрток.
        VisualElement row = Builder().Build(PacketTestFactory.DockPanel(
            PacketTestFactory.Text("Первая", "", PacketTestFactory.Pair("DockPanel.Dock", "Left")),
            PacketTestFactory.Text("Вторая", "", PacketTestFactory.Pair("DockPanel.Dock", "Left"))));

        Assert.That(row.ClassListContains("row"), Is.True);
        Assert.That(row.ClassListContains("jc-center"), Is.True);
        Assert.That(row.childCount, Is.EqualTo(2));
    }

    [Test]
    public void UndockedPanel_BuildsPlainColumn()
    {
        // Колонка секций страницы от сервера: дети без стыковки складываются
        // сверху вниз.
        VisualElement column = Builder().Build(PacketTestFactory.DockPanel(
            PacketTestFactory.Text("Заголовок", ""),
            PacketTestFactory.Text("Тело", "")));

        Assert.That(column.ClassListContains("col"), Is.True);
        Assert.That(column.childCount, Is.EqualTo(2));
    }
}
