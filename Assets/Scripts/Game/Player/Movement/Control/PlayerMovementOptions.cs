#nullable enable

using System;
using Kern.Core.Interfaces;

namespace Kern.Game.Player.Movement;

internal sealed class PlayerMovementOptions
{
    private IRuntimeDebugSettings _debugSettings = null!;

    private bool _autoDig;

    public bool AutoDig
    {
        get => _autoDig;
        set
        {
            _autoDig = value;
            AutoDigChanged?.Invoke(value);
        }
    }

    public bool IgnoreCollision
    {
        get => _debugSettings.IgnoreCollision;
        set
        {
            _debugSettings.IgnoreCollision = value;
            CollisionChanged?.Invoke(value);
        }
    }

    public event Action<bool>? AutoDigChanged;
    public event Action<bool>? CollisionChanged;

    public void BindDebugSettings(IRuntimeDebugSettings debugSettings)
    {
        _debugSettings = debugSettings;
    }
}
