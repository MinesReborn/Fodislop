#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using Kern.Networking.Protocol;
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
    /// <summary>
    /// Пол авто-высоты каркаса: страница без контента (подтверждение «Да/Нет»)
    /// не должна схлопываться в узкую полоску — окно остаётся читаемым.
    /// </summary>
    private const float AutoHeightFloor = 240f;

    private readonly ILocalizationService _localization;
    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IWebAssetLoader _webAssetLoader;
    private readonly UIInputManager _uiInputManager;
    private readonly INetworkService _networkService;
    private readonly UIDocument _document;
    private readonly WindowCommandStream _commands;
    private readonly ModalWindowHandler _modalWindowHandler;
    private readonly IWorldPresentationState _presentationState;
    private readonly IGameplayCamera _gameplayCamera;
    private readonly List<ServerWindowState> _openWindows = [];
    private ulong _pendingTeleportTransitionId;
    private OpenWindowPacket _pendingOpenWindow;
    private CloseWindowPacket _pendingCloseWindow;
    private ModalWindowPacket _pendingModalWindow;
    private byte _pendingWindowCommand;
    private bool _hasPendingModalWindow;

    public ServerWindowPresenter(
        ILocalizationService localization,
        IAssetLoader assetLoader,
        UIInputManager uiInputManager,
        INetworkService networkService,
        UIDocument document,
        WindowCommandStream commands,
        IAsyncOperationSupervisor operations,
        IWebAssetLoader webAssetLoader,
        IWorldPresentationState presentationState,
        IGameplayCamera gameplayCamera)
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
        _gameplayCamera = gameplayCamera;
        _modalWindowHandler = new ModalWindowHandler(document, uiInputManager);
        _commands.OpenRequested += Open;
        _commands.CloseRequested += Close;
        _commands.ModalRequested += ShowModal;
        _presentationState.Changed += OnPresentationStateChanged;

        // Первое окно не платит за разовую инициализацию движка шаблонов
        // (репорт: первое открытие маркета — 3.2 с в биндинге, далее 1 мс).
        WindowBinding.WarmUpInBackground(operations);
    }

    public bool HasOpenWindows => _openWindows.Count > 0;

    public string? TopWindowTag => _openWindows.Count > 0 ? _openWindows[^1].Tag : null;

    public bool IsModalShowing => _modalWindowHandler.IsShowing;

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
                _networkService.Send(new ElementClickPacket(state.Tag, index, []));
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
    public void DismissModal() => _modalWindowHandler.Hide();

    public void Dispose()
    {
        _commands.OpenRequested -= Open;
        _commands.CloseRequested -= Close;
        _commands.ModalRequested -= ShowModal;
        _presentationState.Changed -= OnPresentationStateChanged;
        CancelPendingTeleportRequest();

        // Оверлей тоже убирается. Панель документа переживает сцену, и оставшийся
        // элемент копился между входами в игру: новый обработчик создавал второй,
        // а поиск по классу отдавал старый, скрытый. Плюс Dispose снимает
        // подписку на планировщик.
        _modalWindowHandler.Dispose();
        foreach (ServerWindowState state in _openWindows)
        {
            state.Binding.Dispose();
            state.Overlay.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _commands.SetServerWindowVisibility(false);
    }

    private void Open(OpenWindowPacket packet)
    {
        if (_presentationState.IsTeleportAwaitingDestination)
        {
            CancelPendingTeleportRequest();
        }

        if (ShouldHoldWindowUpdates())
        {
            _pendingOpenWindow = packet;
            _pendingWindowCommand = 1;
            return;
        }

        CancelPendingTeleportRequest();
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
        ServerWindowPacketValidator.Validate(packet);

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

        // Высота каркаса по содержимому (мост ставит PacketUI.AutoSizeHeight
        // на корень каждого протокольного окна): точную высоту рендера знает только
        // клиент — шрифты, переносы, размеры иконок. Каркас обнимает контент
        // в границах: пол (AutoHeightFloor) не даёт крошечным страницам
        // («Да/Нет» подтверждения) схлопываться, потолок — Height из пакета
        // и 96% экрана (max-height в .packet-window-frame); за потолком
        // начинается прокрутка.
        if (AttachedProperties.Has(packet.Content, "PacketUI.AutoSizeHeight"))
        {
            frame.style.height = StyleKeyword.Auto;
            frame.style.minHeight = AutoHeightFloor;
            frame.style.maxHeight = packet.Height;
        }
        else
        {
            frame.style.height = packet.Height;
        }

        element.Add(frame);
        var state = new ServerWindowState
        {
            Tag = packet.WindowTag,
            Overlay = element,
            Frame = frame,
            Binding = new WindowBinding(),
            Tree = packet.Content,
        };

        // Временный замер этапов (репорт: маркет сильно лагает при заходе).
        // Лог показывает, какой именно этап съедает время: сборка дерева,
        // биндинг живых шаблонов или регистрация кликабельных.
        var openWatch = System.Diagnostics.Stopwatch.StartNew();
        BuildWindowContent(state);
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
            old.Binding.Dispose();
            _uiInputManager.PopModal(old.Overlay);
            old.Overlay.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _document.rootVisualElement.Add(element);
        _uiInputManager.PushModal(element);
        _openWindows.Add(state);
        _commands.SetServerWindowVisibility(true);
        Debug.Log($"[PacketUI] Открыто окно '{packet.WindowTag}' ({packet.Width}x{packet.Height}): " +
            $"элементов {elementCount}; сборка и биндинг {buildMs} мс.");
    }

    /// <summary>
    /// Контент окна: сборка дерева из пакета, биндинг живых шаблонов,
    /// регистрация кликабельных, крестик. Окно строится с нуля из дерева
    /// пакета, так что индексы кликабельных и обработчики остаются
    /// согласованными между собой.
    /// </summary>
    private void BuildWindowContent(ServerWindowState state)
    {
        state.Frame.Clear();
        VisualElement packetRoot = new PacketUIBuilder(_assetLoader, _operations, _webAssetLoader).Build(state.Tree);
        PacketCloseActionCaption.Apply(packetRoot, _localization);
        packetRoot.AddToClassList("packet-window-content");
        state.Frame.Add(packetRoot);
        state.Binding.Bind(packetRoot);
        state.ClickableOrder = ClickableElementOrder.Collect(state.Tree);
        RegisterClickableElements(packetRoot, packetRoot, state);
        AddExitCloseButton(state.Frame, state.Tag, state.ClickableOrder);
    }

    private void Close(CloseWindowPacket packet)
    {
        // Cancel a pending destination request before holding window commands.
        // Otherwise a close click during AwaitingDestination is queued, while
        // the state that would release the queue is never left.
        CancelPendingTeleportRequest();

        if (ShouldHoldWindowUpdates())
        {
            _pendingCloseWindow = packet;
            _pendingWindowCommand = 2;
            return;
        }

        if (_openWindows.Count == 0)
        {
            return;
        }

        ServerWindowState state = _openWindows[^1];
        state.Binding.Dispose();
        _uiInputManager.PopModal(state.Overlay);
        state.Overlay.RemoveFromHierarchy();
        _openWindows.RemoveAt(_openWindows.Count - 1);
        _commands.SetServerWindowVisibility(HasOpenWindows);
    }

    private void ShowModal(ModalWindowPacket packet)
    {
        if (ShouldHoldWindowUpdates())
        {
            _pendingModalWindow = packet;
            _hasPendingModalWindow = true;
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
    private void AddExitCloseButton(
        VisualElement frame,
        string windowTag,
        IReadOnlyList<IGUIComponentPacket> clickableOrder)
    {
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

            closeButton.clicked += () => _networkService.Send(
                new ElementClickPacket(windowTag, elementIndex, []));
            frame.Add(closeButton);
            return;
        }
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
        bool startsTeleport = string.Equals(state.Tag, "teleport", StringComparison.Ordinal) &&
            !TextPacketBuilder.IsCloseAction(componentPacket);
        if (startsTeleport)
        {
            if (!_presentationState.TryBeginTeleportRequest(
                _gameplayCamera.Camera.transform.position,
                out _pendingTeleportTransitionId))
            {
                return;
            }
        }

        try
        {
            _networkService.Send(new ElementClickPacket(state.Tag, elementIndex, inputValues));
        }
        catch
        {
            CancelPendingTeleportRequest();
            throw;
        }
    }

    private void OnPresentationStateChanged(WorldPresentationSnapshot snapshot)
    {
        if (_pendingTeleportTransitionId != 0 &&
            snapshot.TransitionId == _pendingTeleportTransitionId &&
            snapshot.Phase != WorldPresentationPhase.AwaitingDestination)
        {
            _pendingTeleportTransitionId = 0;
        }

        if (!ShouldHoldWindowUpdates())
        {
            FlushPendingWindowUpdates();
        }
    }

    private bool ShouldHoldWindowUpdates() =>
        _presentationState.HasPublishedView &&
        _presentationState.Current.Phase is not (WorldPresentationPhase.Stable or WorldPresentationPhase.Failed);

    private void FlushPendingWindowUpdates()
    {
        byte command = _pendingWindowCommand;
        _pendingWindowCommand = 0;
        if (command == 1)
        {
            Open(_pendingOpenWindow);
        }
        else if (command == 2)
        {
            Close(_pendingCloseWindow);
        }

        if (_hasPendingModalWindow)
        {
            _hasPendingModalWindow = false;
            ShowModal(_pendingModalWindow);
        }
    }

    private void CancelPendingTeleportRequest()
    {
        ulong transitionId = _pendingTeleportTransitionId;
        _pendingTeleportTransitionId = 0;
        if (transitionId != 0)
        {
            _presentationState.CancelTeleportRequest(transitionId);
        }
    }
}
