#nullable enable

using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Game.Player.Stats;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.HUD;

internal sealed class PlayerHUDStatsPresenter
{
    private readonly IPlayerStats _stats;
    private readonly ILocalizationService _localization;
    private readonly PlayerHUDSkeletonPulse _skeletonPulse;
    private readonly PlayerHUDBasketView _basketView;
    private readonly Color _hpBarFillColor;
    private readonly Color _hpBarLowColor;
    private readonly Label? _nicknameLabel;
    private readonly Label? _levelLabel;
    private readonly Label? _hpCountLabel;
    private readonly VisualElement? _hpBarFill;
    private readonly Label? _moneyLabel;
    private readonly Label? _creditsLabel;
    private readonly Label? _geologyLabel;
    private readonly Label? _basketPercentLabel;
    private readonly VisualElement? _basketSeparator;
    private readonly VisualElement? _basketRow;

    public PlayerHUDStatsPresenter(
        IPlayerStats stats,
        ILocalizationService localization,
        PlayerHUDSkeletonPulse skeletonPulse,
        PlayerHUDBasketView basketView,
        Color hpBarFillColor,
        Color hpBarLowColor,
        Label? nicknameLabel,
        Label? levelLabel,
        Label? hpCountLabel,
        VisualElement? hpBarFill,
        Label? moneyLabel,
        Label? creditsLabel,
        Label? geologyLabel,
        Label? basketPercentLabel,
        VisualElement? basketSeparator,
        VisualElement? basketRow,
        bool _isLoadedInitially)
    {
        _stats = stats;
        _localization = localization;
        _skeletonPulse = skeletonPulse;
        _basketView = basketView;
        _hpBarFillColor = hpBarFillColor;
        _hpBarLowColor = hpBarLowColor;
        _nicknameLabel = nicknameLabel;
        _levelLabel = levelLabel;
        _hpCountLabel = hpCountLabel;
        _hpBarFill = hpBarFill;
        _moneyLabel = moneyLabel;
        _creditsLabel = creditsLabel;
        _geologyLabel = geologyLabel;
        _basketPercentLabel = basketPercentLabel;
        _basketSeparator = basketSeparator;
        _basketRow = basketRow;
        IsLoaded = _isLoadedInitially;
    }

    public bool IsLoaded { get; private set; }

    public void Refresh()
    {
        IPlayerStats stats = _stats;
        if (!IsLoaded && (stats.Health > 0 || stats.Level > 0 || stats.Money > 0 || !string.IsNullOrEmpty(stats.Nickname)))
        {
            IsLoaded = true;
            _skeletonPulse.Stop();
        }

        if (_nicknameLabel != null)
        {
            _nicknameLabel.text = string.IsNullOrEmpty(stats.Nickname) ? "---" : stats.Nickname;
        }

        if (_levelLabel != null)
        {
            _levelLabel.text = IsLoaded ? _localization.Get("hud.level", stats.Level) : string.Empty;
        }

        float percentage = stats.HealthPercent;
        if (_hpBarFill != null)
        {
            _hpBarFill.style.width = new Length(percentage * 100, LengthUnit.Percent);
            _hpBarFill.style.backgroundColor = percentage < 0.25f ? _hpBarLowColor : _hpBarFillColor;
        }

        if (_hpCountLabel != null)
        {
            _hpCountLabel.text = IsLoaded ? $"{stats.Health:N0} / {stats.MaxHealth:N0}" : "-- / --";
        }

        if (_moneyLabel != null)
        {
            _moneyLabel.text = IsLoaded ? $"<color=lime>${stats.Money:N0}</color>" : "---";
        }

        if (_creditsLabel != null)
        {
            _creditsLabel.text = IsLoaded ? $"<color=yellow>C {stats.Credits:N0}</color>" : "---";
        }

        if (_geologyLabel != null)
        {
            bool showGeology = IsLoaded && stats.GeologyCurrent > 0;
            _geologyLabel.text = showGeology
                ? _localization.Get("hud.geology", stats.GeologyCurrent, stats.GeologyMax, stats.GeologyText)
                : string.Empty;
            _geologyLabel.style.display = showGeology ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_basketPercentLabel != null)
        {
            bool showBasket = IsLoaded && stats.BasketMaxPercent > 0;
            _basketPercentLabel.text = showBasket
                ? _localization.Get("hud.basket_percent", stats.BasketMaxPercent)
                : string.Empty;
            _basketPercentLabel.style.display = showBasket ? DisplayStyle.Flex : DisplayStyle.None;
            SetDisplayed(_basketSeparator, showBasket);
            SetDisplayed(_basketRow, showBasket);
        }

        _basketView.Refresh(stats);
    }

    private static void SetDisplayed(VisualElement? element, bool visible)
    {
        if (element != null)
        {
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
