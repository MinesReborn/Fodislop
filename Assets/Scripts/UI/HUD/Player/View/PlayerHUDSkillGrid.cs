#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

// Сетка умений PlayerHud (как в старом клиенте): иконка умения, вокруг неё -
// круговой лаймовый прогресс-бар (SkillProgressRing), над иконкой - "up" при
// готовности апгрейда (xp >= 100%), прыгающее вверх-вниз для привлечения
// внимания. Вертикальной полосы больше нет.
public sealed class PlayerHUDSkillGrid
{
    private const int SKILL_GRID_COLS = 4;
    private const string ReadyText = "up";
    private const long BounceIntervalMs = 40L;

    private readonly Dictionary<SkillType, (Label arrow, SkillProgressRing ring)> _skillIcons = new();
    private readonly Dictionary<SkillType, IVisualElementScheduledItem> _bounceSchedules = new();

    private VisualElement? _skillContainer;
    private VisualElement? _currentSkillRow;
    private int _skillCountInRow;

    public void Initialize(VisualElement skillContainer)
    {
        _skillContainer = skillContainer;
        _currentSkillRow = null;
        _skillCountInRow = 0;
        _skillIcons.Clear();
    }

    public void UpdateSkillProgress(SkillType skill, long current, long max)
    {
        if (!_skillIcons.TryGetValue(skill, out var icon))
        {
            icon = CreateSkillIcon(skill);
        }

        float progress = max > 0 ? Mathf.Clamp01(current / (float)max) : 0f;
        bool ready = progress >= 1f;

        // Кольцо плавно догоняет целевое заполнение - линия движется, пока
        // умение прокачивается.
        icon.ring.SetTarget(progress);

        icon.arrow.text = ready ? ReadyText : string.Empty;
        if (ready)
        {
            StartBounce(skill, icon.arrow);
        }
        else
        {
            StopBounce(skill, icon.arrow);
        }
    }

    public void ClearSchedules()
    {
        foreach (var schedule in _bounceSchedules.Values)
        {
            schedule.Pause();
        }

        _bounceSchedules.Clear();

        foreach (var (_, ring) in _skillIcons.Values)
        {
            ring.PauseAnimation();
        }
    }

    // "up" прыгает вверх-вниз (0..-4px), как в старом клиенте.
    private void StartBounce(SkillType skill, Label arrow)
    {
        if (_bounceSchedules.ContainsKey(skill))
        {
            return;
        }

        float elapsed = 0f;
        IVisualElementScheduledItem schedule = arrow.schedule.Execute(() =>
        {
            elapsed += BounceIntervalMs / 1000f;
            float y = -Mathf.PingPong(elapsed * 10f, 4f);
            arrow.style.translate = new Translate(0, y);
        }).Every(BounceIntervalMs);
        _bounceSchedules[skill] = schedule;
    }

    private void StopBounce(SkillType skill, Label arrow)
    {
        if (_bounceSchedules.TryGetValue(skill, out var existing))
        {
            existing.Pause();
            _bounceSchedules.Remove(skill);
        }

        arrow.style.translate = new Translate(0, 0);
    }

    private void EnsureSkillRow()
    {
        if (_currentSkillRow != null && _skillCountInRow < SKILL_GRID_COLS)
        {
            return;
        }

        _currentSkillRow = new VisualElement();
        _currentSkillRow.AddToClassList("hud-skill-row");
        _skillContainer?.Add(_currentSkillRow);
        _skillCountInRow = 0;
    }

    private (Label arrow, SkillProgressRing ring) CreateSkillIcon(SkillType skill)
    {
        EnsureSkillRow();

        // Ячейка: иконка умения, поверх неё - круговой прогресс-бар
        // (абсолютный слой вокруг иконки), над иконкой - "up".
        var cell = new VisualElement();
        cell.AddToClassList("hud-skill-icon");

        var iconImage = new Image();
        iconImage.AddToClassList("hud-skill-icon-image");

        Texture2D? tex = Resources.Load<Texture2D>($"Skills/{skill}");
        if (tex != null)
        {
            RuntimeTextureFactory.ApplySampling(
                tex,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            iconImage.image = tex;
        }

        cell.Add(iconImage);

        var ring = new SkillProgressRing();
        cell.Add(ring);

        var arrow = new Label(ReadyText);
        arrow.AddToClassList("hud-skill-arrow");
        cell.Add(arrow);

        _currentSkillRow?.Add(cell);
        _skillCountInRow++;

        var result = (arrow, ring);
        _skillIcons[skill] = result;
        return result;
    }
}
