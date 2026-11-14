#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Networking.Processing;
using Kern.UI.Controls;
using MinesServer.Networking.Server.Packets.GUI;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.AssetPipeline.Loading;
using NUnit.Framework;
using Kern.UI.ServerWindows;

namespace Kern.Tests.UI;

/// <summary>
/// Контракт полей ввода и геометрии, назначенных сервером: поле с
/// подсказкой и маской пароля, размеры элемента без абсолютизации
/// (Element.*) против геометрии канваса (Canvas.*).
/// </summary>
[TestFixture]
public class PacketStyledElementTests
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
            throw new NotSupportedException("Styled element build must not load animated sprites.");
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
    public void TextBox_WithPlaceholderAndPassword_AppliesInputTraits()
    {
        var field = (RegexTextField)Builder().Build(PacketTestFactory.TextBox(
            "password", "Пароль", isPassword: true));

        Assert.That(field.textEdition.placeholder, Is.EqualTo("Пароль"));
        Assert.That(field.isPasswordField, Is.True);
    }

    [Test]
    public void TextBox_WithoutServerTraits_LeavesFieldUntouched()
    {
        var field = (RegexTextField)Builder().Build(PacketTestFactory.TextBox("login"));

        Assert.That(field.textEdition.placeholder, Is.EqualTo(string.Empty));
        Assert.That(field.isPasswordField, Is.False);
    }

    [Test]
    public void ElementSize_SetsSizeWithoutAbsolutization()
    {
        var element = Builder().Build(PacketTestFactory.Text(
            "вход", "", PacketTestFactory.Pair(AttachedPropertyKeys.ElementWidth, "220")));

        Assert.That(element.style.width.value.value, Is.EqualTo(220f));
        Assert.That(element.ClassListContains("abs"), Is.False,
            "Element.* задаёт размер, не позицию");
    }

    [Test]
    public void CanvasGeometry_StillAbsolutizesElement()
    {
        var element = Builder().Build(PacketTestFactory.Text(
            "вход", "",
            PacketTestFactory.Pair(AttachedPropertyKeys.CanvasX, "10"),
            PacketTestFactory.Pair(AttachedPropertyKeys.CanvasWidth, "50")));

        Assert.That(element.style.width.value.value, Is.EqualTo(50f));
        Assert.That(element.ClassListContains("abs"), Is.True);
    }

    [Test]
    public void TextFontMark_AddsSkinFontClass()
    {
        // Гарнитура приходит attached-свойством (UITK не парсит <font>):
        // имя из пакета превращается в скин-класс packet-font-*.
        var label = Builder().Build(PacketTestFactory.Text(
            "метка", "", PacketTestFactory.Pair(AttachedPropertyKeys.TextFont, "Mono")));

        Assert.That(label.ClassListContains("packet-font-mono"), Is.True);
    }
}
