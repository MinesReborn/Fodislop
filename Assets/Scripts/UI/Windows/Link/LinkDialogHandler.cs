#nullable enable

using System;
using Kern.Core;
using Kern.Core.Localization;
using Kern.Core.Platform;
using Kern.UI.Input;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI.Windows;

// Сервер просит открыть ссылку (OpenURLPacket) — макет visual/kern-ui #linkDialog. Браузер открывается только
// после «Открыть»: адрес показан целиком, домен выделен, чтобы было видно, куда ведёт. Ссылки не http(s)
// окно не показывает вовсе — их отклоняет ExternalUrlOpener.
public sealed class LinkDialogHandler : IDisposable
{
    private readonly UIDocument _doc;
    private readonly UIInputManager _uiInput;
    private readonly ILocalizationService _loc;
    private VisualElement? _overlay;
    private Label? _host;
    private Label? _path;
    private string? _url;
    private IVisualElementScheduledItem? _keyWatcher;

    public LinkDialogHandler(UIDocument doc, UIInputManager uiInput, ILocalizationService loc)
    {
        _doc = doc;
        _uiInput = uiInput;
        _loc = loc;
    }

    public bool IsShowing => _overlay != null && !UIState.IsHidden(_overlay);

    public void Show(string url)
    {
        if (!ExternalUrlOpener.IsAllowed(url))
        {
            Debug.LogWarning($"[LinkDialog] Отклонена ссылка не http(s): '{url}'");
            return;
        }

        EnsureCreated();
        Localize(_overlay!);
        var uri = new Uri(url);
        _url = url;
        _host!.text = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        string rest = uri.PathAndQuery + uri.Fragment;
        _path!.text = rest == "/" ? string.Empty : rest;

        VisualElement overlay = _overlay!;
        UIState.Show(overlay);
        overlay.SetEnabled(true);
        overlay.pickingMode = PickingMode.Position;
        if (_doc.rootVisualElement.panel != null)
        {
            _keyWatcher ??= overlay.schedule.Execute(PollKeys).Every(16);
            _keyWatcher.Resume();
        }
    }

    public void Hide()
    {
        _keyWatcher?.Pause();
        _url = null;
        if (_overlay != null)
        {
            UIState.Hide(_overlay);
            _overlay.SetEnabled(false);
            _overlay.pickingMode = PickingMode.Ignore;
        }
    }

    public void Dispose()
    {
        _keyWatcher?.Pause();
        _keyWatcher = null;
        _overlay?.RemoveFromHierarchy();
        _overlay = null;
        _host = null;
        _path = null;
    }

    private void Open()
    {
        string? url = _url;
        Hide();
        ExternalUrlOpener.TryOpen(url, nameof(LinkDialogHandler));
    }

    // Клавиши — из планировщика панели, как у ModalWindowHandler: без клавиатурного фокуса KeyDownEvent не приходит.
    // Escape — «Отмена», Enter — «Открыть».
    private void PollKeys()
    {
        if (!IsShowing || Keyboard.current == null)
        {
            return;
        }

        bool escape = Keyboard.current.escapeKey.wasPressedThisFrame;
        bool enter = Keyboard.current.enterKey.wasPressedThisFrame ||
                     Keyboard.current.numpadEnterKey.wasPressedThisFrame;
        if (escape && !_uiInput.IsChatFocused)
        {
            // Метка не даёт паузе открыться тем же нажатием
            _uiInput.ConsumeEscape();
            Hide();
        }
        else if (enter && !_uiInput.IsChatFocused)
        {
            Open();
        }
    }

    private void EnsureCreated()
    {
        if (_overlay != null)
        {
            if (!_doc.rootVisualElement.Contains(_overlay))
            {
                _doc.rootVisualElement.Add(_overlay);
            }

            return;
        }

        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(ResourcePaths.LinkDialogUxml) ??
            throw new InvalidOperationException($"[LinkDialog] Resources/{ResourcePaths.LinkDialogUxml}.uxml is required.");
        TemplateContainer tree = template.Instantiate();
        _overlay = Require<VisualElement>(tree, "LinkOverlay");
        _host = Require<Label>(tree, "LinkHost");
        _path = Require<Label>(tree, "LinkPath");
        Require<Button>(tree, "LinkCancel").clicked += Hide;
        Require<Button>(tree, "LinkOpen").clicked += Open;
        _overlay.SetEnabled(false);

        // Оверлей — прямой ребёнок корня, как у ModalWindowHandler: обёртка шаблона схлопывается в ноль
        _overlay.RemoveFromHierarchy();
        _doc.rootVisualElement.Add(_overlay);
    }

    // Тексты ставятся при каждом показе: язык мог смениться, пока окно было скрыто
    private void Localize(VisualElement tree)
    {
        Require<Label>(tree, "LinkSource").text = _loc.Get("modal.source_server").ToUpperInvariant();
        Require<Label>(tree, "LinkTitle").text = _loc.Get("link.title").ToUpperInvariant();
        Require<Label>(tree, "LinkText").text = _loc.Get("link.text");
        Require<Button>(tree, "LinkCancel").text = _loc.Get("link.cancel");
        Require<Button>(tree, "LinkOpen").text = _loc.Get("link.open");
    }

    private static T Require<T>(VisualElement tree, string name)
        where T : VisualElement =>
        tree.Q<T>(name) ?? throw new InvalidOperationException($"[LinkDialog] {name} is missing from LinkDialog.uxml.");
}
