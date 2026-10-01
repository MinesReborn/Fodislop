#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core.Interfaces;
using Kern.UI.HUD.Player.Model;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD.Player.View;

public sealed class PlayerHUDBasketView
{
    private readonly List<Texture2D> _crystalTextures = new();
    private readonly List<VisualElement> _basketCrystalFills = new();
    private readonly List<Label> _basketCrystalCounts = new();
    private readonly List<VisualElement> _basketRows = new();

    private VisualElement? _basketContainer;

    public void Initialize(VisualElement basketContainer)
    {
        _basketContainer = basketContainer;
    }

    public async UniTask LoadCrystalTextures(IAssetLoader assetLoader, CancellationToken cancellationToken)
    {
        _crystalTextures.Clear();
        foreach (CrystalType ct in Enum.GetValues(typeof(CrystalType)))
        {
            if (ct == CrystalType.Unknown)
            {
                continue;
            }

            string name = ct.ToString().ToLowerInvariant();
            Texture2D? tex;
            try
            {
                tex = await assetLoader.GetTextureAsync(
                    "Crystals/" + name,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The loader can be torn down before the HUD receives its own
                // destroy cancellation during a scene transition. In that case
                // the request is intentionally abandoned, not an optional asset
                // failure worth reporting as a warning.
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[PlayerHUD] Optional crystal texture '{name}' was skipped: " +
                    exception.Message);
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (tex != null)
            {
                _crystalTextures.Add(tex);
            }
        }
    }

    public void RebuildRows()
    {
        if (_basketContainer == null)
        {
            return;
        }

        _basketContainer.Clear();
        _basketCrystalFills.Clear();
        _basketCrystalCounts.Clear();
        _basketRows.Clear();

        for (int i = 0; i < _crystalTextures.Count; i++)
        {
            var row = new VisualElement();
            row.AddToClassList("hud-crystal-row");

            var dot = new Image();
            dot.AddToClassList("hud-crystal-dot");
            if (_crystalTextures[i] != null)
            {
                dot.style.backgroundImage = new StyleBackground(_crystalTextures[i]);
            }

            row.Add(dot);

            // Полоса с тонированным зоной треком и числом внутри по центру:
            // белый жирный текст с плотной чёрной обводкой читается и на
            // светлой заливке, и на тёмной части трека.
            var bar = new VisualElement();
            bar.AddToClassList("hud-crystal-bar");

            var fill = new VisualElement();
            fill.AddToClassList("hud-crystal-fill");
            bar.Add(fill);

            var count = new Label("0/0");
            count.AddToClassList("hud-crystal-count");
            count.pickingMode = PickingMode.Ignore;
            bar.Add(count);
            row.Add(bar);

            // Строка создаётся скрытой: появится, только когда Refresh
            // подтвердит наличие кристаллов этого цвета в корзине.
            row.style.display = DisplayStyle.None;

            _basketCrystalFills.Add(fill);
            _basketCrystalCounts.Add(count);
            _basketRows.Add(row);
            _basketContainer.Add(row);
        }
    }

    public void Refresh(PlayerStatsModel stats)
    {
        for (int i = 0; i < _basketCrystalFills.Count && i < stats.BasketContents.Length; i++)
        {
            // Кристаллов с нулевым количеством в корзине нет - строка скрывается,
            // чтобы не оставлять пустое пространство.
            bool show = stats.BasketContents[i] > 0;
            _basketRows[i].style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                continue;
            }

            long content = stats.BasketContents[i];
            uint capacity = stats.BasketCapacity;
            _basketCrystalCounts[i].text = $"{FormatCompact(content)}/{FormatCompact(capacity)}";

            int percent = capacity > 0 ? (int)(content * 100 / capacity) : 0;

            // Зона красит сразу всю строку: число, трек и заливку.
            // <100% лайм, 100-114% жёлтый, 114%+ коралл (перегруз).
            VisualElement row = _basketRows[i];
            row.EnableInClassList("hud-crystal-row--ok", percent < 100);
            row.EnableInClassList("hud-crystal-row--warn", percent is >= 100 and <= 114);
            row.EnableInClassList("hud-crystal-row--over", percent > 114);

            // В перегрузе полоса заполнена целиком, состояние передаёт цвет.
            VisualElement fill = _basketCrystalFills[i];
            fill.style.width = new StyleLength(Length.Percent(Mathf.Clamp(percent, 0, 100)));
        }
    }

    private static string FormatCompact(long val)
    {
        if (val >= 1_000_000)
        {
            return $"{val / 1_000_000f:F1}M";
        }

        if (val >= 10_000)
        {
            return $"{val / 1_000}K";
        }

        return val.ToString("N0");
    }
}
