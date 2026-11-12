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
/// Контракт стилей, назначенных сервером в пакете: поле ввода с подсказкой
/// и маской пароля, текст с серверной типографикой, размеры элемента без
/// абсолютизации (Element.*) против геометрии канваса (Canvas.*).
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
            CancellationToken cancellationToken = default,
            int timeoutSeconds = AssetStreamingLimits.AssetRequestTimeoutSeconds) =>
            UniTask.FromResult<byte[]?>(null);

        public UniTask<byte[]?> GetAssetBytesAsync(
            string filename,
            string expectedETag,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = AssetStreamingLimits.AssetRequestTimeoutSeconds) =>
            UniTask.FromResult<byte[]?>(null);

        public UniTask<string> GetAssetPathAsync(
            string filename,
            CancellationToken cancellationToken = default,
            int timeoutSeconds = AssetStreamingLimits.AssetRequestTimeoutSeconds) =>
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
    public void TextBox_WithPlaceholderPasswordLimit_AppliesInputTraits()
    {
        var field = (RegexTextField)Builder().Build(PacketTestFactory.TextBox(
            "password", "Пароль", isPassword: true, maxLength: 16));

        Assert.That(field.textEdition.placeholder, Is.EqualTo("Пароль"));
        Assert.That(field.isPasswordField, Is.True);
        Assert.That(field.maxLength, Is.EqualTo(16));
    }

    [Test]
    public void TextBox_WithoutServerTraits_LeavesFieldUntouched()
    {
        var field = (RegexTextField)Builder().Build(PacketTestFactory.TextBox("login"));

        Assert.That(field.textEdition.placeholder, Is.EqualTo(string.Empty));
        Assert.That(field.isPasswordField, Is.False);
        Assert.That(field.maxLength, Is.EqualTo(0), "0 = лимита нет");
    }

    [Test]
    public void StyledText_AppliesServerTypography()
    {
        var label = (Label)Builder().Build(PacketTestFactory.StyledText(
            "Project Kern", "",
            unchecked((int)0xFFD8D5EE), 32, fontId: 1 /* Display */, isBold: true,
            letterSpacing: 2, textAlign: 2 /* Center */));

        Assert.That(label.style.color.value, Is.EqualTo(new Color(0xD8 / 255f, 0xD5 / 255f, 0xEE / 255f, 1f)));
        Assert.That(label.style.fontSize.value.value, Is.EqualTo(32f));
        Assert.That(label.ClassListContains("packet-font-display"), Is.True);
        Assert.That(label.style.letterSpacing.value.value, Is.EqualTo(2f));
        Assert.That(label.style.unityTextAlign.value, Is.EqualTo(TextAnchor.MiddleCenter));
    }

    [Test]
    public void StyledButton_AppliesServerTypographyToo()
    {
        // Кликабельный текст — та же серверная типографика (кнопка «Войти»
        // несёт кегль и цвет на-сигнале).
        var button = Builder().Build(PacketTestFactory.StyledText(
            "Войти", ".", unchecked((int)0xFF160602), 17, fontId: 0, isBold: true,
            letterSpacing: 1, textAlign: 2));

        Assert.That(button, Is.InstanceOf<Button>());
        Assert.That(button.style.fontSize.value.value, Is.EqualTo(17f));
        Assert.That(button.style.color.value, Is.EqualTo(new Color(0x16 / 255f, 0x06 / 255f, 0x02 / 255f, 1f)));
    }

    [Test]
    public void DefaultText_LeavesSkinValuesUnset()
    {
        var label = (Label)Builder().Build(PacketTestFactory.StyledText(
            "plain", "", textColorArgb: 0, fontSize: 0, fontId: 0, isBold: false,
            letterSpacing: 0, textAlign: 0 /* Default */));

        Assert.That(label.style.color.keyword, Is.EqualTo(StyleKeyword.Undefined),
            "цвет скина клиента: инлайн-цвета нет");
        Assert.That(label.style.fontSize.keyword, Is.EqualTo(StyleKeyword.Undefined),
            "кегль скина клиента: инлайн-кегля нет");
        Assert.That(label.ClassListContains("packet-font-display"), Is.False);
        Assert.That(label.ClassListContains("packet-font-mono"), Is.False);
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
}
