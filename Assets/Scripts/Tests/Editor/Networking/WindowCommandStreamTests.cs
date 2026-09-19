#nullable enable

using Kern.Networking;
using MinesServer.Networking.Server.Packets.GUI;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public class WindowCommandStreamTests
{
    [Test]
    public void PublishOpen_RaisesOpenRequested()
    {
        var stream = new WindowCommandStream();
        OpenWindowPacket? received = null;
        stream.OpenRequested += packet => received = packet;

        var packet = new OpenWindowPacket("shop", 300, 200, null!);
        stream.PublishOpenWindow(packet);

        Assert.AreEqual(packet, received);
    }

    [Test]
    public void PublishClose_RaisesCloseRequested()
    {
        var stream = new WindowCommandStream();
        int closeEvents = 0;
        stream.CloseRequested += _ => closeEvents++;

        stream.PublishCloseWindow(new CloseWindowPacket());

        Assert.AreEqual(1, closeEvents);
    }

    [Test]
    public void PublishModal_RaisesModalRequested()
    {
        var stream = new WindowCommandStream();
        ModalWindowPacket? received = null;
        stream.ModalRequested += packet => received = packet;

        var packet = new ModalWindowPacket("title", "body", "OK", "");
        stream.PublishModalWindow(packet);

        Assert.AreEqual(packet, received);
    }

    [Test]
    public void Unsubscribe_PreventsDisposedPresenterListenerFromReceivingCommands()
    {
        var stream = new WindowCommandStream();
        int calls = 0;
        void Handler(CloseWindowPacket _) => calls++;
        stream.CloseRequested += Handler;
        stream.CloseRequested -= Handler;

        stream.PublishCloseWindow(new CloseWindowPacket());

        Assert.AreEqual(0, calls);
    }

    [Test]
    public void SetOpenWindowVisibility_PublishesOnlyStateChanges()
    {
        var stream = new WindowCommandStream();
        int calls = 0;
        bool observed = false;
        stream.OpenWindowVisibilityChanged += visible =>
        {
            calls++;
            observed = visible;
        };

        stream.SetServerWindowVisibility(true);
        stream.SetServerWindowVisibility(true);

        Assert.That(stream.HasOpenWindows, Is.True);
        Assert.That(observed, Is.True);
        Assert.That(calls, Is.EqualTo(1));

        stream.SetServerWindowVisibility(false);

        Assert.That(stream.HasOpenWindows, Is.False);
        Assert.That(observed, Is.False);
        Assert.That(calls, Is.EqualTo(2));
    }
}
