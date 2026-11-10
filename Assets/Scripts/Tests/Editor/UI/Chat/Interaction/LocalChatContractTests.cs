#nullable enable

using System.IO;
using Kern.Core;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class LocalChatContractTests
{
    private static string ReadAsset(params string[] segments)
    {
        string path = Application.dataPath;
        foreach (string segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return File.ReadAllText(path);
    }

    // Окно глобального чата и локальный чат — разные каналы с разными пакетами.
    // Смешение их означало бы либо отправку локального сообщения в глобальный
    // канал, либо появление вкладки локального канала в глобальном окне.
    [Test]
    public void GlobalChatWindow_DoesNotSelectOrSendLocalChannel()
    {
        string uxml = ReadAsset("Resources/UI/Chat/Global/Templates/GlobalChat.uxml");
        string controller = ReadAsset("Scripts/UI/Chat/Window/Presentation/GlobalChatUI.cs");
        string floatingChat = ReadAsset("Scripts/UI/Chat/Floating/Presentation/FloatingChatManager.cs");

        Assert.That(uxml, Does.Not.Contain("LocalChannelButton"));
        Assert.That(uxml, Does.Not.Contain("chat.channel.local"));
        Assert.That(controller, Does.Not.Contain("SendLocalChatMessagePacket"));
        Assert.That(floatingChat, Does.Contain("LocalMessageReceived += ShowLocalChat"));
    }

    // Локальный чат отправляется из своего поля ввода, а не из окна глобального.
    [Test]
    public void LocalChatInput_SendsLocalChatPacket()
    {
        string localInput = ReadAsset("Scripts/UI/Chat/Input/LocalChatInput.cs");
        string floatingChat = ReadAsset("Scripts/UI/Chat/Floating/Presentation/FloatingChatManager.cs");

        Assert.That(localInput, Does.Contain("new SendLocalChatMessagePacket(text)"));
        Assert.That(floatingChat, Does.Contain("new LocalChatInput("));
    }

    // T принадлежит локальному чату: глобальное окно открывает кнопка HUD.
    [Test]
    public void TKey_BelongsToLocalChatOnly()
    {
        string globalController = ReadAsset("Scripts/UI/Chat/Window/Presentation/GlobalChatUI.cs");
        string localInput = ReadAsset("Scripts/UI/Chat/Input/LocalChatInput.cs");

        Assert.That(globalController, Does.Not.Contain("tKey"));
        Assert.That(localInput, Does.Contain("tKey.wasPressedThisFrame"));
    }

    // Сервер отвечает ChatMessageListPacket с тегом из World.CheckGlobalChats,
    // а GlobalChatUI отбрасывает пакеты с любым другим тегом. Расхождение
    // оставляет окно глобального чата пустым при полностью рабочей сети.
    [Test]
    public void GlobalChannelTag_MatchesServerSeededChannel()
    {
        string globalController = ReadAsset("Scripts/UI/Chat/Window/Presentation/GlobalChatUI.cs");
        string globalChannels = ReadAsset("Scripts/UI/Chat/Window/Channels/GlobalChatChannelPresenter.cs");

        Assert.That(
            ChatProtocolLimits.GlobalChannelTag,
            Is.EqualTo("FED"),
            "Server seeds its only global channel with Tag = \"FED\" (World.CheckGlobalChats).");
        Assert.That(globalChannels, Does.Contain("ChatProtocolLimits.GlobalChannelTag"));
        Assert.That(globalController, Does.Not.Contain("\"global\""));
    }

    [Test]
    public void LegacyLocalChatPopup_DoesNotReturn()
    {
        string legacyController = Path.Combine(
            Application.dataPath,
            "Scripts/UI/Chat/LocalChatPopup.cs");

        Assert.That(File.Exists(legacyController), Is.False);
    }

    [Test]
    public void LocalChat_UxmlAndStylesArePresent()
    {
        string uxml = ReadAsset("Resources/UI/Chat/Local/Templates/LocalChat.uxml");
        string uss = ReadAsset("Resources/UI/Chat/Global/Styles/Chat.uss");

        Assert.That(uxml, Does.Contain("LocalChatPanel"));
        Assert.That(uxml, Does.Contain("LocalChatField"));
        Assert.That(uss, Does.Contain(".lchat-panel"));
        Assert.That(uss, Does.Contain(".lchat-input"));
    }
}
