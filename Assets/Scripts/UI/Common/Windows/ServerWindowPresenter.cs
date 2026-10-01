#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kern.Core.Interfaces;
using Kern.Networking;
using Kern.UI.Binding;
using Kern.UI.Builders;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer.Unity;

namespace Kern.UI;

public sealed class ServerWindowPresenter : IDisposable
{
    private static readonly Regex _canvasImagePattern = new(
        @"^(?<x>-?\d+x)?(?<y>-?\d+y)?(?<h>\d+h)?(?<w>\d+w)=I#(?<uri>.+)$",
        RegexOptions.CultureInvariant);

    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
    private readonly IWebAssetLoader _webAssetLoader;
    private readonly UIInputManager _uiInputManager;
    private readonly INetworkService _networkService;
    private readonly UIDocument _document;
    private readonly WindowCommandStream _commands;
    private readonly ModalWindowHandler _modalWindowHandler;
    private readonly List<(string Tag, VisualElement Root, WindowBinding Binding)> _openWindows = [];

    public ServerWindowPresenter(
        IAssetLoader assetLoader,
        UIInputManager uiInputManager,
        INetworkService networkService,
        UIDocument document,
        WindowCommandStream commands,
        IAsyncOperationSupervisor operations,
        IWebAssetLoader webAssetLoader)
    {
        _assetLoader = assetLoader;
        _operations = operations;
        _webAssetLoader = webAssetLoader;
        _uiInputManager = uiInputManager;
        _networkService = networkService;
        _document = document;
        _commands = commands;
        _modalWindowHandler = new ModalWindowHandler(document, uiInputManager);
        _commands.OpenRequested += Open;
        _commands.CloseRequested += Close;
        _commands.ModalRequested += ShowModal;
    }

    public bool HasOpenWindows => _openWindows.Count > 0;

    public string? TopWindowTag => _openWindows.Count > 0 ? _openWindows[^1].Tag : null;

    public bool IsModalShowing => _modalWindowHandler.IsShowing;

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

        // Оверлей тоже убирается. Панель документа переживает сцену, и оставшийся
        // элемент копился между входами в игру: новый обработчик создавал второй,
        // а поиск по классу отдавал старый, скрытый. Плюс Dispose снимает
        // подписку на планировщик.
        _modalWindowHandler.Dispose();
        foreach ((_, VisualElement root, WindowBinding binding) in _openWindows)
        {
            binding.Dispose();
            root.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _commands.SetServerWindowVisibility(false);
    }

    private void Open(OpenWindowPacket packet)
    {
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
        bool actionTagWindow = packet.WindowTag == "exit" && packet.Content is ScrollViewerPacket;
        if (actionTagWindow)
        {
            packet = ExpandSerializedRows(packet);
        }

        ValidateWindowPacket(packet);

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
        VisualElement packetRoot = new PacketUIBuilder(_assetLoader, _operations, _webAssetLoader).Build(packet.Content);
        packetRoot.AddToClassList("packet-window-content");
        frame.Add(packetRoot);
        element.Add(frame);
        var binding = new WindowBinding();
        binding.Bind(packetRoot);
        var packetOrder = new List<IGUIComponentPacket>();
        CollectPacketOrder(packet.Content, packetOrder);
        RegisterClickableElements(packetRoot, packetRoot, packet.WindowTag, packetOrder, actionTagWindow);
        if (actionTagWindow)
        {
            AddExitCloseButton(frame, packetOrder);
        }

        _document.rootVisualElement.Add(element);
        _uiInputManager.PushModal(element);
        _openWindows.Add((packet.WindowTag, element, binding));
        _commands.SetServerWindowVisibility(true);
    }

