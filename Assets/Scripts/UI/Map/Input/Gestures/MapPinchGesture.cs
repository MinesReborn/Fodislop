#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Map;

// Пинч двумя пальцами по карте мира — тач-аналог колеса. Первый палец
// двигает карту (MapInteractionController), второй переводит жест в масштаб:
// перетаскивание отменяется, а отношение расстояний между пальцами
// становится шагами зума с якорем в середине между ними.
internal sealed class MapPinchGesture
{
    // Шаг зума колеса — множитель 0.85 на щелчок (MapInteractionController).
    private static readonly float s_logStep = Mathf.Log(1f / 0.85f);
    private const float MinimumStep = 0.02f;

    private int _first = -1;
    private int _second = -1;
    private Vector2 _firstPosition;
    private Vector2 _secondPosition;
    private float _lastDistance;

    public bool IsPinching => _first >= 0 && _second >= 0;

    public void Reset(VisualElement? image)
    {
        if (image != null && _first >= 0 && image.HasPointerCapture(_first))
        {
            image.ReleasePointer(_first);
        }

        if (image != null && _second >= 0 && image.HasPointerCapture(_second))
        {
            image.ReleasePointer(_second);
        }

        _first = -1;
        _second = -1;
        _lastDistance = 0f;
    }

    // Возвращает true, если это касание начало пинч.
    public bool Down(PointerDownEvent evt, VisualElement image)
    {
        if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
        {
            return false;
        }

        if (_first < 0)
        {
            _first = evt.pointerId;
            _firstPosition = evt.position;
            return false;
        }

        if (_second >= 0 || evt.pointerId == _first)
        {
            return false;
        }

        _second = evt.pointerId;
        _secondPosition = evt.position;
        _lastDistance = Vector2.Distance(_firstPosition, _secondPosition);
        image.CapturePointer(evt.pointerId);
        return true;
    }

    // Шаги зума в единицах колеса (знак как у WheelEvent.delta.y: меньше
    // нуля — приближение) и середина между пальцами в координатах панели.
    public bool Move(PointerMoveEvent evt, out float wheelDelta, out Vector2 center)
    {
        wheelDelta = 0f;
        center = default;
        if (evt.pointerId == _first)
        {
            _firstPosition = evt.position;
        }
        else if (evt.pointerId == _second)
        {
            _secondPosition = evt.position;
        }
        else
        {
            return false;
        }

        if (!IsPinching)
        {
            return false;
        }

        float distance = Vector2.Distance(_firstPosition, _secondPosition);
        center = (_firstPosition + _secondPosition) * 0.5f;
        if (_lastDistance <= 1f || distance <= 1f)
        {
            _lastDistance = distance;
            return true;
        }

        // Мелкие сдвиги копятся: зум отбрасывает шаг меньше сотой щелчка, и
        // медленный пинч иначе не масштабировал бы вовсе.
        float delta = -Mathf.Log(distance / _lastDistance) / s_logStep;
        if (Mathf.Abs(delta) >= MinimumStep)
        {
            wheelDelta = delta;
            _lastDistance = distance;
        }

        return true;
    }

    public void Up(PointerUpEvent evt, VisualElement? image)
    {
        if (evt.pointerId == _second)
        {
            image?.ReleasePointer(evt.pointerId);
            _second = -1;
        }
        else if (evt.pointerId == _first)
        {
            // Ведущий палец ушёл — пинч закончен, второй палец отпускается.
            if (_second >= 0)
            {
                image?.ReleasePointer(_second);
            }

            _first = -1;
            _second = -1;
        }
    }
}
