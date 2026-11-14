#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using Kern.Networking.Protocol;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Contracts.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

using Kern.AssetPipeline.Loading;
using Kern.Networking.Gateways;
using Kern.UI.Input;
using Kern.UI.Windows;

namespace Kern.UI.ServerWindows;

public sealed class ServerWindowPresenter : IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IWebAssetLoader _webAssetLoader;
    private readonly UIInputManager _uiInputManager;
    private readonly INetworkService _networkService;
    private readonly UIDocument _document;
    private readonly WindowCommandStream _commands;
    private readonly ModalWindowHandler _modalWindowHandler;
    private readonly LinkDialogHandler _linkDialog;
    private readonly IWorldPresentationState? _presentationState;
    private readonly List<ServerWindowState> _openWindows = [];
    private readonly Queue<Action> _pendingWindowCommands = new();
    private bool _isFlushingWindowCommands;

    public ServerWindowPresenter(
        ILocalizationService localization,
        IAssetLoader assetLoader,
        UIInputManager uiInputManager,
        INetworkService networkService,
        UIDocument document,
        WindowCommandStream commands,
        IAsyncOperationSupervisor operations,
        IWebAssetLoader webAssetLoader,
        IWorldPresentationState? presentationState = null)
    {
        _localization = localization;
        _assetLoader = assetLoader;
        _operations = operations;
        _webAssetLoader = webAssetLoader;
        _uiInputManager = uiInputManager;
        _networkService = networkService;
        _document = document;
        _commands = commands;
        _presentationState = presentationState;
        _modalWindowHandler = new ModalWindowHandler(document, uiInputManager, assetLoader, webAssetLoader, operations);
        _commands.OpenRequested += Open;
        _commands.CloseRequested += Close;
        _commands.ModalRequested += ShowModal;
        _linkDialog = new LinkDialogHandler(document, uiInputManager, localization);
        _commands.LinkRequested += ShowLink;
        if (_presentationState != null)
        {
            _presentationState.Changed += OnPresentationStateChanged;
        }
    }

    public bool HasOpenWindows => _openWindows.Count > 0;

    public string? TopWindowTag => _openWindows.Count > 0 ? _openWindows[^1].Tag : null;

    public bool IsModalShowing => _modalWindowHandler.IsShowing || _linkDialog.IsShowing;

    /// <summary>
    /// ESC отправляет обычный ElementClickPacket по элементу, явно помеченному
    /// как действие закрытия. Если его нет, ESC поглощается: клик по случайному
    /// элементу хуже бездействия.
    /// </summary>
    public bool TryRequestCloseTopWindow()
    {
        if (_openWindows.Count == 0)
        {
            return false;
        }

        ServerWindowState state = _openWindows[^1];
        for (int index = state.ClickableOrder.Count - 1; index >= 0; index--)
        {
            if (TextPacketBuilder.IsCloseAction(state.ClickableOrder[index]))
            {
                InvokeServerAction(state, index);
                return true;
            }
        }

        return true;
    }

    /// <summary>
    /// Закрыть серверное окно. Вызывается из PauseMenu: он владеет Escape в
    /// фазе Update, и модалка должна гаситься там же — иначе нажатие
    /// открывало паузу поверх заблокированного ввода, а окно оставалось.
    /// </summary>
    public void DismissModal()
    {
        _modalWindowHandler.Hide();
        _linkDialog.Hide();
    }

    public void Dispose()
    {
        _commands.OpenRequested -= Open;
        _commands.CloseRequested -= Close;
        _commands.ModalRequested -= ShowModal;
        _commands.LinkRequested -= ShowLink;
        if (_presentationState != null)
        {
            _presentationState.Changed -= OnPresentationStateChanged;
        }
        _pendingWindowCommands.Clear();

        // Оверлей тоже убирается. Панель документа переживает сцену, и оставшийся
        // элемент копился между входами в игру: новый обработчик создавал второй,
        // а поиск по классу отдавал старый, скрытый. Плюс Dispose снимает
        // подписку на планировщик.
        _modalWindowHandler.Dispose();
        _linkDialog.Dispose();
        foreach (ServerWindowState state in _openWindows)
        {
            state.Overlay.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _commands.SetServerWindowVisibility(false);
    }

    private void Open(OpenWindowPacket packet)
    {
        if (ShouldHoldWindowUpdates())
        {
            _pendingWindowCommands.Enqueue(() => Open(packet));
            return;
        }

        try
        {
            OpenCore(packet);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PacketUI] Failed to open window '{packet.WindowTag}' " +
                $"({packet.Width}x{packet.Height}): {exception}");
            throw;
        }
    }

    private void OpenCore(OpenWindowPacket packet)
    {
        if (packet.Content == null || packet.Width == 0 || packet.Height == 0)
        {
            throw new InvalidOperationException(
                $"[PacketUI] Invalid OpenWindowPacket tag='{packet.WindowTag}', " +
                $"size={packet.Width}x{packet.Height}, " +
                $"root={packet.Content?.GetType().Name ?? "null"}: " +
                "missing content or zero window dimensions.");
        }

        // UIDocument може бути вимкненим (BypassGameUI). Серверне вікно потребує
        // активного дерева — вмикаємо документ перед додаванням елемента.
        if (!_document.enabled)
        {
            _document.enabled = true;
        }

        VisualElement element = new();
        element.AddToClassList("packet-window-overlay");
        VisualElement frame = new();
        frame.AddToClassList("packet-window-frame");
        frame.style.width = packet.Width;

        frame.style.height = packet.Height;

        // Тема окна выбирается сервером: Kern — скин главной темы поверх меню
        // (окно авторизации), SciFi — текущий скин игровых окон. KernVector
        // привязывается к оверлею сразу: векторный корпус панели (срез угла,
        // засечки, зерно) рисуется при первом стайле, а не на ближайшем
        // пересмотре дерева корня документа.
        if (packet.Theme == WindowTheme.Kern)
        {
            element.AddToClassList("packet-window-overlay--kern");
            frame.AddToClassList("packet-window--kern");
            Screens.KernVector.Attach(element);
        }

        element.Add(frame);
        var state = new ServerWindowState
        {
            Tag = packet.WindowTag,
            Overlay = element,
            Frame = frame,
            Tree = packet.Content,
        };

        // Временный замер этапов (репорт: маркет сильно лагает при заходе).
        // Лог показывает, какой именно этап съедает время: сборка дерева,
        // регистрация кликабельных.
        var openWatch = System.Diagnostics.Stopwatch.StartNew();
        BuildWindowContent(state);
        // Клик по телу окна вне текстового поля забирает фокус себе: в UI
        // Toolkit фокус не уходит при клике по некликабельному, и рамка
        // сфокусированного поля авторизации висела бы постоянно.
        frame.focusable = true;
        frame.RegisterCallback<PointerDownEvent>(e =>
        {
            for (VisualElement? target = e.target as VisualElement; target != null; target = target.parent)
            {
                if (target is TextField)
                {
                    return;
                }
            }

            frame.Focus();
        });
        long buildMs = openWatch.ElapsedMilliseconds;
        int elementCount = 0;
        CountTree(element);
        void CountTree(VisualElement e)
        {
            elementCount++;
            foreach (VisualElement child in e.Children())
            {
                CountTree(child);
            }
        }

        // Легаси-GUI — одно окно на игрока: сервер переотрисовывает его на
        // КАЖДОЕ действие (вход, таб, кнопка), раздавая каждому рендеру новый
        // уникальный тег (ProtocolWindow.MakeTag). Поэтому заменяем по тегу
        // нельзя — новый рендер закрывает ВСЕ прежние окна. Без этого
        // копии окон накапливаются в иерархии весь сеанс: каждая остаётся в
        // layout и модальном стеке под новой, и лаги растут с каждым кликом
        // (репорт: маркет сильно лагает при заходе).
        foreach (ServerWindowState old in _openWindows)
        {
            _uiInputManager.PopModal(old.Overlay);
            old.Overlay.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _document.rootVisualElement.Add(element);
        _uiInputManager.PushModal(element);
        _openWindows.Add(state);
        _commands.SetServerWindowVisibility(true);
        Debug.Log($"[PacketUI] Открыто окно '{packet.WindowTag}' ({packet.Width}x{packet.Height}): " +
            $"элементов {elementCount}; сборка {buildMs} мс.");
    }

    /// <summary>
    /// Контент окна: сборка дерева из пакета,
    /// регистрация кликабельных, крестик. Окно строится с нуля из дерева
    /// пакета, так что индексы кликабельных и обработчики остаются
    /// согласованными между собой.
    /// </summary>
    private void BuildWindowContent(ServerWindowState state)
    {
        state.Frame.Clear();
        VisualElement packetRoot = new PacketUIBuilder(_assetLoader, _operations, _webAssetLoader).Build(state.Tree);
        packetRoot.AddToClassList("packet-window-content");
        state.Frame.Add(packetRoot);
        state.ClickableOrder = ClickableElementOrder.Collect(state.Tree);
        RegisterClickableElements(packetRoot, packetRoot, state);
        AddExitCloseButton(state);
    }

    private void Close(CloseWindowPacket packet)
    {
        if (ShouldHoldWindowUpdates())
        {
            _pendingWindowCommands.Enqueue(() => Close(packet));
            return;
        }

        if (_openWindows.Count == 0)
        {
            return;
        }

        ServerWindowState state = _openWindows[^1];
        _uiInputManager.PopModal(state.Overlay);
        state.Overlay.RemoveFromHierarchy();
        _openWindows.RemoveAt(_openWindows.Count - 1);
        _commands.SetServerWindowVisibility(HasOpenWindows);
    }

    private void ShowLink(string url)
    {
        if (ShouldHoldWindowUpdates())
        {
            _pendingWindowCommands.Enqueue(() => ShowLink(url));
            return;
        }

        _linkDialog.Show(url);
    }

    private void ShowModal(ModalWindowPacket packet)
    {
        if (ShouldHoldWindowUpdates())
        {
            _pendingWindowCommands.Enqueue(() => ShowModal(packet));
            return;
        }

        _modalWindowHandler.Show(packet);
    }

    private void RegisterClickableElements(VisualElement element, VisualElement windowRoot, ServerWindowState state)
    {
        if (element.userData is IGUIComponentPacket componentPacket &&
            !string.IsNullOrEmpty(componentPacket.OnClickContext))
        {
            int elementIndex = ClickableElementOrder.IndexOf(state.Tree, componentPacket);
            if (elementIndex < 0)
            {
                string message = $"[PacketUI] Clickable component {componentPacket.GetType().Name} " +
                    $"is missing from packet tree for window '{state.Tag}'.";
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            element.RegisterCallback<ClickEvent>(click =>
            {
                click.StopPropagation();
                HandleElementClick(element, windowRoot, elementIndex, state);
            });
        }

        foreach (VisualElement child in element.Children())
        {
            RegisterClickableElements(child, windowRoot, state);
        }
    }

    /// <summary>
    /// The frame close button sends an ElementClickPacket for the component
    /// marked as the close action, using this window's tag and shared preorder index.
    /// </summary>
    private void AddExitCloseButton(ServerWindowState state)
    {
        VisualElement frame = state.Frame;
        IReadOnlyList<IGUIComponentPacket> clickableOrder = state.ClickableOrder;
        for (int elementIndex = 0; elementIndex < clickableOrder.Count; elementIndex++)
        {
            if (!TextPacketBuilder.IsCloseAction(clickableOrder[elementIndex]))
            {
                continue;
            }

            var closeButton = new Button { name = "PacketWindowClose", tooltip = _localization.Get("common.close") };
            closeButton.AddToClassList("packet-window-close");
            closeButton.focusable = false;

            // VisualElement.classes в публичном API Unity 6000.6 нет, классы
            // добавляются через AddToClassList.
            var closeBarA = new VisualElement();
            closeBarA.AddToClassList("packet-window-close-bar");
            closeBarA.AddToClassList("packet-window-close-bar--a");
            closeButton.Add(closeBarA);
            var closeBarB = new VisualElement();
            closeBarB.AddToClassList("packet-window-close-bar");
            closeBarB.AddToClassList("packet-window-close-bar--b");
            closeButton.Add(closeBarB);

            int closeIndex = elementIndex;
            closeButton.clicked += () => InvokeServerAction(state, closeIndex);
            frame.Add(closeButton);
            return;
        }
    }

    private void InvokeServerAction(ServerWindowState state, int elementIndex)
    {
        IGUIComponentPacket packet = state.ClickableOrder[elementIndex];
        VisualElement? element = FindPacketElement(state.Frame, packet);
        VisualElement? root = FindPacketElement(state.Frame, state.Tree);
        if (element == null || root == null)
        {
            throw new InvalidOperationException($"[PacketUI] Missing action {elementIndex} in window '{state.Tag}'.");
        }

        HandleElementClick(element, root, elementIndex, state);
    }

    private static VisualElement? FindPacketElement(VisualElement element, IGUIComponentPacket packet)
    {
        if (ReferenceEquals(element.userData, packet))
        {
            return element;
        }

        foreach (VisualElement child in element.Children())
        {
            VisualElement? found = FindPacketElement(child, packet);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void HandleElementClick(
        VisualElement clickedElement,
        VisualElement windowRoot,
        int elementIndex,
        ServerWindowState state)
    {
        if (clickedElement.userData is not IGUIComponentPacket componentPacket)
        {
            string message = $"[PacketUI] Clicked element {elementIndex} in window '{state.Tag}' " +
                "has no GUI component packet.";
            Debug.LogError(message);
            throw new InvalidOperationException(message);
        }

        // OnClickContext — указатель на контейнер входов («.» / «/n» от корня
        // окна): значения инпутов (слайдеры crys0..crys5, поле "input") собираются
        // с него и отправляются в Context — сервер подставляет их в макросы
        // %M%/%I% action-строки (ProtocolWindow.SubstituteActionMacros) до
        // диспетчеризации. Без значений кнопки типа «Продать:%M%» умирают
        // в long.Parse("%M%") на сервере.
        VisualElement? inputRoot = ClickContextResolver.ResolveRoot(
            clickedElement,
            windowRoot,
            componentPacket.OnClickContext);
        if (inputRoot == null)
        {
            string message = $"[PacketUI] Invalid OnClickContext '{componentPacket.OnClickContext}' " +
                $"for component {elementIndex} ({componentPacket.GetType().Name}) " +
                $"in window '{state.Tag}'.";
            Debug.LogError(message);
            throw new InvalidOperationException(message);
        }

        StringPairPacket[] inputValues = ClickContextResolver.CollectInputValues(inputRoot);

        _networkService.Send(new ElementClickPacket(state.Tag, elementIndex, inputValues));
    }

    private void OnPresentationStateChanged(WorldPresentationSnapshot snapshot)
    {
        if (!ShouldHoldWindowUpdates())
        {
            FlushPendingWindowUpdates();
        }
    }

    // Без состояния мира (хост окон в сцене меню) держать окна нечего: любое
    // окно показывается сразу.
    private bool ShouldHoldWindowUpdates() =>
        _presentationState != null &&
        _presentationState.HasPublishedView &&
        _presentationState.Current.Phase is not (WorldPresentationPhase.Stable or WorldPresentationPhase.Failed);

    /// Закрыть все окна, не отписываясь от потока команд. Разрыв транспорта
    /// (GatewayWindowProcessor) гасит окна, оставшиеся от сессии, которая уже
    /// мертва: сервер после переподключения отрисовывает окна заново.
    public void CloseAllWindows()
    {
        if (ShouldHoldWindowUpdates())
        {
            _pendingWindowCommands.Enqueue(CloseAllWindows);
            return;
        }

        foreach (ServerWindowState state in _openWindows)
        {
            _uiInputManager.PopModal(state.Overlay);
            state.Overlay.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _commands.SetServerWindowVisibility(false);
    }

    private void FlushPendingWindowUpdates()
    {
        if (_isFlushingWindowCommands)
        {
            return;
        }

        _isFlushingWindowCommands = true;
        try
        {
            while (_pendingWindowCommands.Count > 0 && !ShouldHoldWindowUpdates())
            {
                _pendingWindowCommands.Dequeue().Invoke();
            }
        }
        finally
        {
            _isFlushingWindowCommands = false;
        }
    }
}
