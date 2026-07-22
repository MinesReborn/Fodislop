using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.Core.Interfaces;
using Fodinae.Scripts.UI;
using Fodinae.UI;
using Fodinae.UI.Binding;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.Networking.Processors
{
    /// <summary>
    /// Decoupled SOLID Processor for dynamic server WPF windows and element click contexts.
    /// Handles OpenWindowPacket, CloseWindowPacket, and ElementClickPacket dispatching.
    /// </summary>
    public class WindowPacketProcessor : IPacketProcessor<OpenWindowPacket>, IPacketProcessor<CloseWindowPacket>, IInputBlocker
    {
        private UIDocument _uiDocument;
        private ModalWindowHandler _modalWindowHandler;
        private readonly List<(string tag, VisualElement root, WindowBinding binding, List<VisualElement> clickableElements)> _openWindows = new();

        public bool HasOpenWindows => _openWindows.Count > 0;
        public string TopWindowTag => _openWindows.Count > 0 ? _openWindows[^1].tag : null;
        public bool IsModalShowing => _modalWindowHandler?.IsShowing == true;
        bool IInputBlocker.IsInputBlocked => HasOpenWindows || IsModalShowing || PauseMenu.IsMenuOpen;

        public void Initialize(UIDocument doc, ModalWindowHandler handler)
        {
            _uiDocument = doc;
            _modalWindowHandler = handler;
        }

        public void Process(OpenWindowPacket packet)
        {
            Debug.Log($"[WindowPacketProcessor] Opening window '{packet.WindowTag}'");

            if (_uiDocument == null)
            {
                _uiDocument = Object.FindAnyObjectByType<UIDocument>();
                if (_uiDocument == null)
                {
                    Debug.LogError("[WindowPacketProcessor] Cannot open window: UIDocument not found");
                    return;
                }
            }

            var builder = new PacketUIBuilder();
            var element = builder.Build(packet.Content);

            element.style.width = packet.Width;
            element.style.height = packet.Height;
            element.style.position = Position.Absolute;
            element.style.left = new Length(50, LengthUnit.Percent);
            element.style.top = new Length(50, LengthUnit.Percent);
            element.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));

            _uiDocument.rootVisualElement.Add(element);
            UIInputManager.Instance.PushModal(element);

            var binding = new WindowBinding();
            binding.Bind(element);

            var clickableElements = RegisterClickableElements(element, packet.WindowTag);
            _openWindows.Add((packet.WindowTag, element, binding, clickableElements));
        }

        public void Process(CloseWindowPacket packet)
        {
            Debug.Log("[WindowPacketProcessor] Closing top window");

            if (_openWindows.Count == 0)
            {
                return;
            }

            var (_, root, binding, _) = _openWindows[^1];
            binding.Dispose();
            UIInputManager.Instance.PopModal(root);

            if (_uiDocument != null)
            {
                _uiDocument.rootVisualElement.Remove(root);
            }

            _openWindows.RemoveAt(_openWindows.Count - 1);
        }

        public void HandleModalWindow(ModalWindowPacket packet)
        {
            Debug.Log("[WindowPacketProcessor] Handling ModalWindowPacket");

            if (_uiDocument == null)
            {
                _uiDocument = Object.FindAnyObjectByType<UIDocument>();
            }

            if (_modalWindowHandler == null && _uiDocument != null)
            {
                _modalWindowHandler = new ModalWindowHandler(_uiDocument);
            }

            _modalWindowHandler?.Show(packet);
        }

        private List<VisualElement> RegisterClickableElements(VisualElement windowRoot, string windowTag)
        {
            var clickableElements = new List<VisualElement>();
            WalkForClickable(windowRoot, clickableElements, windowTag);
            return clickableElements;
        }

        private void WalkForClickable(VisualElement element, List<VisualElement> clickableElements, string windowTag)
        {
            if (element.userData is IGUIComponentPacket componentPacket &&
                !string.IsNullOrEmpty(componentPacket.OnClickContext))
            {
                var elementIndex = clickableElements.Count;
                clickableElements.Add(element);
                element.RegisterCallback<ClickEvent>(_ => HandleElementClick(element, elementIndex, windowTag));
            }

            foreach (var child in element.Children())
            {
                WalkForClickable(child, clickableElements, windowTag);
            }
        }

        private void HandleElementClick(VisualElement clickedElement, int elementIndex, string windowTag)
        {
            var windowEntry = _openWindows.Find(w => w.tag == windowTag);
            if (windowEntry == default)
            {
                return;
            }

            var (_, windowRoot, _, _) = windowEntry;
            if (clickedElement.userData is not IGUIComponentPacket componentPacket)
            {
                return;
            }

            var clickContext = componentPacket.OnClickContext;
            var inputRoot = ClickContextResolver.ResolveRoot(clickedElement, windowRoot, clickContext);
            var inputValues = ClickContextResolver.CollectInputValues(inputRoot);

            NetworkService.Send(new ElementClickPacket(windowTag, elementIndex, inputValues));
        }

        public void Dispose()
        {
            _modalWindowHandler?.Hide();

            foreach (var (_, root, binding, _) in _openWindows)
            {
                binding.Dispose();
                if (_uiDocument != null)
                {
                    _uiDocument.rootVisualElement.Remove(root);
                }
            }

            _openWindows.Clear();
        }
    }
}
