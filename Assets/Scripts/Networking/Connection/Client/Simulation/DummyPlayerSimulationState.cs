#nullable enable

using System;
using System.Collections.Generic;
using MinesServer.Data;

namespace MinesServer.Networking.Connection.Client;

internal sealed class DummyPlayerSimulationState
{
    private const int MaximumHealth = 500;
    private long[] _basketContents = new long[6];
    private readonly Stack<CellType> _geologyStack = new();

    public ushort X { get; private set; }

    public ushort Y { get; private set; }

    public Direction Direction { get; private set; } = Direction.Up;

    public int Health { get; private set; } = MaximumHealth;

    public bool Aggression { get; private set; }

    public bool AutoDig { get; private set; }

    public int GeologyCount => _geologyStack.Count;

    public void SetPosition(ushort x, ushort y)
    {
        X = x;
        Y = y;
    }

    public void SetDirection(Direction direction)
    {
        Direction = direction;
    }

    public void Respawn(ushort x, ushort y)
    {
        SetPosition(x, y);
        Direction = Direction.Up;
        Health = MaximumHealth;
    }

    public bool ToggleAggression()
    {
        Aggression = !Aggression;
        return Aggression;
    }

    public bool ToggleAutoDig()
    {
        AutoDig = !AutoDig;
        return AutoDig;
    }

    public void SetHealth(int health)
    {
        Health = Math.Clamp(health, 0, MaximumHealth);
    }

    public int Heal(int amount)
    {
        SetHealth(Health + Math.Max(0, amount));
        return Health;
    }

    public long[] ResetBasket(int slots = 6)
    {
        if (slots < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slots));
        }

        _basketContents = new long[slots];
        return _basketContents;
    }

    public long[]? AddToBasket(int index, long amount)
    {
        if (index < 0 || index >= _basketContents.Length)
        {
            return null;
        }

        var updated = new long[_basketContents.Length];
        Array.Copy(_basketContents, updated, updated.Length);
        updated[index] += amount;
        _basketContents = updated;
        return updated;
    }

    public void PushGeology(CellType cellType)
    {
        _geologyStack.Push(cellType);
    }

    public bool TryPopGeology(out CellType cellType)
    {
        return _geologyStack.TryPop(out cellType);
    }
}
