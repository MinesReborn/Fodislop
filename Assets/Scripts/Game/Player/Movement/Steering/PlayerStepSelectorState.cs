#nullable enable

using UnityEngine;

namespace Kern.Game.Player.Movement;

// Память выбора шага между кадрами ручного движения. Только данные:
// правила живут в PlayerStepSelector, обход — в ObstacleDetourPlanner.
// Все направления — в осях Unity (y вверх), как у MovementIntent.
public sealed class PlayerStepSelectorState
{
    // Удерживаемое намерение и момент, с которого оно удерживается.
    public MovementIntent Intent;
    public float IntentSince;

    // Последний отправленный шаг: диагональ чередует оси относительно него.
    public Vector2Int LastStep;

    // Шагов, сделанных в текущем намерении. Обход включается только когда
    // робот уже идёт, а не от короткого нажатия в сторону породы.
    public int StepsInIntent;

    // Первый шаг обхода на прошлом расчёте: при равной цене сторона
    // не переключается, робот не мечется влево-вправо.
    public Vector2Int LastDetourStep;

    // Результат прошлого расчёта и всё, от чего он зависит. Выбор вызывается
    // каждый кадр, а меняется только после шага, правки карты или настроек.
    public bool HasCachedStep;
    public Vector2Int CachedPosition;
    public long CachedRevision;
    public int CachedFlags;
    public Vector2Int CachedLastStep;
    public Vector2Int CachedStep;
}
