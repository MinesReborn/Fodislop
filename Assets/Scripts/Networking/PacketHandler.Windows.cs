using System.Collections.Generic;
using System.Linq;
using Fodinae.Scripts.UI;
using Fodinae.UI;
using Fodinae.UI.Binding;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.Networking
{
    /// <summary>
    /// Partial PacketHandler implementation for Dynamic Server Windows and Modal Dialogs.
    /// Handles OpenWindowPacket, CloseWindowPacket, ModalWindowPacket, and Element Click Dispatching.
    /// </summary>
    public partial class PacketHandler
    {
        private void HandleOpenWindowPacket(OpenWindowPacket packet)
        {
            _packetCount++;
            Debug.Log($"[PacketHandler] Handling OpenWindowPacket: {packet.WindowTag}");

            if (_uiDocument == null)
            {
                _uiDocument = FindAnyObjectByType<UIDocument>();
                if (_uiDocument == null)
                {
                    Debug.LogError("[PacketHandler] Cannot open window: UIDocument not found");
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

            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _uiDocument.rootVisualElement.Add(overlay);

            _uiDocument.rootVisualElement.Add(element);
            UIInputManager.Instance.PushModal(element);

            // Set up SmartFormat binding for this window
            var binding = new WindowBinding();
            binding.Bind(element);

            // Register clickable elements via DFS traversal
            var clickableElements = RegisterClickableElements(element, packet.WindowTag);

            var windowIndex = _openWindows.Count;
            _openWindows.Add((packet.WindowTag, element, overlay, binding, clickableElements));
            Debug.Log($"[PacketHandler] Window '{packet.WindowTag}' opened with {clickableElements.Count} clickable elements (window index {windowIndex})");
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
                WireClickHandler(element, elementIndex, windowTag);
            }

            foreach (var child in element.Children())
            {
                WalkForClickable(child, clickableElements, windowTag);
            }
        }

        private void WireClickHandler(VisualElement element, int elementIndex, string windowTag)
        {
            element.RegisterCallback<ClickEvent>(_ => HandleElementClick(element, elementIndex, windowTag));
        }

        private void HandleElementClick(VisualElement clickedElement, int elementIndex, string windowTag)
        {
            var windowEntry = _openWindows.Find(w => w.tag == windowTag);
            if (windowEntry == default)
            {
                Debug.LogWarning($"[PacketHandler] Cannot handle element click: window '{windowTag}' not found");
                return;
            }

            var (_, windowRoot, _, _, _) = windowEntry;

            if (clickedElement.userData is not IGUIComponentPacket componentPacket)
            {
                Debug.LogWarning("[PacketHandler] Clicked element has no IGUIComponentPacket userData");
                return;
            }

            var clickContext = componentPacket.OnClickContext;
            Debug.Log($"[PacketHandler] Element click: index={elementIndex}, context='{clickContext}', window='{windowTag}'");

            var inputRoot = ClickContextResolver.ResolveRoot(clickedElement, windowRoot, clickContext);
            var inputValues = ClickContextResolver.CollectInputValues(inputRoot);

            var elementClickPacket = new ElementClickPacket(windowTag, elementIndex, inputValues);
            NetworkService.Send(elementClickPacket);

            Debug.Log($"[PacketHandler] Sent ElementClickPacket: index={elementIndex}, contextValues={inputValues.Length}");
        }

        private void HandleCloseWindowPacket(CloseWindowPacket packet)
        {
            _packetCount++;
            Debug.Log("[PacketHandler] Handling CloseWindowPacket");

            _modalWindowHandler?.Hide();

            if (_openWindows.Count == 0)
            {
                return;
            }

            var (_, root, overlay, binding, _) = _openWindows[^1];
            binding.Dispose();
            UIInputManager.Instance.PopModal(root);
            if (overlay.parent != null)
            {
                overlay.parent.Remove(overlay);
            }

            _uiDocument.rootVisualElement.Remove(root);
            _openWindows.RemoveAt(_openWindows.Count - 1);
        }

        private void HandleModalWindowPacket(ModalWindowPacket packet)
        {
            _packetCount++;
            Debug.Log("[PacketHandler] Handling ModalWindowPacket");

            if (_uiDocument == null)
            {
                _uiDocument = FindAnyObjectByType<UIDocument>();
            }

            if (_modalWindowHandler == null && _uiDocument != null)
            {
                _modalWindowHandler = new ModalWindowHandler(_uiDocument);
            }

            _modalWindowHandler?.Show(packet);
        }
    }
}
