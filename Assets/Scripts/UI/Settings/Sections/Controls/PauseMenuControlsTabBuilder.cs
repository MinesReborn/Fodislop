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

using Kern.UI.Input;

namespace Kern.UI.Settings;

// Вкладка «Управление» настроек паузы: схема движения дрона (клавиатура или
// указатель мыши) и список клавиш. Клавиатурные действия перебиндиваются:
// клик по кнопке с клавишей запускает перехват — на это время
// UIInputManager.IsKeyCaptureInProgress гасит игровые хоткеи и Escape меню
// паузы, следующая нажатая клавиша (Escape отменяет) сохраняется в конфиг
// Interface и действует сразу: PlayerInputHandler читает привязки из живого
// конфига, а не из хардкода. Значение привязки — имя enum
// UnityEngine.InputSystem.Key; «Сбросить до стандартных» возвращает дефолты.
internal sealed class PauseMenuControlsTabBuilder
{
    // Одно перебиндиваемое действие: ключ локализации, доступ к полю
    // InterfaceSettings и клавиша по умолчанию.
    private readonly record struct KeyAction(
        string LabelKey,
        Func<InterfaceSettings, string> Get,
        Action<InterfaceSettings, string> Set,
        string DefaultKey);

    private static readonly KeyAction[] s_keyActions = new KeyAction[]
    {

        new(
            "settings.controls.key_move_up",
            settings => settings.KeyMoveUp,
            (settings, key) => settings.KeyMoveUp = key,
            InterfaceSettings.DefaultKeyMoveUp),
        new(
            "settings.controls.key_move_down",
            settings => settings.KeyMoveDown,
            (settings, key) => settings.KeyMoveDown = key,
            InterfaceSettings.DefaultKeyMoveDown),
        new(
            "settings.controls.key_move_left",
            settings => settings.KeyMoveLeft,
            (settings, key) => settings.KeyMoveLeft = key,
            InterfaceSettings.DefaultKeyMoveLeft),
        new(
            "settings.controls.key_move_right",
            settings => settings.KeyMoveRight,
            (settings, key) => settings.KeyMoveRight = key,
            InterfaceSettings.DefaultKeyMoveRight),
        new(
            "settings.controls.key_move_up_alternate",
            settings => settings.KeyMoveUpAlternate,
            (settings, key) => settings.KeyMoveUpAlternate = key,
            InterfaceSettings.DefaultKeyMoveUpAlternate),
        new(
            "settings.controls.key_move_down_alternate",
            settings => settings.KeyMoveDownAlternate,
            (settings, key) => settings.KeyMoveDownAlternate = key,
            InterfaceSettings.DefaultKeyMoveDownAlternate),
        new(
            "settings.controls.key_move_left_alternate",
            settings => settings.KeyMoveLeftAlternate,
            (settings, key) => settings.KeyMoveLeftAlternate = key,
            InterfaceSettings.DefaultKeyMoveLeftAlternate),
        new(
            "settings.controls.key_move_right_alternate",
            settings => settings.KeyMoveRightAlternate,
            (settings, key) => settings.KeyMoveRightAlternate = key,
            InterfaceSettings.DefaultKeyMoveRightAlternate),
        new(
            "settings.controls.key_turn_only",
            settings => settings.KeyTurnOnly,
            (settings, key) => settings.KeyTurnOnly = key,
            InterfaceSettings.DefaultKeyTurnOnly),
        new(
            "settings.controls.key_turn_only_alternate",
            settings => settings.KeyTurnOnlyAlternate,
            (settings, key) => settings.KeyTurnOnlyAlternate = key,
            InterfaceSettings.DefaultKeyTurnOnlyAlternate),
        new(
            "settings.controls.key_fast_move",
            settings => settings.KeyFastMove,
            (settings, key) => settings.KeyFastMove = key,
            InterfaceSettings.DefaultKeyFastMove),
        new(
            "settings.controls.key_dig",
            settings => settings.KeyDig,
            (settings, key) => settings.KeyDig = key,
            InterfaceSettings.DefaultKeyDig),
        new(
            "settings.controls.key_autodig",
            settings => settings.KeyAutoDig,
            (settings, key) => settings.KeyAutoDig = key,
            InterfaceSettings.DefaultKeyAutoDig),
        new(
            "settings.controls.key_geo",
            settings => settings.KeyGeo,
            (settings, key) => settings.KeyGeo = key,
            InterfaceSettings.DefaultKeyGeo),
        new(
            "settings.controls.key_heal",
            settings => settings.KeyHeal,
            (settings, key) => settings.KeyHeal = key,
            InterfaceSettings.DefaultKeyHeal),
        new(
            "settings.controls.key_build_cyan",
            settings => settings.KeyBuildCyan,
            (settings, key) => settings.KeyBuildCyan = key,
            InterfaceSettings.DefaultKeyBuildCyan),
        new(
            "settings.controls.key_build_gray",
            settings => settings.KeyBuildGray,
            (settings, key) => settings.KeyBuildGray = key,
            InterfaceSettings.DefaultKeyBuildGray),
        new(
            "settings.controls.key_build_green",
            settings => settings.KeyBuildGreen,
            (settings, key) => settings.KeyBuildGreen = key,
            InterfaceSettings.DefaultKeyBuildGreen),
        new(
            "settings.controls.key_build_white",
            settings => settings.KeyBuildWhite,
            (settings, key) => settings.KeyBuildWhite = key,
            InterfaceSettings.DefaultKeyBuildWhite),

    };

