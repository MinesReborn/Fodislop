#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

// Прогресс-бар опыта умения: трек с лаймовой заливкой ПОД иконкой и
// процентами ПРЯМО В треке. Число одно: белые жирные цифры с плотной
// чёрной обводкой (проверенный рецепт числа корзины - читается и на
// лайме, и на тёмном треке) при ЛЮБОМ заполнении, знак "%" мельче
// через rich-тег.
//
// Значение не прыгает мгновенно: заливка и проценты догоняют целевое
// значение плавно (доля нормы за тик), поэтому при прокачке умения полосу
// видно движущейся.
//
// Перелив опыта (current > max, то есть > 100%): длина заливки держится
// полной, а цвет плавно уходит из лайма в красный (--state-danger) -
// чем дальше перелив, тем краснее (см. FullRedOverflow). Проценты при
// этом честно показывают сверхнорму: "120%".
internal sealed class SkillProgressBar : VisualElement
{
    private const float CatchUpPerTick = 0.02f; // долей нормы за тик (16 мс)
    private const long TickIntervalMs = 16L;
    private const int MaxShownPercent = 999; // защита от огромных значений

    // Лайм старого клиента; красный - оттенок --state-danger из
    // ThemeTokens.uss (#ff5555), чтобы совпадать с остальной "опасностью".
    // Альфа ~0.65 - как у заливки полосы корзины.
    private static readonly Color32 s_fillColor = new(163, 230, 53, 165);
    private static readonly Color32 s_readyColor = new(255, 85, 85, 170);

    // Перелив опыта сверх 100%, при котором заливка становится полностью
    // красной: 0.5 = +50% (то есть 150% опыта). Ручка насыщенности красного.
    private const float FullRedOverflow = 0.5f;

    private readonly VisualElement _fill;
    private readonly Label _percent;

    private float _displayed; // анимируемое "сырое" значение (может быть > 1)
    private float _target;
    private IVisualElementScheduledItem? _animation;

    public SkillProgressBar()
    {
        AddToClassList("hud-skill-bar");

        _fill = new VisualElement();
        _fill.AddToClassList("hud-skill-bar-fill");
        Add(_fill);

        _percent = new Label("0%");
        _percent.AddToClassList("hud-skill-bar-pct");
        Add(_percent);

        // Сразу применяем нулевое состояние: без этого заливка успевает
        // отрисоваться растянутой на весь трек до первого обновления.
        ApplyVisuals();
    }

    // Плавно двигает полосу к целевому прогрессу. progress - "сырое"
    // отношение current/max БЕЗ клампа: всё, что сверх 1.0, красит заливку
    // (длина при этом держится полной), а проценты показывают сверхнорму.
    public void SetTarget(float progress)
    {
        _target = progress;
        if (Mathf.Approximately(_displayed, _target))
        {
            _displayed = _target;
            _animation?.Pause();
            ApplyVisuals();
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

    public void PauseAnimation()
    {
        _animation?.Pause();
    }

    private void AnimateTick()
    {
        _displayed = Mathf.MoveTowards(_displayed, _target, CatchUpPerTick);
        if (Mathf.Approximately(_displayed, _target))
        {
            _displayed = _target;
            _animation?.Pause();
        }

        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        _fill.style.width = Length.Percent(Mathf.Clamp01(_displayed) * 100f);
        _fill.style.backgroundColor = new StyleColor(Color32.Lerp(
            s_fillColor,
            s_readyColor,
            Mathf.Clamp01((_displayed - 1f) / FullRedOverflow)));

        // Цифры крупным кеглем, "%" - мельче (rich-тег): число читается
        // легче монолитной строки. Цвет всегда белый - задаёт USS.
        int percent = Mathf.Min(Mathf.RoundToInt(_displayed * 100f), MaxShownPercent);
        _percent.text = percent + "<size=9>%</size>";
    }
}
