#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.UI.Controls;
using Kern.UI.Motion;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

// Панель игрока и груз — форматы макета visual/kern-ui (js/hud.js renderPlayer, renderCargo):
// прочность — 20 точек по 5 % и число цветом доли; деньги и кредиты — в ячейках по 5 знаков, длиннее — сокращение;
// груз — 20 ячеек цветом порога 50 / 100 / 115 / 200 %; геология — ячейка на блок (до 20), счёт «N/M», последний блок словом.
internal sealed class PlayerHUDStatsPresenter
{
    private const int HpDots = 20;
    private const int CargoCells = 20;
    private const int GeologyCellsMax = 20;
    private const int WalletDigits = 5;

    private static readonly string[] CargoBands = { "cargo--low", "cargo--normal", "cargo--warn", "cargo--high", "cargo--danger" };

    private readonly IPlayerStats _stats;
    private readonly PlayerHUDBasketView _basketView;
    private readonly Label _nickname;
    private readonly Label _playerId;
    private readonly VisualElement _hpDots;
    private readonly VisualElement _hpValue;
    private readonly Label _hpCount;
    private readonly Label _money;
    private readonly Label _credits;
    private readonly VisualElement _cargoPanel;
    private readonly VisualElement _cargoPercent;
    private readonly Label _cargoPercentLabel;
    private readonly VisualElement _cargoCells;
    private readonly VisualElement _geology;
    private readonly VisualElement _geologyCells;
    private readonly Label _geologyCount;
    private readonly Label _geologyName;

    private float _hpWas = 1f;
    private int _cargoWas;
    private string _cargoBand = string.Empty;

    public PlayerHUDStatsPresenter(IPlayerStats stats, PlayerHUDBasketView basketView, VisualElement tree)
    {
        _stats = stats;
        _basketView = basketView;
        _nickname = Require<Label>(tree, "NicknameLabel");
        _playerId = Require<Label>(tree, "PlayerIdLabel");
        _hpDots = Require<VisualElement>(tree, "HPDots");
        _hpCount = Require<Label>(tree, "HPCountLabel");
        _hpValue = _hpCount.parent;
        _money = Require<Label>(tree, "MoneyLabel");
        _credits = Require<Label>(tree, "CreditsLabel");
        _cargoPanel = Require<VisualElement>(tree, "CargoPanel");
        _cargoPercent = Require<VisualElement>(tree, "CargoPercent");
        _cargoPercentLabel = Require<Label>(tree, "BasketPercentLabel");
        _cargoCells = Require<VisualElement>(tree, "CargoCells");
        _geology = Require<VisualElement>(tree, "GeologyBlock");
        _geologyCells = Require<VisualElement>(tree, "GeologyCells");
        _geologyCount = Require<Label>(tree, "GeologyCountLabel");
        _geologyName = Require<Label>(tree, "GeologyLabel");
        KernQuant.Set(_hpDots, HpDots, 0);
        KernQuant.Set(_cargoCells, CargoCells, 0);
    }

    public bool IsLoaded { get; private set; }

    public void Refresh()
    {
        IPlayerStats stats = _stats;
        if (!IsLoaded && (stats.Health > 0 || stats.Level > 0 || stats.Money > 0 || !string.IsNullOrEmpty(stats.Nickname)))
        {
            IsLoaded = true;
        }

        _nickname.text = string.IsNullOrEmpty(stats.Nickname) ? "---" : stats.Nickname;
        _playerId.text = stats.PlayerId > 0 ? "#" + stats.PlayerId : string.Empty;
        SetDisplayed(_playerId, stats.PlayerId > 0);

        RefreshHealth(stats);
        RefreshWallet(stats);
        RefreshCargo(stats);
        RefreshGeology(stats);
        _basketView.Refresh(stats);
    }

    private void RefreshHealth(IPlayerStats stats)
    {
        if (!IsLoaded)
        {
            _hpCount.text = "--";
            return;
        }

        float f = Mathf.Clamp01(stats.HealthPercent);
        // Точка — 5 %: живой робот — хотя бы одна точка, раненый — никогда не все 20
        int on = f <= 0f ? 0 : f >= 1f ? HpDots : Math.Min(HpDots - 1, Math.Max(1, Mathf.RoundToInt(f * HpDots)));
        Color tone = HpTone(f);
        KernQuant.Set(_hpDots, HpDots, on, (dot, lit) =>
        {
            dot.style.borderTopColor = tone;
            dot.style.borderRightColor = tone;
            dot.style.borderBottomColor = tone;
            dot.style.borderLeftColor = tone;
            dot.style.backgroundColor = lit ? tone : Color.clear;
        });
        KernCount.Set(_hpCount, stats.Health, KernNumbers.Group);
        _hpValue.style.color = tone;
        _hpCount.style.color = tone;
        if (f < 0.25f && _hpWas >= 0.25f)
        {
            KernFlash.Play(_hpValue);
        }

        _hpWas = f;
    }