    private readonly UIDocument _doc;
    private readonly IClientConfigManager _clientConfig;
    private readonly ICollection<Action> _refreshers;
    private readonly ILocalizationService _loc;
    private readonly UIInputManager _uiInput;

    private DropdownField? _schemeDropdown;
    private VisualElement? _keyList;

    // Идущий перехват: действие, его кнопка и планируемый опрос клавиатуры.
    private KeyAction? _capturedAction;
    private Button? _capturedButton;
    private IVisualElementScheduledItem? _captureTask;

    public PauseMenuControlsTabBuilder(
        UIDocument doc,
        IClientConfigManager clientConfig,
        ICollection<Action> refreshers,
        ILocalizationService loc,
        UIInputManager uiInput)
    {
        _doc = doc;
        _clientConfig = clientConfig;
        _refreshers = refreshers;
        _loc = loc;
        _uiInput = uiInput ?? throw new ArgumentNullException(nameof(uiInput));
    }

    public VisualElement Build(ScrollView controlsScroll)
    {
        VisualElement controlsSection = controlsScroll.Q<VisualElement>("ControlsSection") ??
            throw new InvalidOperationException("[PauseMenu] ControlsSection is missing from PauseMenu.uxml.");

        // Схема движения — единственная переключаемая часть управления.
        // UpdateSection сохраняет выбор в конфиг сразу, список клавиш ниже
        // перестраивается под выбранную схему.
        var schemeRow = new VisualElement();
        schemeRow.AddToClassList("pause-slider-container");
        var schemeLabel = new Label(_loc.Get("gateway.onb.controls_scheme_label"));
        schemeLabel.AddToClassList("pause-slider-label");
        schemeRow.Add(schemeLabel);

        var schemeDropdown = new DropdownField();
        schemeDropdown.choices = new List<string>
        {
            _loc.Get("gateway.onb.controls.keyboard"),
            _loc.Get("gateway.onb.controls.mouse"),
        };
        schemeDropdown.index = Mathf.Clamp(_clientConfig.Config.Interface.ControlScheme, 0, 1);
        schemeDropdown.RegisterValueChangedCallback(_ =>
        {
            _clientConfig.UpdateSection(
                config => config.Interface,
                settings => settings.ControlScheme = schemeDropdown.index);
            UpdateKeyList();
        });
        schemeRow.Add(schemeDropdown);
        controlsSection.Add(schemeRow);

        // Помощь в движении: при выключенном автокопании робот сам обходит
        // препятствие на пути. Читается из живого конфига каждый шаг.
        controlsSection.Add(PauseMenuUIFactory.CreateBoundToggle(
            _loc.Get("settings.controls.movement_assist"),
            () => _clientConfig.Config.Interface.MovementAssist,
            value => _clientConfig.UpdateSection(
                config => config.Interface,
                settings => settings.MovementAssist = value),
            _refreshers));

        _keyList = new VisualElement();
        _keyList.AddToClassList("controls-key-list");
        controlsSection.Add(_keyList);

        var resetButton = new Button(ResetToDefaults)
        {
            text = _loc.Get("settings.controls.reset"),
        };
        resetButton.AddToClassList("pause-btn");
        controlsSection.Add(resetButton);

        _schemeDropdown = schemeDropdown;
        UpdateKeyList();

        // Как и остальные вкладки: при каждом открытии настроек значения
        // перечитываются из живого конфига, а не из момента сборки меню.
        _refreshers.Add(RefreshFromConfig);

        return controlsScroll;
    }