    private static OpenWindowPacket ExpandSerializedRows(OpenWindowPacket packet)
    {
        var scroll = (ScrollViewerPacket)packet.Content;
        var children = new List<IGUIComponentPacket>();
        int footerStart = scroll.Children.Count;
        while (footerStart > 0 &&
            scroll.Children[footerStart - 1] is TextPacket footerText &&
            !string.IsNullOrEmpty(footerText.OnClickContext))
        {
            footerStart--;
        }

        for (int index = 0; index < scroll.Children.Count;)
        {
            IGUIComponentPacket child = scroll.Children[index];

            if (index >= footerStart && child is TextPacket footerButton)
            {
                bool isExit = footerButton.OnClickContext is "exit" or "exit:0";
                children.Add(footerButton with
                {
                    AttachedProperties = isExit
                        ?
                        [
                            .. footerButton.AttachedProperties,
                            new StringPairPacket("PacketUI.FooterAction", "1"),
                            new StringPairPacket("PacketUI.ExitAction", "1"),
                        ]
                        :
                        [
                            .. footerButton.AttachedProperties,
                            new StringPairPacket("PacketUI.FooterAction", "1"),
                        ],
                });
                index++;
                continue;
            }

            if (index == 0 && child is TextPacket title &&
                string.IsNullOrEmpty(title.OnClickContext))
            {
                children.Add(title with
                {
                    AttachedProperties =
                    [
                        .. title.AttachedProperties,
                        new StringPairPacket("PacketUI.Title", "1"),
                        new StringPairPacket("Text.Align", "Center"),
                    ],
                });
                index++;

                // Полоса вкладок легаси-окна: мост (ProtocolWindow) помечает
                // вкладки PacketUI.TabAction/TabActive и кладёт их сразу за
                // заголовком. Собираем их в горизонтальный DockPanel-ряд.
                int tabCount = CountTabStrip(scroll.Children, index, footerStart);
                if (tabCount > 0)
                {
                    children.Add(BuildTabStrip(scroll.Children, index, tabCount));
                    index += tabCount;
                }

                continue;
            }

            if (child is TextPacket text &&
                (text.Text.Contains("\nbutton\n", StringComparison.Ordinal) ||
                 text.Text.Contains("=I#", StringComparison.Ordinal)))
            {
                ExpandSerializedContent(text, children);
            }
            else
            {
                children.Add(child);
            }

            index++;
        }

        return packet with { Content = scroll with { Children = children } };
    }

    // Вкладка легаси-окна — TextPacket с маркером моста ProtocolWindow.
    private static bool IsTabPacket(IGUIComponentPacket packet) =>
        packet is TextPacket text &&
        (AttachedProperties.Has(text, "PacketUI.TabAction") ||
         AttachedProperties.Has(text, "PacketUI.TabActive"));

    private static int CountTabStrip(IReadOnlyList<IGUIComponentPacket> children, int start, int limit)
    {
        int count = 0;
        while (start + count < limit && IsTabPacket(children[start + count]))
        {
            count++;
        }

        return count;
    }

    private static IGUIComponentPacket BuildTabStrip(
        IReadOnlyList<IGUIComponentPacket> children,
        int start,
        int count)
    {
        var tabs = new List<IGUIComponentPacket>(count);
        for (int i = 0; i < count; i++)
        {
            var tab = (TextPacket)children[start + i];
            tabs.Add(tab with
            {
                AttachedProperties =
                [
                    .. tab.AttachedProperties,
                    new StringPairPacket("DockPanel.Dock", "Left"),
                ],
            });
        }

        return new DockPanelPacket
        {
            Children = tabs,
            AttachedProperties = [new StringPairPacket("DockPanel.Dock", "Top")],
        };
    }

