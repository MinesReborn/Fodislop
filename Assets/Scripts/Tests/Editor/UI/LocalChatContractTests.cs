#nullable enable

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

[TestFixture]
public sealed class LocalChatContractTests
{
    [Test]
    public void ActiveChatUi_ContainsLocalChannelAndNetworkRoute()
    {
        string uxml = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Resources/UI/GlobalChat.uxml"));
        string controller = File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Scripts/UI/Chat/GlobalChatUI.cs"));

        Assert.That(uxml, Does.Contain("name=\"LocalChannelButton\""));
        Assert.That(uxml, Does.Contain("text=\"chat.channel.local\""));
        Assert.That(controller, Does.Contain("new SendLocalChatMessagePacket(text)"));
        Assert.That(controller, Does.Contain("LocalMessageReceived += AddLocalMessage"));
        Assert.That(controller, Does.Contain("Keyboard.current.tKey.wasPressedThisFrame"));
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