    private void RefreshFromConfig()
    {
        // Перечитывание может прийти посреди перехвата (открытие настроек,
        // чужой refresher) — сначала гасим его, чтобы не потерять кнопку.
        CancelCapture();
        if (_schemeDropdown != null)
        {
            _schemeDropdown.index = Mathf.Clamp(_clientConfig.Config.Interface.ControlScheme, 0, 1);
        }

        UpdateKeyList();
    }

    // Сброс управления до дефолтного исполняется по текущему управлению:
    // сначала клиентское подтверждение, затем схема и все перебинденные
    // клавиши возвращаются к дефолтным (WASD —
    // InterfaceSettings.DefaultControlScheme), конфиг сохраняется, и вкладка
    // сразу перечитывает актуальное состояние — дропдаун и список клавиш.
    private void ResetToDefaults()
    {
        PauseMenuConfirmation.ConfirmResetControls(
            _doc,
            () =>
            {
                _clientConfig.UpdateSection(
                    config => config.Interface,
                    settings =>
                    {
                        settings.ControlScheme = InterfaceSettings.DefaultControlScheme;
                        settings.MovementAssist = InterfaceSettings.DefaultMovementAssist;
                        foreach (KeyAction action in s_keyActions)
                        {
                            action.Set(settings, action.DefaultKey);
                        }
                    });
                RefreshFromConfig();
            },
            _loc);
    }

    private void UpdateKeyList()
    {
        if (_keyList == null)
        {
            return;
        }

        _keyList.Clear();
        if (_clientConfig.Config.Interface.ControlScheme == 1)
        {
            AddKeyRow(
                _loc.Get("settings.controls.mouse_move"),
                _loc.Get("settings.controls.mouse_move_key"));
            AddKeyRow(
                _loc.Get("settings.controls.mouse_route"),
                _loc.Get("settings.controls.mouse_route_key"));
        }

        foreach (KeyAction action in s_keyActions)
        {
            AddRebindableRow(action);
        }
    }

    private void AddKeyRow(string actionText, string keyText)
    {
        var row = new VisualElement();
        row.AddToClassList("controls-key-row");

        var actionLabel = new Label(actionText);
        actionLabel.AddToClassList("pause-slider-label");
        row.Add(actionLabel);

        var keyLabel = new Label(keyText);
        keyLabel.AddToClassList("controls-key-value");
        row.Add(keyLabel);

        _keyList?.Add(row);
    }

    // Строка перебиндиваемого действия: клик по кнопке с клавишей запускает
    // перехват следующего нажатия.
    private void AddRebindableRow(KeyAction action)
    {
        var row = new VisualElement();
        row.AddToClassList("controls-key-row");

        var actionLabel = new Label(_loc.Get(action.LabelKey));
        actionLabel.AddToClassList("pause-slider-label");
        row.Add(actionLabel);

        var keyButton = new Button
        {
            text = FormatKey(action.Get(_clientConfig.Config.Interface)),
            focusable = false,
        };
        keyButton.AddToClassList("controls-key-value");
        keyButton.AddToClassList("controls-key-btn");
        keyButton.clicked += () => BeginCapture(action, keyButton);
        row.Add(keyButton);

        _keyList?.Add(row);
    }

