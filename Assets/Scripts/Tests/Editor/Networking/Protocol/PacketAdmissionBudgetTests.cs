#nullable enable

using System;
using Kern.Networking.Connection;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class PacketAdmissionBudgetTests
{
    [Test]
    public void RejectsByPacketCountAndRestoresCapacityAfterRelease()
    {
        var budget = new PacketAdmissionBudget(2, 100);

        Assert.That(budget.TryReserve(10), Is.True);
        Assert.That(budget.TryReserve(20), Is.True);
        Assert.That(budget.TryReserve(1), Is.False);
        Assert.That(budget.PacketCount, Is.EqualTo(2));
        Assert.That(budget.PacketBytes, Is.EqualTo(30));

        budget.Release(10);

        Assert.That(budget.TryReserve(40), Is.True);
        Assert.That(budget.PacketCount, Is.EqualTo(2));
        Assert.That(budget.PacketBytes, Is.EqualTo(60));
    }

    [Test]
    public void RejectsPacketThatWouldExceedByteBudgetEvenWhenCountIsAvailable()
    {
        var budget = new PacketAdmissionBudget(10, 64);

        Assert.That(budget.TryReserve(40), Is.True);
        Assert.That(budget.TryReserve(25), Is.False);
        Assert.That(budget.PacketCount, Is.EqualTo(1));
        Assert.That(budget.PacketBytes, Is.EqualTo(40));

        budget.Release(40);
        Assert.That(budget.TryReserve(64), Is.True);
    }

    [Test]
    public void NormalizesZeroSizePacketsAndRejectsMismatchedRelease()
    {
        var budget = new PacketAdmissionBudget(1, 1);

        Assert.That(budget.TryReserve(0), Is.True);
        Assert.That(budget.PacketBytes, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => budget.Release(2));

        budget.Clear();
        Assert.That(budget.PacketCount, Is.Zero);
        Assert.That(budget.PacketBytes, Is.Zero);
    }

    // Пакет больше всего байтового лимита обязан войти в пустую очередь:
    // иначе поток чтения, ждущий под давлением, не дождался бы никогда.
    [Test]
    public void OversizedPacketEntersAnEmptyQueueButNotAnOccupiedOne()
    {
        var budget = new PacketAdmissionBudget(4, 64);

        Assert.That(budget.TryReserve(1000), Is.True);
        Assert.That(budget.TryReserve(1), Is.False);

        budget.Release(1000);
        Assert.That(budget.TryReserve(1), Is.True);
    }

    [Test]
    public void ForcedReserveExceedsLimitsAndIsReleasedNormally()
    {
        var budget = new PacketAdmissionBudget(1, 8);

        Assert.That(budget.TryReserve(8), Is.True);
        budget.Reserve(8);
        Assert.That(budget.PacketCount, Is.EqualTo(2));
        Assert.That(budget.PacketBytes, Is.EqualTo(16));

        budget.Release(8);
        budget.Release(8);
        Assert.That(budget.PacketCount, Is.Zero);
    }
}
