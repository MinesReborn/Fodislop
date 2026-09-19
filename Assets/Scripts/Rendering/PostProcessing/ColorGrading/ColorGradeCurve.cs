#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public enum ColorCurveInterpolation
{
    Linear = 0,
    Smooth = 1,
}

// Вид кривой определяет, что считается «ничего не менять».
//
// Tone — кривая тона (master, R, G, B): вход по горизонтали, выход по
// вертикали, нейтраль — диагональ.
//
// Hue и Range — кривые вида «X против Y», как в профессиональных программах
// цветокоррекции: нейтраль — ровная линия на 0.5, выше — больше, ниже —
// меньше. Значение кривой — не абсолютный результат, а сдвиг или множитель,
// поэтому тёмные и серые пиксели не выбрасываются в заданную яркость или
// насыщенность. У Hue горизонталь — круг оттенков: 0° и 360° — один цвет,
// и крайние точки держат одно значение.
public enum ColorGradeCurveKind
{
    Tone = 0,
    Hue = 1,
    Range = 2,
}

public sealed class ColorGradeCurve
{
    public const int MaxPoints = 16;
    public const float NeutralLevel = 0.5f;

    private readonly Vector2[] _points = new Vector2[MaxPoints];

    public ColorGradeCurve(ColorGradeCurveKind kind = ColorGradeCurveKind.Tone)
    {
        // Проверка диапазоном, а не Enum.IsDefined: тот упаковывает значение
        // и ходит в reflection, а кривая создаётся при каждом клонировании грейда.
        Kind = kind is >= ColorGradeCurveKind.Tone and <= ColorGradeCurveKind.Range
            ? kind
            : ColorGradeCurveKind.Tone;
        Reset();
    }

    public ColorGradeCurveKind Kind { get; }

    public int PointCount { get; private set; }

    public ColorCurveInterpolation Interpolation { get; set; }

    public IReadOnlyList<Vector2> Points => _points;

