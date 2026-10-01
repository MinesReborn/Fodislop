#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.Core.Interfaces;

// Клик-маршрут: ЛКМ по миру строит путь до клетки и ведёт робота сам.
// Реализуется PlayerMovementController; рендер пунктирной линии и прочие
// слушатели подписываются на OnPathChanged.
public interface IClickPathWalker
{
    bool IsPathActive { get; }

    // Остаток маршрута в серверных координатах: от следующего шага до цели
    // включительно. null - маршрут не активен (завершён или снят).
    IReadOnlyList<Vector2Int>? Path { get; }

    // Индекс следующей клетки маршрута в Path (сколько шагов уже пройдено).
    int PathIndex { get; }

    event Action<IReadOnlyList<Vector2Int>?>? OnPathChanged;

    // Строит маршрут от текущей клетки робота до target (серверные координаты)
    // и включает авто-движение по нему. false - путь не найден, цель совпадает
    // с текущей клеткой или карта ещё не готова.
    bool TryStartPath(Vector2Int target);

    // Снимает активный маршрут (без перезапуска автоматом).
    void CancelPath();
}
