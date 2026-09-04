#nullable enable

using UnityEngine;

namespace Fodinae.World;

/// <summary>
/// Привязка мира к пиксельной сетке экрана.
/// </summary>
/// <remarks>
/// ОТКУДА БЕРЁТСЯ МУАР. Тайл занимает 32 пикселя текстуры, фильтрация
/// Point. Сколько экранных пикселей приходится на один тексель, задаёт
/// высота окна и <c>orthographicSize</c>: при высоте 1080 и размере 7 это
/// 1080 / (7 * 2 * 16) = 4.821 — дробное число.
///
/// Дробное отношение при ближайшей выборке означает, что часть строк
/// текселей выводится дважды, а часть не выводится вовсе. На регулярной
/// сетке тайлов это читается как муар, а поскольку камера едет и зумится
/// непрерывно, рисунок ещё и ползёт — то самое «кипение» изображения.
///
/// Сглаживание тут бессильно: MSAA сглаживает края геометрии, а здесь
/// проблема в выборке текстуры на полностью покрытом квадрате.
///
/// Лечится единственным способом: держать число пикселей на тексель целым
/// и ставить камеру в узлы получившейся сетки. Тогда каждый тексель
/// занимает одинаковое целое число пикселей, и дублировать или терять
/// строки становится нечего.
/// </remarks>
public static class PixelGrid
{
    /// <summary>
    /// Наименьшее допустимое число экранных пикселей на тексель. Ниже
    /// единицы тексели начали бы выпадать при любом выравнивании.
    /// </summary>
    public const int MinimumPixelsPerTexel = 1;

    /// <summary>
    /// Наибольшее. Дальше кадр держит меньше двух тайлов по высоте —
    /// в такой зум игра всё равно не пускает.
    /// </summary>
    public const int MaximumPixelsPerTexel = 64;

    /// <summary>
    /// Ближайший к желаемому масштаб рендера, при котором апскейл до окна
    /// остаётся целократным.
    /// </summary>
    /// <remarks>
    /// Масштаб рендера — это второй пересчёт поверх первого: кадр рисуется
    /// в буфер высотой H*scale и растягивается до H. При дробном множителе
    /// растяжение размазывает уже выровненную сетку текселей, и муар
    /// возвращается на ровном месте — то есть экономия кадра оплачивается
    /// ровно той картинкой, ради которой всё делалось.
    ///
    /// Целократность даёт только обратная величина целого: 1, 1/2, 1/3.
    /// Авторские 0.65, 0.8 и 0.9 ни одной из них не являются.
    /// </remarks>
    public static float QuantizeRenderScale(float desiredScale, float minimumScale, float maximumScale)
    {
        if (desiredScale <= 0f)
        {
            return maximumScale;
        }

        float best = maximumScale;
        float bestDistance = float.MaxValue;
        for (int divisor = 1; divisor <= 8; divisor++)
        {
            float candidate = 1f / divisor;
            if (candidate < minimumScale || candidate > maximumScale)
            {
                continue;
            }

            float distance = Mathf.Abs(candidate - desiredScale);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Высота буфера, в котором на самом деле рисуется кадр.
    /// </summary>
    /// <remarks>
    /// Сетка текселей живёт здесь, а не в окне: выравнивать зум по высоте
    /// окна при масштабе рендера меньше единицы значит выравнивать не то.
    /// </remarks>
    public static int RenderHeight(int screenHeight, float renderScale)
    {
        if (screenHeight <= 0 || renderScale <= 0f)
        {
            return screenHeight;
        }

        return Mathf.Max(1, Mathf.RoundToInt(screenHeight * renderScale));
    }

    /// <summary>Сколько экранных пикселей приходится на один тексель.</summary>
    public static float PixelsPerTexel(float orthographicSize, int screenHeight)
    {
        if (orthographicSize <= 0f || screenHeight <= 0)
        {
            return 0f;
        }

        return screenHeight / (orthographicSize * 2f * RenderingConstants.PIXELS_PER_UNIT);
    }

    /// <summary>Размер камеры, дающий ровно столько пикселей на тексель.</summary>
    public static float OrthographicSizeFor(int pixelsPerTexel, int screenHeight)
    {
        if (pixelsPerTexel <= 0 || screenHeight <= 0)
        {
            return 0f;
        }

        return screenHeight / (pixelsPerTexel * 2f * RenderingConstants.PIXELS_PER_UNIT);
    }

    /// <summary>
    /// Ближайший к желаемому размер камеры, дающий целое число пикселей
    /// на тексель.
    /// </summary>
    /// <remarks>
    /// Из-за этого зум становится ступенчатым, и ступеней немного: при
    /// высоте 1080 и пределах от 5 до 30 их пять. Это не побочный эффект,
    /// а суть приёма — промежуточные размеры и есть те, на которых
    /// появляется муар.
    ///
    /// Если целой ступени в пределах не нашлось, возвращается ближайшая
    /// граница: отдать дробный размер значило бы молча вернуть муар.
    /// </remarks>
    public static float QuantizeOrthographicSize(
        float desiredSize,
        int screenHeight,
        float minimumSize,
        float maximumSize)
    {
        if (screenHeight <= 0 || minimumSize <= 0f || maximumSize < minimumSize)
        {
            return desiredSize;
        }

        float best = 0f;
        float bestDistance = float.MaxValue;

        for (int pixelsPerTexel = MinimumPixelsPerTexel;
            pixelsPerTexel <= MaximumPixelsPerTexel;
            pixelsPerTexel++)
        {
            float candidate = OrthographicSizeFor(pixelsPerTexel, screenHeight);
            if (candidate < minimumSize || candidate > maximumSize)
            {
                continue;
            }

            float distance = Mathf.Abs(candidate - desiredSize);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best > 0f)
        {
            return best;
        }

        // Ни одна ступень не попала в разрешённый диапазон: такое бывает
        // на очень низком разрешении окна. Ближняя граница хотя бы
        // предсказуема, в отличие от произвольного дробного размера.
        return desiredSize < minimumSize ? minimumSize : maximumSize;
    }

    /// <summary>
    /// Размер шага сетки в юнитах мира — один экранный пиксель.
    /// </summary>
    public static float SnapUnit(float orthographicSize, int screenHeight)
    {
        float pixelsPerTexel = PixelsPerTexel(orthographicSize, screenHeight);
        if (pixelsPerTexel <= 0f)
        {
            return 0f;
        }

        return 1f / (RenderingConstants.PIXELS_PER_UNIT * pixelsPerTexel);
    }

    /// <summary>
    /// Ставит точку в узел пиксельной сетки.
    /// </summary>
    /// <remarks>
    /// Целого числа пикселей на тексель мало: если камера стоит на
    /// полпикселя мимо, сетка текселей снова разъезжается с сеткой
    /// экрана, и муар возвращается на ровном месте.
    /// </remarks>
    public static Vector2 Snap(Vector2 position, float snapUnit)
    {
        if (snapUnit <= 0f)
        {
            return position;
        }

        return new Vector2(
            Mathf.Round(position.x / snapUnit) * snapUnit,
            Mathf.Round(position.y / snapUnit) * snapUnit);
    }
}
