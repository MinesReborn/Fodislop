#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;
internal sealed class MenuLoaderProgress
{
    // Значения — ключи словаря локализации, а не текст: Get() возвращает
    // ключ как есть, если его нет в словаре, поэтому литеральные фазы
    // (SpawnSync/SurfaceAssets) проходят без изменений.
    private readonly (WorldLoadPhase Phase, string Label)[] _PhaseSteps =
    {
        (WorldLoadPhase.Handshake, "menu.loading.phase.handshake"),
        (WorldLoadPhase.WorldManifest, "menu.loading.phase.assets"),
        (WorldLoadPhase.SpawnSync, "menu.loading.phase.spawn_sync"),
        (WorldLoadPhase.TerrainMesh, "menu.loading.phase.terrain"),
        (WorldLoadPhase.SurfaceAssets, "menu.loading.phase.surface_assets"),
    };

    private readonly VisualElement? _loaderProgressFill;
    private readonly Label? _loaderPhaseLabel;
    private readonly Label? _loaderPhaseCount;
    private readonly VisualElement? _loaderPhaseList;
    private readonly ILocalizationService _loc;

    private readonly List<(VisualElement Item, Label Icon)> _phaseItems = new();

    private WorldLoadPhase _lastAppliedPhase = (WorldLoadPhase)(-1);
    private bool _lastAppliedDone;
    private int _lastAppliedPhaseIndex = -1;
    private float _lastAppliedProgress = -1f;

    public MenuLoaderProgress(
        VisualElement? loaderProgressFill,
        Label? loaderPhaseLabel,
        Label? loaderPhaseCount,
        VisualElement? loaderPhaseList,
        ILocalizationService loc)
    {
        _loaderProgressFill = loaderProgressFill;
        _loaderPhaseLabel = loaderPhaseLabel;
        _loaderPhaseCount = loaderPhaseCount;
        _loaderPhaseList = loaderPhaseList;
        _loc = loc;

        BuildPhaseList();
    }

    private void BuildPhaseList()
    {
        if (_loaderPhaseList == null)
        {
            return;
        }

        _phaseItems.Clear();
        _loaderPhaseList.Clear();
        foreach ((WorldLoadPhase _, string label) in _PhaseSteps)
        {
            var item = new VisualElement();
            item.AddToClassList("mm-loader-phase-item");

            var icon = new Label("○");
            icon.AddToClassList("mm-loader-phase-icon");
            item.Add(icon);

            var text = new Label(Localize(label));
            item.Add(text);

            _loaderPhaseList.Add(item);
            _phaseItems.Add((item, icon));
        }
    }

    // Get() пропускает не-ключи как есть (возвращает сам ключ), поэтому
    // литеральные фазы без перевода не меняются. Null-безопасно: без
    // инжекта локализации текст остаётся как в _PhaseSteps/Uxml.
    private string Localize(string keyOrText) => _loc == null ? keyOrText : _loc.Get(keyOrText);

    public void RefreshLocalization() => BuildPhaseList();

    public void UpdateProgress(WorldLoadPhase phase)
    {
        int totalPhases = _PhaseSteps.Length;
        bool done = phase >= WorldLoadPhase.Done;
        int phaseIndex = Mathf.Clamp((int)phase, 0, totalPhases);

        if (phase == _lastAppliedPhase)
        {
            return;
        }

        _lastAppliedPhase = phase;
        _lastAppliedDone = done;
        _lastAppliedPhaseIndex = phaseIndex;

        float progress = done ? 1f : Mathf.Clamp01(phaseIndex / (float)totalPhases);

        if (_loaderProgressFill != null && !Mathf.Approximately(progress, _lastAppliedProgress))
        {
            _lastAppliedProgress = progress;
            _loaderProgressFill.style.width = new Length(progress * 100f, LengthUnit.Percent);
        }

        if (_loaderPhaseLabel != null)
        {
            _loaderPhaseLabel.text = done
                ? Localize("menu.loading.phase.ready")
                : Localize(_PhaseSteps[phaseIndex].Label);
        }

        if (_loaderPhaseCount != null)
        {
            _loaderPhaseCount.text = done
                ? $"{totalPhases} / {totalPhases}"
                : $"{phaseIndex + 1} / {totalPhases}";
        }

        for (int i = 0; i < _phaseItems.Count; i++)
        {
            (VisualElement item, Label icon) = _phaseItems[i];
            bool isDone = done || i < phaseIndex;
            bool isActive = !done && i == phaseIndex;
            item.EnableInClassList("mm-loader-phase-item--done", isDone);
            item.EnableInClassList("mm-loader-phase-item--active", isActive);
            icon.text = isDone ? "✓" : isActive ? "◆" : "○";
        }
    }
}