    private string FormatKey(string keyName)
    {
        // "Space" → "SPACE", "LeftShift" → "LEFT SHIFT": имена enum Key
        // выводятся капсом, как значения в прежнем хардкод-списке.
        if (keyName == "None")
        {
            return _loc.Get("settings.controls.unassigned");
        }

        return Regex.Replace(keyName, @"\B[A-Z]", " $0").ToUpperInvariant();
    }

    private void BeginCapture(KeyAction action, Button button)
    {
        if (_capturedButton != null)
        {
            // Гасим прежний перехват без перестроения списка: перестроение
            // отцепило бы кнопку текущего клика, и планировщик на ней не тикал.
            KeyAction previous = _capturedAction!.Value;
            Button previousButton = _capturedButton;
            StopCaptureTask();
            previousButton.text = FormatKey(previous.Get(_clientConfig.Config.Interface));
        }

        _capturedAction = action;
        _capturedButton = button;
        button.text = _loc.Get("settings.controls.press_key");
        button.AddToClassList("controls-key-btn--listening");
        _uiInput.IsKeyCaptureInProgress = true;
        // Перестроение меню (смена языка) отцепляет дерево вместе с кнопкой:
        // перехват обязан погаснуть, иначе флаг навсегда блокирует ввод.
        button.RegisterCallbackOnce<DetachFromPanelEvent>(OnCaptureButtonDetached);
        _captureTask = button.schedule.Execute(PollCapture).Every(0L);
    }

    private void OnCaptureButtonDetached(DetachFromPanelEvent evt)
    {
        CancelCapture();
    }

    // Отмена перехвата без назначения: список перестраивается и возвращает
    // кнопкам их актуальные клавиши.
    private void CancelCapture()
    {
        if (_capturedButton == null)
        {
            return;
        }

        StopCaptureTask();
        UpdateKeyList();
    }

    private void PollCapture()
    {
        if (!Application.isFocused || _capturedButton == null ||
            _capturedButton.panel == null ||
            _capturedButton.resolvedStyle.display == DisplayStyle.None)
        {
            // Страницу спрятали (закрыли меню, ушли на другую вкладку), пока
            // шёл перехват: гасим его, чтобы флаг не блокировал ввод в игре.
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
        if (assigned is Key.Enter or Key.NumpadEnter or Key.Tab or Key.M or Key.I or
            Key.P or Key.R or Key.T or Key.F1 or Key.F2 or
            Key.Digit1 or Key.Digit2 or Key.Digit3 or Key.Digit4 or Key.Digit5 or
            Key.Digit6 or Key.Digit7 or Key.Digit8 or Key.Digit9)
        {
            _capturedButton!.text = _loc.Get("settings.controls.reserved_key");
            return;
        }

        KeyAction action = _capturedAction!.Value;
        string keyName = assigned.ToString();
        StopCaptureTask();
        _clientConfig.UpdateSection(
            config => config.Interface,
            settings =>
            {
                // Обмен привязками сохраняет уникальность и пользовательский выбор.
                string previousKey = action.Get(settings);
                foreach (KeyAction other in s_keyActions)
                {
                    if (other.LabelKey != action.LabelKey &&
                        string.Equals(
                            other.Get(settings),
                            keyName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        other.Set(settings, previousKey);
                    }
                }

                action.Set(settings, keyName);
            });
        UpdateKeyList();
    }

    private void StopCaptureTask()
    {
        _captureTask?.Pause();
        _captureTask = null;
        _uiInput.IsKeyCaptureInProgress = false;
        if (_capturedButton != null)
        {
            _capturedButton.RemoveFromClassList("controls-key-btn--listening");
        }

        _capturedButton = null;
        _capturedAction = null;
    }
}
