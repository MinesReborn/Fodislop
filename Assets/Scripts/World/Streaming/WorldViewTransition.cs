#nullable enable

using UnityEngine;

namespace Kern.World.Streaming;

/// <summary>
/// Shared state for explicitly held world publications and destination readiness.
/// CameraFollow teleports immediately outside its reference viewport and releases
/// any held publication; it does not wait for this readiness state.
/// </summary>
public sealed class WorldViewTransition
{
    private RectInt _readyCells;
    private bool _hasReadyCells;

    /// <summary>Камера стоит, пока место назначения готовится.</summary>
    public bool IsHolding { get; private set; }

    /// <summary>Куда камера встанет после перехода, в мировых координатах.</summary>
    public Vector3 Destination { get; private set; }

    /// <summary>
    /// Есть ли что удерживать: опубликованный вид мира. До первой публикации
    /// экран закрыт загрузкой мира, и камера переставляется сразу.
    /// </summary>
    public bool CanHold { get; set; }

    public void Hold(Vector3 destination)
    {
        if (!IsHolding)
        {
            IsHolding = true;
            _hasReadyCells = false;
        }

        Destination = destination;
    }

    /// <summary>
    /// Террейн: клетки мира, которые будут на экране сразу после перехода,
    /// собраны, текстуры их типов на месте, и шаг ждёт публикации.
    /// </summary>
    public void MarkReady(RectInt readyCells)
    {
        _readyCells = readyCells;
        _hasReadyCells = true;
    }

    public void ClearReady() => _hasReadyCells = false;

    /// <summary>
    /// Готово ли место назначения для кадра камеры с центром в
    /// <paramref name="position"/> и полуразмерами кадра в клетках.
    /// </summary>
    public bool IsReadyFor(Vector3 position, float halfWidthCells, float halfHeightCells, float cellSize)
    {
        if (!_hasReadyCells)
        {
            return false;
        }

        int minX = Mathf.FloorToInt((position.x / cellSize) - halfWidthCells);
        int minY = Mathf.FloorToInt((position.y / cellSize) - halfHeightCells);
        int maxX = Mathf.CeilToInt((position.x / cellSize) + halfWidthCells);
        int maxY = Mathf.CeilToInt((position.y / cellSize) + halfHeightCells);
        return minX >= _readyCells.xMin && minY >= _readyCells.yMin &&
            maxX <= _readyCells.xMax && maxY <= _readyCells.yMax;
    }

    public void Release()
    {
        IsHolding = false;
        _hasReadyCells = false;
    }
}
