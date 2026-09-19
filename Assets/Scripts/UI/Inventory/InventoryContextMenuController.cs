#nullable enable

using System;
using Kern.Core.Localization;
using Kern.Core.Models;
using Kern.Game.Inventory;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Inventory
{
    internal sealed class InventoryContextMenuController
    {
        private readonly UIDocument _doc;
        private readonly IInventoryModel _model;
        private readonly ILocalizationService _loc;
        private VisualElement? _contextMenu;

        public InventoryContextMenuController(UIDocument doc, IInventoryModel model, ILocalizationService loc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        }

        public void ShowContextMenu(Vector2 mousePosition, int slotIndex, Action<ItemData> showItemInfo)
        {
            ItemData? item = _model.GetSlot(slotIndex);
            if (item == null)
            {
                return;
            }

            VisualElement root = _doc.rootVisualElement;
            _contextMenu = new VisualElement
            {
                name = "ContextMenu",
            };
            _contextMenu.AddToClassList("inv-context-menu");
            _contextMenu.style.left = mousePosition.x;
            _contextMenu.style.top = mousePosition.y;
            _contextMenu.pickingMode = PickingMode.Position;

            AddContextMenuItem(_loc.Get("inventory.context_use"), () =>
            {
                _model.SelectSlot(slotIndex);
                _model.UseSelectedItem();
                HideContextMenu();
            });

            AddContextMenuItem(_loc.Get("inventory.context_info"), () =>
            {
                showItemInfo(item);
                HideContextMenu();
            });

            root.Add(_contextMenu);
            root.RegisterCallback<MouseDownEvent>(OnContextMenuOutsideClick, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnContextMenuEscape, TrickleDown.TrickleDown);
        }

        public void HideContextMenu()
        {
            if (_contextMenu != null)
            {
                _contextMenu.RemoveFromHierarchy();
                _contextMenu = null;
            }

            if (_doc.rootVisualElement is not VisualElement root)
            {
                return;
            }

            root.UnregisterCallback<MouseDownEvent>(OnContextMenuOutsideClick, TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyDownEvent>(OnContextMenuEscape, TrickleDown.TrickleDown);
        }

        private void AddContextMenuItem(string labelText, Action onClick)
        {
            Button button = new(onClick)
            {
                text = labelText,
            };
            button.AddToClassList("inv-context-btn");
            _contextMenu?.Add(button);
        }

        private void OnContextMenuOutsideClick(MouseDownEvent evt)
        {
            if (_contextMenu != null && !_contextMenu.worldBound.Contains(evt.mousePosition))
            {
                HideContextMenu();
            }
        }

        private void OnContextMenuEscape(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                HideContextMenu();
            }
        }
    }
}
