#nullable enable

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class LocalChatContractTests
{
    [Test]
    public void GlobalChatTab_DoesNotSelectOrSendLocalChannel()
    {
        string uxml = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Resources/UI/Gameplay/GlobalChat.uxml"));
        string controller = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Scripts/UI/Chat/GlobalChatUI.cs"));
        string floatingChat = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Scripts/UI/Chat/Floating/FloatingChatManager.cs"));

        Assert.That(uxml, Does.Not.Contain("LocalChannelButton"));
        Assert.That(uxml, Does.Not.Contain("chat.channel.local"));
        Assert.That(controller, Does.Not.Contain("SendLocalChatMessagePacket"));
        Assert.That(controller, Does.Contain("new SendChatMessagePacket(\"global\", text)"));
        Assert.That(floatingChat, Does.Contain("LocalMessageReceived += ShowLocalChat"));
    }

    [Test]
    public void DisconnectedLegacyLocalChatPopup_DoesNotReturn()
    {
        string legacyController = Path.Combine(
            Application.dataPath,
            "Scripts/UI/Chat/LocalChatPopup.cs");
        string legacyUxml = Path.Combine(
            Application.dataPath,
            "Resources/UI/LocalChat.uxml");

        Assert.That(File.Exists(legacyController), Is.False);
        Assert.That(File.Exists(legacyUxml), Is.False);
    }
}
