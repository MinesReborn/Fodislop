#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.UI.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Menu;
internal sealed class MenuLoaderProgress
{
    // Ключи словаря: название фазы (строка счёта и список) и пояснение под заголовком (макет: STEPS label / detail)
    private readonly (WorldLoadPhase Phase, string Label, string Detail)[] _PhaseSteps =
    {
        (WorldLoadPhase.Handshake, "menu.loading.phase.handshake", "menu.loading.detail.handshake"),
        (WorldLoadPhase.WorldManifest, "menu.loading.phase.assets", "menu.loading.detail.assets"),
        (WorldLoadPhase.SpawnSync, "menu.loading.phase.spawn_sync", "menu.loading.detail.spawn_sync"),
        (WorldLoadPhase.TerrainMesh, "menu.loading.phase.terrain", "menu.loading.detail.terrain"),
    };

    private readonly VisualElement? _loaderProgressFill;
    private readonly Label? _loaderPhaseLabel;
    private readonly Label? _loaderPhaseCount;
    private readonly VisualElement? _loaderPhaseList;
    private readonly Label _explain;
    private readonly ILocalizationService _loc;

    private readonly List<(VisualElement Item, Label Icon)> _phaseItems = new();

    private WorldLoadPhase _lastAppliedPhase = (WorldLoadPhase)(-1);
    private AssetSyncProgress _lastAssetSync = new(-1, -1, -1, -1);
    private bool _lastAppliedDone;
    private int _lastAppliedPhaseIndex = -1;
    private float _lastAppliedProgress = -1f;
    private bool _failed;

    public MenuLoaderProgress(
        VisualElement? loaderProgressFill,
        Label? loaderPhaseLabel,
        Label? loaderPhaseCount,
        VisualElement? loaderPhaseList,
        Label explain,
        ILocalizationService loc)
    {
        _explain = explain;
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
        foreach ((WorldLoadPhase _, string label, string _) in _PhaseSteps)
        {
            var item = new VisualElement();
            item.AddToClassList("mm-loader-phase-item");

            // Точка этапа рисуется USS (макет: .phases li::before); знака в тексте нет.
            // Рамка вокруг точки идущей фазы — outline макета, в USS его нет
            var icon = new Label(string.Empty);
            icon.AddToClassList("mm-loader-phase-icon");
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("mm-loader-phase-ring");
            icon.Add(ring);
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
    private static string FormatMegabytes(long bytes) =>
        (bytes / 1048576d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private string Localize(string keyOrText) => _loc == null ? keyOrText : _loc.Get(keyOrText);

    public void RefreshLocalization()
    {
        BuildPhaseList();
        WorldLoadPhase phase = _lastAppliedPhase;
        bool failed = _failed;
        AssetSyncProgress assetSync = _lastAssetSync;
        _lastAppliedPhase = (WorldLoadPhase)(-1);
        _lastAssetSync = new AssetSyncProgress(-1, -1, -1, -1);
        _failed = false;
        if ((int)phase >= 0)
        {
            UpdateProgress(phase, assetSync);
        }

        if (failed)
        {
            ShowFailure();
        }
    }

    public void ResetFailure()
    {
        _failed = false;
        _explain.RemoveFromClassList("is-bad");
        foreach ((VisualElement item, Label _) in _phaseItems)
        {
            item.RemoveFromClassList("mm-loader-phase-item--fail");
        }
    }

    // Вход сорвался: пояснение — отказ, точка фазы, на которой встали, — отказ (макет: BAD, li.is-fail)
    public void ShowFailure()
    {
        _failed = true;
        SetExplain(Localize("menu.loading.failed"));
        _explain.AddToClassList("is-bad");
        if (_lastAppliedPhaseIndex >= 0 && _lastAppliedPhaseIndex < _phaseItems.Count)
        {
            VisualElement item = _phaseItems[_lastAppliedPhaseIndex].Item;
            item.RemoveFromClassList("mm-loader-phase-item--active");
            item.AddToClassList("mm-loader-phase-item--fail");
        }
    }

    private void SetExplain(string text)
    {
        _explain.text = text;
        UILocalizer.ApplyLineHeight(_explain);
    }

    public void UpdateProgress(WorldLoadPhase phase, AssetSyncProgress assetSync)
    {
        if (_failed)
        {
            return;
        }

        int totalPhases = _PhaseSteps.Length;
        bool done = phase >= WorldLoadPhase.Done;
        int phaseIndex = Mathf.Clamp((int)phase, 0, totalPhases);

        if (phase == _lastAppliedPhase)
        {
            ApplyAssetSync(phase, assetSync);
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

        // Всё пройдено — подпись последней фазы и полный счёт (макет: cur = min(done, последняя))
        int labelIndex = Mathf.Min(phaseIndex, totalPhases - 1);
        if (_loaderPhaseLabel != null)
        {
            _loaderPhaseLabel.text = Localize(_PhaseSteps[labelIndex].Label);
        }

        SetExplain(Localize(_PhaseSteps[labelIndex].Detail));

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
        }

        ApplyAssetSync(phase, assetSync);
    }

    // Проверка кэша и вес скачанного растут внутри фазы, а фаза не меняется: подпись обновляем отдельно от смены фазы
    private void ApplyAssetSync(WorldLoadPhase phase, AssetSyncProgress assetSync)
    {
        if (phase != WorldLoadPhase.WorldManifest || assetSync == _lastAssetSync)
        {
            return;
        }

        _lastAssetSync = assetSync;
        if (_loaderPhaseLabel == null)
        {
            return;
        }

        string label = Localize(_PhaseSteps[(int)phase].Label);
        if (_loc == null)
        {
            _loaderPhaseLabel.text = label;
            return;
        }

        // Пока кэш проверяется — «проверено из скольких»; потом, если что-то качается, — вес скачанного
        string status = assetSync.FilesTotal > 0 && assetSync.FilesChecked < assetSync.FilesTotal
            ? _loc.Get("menu.loading.assets_check", assetSync.FilesChecked, assetSync.FilesTotal)
            : assetSync.BytesTotal > 0
                ? _loc.Get(
                    "menu.loading.assets_size",
                    FormatMegabytes(assetSync.BytesDone),
                    FormatMegabytes(assetSync.BytesTotal))
                : string.Empty;
        _loaderPhaseLabel.text = status.Length > 0 ? $"{label} · {status}" : label;
    }
}
