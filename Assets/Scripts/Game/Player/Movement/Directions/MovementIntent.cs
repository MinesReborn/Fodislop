#nullable enable

using UnityEngine;

namespace Kern.Game.Player.Movement;

// Намерение движения на этот кадр в осях Unity (y вверх): одна ось для
// прямого хода или две для диагонали (псевдо-8 направлений). Сервер
// принимает только шаг в соседнюю по стороне клетку, поэтому диагональ —
// это чередование шагов по Primary и Secondary.
// Primary — ось с большим отклонением ввода; при равенстве (клавиатура,
// W+A) — вертикальная, как у прежнего одноосного сведения.
public readonly struct MovementIntent
{
    public static readonly MovementIntent None = default;

    public readonly Vector2Int Primary;
    public readonly Vector2Int Secondary;

    public MovementIntent(Vector2Int primary, Vector2Int secondary)
    {
        Primary = primary;
        Secondary = secondary;
    }

    public bool IsNone => Primary == Vector2Int.zero;

    public bool IsDiagonal => Secondary != Vector2Int.zero;
}
