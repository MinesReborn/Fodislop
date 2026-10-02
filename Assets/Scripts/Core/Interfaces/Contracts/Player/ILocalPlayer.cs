#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Core.Interfaces;
public interface ILocalPlayer
{
    GameObject gameObject { get; }

    Transform transform { get; }

    bool isActiveAndEnabled { get; }

    uint BotId { get; }

    Vector2Int Position { get; }

    bool HasServerPosition { get; }

    bool IsGameplayVisible { get; }

    Direction LastDirection { get; }

    bool IgnoreCollision { get; set; }

    bool AutoDig { get; set; }

    bool Aggression { get; set; }

    event Action<Vector2Int, Vector2Int>? OnPlayerMoved;

    // Мгновенное перемещение по решению сервера (респаун, ТП): камера должна
    // щёлкнуть на место, а не догонять сглаживанием.
    event Action? OnPlayerTeleported;

    event Action<bool>? OnAutoDigChanged;

    event Action<bool>? OnAggressionChanged;

    void UpdateServerPosition(Vector2Int position, bool teleport = false);

    void ResetServerPosition();

    void ConfirmDigAction(ushort x, ushort y);

    // Направление, в котором клиент сам копал эту клетку последним.
    bool TryGetDigDirection(ushort x, ushort y, out Direction direction);

    void ResetDirection();

    void Initialize(uint botId);

    void SetGameplayVisible();

    T GetComponent<T>();

    bool TryGetComponent<T>(out T component);
}
