#nullable enable

using Kern.Networking;
using MinesServer.Networking.Server.Packets.GUI;
using NUnit.Framework;

namespace Kern.Tests.Networking;

[TestFixture]
public sealed class WindowCommandStreamFuzzTests
{
    [Test]
    public void RandomVisibilitySequence_EmitsOnlyStateTransitions()
    {
        var random = new System.Random(42);
        var stream = new WindowCommandStream();
        int notifications = 0;
        bool expected = false;
        stream.OpenWindowVisibilityChanged += _ => notifications++;

        for (int step = 0; step < 10_000; step++)
        {
            bool next = random.Next(2) == 0;
            stream.SetServerWindowVisibility(next);
            expected = next;
            Assert.That(stream.HasOpenWindows, Is.EqualTo(expected), $"step={step}");
        }

        random = new System.Random(42);
        expected = false;
        int expectedNotifications = 0;
        for (int step = 0; step < 10_000; step++)
        {
            bool next = random.Next(2) == 0;
            if (next != expected)
            {
                expectedNotifications++;
            }

            expected = next;
        }

        Assert.That(notifications, Is.EqualTo(expectedNotifications));
    }

    [Test]
    public void RandomCommands_ReachOnlyTheirTypedSubscribers()
    {
        var random = new System.Random(43);
        var stream = new WindowCommandStream();
        int opens = 0;
        int closes = 0;
        int modals = 0;
        stream.OpenRequested += _ => opens++;
        stream.CloseRequested += _ => closes++;
        stream.ModalRequested += _ => modals++;

        int expectedOpens = 0;
        int expectedCloses = 0;
        int expectedModals = 0;
        for (int step = 0; step < 10_000; step++)
        {
            switch (random.Next(3))
            {
                case 0:
                    stream.PublishOpenWindow(new OpenWindowPacket("window", 100, 100, null));
                    expectedOpens++;
                    break;
                case 1:
                    stream.PublishCloseWindow(new CloseWindowPacket());
                    expectedCloses++;
                    break;
                default:
                    stream.PublishModalWindow(new ModalWindowPacket("title", "body", "ok", ""));
                    expectedModals++;
                    break;
            }
        }

        Assert.That(opens, Is.EqualTo(expectedOpens));
        Assert.That(closes, Is.EqualTo(expectedCloses));
        Assert.That(modals, Is.EqualTo(expectedModals));
    }

    [Test]
    public void Modal_DoesNotChangeServerVisibility()
    {
        var stream = new WindowCommandStream();
        stream.SetServerWindowVisibility(true);
        stream.PublishModalWindow(new ModalWindowPacket("title", "body", "ok", ""));

        Assert.That(stream.HasOpenWindows, Is.True);
    }

    [Test]
    public void RepeatingVisibility_DoesNotNotifyAgain()
    {
        var stream = new WindowCommandStream();
        int notifications = 0;
        stream.OpenWindowVisibilityChanged += _ => notifications++;

        stream.SetServerWindowVisibility(true);
        stream.SetServerWindowVisibility(true);
        stream.SetServerWindowVisibility(false);
        stream.SetServerWindowVisibility(false);

        Assert.That(notifications, Is.EqualTo(2));
    }
}
