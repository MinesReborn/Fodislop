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

    uint BotID { get; }

    Vector2Int Position { get; }

    bool HasServerPosition { get; }

    bool IsGameplayVisible { get; }

    Direction LastDirection { get; }

    bool IgnoreCollision { get; set; }

    bool AutoDig { get; set; }

    bool Aggression { get; set; }

    event Action<Vector2Int, Vector2Int>? OnPlayerMoved;

    event Action<bool>? OnAutoDigChanged;

    event Action<bool>? OnAggressionChanged;

    void UpdateServerPosition(Vector2Int position);

    void ResetDirection();

    void Initialize(uint botID);

    void SetGameplayVisible();

    void ToggleAggression();

    T GetComponent<T>();

    bool TryGetComponent<T>(out T component);
}
