#nullable enable

using System;
using System.Collections.Generic;
using Kern.Audio;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Rendering.Display;
using Kern.UI.Localization;
using Kern.UI.Screens;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Settings;

// Окно настроек — одно на меню и игру (макет visual/kern-ui #settingsDialog). Собирается в корень документа
// поверх экрана; вкладки «Графика», «Звук», «Интерфейс», «Управление». Всё применяется сразу.
// При смене языка окно пересобирается целиком с той же вкладкой (реестр ILocalizableUI).
internal sealed class SettingsWindow : ILocalizableUI, IDisposable
{
    private static readonly string[] s_tabNames =
    {
        "SettingsTabGraphics",
        "SettingsTabAudio",
        "SettingsTabInterface",
        "SettingsTabControls",
    };

    private static readonly string[] s_pageNames =
    {
        "SettingsPageGraphics",
        "SettingsPageAudio",
        "SettingsPageInterface",
        "SettingsPageControls",
    };

    private readonly UIDocument _doc;
    private readonly VisualElement _root;
    private readonly IClientConfigManager _config;
    private readonly IAudioSystem _audio;
    private readonly DisplayManager _display;
    private readonly ISettingsApply _apply;
    private readonly ILocalizationService _loc;
    private readonly Action<bool> _setCapturing;
    private readonly IPlayerStats? _stats;
    private readonly List<Action> _refreshers = new();

    private TemplateContainer? _tree;
    private VisualElement? _layer;
    private Button[] _tabs = Array.Empty<Button>();
    private VisualElement[] _pages = Array.Empty<VisualElement>();
    private ScrollView? _body;
    private int _tab;

    public SettingsWindow(
        UIDocument doc,
        VisualElement root,
        IClientConfigManager config,
        IAudioSystem audio,
        DisplayManager display,
        ISettingsApply apply,
        ILocalizationService loc,
        IPlayerStats? stats,
        Action<bool> setCapturing)
    {
        _doc = doc;
        _root = root;
        _config = config;
        _audio = audio;
        _display = display;
        _apply = apply;
        _loc = loc;
        _stats = stats;
        // Перехват клавиш (назначение клавиши, калибровка) виден и окну: меню не закрывает настройки по Escape,
        // пока клавиши принадлежат перехвату
        _setCapturing = capturing =>
        {
            IsCapturing = capturing;
            setCapturing(capturing);
        };
        // Реестр сразу вызывает ApplyLocalizedText — первая сборка — и повторяет его на каждой смене языка
        _loc.RegisterLocalizable(this);
    }

    public event Action? Closed;

    public bool IsOpen { get; private set; }

    public bool IsCapturing { get; private set; }

    public void Open()
    {
        foreach (Action refresh in new List<Action>(_refreshers))
        {
            refresh();
        }

        _tree?.BringToFront();
        UIState.SetHidden(_layer!, false);
        IsOpen = true;
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        UIState.SetHidden(_layer!, true);
        IsOpen = false;
        _setCapturing(false);
        Closed?.Invoke();
    }

    public void Dispose()
    {
        _loc.UnregisterLocalizable(this);
        _tree?.RemoveFromHierarchy();
        _tree = null;
    }

    private void Build()
    {
        VisualTreeAsset template = Resources.Load<VisualTreeAsset>(ResourcePaths.SettingsWindowUxml)
            ?? throw new InvalidOperationException($"[Settings] Resources/{ResourcePaths.SettingsWindowUxml}.uxml is required.");
        _tree = template.Instantiate();
        _tree.AddToClassList("ui-fullscreen");
        _tree.pickingMode = PickingMode.Ignore;
        _layer = Require<VisualElement>(_tree, "SettingsLayer");
        _body = Require<ScrollView>(_tree, "SettingsBody");
        _tabs = Array.ConvertAll(s_tabNames, name => Require<Button>(_tree, name));
        _pages = Array.ConvertAll(s_pageNames, name => Require<VisualElement>(_tree, name));
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i;
            _tabs[i].focusable = false;
            _tabs[i].clicked += () => ShowTab(index);
        }

        Require<Button>(_tree, "SettingsClose").clicked += Close;
        // Клик по затемнению вокруг окна закрывает его, как в макете
        _layer.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == _layer)
            {
                Close();
            }
        });

        _refreshers.Clear();
        new SettingsGraphicsPage(_doc, _config, _display, _apply, _refreshers, _loc, _stats, _setCapturing).Build(_pages[0]);
        new SettingsAudioPage(_config, _audio, _refreshers, _loc).Build(_pages[1]);
        new SettingsInterfacePage(_doc, _config, _apply, _refreshers, _loc).Build(_pages[2]);
        new SettingsControlsPage(_doc, _config, _refreshers, _loc, _setCapturing).Build(_pages[3]);

        _tree.Localize(_loc);
        _root.Add(_tree);
        ShowTab(_tab);
        UIState.SetHidden(_layer, !IsOpen);
    }

    public void ApplyLocalizedText()
    {
        _tree?.RemoveFromHierarchy();
        Build();
        if (IsOpen)
        {
            Open();
        }
    }

    private void ShowTab(int index)
    {
        _tab = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].EnableInClassList("is-on", i == index);
            _tabs[i].EnableInClassList("is-on", i == index);
        }

        if (_body != null)
        {
            _body.scrollOffset = Vector2.zero;
        }
    }

    private static T Require<T>(VisualElement tree, string name)
        where T : VisualElement =>
        tree.Q<T>(name) ?? throw new InvalidOperationException($"[Settings] {name} is missing from SettingsWindow.uxml.");
}