    private static void ExpandSerializedContent(TextPacket source, List<IGUIComponentPacket> children)
    {
        string[] lines = source.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var plain = new StringBuilder();
        for (int index = 0; index < lines.Length;)
        {
            Match canvasImage = _canvasImagePattern.Match(lines[index]);
            if (canvasImage.Success)
            {
                FlushPlainText(plain, children);
                int x = CanvasNumber(canvasImage, "x");
                int y = CanvasNumber(canvasImage, "y");
                int width = CanvasNumber(canvasImage, "w");
                int height = CanvasNumber(canvasImage, "h");
                if (width <= 0 || height <= 0 || x < 0 || y < 0)
                {
                    throw new InvalidOperationException(
                        $"[PacketUI] Invalid serialized canvas image bounds at line {index + 1}.");
                }

                children.Add(new CanvasPacket
                {
                    AttachedProperties =
                    [
                        new StringPairPacket("PacketUI.CanvasHeight", (y + height).ToString(CultureInfo.InvariantCulture)),
                    ],
                    Children =
                    [
                        new TextPacket
                        {
                            AttachedProperties =
                            [
                                new StringPairPacket("PacketUI.ImageURI", canvasImage.Groups["uri"].Value),
                                new StringPairPacket("Canvas.X", x.ToString(CultureInfo.InvariantCulture)),
                                new StringPairPacket("Canvas.Y", y.ToString(CultureInfo.InvariantCulture)),
                                new StringPairPacket("Canvas.Width", width.ToString(CultureInfo.InvariantCulture)),
                                new StringPairPacket("Canvas.Height", height.ToString(CultureInfo.InvariantCulture)),
                            ],
                        },
                    ],
                });
                index++;
                continue;
            }

            if (index + 1 < lines.Length && lines[index + 1] == "button")
            {
                if (index + 4 >= lines.Length ||
                    lines[index + 2] != lines[index + 4] ||
                    string.IsNullOrWhiteSpace(lines[index + 3]))
                {
                    throw new InvalidOperationException(
                        $"[PacketUI] Malformed serialized button in window text at line {index + 1}.");
                }

                FlushPlainText(plain, children);
                children.Add(new TextPacket
                {
                    Text = lines[index],
                    AttachedProperties = [new StringPairPacket("PacketUI.RowLabel", "1")],
                });
                children.Add(new TextPacket
                {
                    Text = lines[index + 2],
                    OnClickContext = lines[index + 3],
                });
                index += 5;
                continue;
            }

            if (index + 1 < lines.Length && lines[index + 1] == "text")
            {
                FlushPlainText(plain, children);
                children.Add(new TextPacket { Text = lines[index] });
                index += 2;
                continue;
            }

            if (plain.Length > 0)
            {
                plain.Append('\n');
            }

            plain.Append(lines[index]);
            index++;
        }

        FlushPlainText(plain, children);
    }

