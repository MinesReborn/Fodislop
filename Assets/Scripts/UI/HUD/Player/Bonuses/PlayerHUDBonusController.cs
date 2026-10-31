#nullable enable

using System;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.UI.Input;
using Kern.UI.Screens;

namespace Kern.UI.HUD;

public sealed class PlayerHUDBonusController
{
    private readonly Action<ElementClickPacket> _sendPacket;
    private readonly ILocalizationService _loc;
    private readonly UIInputManager? _uiInput;
    private Button? _bonusButton;
    private VisualElement? _bonusPanel;
    private Label? _bonusStatusLabel;
    private Button? _bonusClaimButton;
    private bool _isBonusOpen;

    public PlayerHUDBonusController(
        Action<ElementClickPacket> sendPacket,
        ILocalizationService loc,
        UIInputManager? uiInput = null)
    {
        _sendPacket = sendPacket;
        _loc = loc;
        _uiInput = uiInput;
    }

    public void Initialize(VisualElement root)
    {
        _bonusButton = root.Q<Button>("BonusButton");
        if (_bonusButton != null)
        {
            _bonusButton.focusable = false;
            _bonusButton.clicked += ToggleBonusPanel;
        }

        _bonusPanel = root.Q<VisualElement>("BonusPanel");
        if (_bonusPanel != null)
        {
            _bonusPanel.focusable = false;
            _bonusPanel.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_isBonusOpen)
                {
                    ClampPanelToScreen();
                }
            });
        }

        var bonusCloseButton = root.Q<Button>("BonusCloseButton");
        if (bonusCloseButton != null)
        {
            bonusCloseButton.focusable = false;
            bonusCloseButton.clicked += CloseBonusPanel;
        }

        _bonusStatusLabel = root.Q<Label>("BonusStatusLabel");
        _bonusClaimButton = root.Q<Button>("BonusClaimButton");
        if (_bonusClaimButton != null)
        {
            _bonusClaimButton.focusable = false;
            _bonusClaimButton.clicked += ClaimDailyBonus;
        }
    }

    public void Update()
    {
        if (!_isBonusOpen)
        {
            return;
        }

        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame &&
            _uiInput != null &&
            !_uiInput.IsEscapeConsumedThisFrame)
        {
            CloseBonusPanel();
            _uiInput.ConsumeEscape();
        }
    }

    public void ToggleBonusPanel()
    {
        if (_bonusPanel == null)
        {
            return;
        }

        _isBonusOpen = !_isBonusOpen;
        UIState.SetHidden(_bonusPanel, !_isBonusOpen);
        if (_isBonusOpen)
        {
            ClampPanelToScreen();
        }
    }

    public void CloseBonusPanel()
    {
        _isBonusOpen = false;
        if (_bonusPanel != null)
        {
            UIState.Hide(_bonusPanel);
        }
    }

    private void ClampPanelToScreen()
    {
        if (_bonusPanel == null || _bonusPanel.panel == null)
        {
            return;
        }

        Rect screen = _bonusPanel.panel.visualTree.layout;
        if (screen.width <= 0f || screen.height <= 0f)
        {
            return;
        }

        float panelWidth = _bonusPanel.layout.width > 0f ? _bonusPanel.layout.width : 260f;
        float panelHeight = _bonusPanel.layout.height > 0f ? _bonusPanel.layout.height : 120f;

        float targetLeft = _bonusButton != null && _bonusButton.layout.width > 0f
            ? _bonusButton.layout.x
            : 256f;
        float targetTop = _bonusButton != null && _bonusButton.layout.height > 0f
            ? _bonusButton.layout.yMax + 6f
            : 66f;

        float clampedLeft = Mathf.Clamp(targetLeft, 10f, Mathf.Max(10f, screen.width - panelWidth - 10f));
        float clampedTop = Mathf.Clamp(targetTop, 10f, Mathf.Max(10f, screen.height - panelHeight - 10f));

        _bonusPanel.style.left = clampedLeft;
        _bonusPanel.style.top = clampedTop;
    }

    public void UpdateDailyBonusPanel(IPlayerStats? stats)
    {
        if (_bonusStatusLabel == null || _bonusButton == null || stats == null)
        {
            return;
        }

        if (stats.DailyBonusAvailable)
        {
            UIState.Show(_bonusButton);
            _bonusStatusLabel.text = _loc.Get("hud.bonus.available");
            _bonusStatusLabel.style.color = Color.green;
            if (_bonusClaimButton != null)
            {
                UIState.Show(_bonusClaimButton);
            }
        }
        else
        {
            UIState.Hide(_bonusButton);
            CloseBonusPanel();
            _bonusStatusLabel.text = _loc.Get("hud.bonus.none");
            _bonusStatusLabel.style.color = Color.gray;
            if (_bonusClaimButton != null)
            {
                UIState.Hide(_bonusClaimButton);
            }
        }
    }

    public void ClaimDailyBonus()
    {
        _sendPacket(new ElementClickPacket("daily_bonus", 0, Array.Empty<StringPairPacket>()));
    }
}
