#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Вкладка «Управление» — макет: клавиши движения и поворота (по две), медленное движение,
// «Помощь в движении», действия и постройки, мышь (показ без переназначения), «Сбросить до стандартных».
// Клик по клавише — перехват следующего нажатия (Escape отменяет); на время перехвата игровые клавиши
// и Escape паузы молчат (setCapturing). Привязка сохраняется в конфиг и действует сразу.
internal sealed class SettingsControlsPage
{
    private readonly UIDocument _doc;
    private readonly IClientConfigManager _config;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
    private readonly Action<bool> _setCapturing;

    private readonly List<(KeyAction Action, Button Key)> _keys = new();
    private VisualElement? _page;
    private KeyAction? _capturedAction;
    private Button? _capturedKey;
    private IVisualElementScheduledItem? _captureTask;

    public SettingsControlsPage(
        UIDocument doc,
        IClientConfigManager config,
        ICollection<Action> refreshers,
        ILocalizationService loc,
        Action<bool> setCapturing)
    {
        _doc = doc;
        _config = config;
        _refreshers = refreshers;
        _loc = loc;
        _setCapturing = setCapturing;
    }

    public void Build(VisualElement page)
    {
        _page = page;
        foreach (KeyRow row in SettingsKeyActions.Movement)
        {
            page.Add(KeyRowElement(row));
        }

        page.Add(SettingsFields.Switch(
            _loc.Get("settings.controls.movement_assist"),
            () => _config.Config.Interface.MovementAssist,
            value => _config.UpdateSection(config => config.Interface, settings => settings.MovementAssist = value),
            _refreshers));

        foreach (KeyRow row in SettingsKeyActions.Actions)
        {
            page.Add(KeyRowElement(row));
        }

        page.Add(SettingsFields.Row(
            _loc.Get("settings.controls.mouse_move"),
            SettingsFields.Keycap(_loc.Get("settings.controls.mouse_move_key"), null)));
        VisualElement route = SettingsFields.Row(
            _loc.Get("settings.controls.mouse_route"),
            SettingsFields.Keycap(_loc.Get("settings.controls.mouse_route_key"), null));
        route.AddToClassList("field--last");
        page.Add(route);

        page.Add(SettingsFields.Foot(_loc.Get("settings.controls.reset"), ResetToDefaults));

        // При каждом открытии окна клавиши перечитываются из живого конфига
        _refreshers.Add(RefreshKeys);
    }

    private VisualElement KeyRowElement(KeyRow row)
    {
        var caps = new VisualElement();
        caps.AddToClassList("keycaps");
        caps.Add(KeyButton(row.Primary));
        if (row.Alternate is KeyAction alternate)
        {
            caps.Add(KeyButton(alternate));
        }

        return SettingsFields.Row(_loc.Get(row.LabelKey), caps);
    }

    private Button KeyButton(KeyAction action)
    {
        Button key = null!;
        key = SettingsFields.Keycap(FormatKey(action.Get(_config.Config.Interface)), () => BeginCapture(action, key));
        _keys.Add((action, key));
        return key;
    }

    private void RefreshKeys()
    {
        CancelCapture();
        foreach ((KeyAction action, Button key) in _keys)
        {
            key.text = FormatKey(action.Get(_config.Config.Interface));
        }
    }

    private void ResetToDefaults()
    {
        PauseMenuConfirmation.ConfirmResetControls(
            _doc,
            () =>
            {
                _config.UpdateSection(
                    config => config.Interface,
                    settings =>
                    {
                        settings.MovementAssist = InterfaceSettings.DefaultMovementAssist;
                        foreach (KeyAction action in SettingsKeyActions.All)
                        {
                            action.Set(settings, action.DefaultKey);
                        }
                    });
                foreach (Action refresh in new List<Action>(_refreshers))
                {
                    refresh();
                }
            },
            _loc);
    }

    // Имя клавиши для показа: стрелки — значками, «LeftShift» — «Left Shift», не назначена — словом
    private string FormatKey(string keyName) => keyName switch
    {
        "None" => _loc.Get("settings.controls.unassigned"),
        "UpArrow" => "↑",
        "DownArrow" => "↓",
        "LeftArrow" => "←",
        "RightArrow" => "→",
        _ => Regex.Replace(keyName, @"\B[A-Z]", " $0"),
    };

    private void BeginCapture(KeyAction action, Button key)
    {
        if (_capturedKey != null)
        {
            KeyAction previous = _capturedAction!.Value;
            Button previousKey = _capturedKey;
            StopCapture();
            previousKey.text = FormatKey(previous.Get(_config.Config.Interface));
        }

        _capturedAction = action;
        _capturedKey = key;
        key.text = _loc.Get("settings.controls.press_key");
        key.AddToClassList("is-listening");
        _setCapturing(true);
        // Пересборка окна (смена языка) отцепляет клавишу: перехват обязан погаснуть, иначе ввод останется заблокирован
        key.RegisterCallbackOnce<DetachFromPanelEvent>(_ => CancelCapture());
        _captureTask = key.schedule.Execute(PollCapture).Every(0L);
    }

    private void CancelCapture()
    {
        if (_capturedKey == null)
        {
            return;
        }

        KeyAction action = _capturedAction!.Value;
        Button key = _capturedKey;
        StopCapture();
        key.text = FormatKey(action.Get(_config.Config.Interface));
    }

    private void PollCapture()
    {
        // Окно закрыли или ушли на другую вкладку посреди перехвата — гасим его
        if (!Application.isFocused || _capturedKey == null || _capturedKey.panel == null ||
            _page?.resolvedStyle.display == DisplayStyle.None)
        {
            CancelCapture();
            return;
        }

        Keyboard? keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            CancelCapture();
            return;
        }

        foreach (var control in keyboard.allKeys)
        {
            if (control.wasPressedThisFrame)
            {
                AssignKey(control.keyCode);
                return;
            }
        }
    }

    private void AssignKey(Key assigned)
    {
        // Клавиши, занятые окнами игры (чат, карта, инвентарь, пауза, быстрые слоты), не назначаются
        if (assigned is Key.Enter or Key.NumpadEnter or Key.Tab or Key.M or Key.I or
            Key.P or Key.R or Key.T or Key.F1 or Key.F2 or
            Key.Digit1 or Key.Digit2 or Key.Digit3 or Key.Digit4 or Key.Digit5 or
            Key.Digit6 or Key.Digit7 or Key.Digit8 or Key.Digit9)
        {
            _capturedKey!.text = _loc.Get("settings.controls.reserved_key");
            return;
        }

        KeyAction action = _capturedAction!.Value;
        string keyName = assigned.ToString();
        StopCapture();
        _config.UpdateSection(
            config => config.Interface,
            settings =>
            {
                // Обмен привязками: клавиша остаётся у одного действия
                string previousKey = action.Get(settings);
                foreach (KeyAction other in SettingsKeyActions.All)
                {
                    if (other.LabelKey != action.LabelKey &&
                        string.Equals(other.Get(settings), keyName, StringComparison.OrdinalIgnoreCase))
                    {
                        other.Set(settings, previousKey);
                    }
                }

                action.Set(settings, keyName);
            });
        RefreshKeys();
    }

    private void StopCapture()
    {
        _captureTask?.Pause();
        _captureTask = null;
        _setCapturing(false);
        _capturedKey?.RemoveFromClassList("is-listening");
        _capturedKey = null;
        _capturedAction = null;
    }
}
