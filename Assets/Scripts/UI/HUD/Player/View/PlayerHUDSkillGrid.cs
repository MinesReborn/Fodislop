#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

public sealed class PlayerHUDSkillGrid
{
    private const int SKILL_GRID_COLS = 4;
    private const float SkillBarHeightPixels = 24f;

    private readonly Dictionary<SkillType, (Label arrow, VisualElement barFill)> _skillIcons = new();
    private readonly Dictionary<SkillType, IVisualElementScheduledItem> _bounceSchedules = new();
    private readonly Dictionary<SkillType, IVisualElementScheduledItem> _pulseSchedules = new();

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
            var created = CreateSkillIcon(skill);
            icon.arrow = created.arrow;
            icon.barFill = created.barFill;
        }

        float progress = max > 0 ? (float)current / max : 0f;

        icon.barFill.style.backgroundColor = Color.Lerp(Color.green, Color.red, Mathf.Clamp01(progress));
        icon.arrow.text = progress >= 1f ? "up" : string.Empty;

        if (progress >= 1f)
        {
            StopBarPulse(skill);
            StartBounce(skill, icon.arrow);
        }
        else
        {
            StopBounce(skill, icon.arrow);
            StartBarPulse(skill, icon.barFill, progress);
        }
    }

    public void ClearSchedules()
    {
        foreach (var schedule in _bounceSchedules.Values)
        {
            schedule.Pause();
        }

        foreach (var schedule in _pulseSchedules.Values)
        {
            schedule.Pause();
        }

        _bounceSchedules.Clear();
        _pulseSchedules.Clear();
    }

    private void StartBounce(SkillType skill, Label arrow)
    {
        StopBounce(skill, arrow);
        arrow.style.translate = new Translate(0, 0);
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

    private void StartBarPulse(SkillType skill, VisualElement barFill, float progress)
    {
        StopBarPulse(skill);

        float normalizedProgress = Mathf.Clamp01(progress);

        barFill.style.height = new Length(SkillBarHeightPixels, LengthUnit.Pixel);
        barFill.style.transformOrigin =
            new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
        barFill.style.scale = new Scale(new Vector2(1f, normalizedProgress));
    }

    private void StopBarPulse(SkillType skill)
    {
        if (_pulseSchedules.TryGetValue(skill, out var existing))
        {
            existing.Pause();
            _pulseSchedules.Remove(skill);
        }

        if (_skillIcons.TryGetValue(skill, out var icon) && icon.barFill != null)
        {
            icon.barFill.style.scale = new Scale(Vector2.one);
        }
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

    private (Label arrow, VisualElement barFill) CreateSkillIcon(SkillType skill)
    {
        EnsureSkillRow();

        var cell = new VisualElement();
        cell.AddToClassList("hud-skill-icon");

        var iconColumn = new VisualElement();
        iconColumn.AddToClassList("hud-skill-icon-column");

        var arrow = new Label("up");
        arrow.AddToClassList("hud-skill-arrow");
        iconColumn.Add(arrow);

        var iconImage = new Image();
        iconImage.AddToClassList("hud-skill-icon-image");

        var tex = Resources.Load<Texture2D>($"Skills/{skill}");
        if (tex != null)
        {
            RuntimeTextureFactory.ApplySampling(
                tex,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            iconImage.image = tex;
        }

        iconColumn.Add(iconImage);
        cell.Add(iconColumn);

        var barContainer = new VisualElement();
        barContainer.AddToClassList("hud-skill-bar-container");

        var barFill = new VisualElement();
        barFill.AddToClassList("hud-skill-bar-fill");
        barFill.AddToClassList("hud-skill-bar-segment");
        barContainer.Add(barFill);
        cell.Add(barContainer);

        _currentSkillRow?.Add(cell);
        _skillCountInRow++;

        _skillIcons[skill] = (arrow, barFill);
        return (arrow, barFill);
    }
}
