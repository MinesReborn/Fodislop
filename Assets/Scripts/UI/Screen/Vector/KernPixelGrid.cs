#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Screens;

// Сетка пикселей экрана для микрографики. Панель масштабируется (1 px макета = 0.667 px экрана в окне 1280×720,
// 1.333 px на 2560×1440), и линия в 1 px панели ложится между пикселями экрана: размывается и тускнеет.
// Браузер волосяную рамку при масштабе держит целым пикселем экрана — так и здесь: края прямоугольников
// привязываются к пикселям экрана, толщина не меньше одного пикселя
internal readonly struct KernPixelGrid
{
    private readonly float _scale;
    private readonly Vector2 _origin;

    private KernPixelGrid(float scale, Vector2 origin)
    {
        _scale = scale;
        _origin = origin;
    }

    // Пиксель экрана в единицах панели
    public float Pixel => 1f / _scale;

    // Масштаб не целый: целые единицы панели ложатся между пикселями экрана
    public bool Fractional => Mathf.Abs(_scale - Mathf.Round(_scale)) > .01f;

    // Сетка элемента на экране. Для подложки-картинки (фон текстового элемента) начало то же:
    // картинка растягивается ровно по рамке элемента
    public static KernPixelGrid Of(VisualElement element)
    {
        float scale = element.scaledPixelsPerPoint;
        if (float.IsNaN(scale) || scale <= 0f)
        {
            scale = 1f;
        }

        Rect world = element.worldBound;
        Vector2 origin = float.IsNaN(world.x) || float.IsNaN(world.y) ? Vector2.zero : world.position;
        return new KernPixelGrid(scale, origin);
    }

    // Прямоугольник по пикселям экрана; нулевую после привязки толщину добирает до одного пикселя
    public Rect Snap(float x, float y, float w, float h)
    {
        float x0 = SnapX(x), x1 = SnapX(x + w), y0 = SnapY(y), y1 = SnapY(y + h);
        if (x1 - x0 < Pixel * .5f)
        {
            x1 = x0 + Pixel;
        }

        if (y1 - y0 < Pixel * .5f)
        {
            y1 = y0 + Pixel;
        }

        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    private float SnapX(float x) => (Mathf.Round((_origin.x + x) * _scale) / _scale) - _origin.x;

    private float SnapY(float y) => (Mathf.Round((_origin.y + y) * _scale) / _scale) - _origin.y;
}
