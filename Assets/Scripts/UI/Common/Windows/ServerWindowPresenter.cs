#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core.Interfaces;
using Fodinae.Networking;
using Fodinae.UI.Binding;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Shared.Packets;
using UnityEngine.UIElements;

namespace Fodinae.UI;

public sealed class ServerWindowPresenter : IDisposable
{
    private readonly IAssetLoader _assetLoader;
    private readonly IAsyncOperationSupervisor _operations;
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
        IAsyncOperationSupervisor operations)
    {
        _assetLoader = assetLoader;
        _operations = operations;
        _uiInputManager = uiInputManager;
        _networkService = networkService;
        _document = document;
        _commands = commands;
        _modalWindowHandler = new ModalWindowHandler(document);
        _commands.OpenRequested += Open;
        _commands.CloseRequested += Close;
        _commands.ModalRequested += ShowModal;
    }

    public bool HasOpenWindows => _openWindows.Count > 0;

    public string? TopWindowTag => _openWindows.Count > 0 ? _openWindows[^1].Tag : null;

    public bool IsModalShowing => _modalWindowHandler.IsShowing;

    public void Dispose()
    {
        _commands.OpenRequested -= Open;
        _commands.CloseRequested -= Close;
        _commands.ModalRequested -= ShowModal;
        _modalWindowHandler.Hide();
        foreach ((_, VisualElement root, WindowBinding binding) in _openWindows)
        {
            binding.Dispose();
            root.RemoveFromHierarchy();
        }

        _openWindows.Clear();
        _commands.SetOpenWindowVisibility(false);
    }

    private void Open(OpenWindowPacket packet)
    {
        VisualElement element = new PacketUIBuilder(_assetLoader, _operations).Build(packet.Content);
        // Размер приходит из пакета — он и остаётся инлайном. Центрирование
        // же константа, и раньше оно тоже стояло инлайном: окно нельзя было
        // сдвинуть ни темой, ни тиром, потому что инлайн бьёт любое правило.
        element.style.width = packet.Width;
        element.style.height = packet.Height;
        element.AddToClassList("centered");
        element.AddToClassList("sci-fi-panel");
        element.AddToClassList("sci-fi-panel--tech");
        element.AddToClassList("sci-fi-window-anim");
        _document.rootVisualElement.Add(element);
        UILayoutTier.Attach(element);
        _uiInputManager.PushModal(element);

        var binding = new WindowBinding();
        binding.Bind(element);
        RegisterClickableElements(element, element, packet.WindowTag, 0);
        _openWindows.Add((packet.WindowTag, element, binding));
        _commands.SetOpenWindowVisibility(true);
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
        _commands.SetOpenWindowVisibility(_openWindows.Count > 0);
    }

    private void ShowModal(ModalWindowPacket packet)
    {
        _modalWindowHandler.Show(packet);
    }

    private int RegisterClickableElements(
        VisualElement element,
        VisualElement windowRoot,
        string windowTag,
        int nextIndex)
    {
        if (element.userData is IGUIComponentPacket componentPacket &&
            !string.IsNullOrEmpty(componentPacket.OnClickContext))
        {
            int elementIndex = nextIndex++;
            element.RegisterCallback<ClickEvent>(_ =>
                HandleElementClick(element, windowRoot, elementIndex, windowTag));
        }

        foreach (VisualElement child in element.Children())
        {
            nextIndex = RegisterClickableElements(child, windowRoot, windowTag, nextIndex);
        }

        return nextIndex;
    }

    private void HandleElementClick(
        VisualElement clickedElement,
        VisualElement windowRoot,
        int elementIndex,
        string windowTag)
    {
        if (clickedElement.userData is not IGUIComponentPacket componentPacket)
        {
            return;
        }

        VisualElement? inputRoot = ClickContextResolver.ResolveRoot(
            clickedElement,
            windowRoot,
            componentPacket.OnClickContext);
        StringPairPacket[] inputValues = ClickContextResolver.CollectInputValues(inputRoot);
        _networkService.Send(new ElementClickPacket(windowTag, elementIndex, inputValues));
    }
}