    // Цвет прочности по доле (js/hud.js hpTone): красный → жёлтый → зелёный
    private static Color HpTone(float f) => new(
        Mathf.Sqrt(Mathf.Sqrt(1f - f)),
        Mathf.Sqrt(Mathf.Sqrt(f)),
        f * (1f - f));

    private void RefreshWallet(IPlayerStats stats)
    {
        if (!IsLoaded)
        {
            _money.text = "---";
            _credits.text = "---";
            return;
        }

        KernCount.Set(_money, stats.Money, v => KernNumbers.Cut(v, WalletDigits));
        KernCount.Set(_credits, stats.Credits, v => KernNumbers.Cut(v, WalletDigits));
        // Полное значение — подсказкой ячейки, когда число сокращено
        bool moneyCut = KernNumbers.Short(stats.Money, WalletDigits) != null;
        bool creditsCut = KernNumbers.Short(stats.Credits, WalletDigits) != null;
        _money.parent.tooltip = moneyCut ? "$" + KernNumbers.Group(stats.Money) : string.Empty;
        _credits.parent.tooltip = creditsCut ? KernNumbers.Group(stats.Credits) : string.Empty;
        _money.EnableInClassList("is-cut", moneyCut);
        _credits.EnableInClassList("is-cut", creditsCut);
    }

    private void RefreshCargo(IPlayerStats stats)
    {
        long[] contents = stats.BasketContents;
        long max = 0;
        for (int i = 0; i < contents.Length; i++)
        {
            max = Math.Max(max, contents[i]);
        }

        // Пустая корзина — панели нет
        bool show = IsLoaded && max > 0 && stats.BasketCapacity > 0;
        SetDisplayed(_cargoPanel, show);
        if (!show)
        {
            return;
        }

        double share = max * 100d / stats.BasketCapacity;
        int percent = (int)Math.Round(share);
        KernCount.Set(_cargoPercentLabel, percent, v => v.ToString(System.Globalization.CultureInfo.InvariantCulture));

        string band = percent < 50 ? CargoBands[0] : percent < 100 ? CargoBands[1] : percent < 115 ? CargoBands[2] : percent < 200 ? CargoBands[3] : CargoBands[4];
        if (band != _cargoBand)
        {
            foreach (string b in CargoBands)
            {
                _cargoPercent.EnableInClassList(b, b == band);
                _cargoCells.EnableInClassList(b, b == band);
            }

            _cargoBand = band;
        }

        // Новый порог груза (100 / 115 / 200 %) — тревога: две вспышки числа и полосы
        if (Band(percent) > Band(_cargoWas))
        {
            KernFlash.Play(_cargoPercent);
            KernFlash.Play(_cargoCells);
        }

        _cargoWas = percent;
        // Ячейка — 5 %; непустой груз — хотя бы одна ячейка. Перегруз — полная полоса цвета порога
        int lit = Math.Min(CargoCells, (int)Math.Ceiling(share / 5d));
        KernQuant.Set(_cargoCells, CargoCells, lit);
    }

    private static int Band(int percent) => percent < 100 ? 0 : percent < 115 ? 1 : percent < 200 ? 2 : 3;

    private void RefreshGeology(IPlayerStats stats)
    {
        bool show = IsLoaded && stats.GeologyCurrent > 0 && stats.GeologyMax > 0;
        SetDisplayed(_geology, show);
        if (!show)
        {
            return;
        }

        int n = stats.GeologyCurrent, total = stats.GeologyMax;
        int cells = Math.Min(total, GeologyCellsMax);
        int lit = Math.Max(1, Mathf.RoundToInt((float)n / total * cells));
        KernQuant.Set(_geologyCells, cells, lit);
        _geologyCount.text = n + "/" + total;
        _geologyName.text = (stats.GeologyText ?? string.Empty).ToUpperInvariant();
    }

    private static T Require<T>(VisualElement tree, string name) where T : VisualElement =>
        tree.Q<T>(name) ?? throw new InvalidOperationException($"[PlayerHUD] {name} is missing from PlayerHUD.uxml.");

    private static void SetDisplayed(VisualElement element, bool visible) =>
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
}
