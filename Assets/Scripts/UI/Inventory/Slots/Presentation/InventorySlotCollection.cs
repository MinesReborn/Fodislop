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

        internal VisualElement CreateCell(ItemType type)
        {
            var cell = new VisualElement();
            cell.name = "Cell_" + type;
            cell.userData = type;
            cell.AddToClassList("inv-cell");
            // Размер клетки продублирован инлайном (62px): иконка 38px по
            // центру оставляет по 12px полей, и цифра количества в правом
            // нижнем углу ложится на тёмный фон клетки, не на спрайт
            // (репорт: «расширить квадратик рамки инвентарной»).
            cell.style.width = 62f;
            cell.style.height = 62f;
            cell.style.minWidth = 62f;
            cell.style.minHeight = 62f;
            // InventoryRoot стоит в picking-mode="Ignore"; ячейка обязана явно
            // вернуть Position, иначе Ignore наследуется на поддерево и слот
            // не получает мышь вообще.
            cell.pickingMode = PickingMode.Position;

            var icon = new VisualElement();
            icon.name = "Icon";
            icon.AddToClassList("inv-icon");
            icon.style.display = DisplayStyle.None;
            // Размер иконки продублирован инлайном: если правило .inv-icon
            // из Inventory.uss не применится, VisualElement по умолчанию
            // растягивается на всю клетку — и цифра количества в любом углу
            // оказывалась бы поверх спрайта (репорт: «налезает на спрайт»).
            icon.style.width = 38f;
            icon.style.height = 38f;
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            // Количество — цифра в правом нижнем углу слота. Растянутый на
            // всю клетку абсолютный контейнер (инлайн, все отступы —
            // литеральные нули) прижимает содержимое флексом в угол — это
            // работает при любых таблицах стилей, в отличие от якорения
            // right/bottom. Сама цифра — чистый текст с тенью, без плашки:
            // угол под неё освобождает увеличенная клетка (62px).
            var qtyWrap = new VisualElement();
            qtyWrap.name = "QuantityWrap";
            qtyWrap.AddToClassList("inv-qty-wrap");
            qtyWrap.style.position = Position.Absolute;
            qtyWrap.style.left = 0f;
            qtyWrap.style.right = 0f;
            qtyWrap.style.top = 0f;
            qtyWrap.style.bottom = 0f;
            qtyWrap.style.justifyContent = Justify.FlexEnd;
            qtyWrap.style.alignItems = Align.FlexEnd;
            qtyWrap.pickingMode = PickingMode.Ignore;
            cell.Add(qtyWrap);

            var qtyLabel = new Label();
            qtyLabel.name = "Quantity";
            qtyLabel.AddToClassList("inv-qty");
            qtyLabel.style.textShadow = new TextShadow
            {
                color = Color.black,
                offset = new Vector2(1, -1),
            };
            // Инлайн перебивает абсолютную раскладку прежних правил
            // .inv-qty из Inventory.uss: цифра — обычный (относительный)
            // элемент размером по содержимому, без плашки — угол под неё
            // освобождает увеличенная клетка.
            qtyLabel.style.position = Position.Relative;
            qtyLabel.style.marginRight = 1f;
            qtyLabel.style.marginBottom = 1f;
            qtyLabel.AddToClassList("text-size-2xs");
            qtyLabel.style.color = Color.white;
            qtyLabel.pickingMode = PickingMode.Ignore;
            qtyWrap.Add(qtyLabel);

            // Подсветка наведения — только мышью: у пальца наведения нет, и
            // подсветка залипала бы на последней тронутой ячейке.
            cell.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
                {
                    cell.AddToClassList("inv-cell--highlight");
                }
            });
            cell.RegisterCallback<PointerLeaveEvent>(_ => cell.RemoveFromClassList("inv-cell--highlight"));

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

                qty.text = item.Quantity > 1 ? item.Quantity.ToString() : string.Empty;
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
                    cell.EnableInClassList("inv-cell--selected", isSelected);
                }
            }
        }

}