    public bool IsNeutral
    {
        get
        {
            if (Kind == ColorGradeCurveKind.Tone)
            {
                return PointCount == 2 &&
                    _points[0] == Vector2.zero &&
                    _points[1] == Vector2.one;
            }

            for (int index = 0; index < PointCount; index++)
            {
                if (Mathf.Abs(_points[index].y - NeutralLevel) > 1e-5f)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Без выделения: проход пишет в свой буфер каждый кадр.
    public void WriteShaderPoints(Vector4[] destination)
    {
        if (destination == null || destination.Length < MaxPoints)
        {
            throw new ArgumentException($"Destination must hold {MaxPoints} points.", nameof(destination));
        }

        for (int index = 0; index < MaxPoints; index++)
        {
            destination[index] = new Vector4(_points[index].x, _points[index].y, 0f, 0f);
        }
    }

    public Vector2 GetPoint(int index)
    {
        if (index < 0 || index >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _points[index];
    }

    public void Reset()
    {
        PointCount = 2;
        if (Kind == ColorGradeCurveKind.Tone)
        {
            _points[0] = Vector2.zero;
            _points[1] = Vector2.one;
        }
        else
        {
            _points[0] = new Vector2(0f, NeutralLevel);
            _points[1] = new Vector2(1f, NeutralLevel);
        }

        Interpolation = ColorCurveInterpolation.Smooth;
        ClearUnusedPoints();
    }

    public int AddPoint(Vector2 point)
    {
        if (PointCount >= MaxPoints)
        {
            return -1;
        }

        point = SanitizePoint(point);
        int index = PointCount;
        while (index > 1 && _points[index - 1].x > point.x)
        {
            _points[index] = _points[index - 1];
            index--;
        }

        _points[index] = point;
        PointCount++;
        Sanitize();
        return index;
    }

    public bool RemovePoint(int index)
    {
        if (index <= 0 || index >= PointCount - 1)
        {
            return false;
        }

        for (int pointIndex = index; pointIndex < PointCount - 1; pointIndex++)
        {
            _points[pointIndex] = _points[pointIndex + 1];
        }

        PointCount--;
        ClearUnusedPoints();
        return true;
    }

    public void SetPoint(int index, Vector2 point)
    {
        if (index < 0 || index >= PointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        point = SanitizePoint(point);
        _points[index] = point;

        // Круг оттенков: край, который двигали, ведёт за собой второй край.
        if (Kind == ColorGradeCurveKind.Hue)
        {
            int last = PointCount - 1;
            if (index == 0)
            {
                _points[last] = new Vector2(_points[last].x, point.y);
            }
            else if (index == last)
            {
                _points[0] = new Vector2(_points[0].x, point.y);
            }
        }

        Sanitize();
    }

    public void Load(Vector2[]? points, int interpolation)
    {
        Reset();
        if (points != null)
        {
            PointCount = Mathf.Clamp(points.Length, 2, MaxPoints);
            for (int index = 0; index < PointCount; index++)
            {
                _points[index] = points[index];
            }
        }

        Interpolation = (ColorCurveInterpolation)interpolation;
        Sanitize();
    }

    public float Evaluate(float x)
    {
        x = float.IsFinite(x) ? Mathf.Clamp01(x) : 0f;
        if (Kind == ColorGradeCurveKind.Tone && IsNeutral)
        {
            return x;
        }

        if (x <= _points[0].x)
        {
            return _points[0].y;
        }

        for (int index = 1; index < PointCount; index++)
        {
            Vector2 right = _points[index];
            if (x <= right.x)
            {
                Vector2 left = _points[index - 1];
                float width = Mathf.Max(right.x - left.x, 1e-5f);
                float t = Mathf.Clamp01((x - left.x) / width);
                if (Interpolation == ColorCurveInterpolation.Smooth)
                {
                    t = t * t * (3f - 2f * t);
                }

                return Mathf.Lerp(left.y, right.y, t);
            }
        }

        return _points[PointCount - 1].y;
    }

    // Сравнение по содержимому. Снимки грейда клонируют кривые, и сравнение
    // по ссылке никогда не узнавало тот же грейд: каждый кадр шла полная
    // пересборка и сброс истории постпроцесса.
    public bool ContentEquals(ColorGradeCurve other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Kind != other.Kind ||
            PointCount != other.PointCount ||
            Interpolation != other.Interpolation)
        {
            return false;
        }

        for (int index = 0; index < MaxPoints; index++)
        {
            if (_points[index] != other._points[index])
            {
                return false;
            }
        }

        return true;
    }

    public ColorGradeCurve Clone()
    {
        var clone = new ColorGradeCurve(Kind)
        {
            PointCount = PointCount,
            Interpolation = Interpolation,
        };
        Array.Copy(_points, clone._points, _points.Length);
        return clone;
    }

    public void Sanitize()
    {
        PointCount = Mathf.Clamp(PointCount, 2, MaxPoints);
        Vector2 first = SanitizePoint(_points[0]);
        _points[0] = new Vector2(0f, first.y);
        for (int index = 1; index < PointCount; index++)
        {
            Vector2 point = SanitizePoint(_points[index]);
            float minimumX = _points[index - 1].x + 1e-4f;
            float maximumX = 1f - (PointCount - 1 - index) * 1e-4f;
            _points[index] = new Vector2(
                Mathf.Clamp(point.x, minimumX, maximumX),
                point.y);
        }

        Vector2 last = SanitizePoint(_points[PointCount - 1]);
        float lastY = Kind == ColorGradeCurveKind.Hue ? _points[0].y : last.y;
        _points[PointCount - 1] = new Vector2(1f, lastY);
        if (Interpolation is not (ColorCurveInterpolation.Linear or ColorCurveInterpolation.Smooth))
        {
            Interpolation = ColorCurveInterpolation.Smooth;
        }

        ClearUnusedPoints();
    }

    private static Vector2 SanitizePoint(Vector2 point) => new(
        float.IsFinite(point.x) ? Mathf.Clamp01(point.x) : 0.5f,
        float.IsFinite(point.y) ? Mathf.Clamp01(point.y) : 0.5f);

    private void ClearUnusedPoints()
    {
        for (int index = PointCount; index < _points.Length; index++)
        {
            _points[index] = Vector2.zero;
        }
    }
}
