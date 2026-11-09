using Kern.Networking;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using NUnit.Framework;

namespace Kern.PacketLayoutTests;

public sealed class ClickableElementOrderTests
{
    [Test]
    public void NestedContainersUseClickablePreorder()
    {
        var first = new TextPacket { Text = "First", OnClickContext = "/" };
        var last = new TextPacket { Text = "Last", OnClickContext = "/" };
        var nested = new DockPanelPacket
        {
            OnClickContext = "/",
            Children = new IGUIComponentPacket[] { new TextPacket { Text = "Caption" }, first },
        };
        var root = new DockPanelPacket { Children = new IGUIComponentPacket[] { nested, last } };

        Assert.That(ClickableElementOrder.Collect(root), Is.EqualTo(new IGUIComponentPacket[] { nested, first, last }));
        Assert.That(ClickableElementOrder.IndexOf(root, first), Is.EqualTo(1));
        Assert.That(ClickableElementOrder.IndexOf(root, last), Is.EqualTo(2));
    }

    [Test]
    public void EqualButtonsRetainDistinctIndices()
    {
        var first = new TextPacket { Text = "Same", OnClickContext = "/" };
        var second = new TextPacket { Text = "Same", OnClickContext = "/" };
        var root = new DockPanelPacket { Children = new IGUIComponentPacket[] { first, second } };

        Assert.That(ClickableElementOrder.IndexOf(root, first), Is.Zero);
        Assert.That(ClickableElementOrder.IndexOf(root, second), Is.EqualTo(1));
        Assert.That(ClickableElementOrder.IndexOf(root, new TextPacket { Text = "Same", OnClickContext = "/" }), Is.EqualTo(-1));
    }

    [Test]
    public void NonClickableComponentHasNoIndex()
    {
        var caption = new TextPacket { Text = "Caption" };
        var root = new DockPanelPacket { Children = new IGUIComponentPacket[] { caption } };

        Assert.That(ClickableElementOrder.Collect(root), Is.Empty);
        Assert.That(ClickableElementOrder.IndexOf(root, caption), Is.EqualTo(-1));
    }
}