    private static int CanvasNumber(Match match, string group)
    {
        string value = match.Groups[group].Value;
        return value.Length == 0
            ? 0
            : int.Parse(value[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static void FlushPlainText(StringBuilder text, List<IGUIComponentPacket> children)
    {
        if (text.Length > 0)
        {
            string content = text.ToString().TrimEnd('\n', '\r');
            if (content.Length > 0)
            {
                children.Add(new TextPacket { Text = content });
            }

            text.Clear();
        }
    }

    private static void ValidateWindowPacket(OpenWindowPacket packet)
    {
        if (packet.Content == null || packet.Width == 0 || packet.Height == 0)
        {
            FailWindowPacket(packet, "missing content or zero window dimensions");
        }

        string? format = AttachedProperties.Find(packet.Content, "Popup.Format");
        if (format != null)
        {
            FailWindowPacket(packet,
                $"unsupported string-based Popup.Format='{format}'; expected GUI components");
        }

        if (packet.Content is ScrollViewerPacket scroll)
        {
            foreach (IGUIComponentPacket child in scroll.Children)
            {
                if (child is TextPacket text &&
                    (text.Text.Contains("\nbutton\n", StringComparison.Ordinal) ||
                     text.Text.Contains("\n3card\n", StringComparison.Ordinal) ||
                     text.Text.Contains("\ndrop\n", StringComparison.Ordinal) ||
                     text.Text.Contains("=I#", StringComparison.Ordinal)))
                {
                    FailWindowPacket(packet,
                        "a TextPacket contains serialized controls; expected clickable GUI components");
                }
            }
        }
    }

    [DoesNotReturn]
    private static void FailWindowPacket(OpenWindowPacket packet, string reason)
    {
        string message = $"[PacketUI] Invalid OpenWindowPacket tag='{packet.WindowTag}', " +
            $"size={packet.Width}x{packet.Height}, root={packet.Content?.GetType().Name ?? "null"}: {reason}.";
        Debug.LogError(message);
        throw new InvalidOperationException(message);
    }

    private void Close(CloseWindowPacket packet)
    {
        if (_openWindows.Count == 0)
        {
            return;
        }

        (_, VisualElement root, WindowBinding binding) = _openWindows[^1];
        binding.Dispose();
        _uiInputManager.PopModal(root);
        root.RemoveFromHierarchy();
        _openWindows.RemoveAt(_openWindows.Count - 1);
        _commands.SetServerWindowVisibility(HasOpenWindows);
    }

    private void ShowModal(ModalWindowPacket packet)
    {
        _modalWindowHandler.Show(packet);
    }

    private static void CollectPacketOrder(
        IGUIComponentPacket packet,
        List<IGUIComponentPacket> order)
    {
        order.Add(packet);
        if (packet is IContainerComponentPacket container)
        {
            foreach (IGUIComponentPacket child in container.Children)
            {
                CollectPacketOrder(child, order);
            }
        }
    }

    private void RegisterClickableElements(
        VisualElement element,
        VisualElement windowRoot,
        string windowTag,
        IReadOnlyList<IGUIComponentPacket> packetOrder,
        bool actionTagWindow)
    {
        if (element.userData is IGUIComponentPacket componentPacket &&
            !string.IsNullOrEmpty(componentPacket.OnClickContext))
        {
            int elementIndex = PacketIndex(packetOrder, componentPacket);
            if (elementIndex < 0)
            {
                string message = $"[PacketUI] Clickable component {componentPacket.GetType().Name} " +
                    $"is missing from packet tree for window '{windowTag}'.";
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            element.RegisterCallback<ClickEvent>(click =>
            {
                click.StopPropagation();
                HandleElementClick(element, windowRoot, elementIndex, windowTag, actionTagWindow);
            });
        }

        foreach (VisualElement child in element.Children())
        {
            RegisterClickableElements(child, windowRoot, windowTag, packetOrder, actionTagWindow);
        }
    }

    private static int PacketIndex(
        IReadOnlyList<IGUIComponentPacket> packetOrder,
        IGUIComponentPacket target)
    {
        for (int index = 0; index < packetOrder.Count; index++)
        {
            if (ReferenceEquals(packetOrder[index], target))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Белый крестик в правом верхнем углу рамки — замена футерной кнопки
    /// «ВЫЙТИ». Клик отправляет ровно тот же ElementClickPacket, что и
    /// убранная кнопка: окно, как и раньше, закрывает сервер. Индекс
    /// элемента считается по packetOrder, поэтому контракт с сервером
    /// не меняется.
    /// </summary>
    private void AddExitCloseButton(
        VisualElement frame,
        IReadOnlyList<IGUIComponentPacket> packetOrder)
    {
        foreach (IGUIComponentPacket candidate in packetOrder)
        {
            if (candidate is not TextPacket exit ||
                !AttachedProperties.Has(exit, "PacketUI.ExitAction") ||
                string.IsNullOrEmpty(exit.OnClickContext))
            {
                continue;
            }

            var closeButton = new Button { name = "PacketWindowClose" };
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

            int elementIndex = PacketIndex(packetOrder, exit);
            string clickContext = exit.OnClickContext;
            closeButton.clicked += () => _networkService.Send(
                new ElementClickPacket(clickContext, elementIndex, []));
            frame.Add(closeButton);
            return;
        }
    }

    private void HandleElementClick(
        VisualElement clickedElement,
        VisualElement windowRoot,
        int elementIndex,
        string windowTag,
        bool actionTagWindow)
    {
        if (clickedElement.userData is not IGUIComponentPacket componentPacket)
        {
            string message = $"[PacketUI] Clicked element {elementIndex} in window '{windowTag}' " +
                "has no GUI component packet.";
            Debug.LogError(message);
            throw new InvalidOperationException(message);
        }

        if (actionTagWindow)
        {
            _networkService.Send(new ElementClickPacket(
                componentPacket.OnClickContext,
                elementIndex,
                []));
            return;
        }

        VisualElement? inputRoot = ClickContextResolver.ResolveRoot(
            clickedElement,
            windowRoot,
            componentPacket.OnClickContext);
        if (inputRoot == null)
        {
            string message = $"[PacketUI] Invalid OnClickContext '{componentPacket.OnClickContext}' " +
                $"for component {elementIndex} ({componentPacket.GetType().Name}) " +
                $"in window '{windowTag}'.";
            Debug.LogError(message);
            throw new InvalidOperationException(message);
        }

        StringPairPacket[] inputValues = ClickContextResolver.CollectInputValues(inputRoot);
        _networkService.Send(new ElementClickPacket(windowTag, elementIndex, inputValues));
    }
}
