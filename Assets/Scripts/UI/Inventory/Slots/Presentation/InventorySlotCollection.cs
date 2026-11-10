#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Core.Models;
using Kern.Game.Inventory;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;
using Kern.UI.Input;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Inventory;

internal sealed class InventorySlotCollection
{
    private readonly IInventoryModel _model;
    private readonly IItemCatalog _catalog;

    internal InventorySlotCollection(IInventoryModel model, IItemCatalog catalog)
    {
        _model = model;
        _catalog = catalog;
    }

    internal void Clear() => _cellElements.Clear();


        internal readonly Dictionary<ItemType, List<VisualElement>> _cellElements = new();

        // Ячейка макета (visual/kern-ui .inv-slots li): 64×64, значок 40 по центру, номер «01» слева сверху,
        // количество справа снизу; размеры — в Inventory.uss. Подсказка — название предмета
        internal VisualElement CreateCell(ItemType type, int index)
        {
            var cell = new VisualElement();
            cell.name = "Cell_" + type;
            cell.userData = type;
            cell.AddToClassList("inv-slot");
            // InventoryRoot стоит в picking-mode="Ignore"; ячейка явно возвращает Position
            cell.pickingMode = PickingMode.Position;

            var number = new Label((index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture));
            number.AddToClassList("inv-slot__index");
            number.pickingMode = PickingMode.Ignore;
            cell.Add(number);

            var icon = new VisualElement();
            icon.name = "Icon";
            icon.AddToClassList("inv-slot__icon");
            icon.style.display = DisplayStyle.None;
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            var qtyLabel = new Label();
            qtyLabel.name = "Quantity";
            qtyLabel.AddToClassList("inv-slot__qty");
            qtyLabel.pickingMode = PickingMode.Ignore;
            cell.Add(qtyLabel);

            // Подсветка наведения — только мышью: у пальца наведения нет, и
            // подсветка залипала бы на последней тронутой ячейке.
            cell.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
                {
                    cell.AddToClassList("is-hover");
                }
            });
            cell.RegisterCallback<PointerLeaveEvent>(_ => cell.RemoveFromClassList("is-hover"));

            // Нажатие выбирает предмет. Повторное касание пальцем уже
            // выбранного — применяет его: на сенсорном экране это единственная
            // замена Enter. Мышь, как и раньше, только выбирает.
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                if (_model!.SelectedItem == type && evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                {
                    _model.UseSelectedItem();
                }
                else
                {
                    _model.Select(type);
                }
            });

            if (!_cellElements.TryGetValue(type, out List<VisualElement>? list))
            {
                list = new List<VisualElement>();
                _cellElements[type] = list;
            }

            list.Add(cell);

            RefreshCell(cell, type);
            return cell;
        }

        internal void RefreshCell(VisualElement cell, ItemType type)
        {
            ItemData? item = _model!.GetItem(type);

            var icon = cell.Q<VisualElement>("Icon");
            var qty = cell.Q<Label>("Quantity");

            if (item != null)
            {
                cell.tooltip = string.IsNullOrWhiteSpace(item.Name) || item.Name == type.ToString()
                    ? _catalog.GetName(type)
                    : item.Name;
                icon.style.display = DisplayStyle.Flex;
                Texture2D? texture = item.Icon ?? _catalog.GetIcon(type);
                if (texture != null)
                {
                    icon.style.backgroundImage = new StyleBackground(texture);
                    icon.style.backgroundColor = Color.clear;
                }
                else
                {
                    icon.style.backgroundImage = null;
                    icon.style.backgroundColor = item.IconColor;
                }

                qty.text = item.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                icon.style.display = DisplayStyle.None;
                qty.text = string.Empty;
            }
        }

        internal void ApplySelection()
        {
            ItemType? selected = _model!.SelectedItem;
            foreach ((ItemType type, List<VisualElement> cells) in _cellElements)
            {
                bool isSelected = type == selected;
                foreach (VisualElement cell in cells)
                {
                    cell.EnableInClassList("is-on", isSelected);
                }
            }
        }

}
