#nullable enable

using Fodinae.Networking;
using MinesServer.Networking.Server.Packets.GUI;
using NUnit.Framework;

namespace Fodinae.Tests.Networking
{
    /// <summary>
    /// WindowCommandStream is the single boundary between packet processors
    /// and window presentation: the processor publishes commands, the
    /// presenter subscribes. These tests pin the pub/sub wiring so a
    /// regression cannot silently disconnect server windows from the UI.
    /// </summary>
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
            stream.PublishOpen(packet);

            Assert.AreEqual(packet, received);
        }

        [Test]
        public void PublishClose_RaisesCloseRequested()
        {
            var stream = new WindowCommandStream();
            int closeEvents = 0;
            stream.CloseRequested += _ => closeEvents++;

            stream.PublishClose(new CloseWindowPacket());

            Assert.AreEqual(1, closeEvents);
        }

        [Test]
        public void PublishModal_RaisesModalRequested()
        {
            var stream = new WindowCommandStream();
            ModalWindowPacket? received = null;
            stream.ModalRequested += packet => received = packet;

            var packet = new ModalWindowPacket("title", "body", "OK", "");
            stream.PublishModal(packet);

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

            stream.PublishClose(new CloseWindowPacket());

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

            stream.SetOpenWindowVisibility(true);
            stream.SetOpenWindowVisibility(true);

            Assert.That(stream.HasOpenWindows, Is.True);
            Assert.That(observed, Is.True);
            Assert.That(calls, Is.EqualTo(1));

            stream.SetOpenWindowVisibility(false);

            Assert.That(stream.HasOpenWindows, Is.False);
            Assert.That(observed, Is.False);
            Assert.That(calls, Is.EqualTo(2));
        }
    }
}
