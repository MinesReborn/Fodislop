#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

// Круговой прогресс-бар умения (как в старом клиенте): лаймовое кольцо
// вокруг иконки, собранное из повёрнутых сегментов - без кастомных мешей,
// поэтому работает в любой версии UI Toolkit. Старт сверху (12 часов),
// заполнение по часовой стрелке. Значение не прыгает мгновенно: сегменты
// загораются по мере того, как отображаемое значение догоняет целевое,
// поэтому при прокачке умения кольцо видно движущимся.
internal sealed class SkillProgressRing : VisualElement
{
    private const int SegmentCount = 32;
    private const float ThicknessPixels = 4f;
    private const float SegmentOverlapPixels = 1.5f; // стыки без щелей
    private const float CatchUpPerTick = 0.02f; // долей круга за тик (16 мс)
    private const long TickIntervalMs = 16L;

    // Лайм старого клиента.
    private static readonly Color32 RingColor = new(163, 230, 53, 255);

    private readonly VisualElement[] _segments = new VisualElement[SegmentCount];
    private float _displayed;
    private float _target;
    private IVisualElementScheduledItem? _animation;

    public SkillProgressRing()
    {
        AddToClassList("hud-skill-ring");
        for (int i = 0; i < SegmentCount; i++)
        {
            var segment = new VisualElement();
            segment.AddToClassList("hud-skill-ring-segment");
            segment.style.backgroundColor = new StyleColor(RingColor);
            segment.style.position = Position.Absolute;
            segment.style.display = DisplayStyle.None;
            Add(segment);
            _segments[i] = segment;
        }

        RegisterCallback<GeometryChangedEvent>(_ => Relayout());
    }

    // Плавно двигает кольцо к целевому прогрессу (0..1).
    public void SetTarget(float progress)
    {
        _target = Mathf.Clamp01(progress);
        if (Mathf.Approximately(_displayed, _target))
        {
            _displayed = _target;
            _animation?.Pause();
            ApplyVisibility();
            return;
        }

        if (_animation == null)
        {
            _animation = schedule.Execute(AnimateTick).Every(TickIntervalMs);
        }
        else
        {
            _animation.Resume();
        }
    }

    // Полная остановка анимации (скрытие HUD).
    public void PauseAnimation() => _animation?.Pause();

    private void AnimateTick()
    {
        _displayed = Mathf.MoveTowards(_displayed, _target, CatchUpPerTick);
        if (Mathf.Approximately(_displayed, _target))
        {
            _displayed = _target;
            _animation?.Pause();
        }

        ApplyVisibility();
    }

    // Раскладка сегментов по окружности (геометрия слоя могла измениться).
    private void Relayout()
    {
        Rect rect = contentRect;
        float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (outerRadius <= ThicknessPixels)
        {
            return;
        }

        float radius = outerRadius - ThicknessPixels * 0.5f;
        float centerX = rect.x + rect.width * 0.5f;
        float centerY = rect.y + rect.height * 0.5f;
        float segmentLength = 2f * radius * Mathf.Tan(Mathf.PI / SegmentCount) + SegmentOverlapPixels;

        for (int i = 0; i < SegmentCount; i++)
        {
            float angleDegrees = -90f + 360f * ((i + 0.5f) / SegmentCount);
            float angleRadians = angleDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(angleRadians);
            float cos = Mathf.Cos(angleRadians);
            VisualElement segment = _segments[i];
            segment.style.width = ThicknessPixels;
            segment.style.height = segmentLength;
            segment.style.left = centerX + sin * radius - ThicknessPixels * 0.5f;
            segment.style.top = centerY - cos * radius - segmentLength * 0.5f;
            segment.style.rotate = new Rotate(new Angle(angleDegrees, AngleUnit.Degree));
        }

        ApplyVisibility();
    }

    // Сегмент горит, если его доля круга уже заполнена отображаемым значением.
    private void ApplyVisibility()
    {
        for (int i = 0; i < SegmentCount; i++)
        {
            bool lit = (i / (float)SegmentCount) < _displayed - 0.0001f;
            _segments[i].style.display = lit ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}