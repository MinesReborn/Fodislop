#nullable enable

using System;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using MinesServer.Networking.Client.Packets.Chat;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI;

internal sealed class ChatColorController
{
    private readonly INetworkService _networkService;
    private readonly Button? _colorButton;
    private readonly VisualElement? _colorGrid;
    private System.Drawing.Color _currentColor = ChatColorPalette.DefaultColor;

    public ChatColorController(
        INetworkService networkService,
        Button? colorButton,
        VisualElement? colorGrid)
    {
        _networkService = networkService;
        _colorButton = colorButton;
        _colorGrid = colorGrid;

        if (_colorButton != null)
        {
            _colorButton.clicked += ToggleColorGrid;
            ApplyButtonColor(_currentColor);
        }

        if (_colorGrid != null)
        {
            foreach (var c in ChatColorPalette.PresetColors)
            {
                var swatch = new Button(() => SelectColor(c));
                swatch.AddToClassList("gchat-swatch");
                swatch.style.backgroundColor = new Color(c.R / 255f, c.G / 255f, c.B / 255f);
                _colorGrid.Add(swatch);
            }
        }
    }

    public void ToggleColorGrid()
    {
        if (_colorGrid != null)
        {
            _colorGrid.style.display = _colorGrid.style.display == DisplayStyle.None
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    public void CloseColorGrid()
    {
        if (_colorGrid != null)
        {
            _colorGrid.style.display = DisplayStyle.None;
        }
    }

    public void SelectColor(System.Drawing.Color color)
    {
        _currentColor = color;
        ApplyButtonColor(color);
        CloseColorGrid();

        try
        {
            _networkService.Send(new ChangeChatColorPacket(color));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GlobalChatUI] Не удалось отправить изменение цвета чата: {ex}");
        }
    }

    private void ApplyButtonColor(System.Drawing.Color color)
    {
        if (_colorButton != null)
        {
            _colorButton.style.backgroundColor = new Color(color.R / 255f, color.G / 255f, color.B / 255f);
        }
    }
}
