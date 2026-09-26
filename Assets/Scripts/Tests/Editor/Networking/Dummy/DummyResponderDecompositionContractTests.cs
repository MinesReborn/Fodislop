#nullable enable

using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Networking;

public sealed class DummyResponderDecompositionContractTests
{
    [Test]
    public void Connection_DelegatesWindowTagsInsteadOfOwningTheirRouting()
    {
        string connection = ReadClientSource("DummyConnection.cs");
        string responder = ReadClientSource("Responders/DummyWindowResponder.cs");

        Assert.That(connection, Does.Contain("_windowResponder.Handle("));
        Assert.That(connection, Does.Contain("elementClick"));
        Assert.That(connection, Does.Not.Contain("packet.WindowTag == \"daily_bonus\""));
        Assert.That(connection, Does.Not.Contain("packet.WindowTag == \"teleport\""));
        Assert.That(connection, Does.Not.Contain("packet.WindowTag == \"missions\""));
        Assert.That(responder, Does.Contain("case \"daily_bonus\":"));
        Assert.That(responder, Does.Contain("case \"teleport\":"));
        Assert.That(responder, Does.Contain("case \"missions\":"));
    }

    [Test]
    public void Connection_DelegatesChatAndInventoryState()
    {
        string connection = ReadClientSource("DummyConnection.cs");

        Assert.That(connection, Does.Not.Contain("_chatColor"));
        Assert.That(connection, Does.Not.Contain("_selectedItemType"));
        Assert.That(connection, Does.Contain("_chatResponder.SendGlobal(globalMsg)"));
        Assert.That(connection, Does.Contain("_inventoryResponder.Use("));
        Assert.That(connection, Does.Contain("_playerState.Direction"));
    }

    [Test]
    public void Connection_DelegatesWorldStartupSnapshotAndLoops()
    {
        string connection = ReadClientSource("DummyConnection.cs");
        string responder = ReadClientSource("Responders/DummyWorldStartupResponder.cs");

        Assert.That(connection, Does.Contain("_worldStartup.InitializeAsync("));
        Assert.That(connection, Does.Not.Contain("new WorldInitPacket("));
        Assert.That(connection, Does.Not.Contain("dummy_ping_loop"));
        Assert.That(responder, Does.Contain("new WorldInitPacket("));
        Assert.That(responder, Does.Contain("dummy_ping_loop"));
        Assert.That(responder, Does.Contain("dummy_online_loop"));
    }

    [Test]
    public void Connection_DelegatesMovementAndPathLifecycle()
    {
        string connection = ReadClientSource("DummyConnection.cs");
        string responder = ReadClientSource("Responders/DummyMovementResponder.cs");
        string worldState = ReadClientSource("Simulation/DummyWorldSimulationState.cs");

        Assert.That(connection, Does.Contain("_actionResponder.Handle(actionPacket)"));
        Assert.That(connection, Does.Not.Contain("dummy_walk_path"));
        Assert.That(responder, Does.Contain("dummy_walk_path"));
        Assert.That(responder, Does.Contain("public void CancelPath()"));
        Assert.That(responder, Does.Not.Contain("GetCellSync"));
        Assert.That(worldState, Does.Not.Contain("GetCellSync"));
        Assert.That(worldState, Does.Contain("EnsureCellAvailableAsync"));
    }

    [Test]
    public void Connection_DelegatesGameplayActionsAndAssetResponses()
    {
        string connection = ReadClientSource("DummyConnection.cs");
        string actions = ReadClientSource("Responders/DummyGameplayActionResponder.cs");

        Assert.That(connection, Does.Contain("_actionResponder.Handle(actionPacket)"));
        Assert.That(connection, Does.Not.Contain("actionPacket.Payload is BzPacket"));
        Assert.That(connection, Does.Not.Contain("class DummyAssetHandler"));
        Assert.That(actions, Does.Contain("case BzPacket:"));
        Assert.That(actions, Does.Contain("case SuicidePacket:"));
        Assert.That(actions, Does.Contain("case GeoPacket:"));
    }

    private static string ReadClientSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(
            Application.dataPath,
            "Scripts/Networking/Connection/Client",
            fileName));
    }
}
