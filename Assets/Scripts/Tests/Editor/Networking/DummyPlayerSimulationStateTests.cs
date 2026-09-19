#nullable enable

using MinesServer.Data;
using MinesServer.Networking.Connection.Client;
using NUnit.Framework;

namespace Kern.Tests.Networking;

public sealed class DummyPlayerSimulationStateTests
{
    [Test]
    public void Respawn_ResetsPositionDirectionAndHealth()
    {
        var state = new DummyPlayerSimulationState();
        state.SetPosition(80, 90);
        state.SetDirection(Direction.Left);
        state.SetHealth(12);

        state.Respawn(25, 50);

        Assert.That(state.X, Is.EqualTo(25));
        Assert.That(state.Y, Is.EqualTo(50));
        Assert.That(state.Direction, Is.EqualTo(Direction.Up));
        Assert.That(state.Health, Is.EqualTo(500));
    }

    [Test]
    public void Toggles_ReturnTheNewAuthoritativeState()
    {
        var state = new DummyPlayerSimulationState();

        Assert.That(state.ToggleAutoDig(), Is.True);
        Assert.That(state.ToggleAutoDig(), Is.False);
        Assert.That(state.ToggleAggression(), Is.True);
        Assert.That(state.AutoDig, Is.False);
        Assert.That(state.Aggression, Is.True);
    }

    [Test]
    public void BasketUpdate_UsesCopyOnWriteAndRejectsInvalidSlot()
    {
        var state = new DummyPlayerSimulationState();
        long[] initial = state.ResetBasket(2);

        long[]? updated = state.AddToBasket(1, 7);

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated![1], Is.EqualTo(7));
        Assert.That(initial[1], Is.Zero);
        Assert.That(state.AddToBasket(2, 1), Is.Null);
    }

    [Test]
    public void GeologyStack_PreservesLastInFirstOutOrder()
    {
        var state = new DummyPlayerSimulationState();
        state.PushGeology(CellType.Road);
        state.PushGeology(CellType.RedBlock);

        Assert.That(state.TryPopGeology(out CellType first), Is.True);
        Assert.That(first, Is.EqualTo(CellType.RedBlock));
        Assert.That(state.TryPopGeology(out CellType second), Is.True);
        Assert.That(second, Is.EqualTo(CellType.Road));
        Assert.That(state.TryPopGeology(out _), Is.False);
    }
}
