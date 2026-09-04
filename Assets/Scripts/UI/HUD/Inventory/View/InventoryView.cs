#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Localization;
using Fodinae.Core.Models;
using Fodinae.Networking;
using Fodinae.UI.HUD.Inventory.Interfaces;
using Fodinae.UI.HUD.Inventory.Model;
using MinesServer.Data;
using MinesServer.Networking.Client.Packets.GUI;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI.HUD.Inventory.View
{
    public class InventoryView : MonoBehaviour, ILocalizableUI
    {
        private const int HOTBAR_COLS = 9;
        private const int INVENTORY_COLS = 9;
        private const int CELLSIZE = 50;

        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private IInventoryModel _model = null!;
        [Inject]
        private Fodinae.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private ILocalizationService _loc = null!;

        private readonly Dictionary<int, List<VisualElement>> _slotElements = new();
        private readonly InventoryDragAndContextMenu _dragAndContext = new();

        private VisualElement? _hotbarContainer;
        private Button? _inventoryButton;
        private VisualElement? _fullInventoryPanel;
        private bool _isInventoryOpen;
        private Label? _capacityLabel;

        private int _lastSelectedSlot = -1;
        private VisualElement _tooltipWrapper = null!;
        private VisualElement _tooltipBg = null!;
        private Label _tooltipName = null!;
        private Label _tooltipDesc = null!;
        private bool _initialized;

        protected void Start()
        {
            TryInitialize();
        }

        public void EnsureInitialized()
        {
            TryInitialize();
        }

        protected void OnDestroy()
        {
            if (_loc != null)
            {
                _loc.UnregisterLocalizable(this);
            }

            if (_model != null)
            {
                _model.OnSlotChanged -= RefreshSlot;
                _model.OnSlotSelected -= OnModelSlotSelected;
            }

            _dragAndContext.Cleanup(_doc?.rootVisualElement);
        }

        protected void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.tabKey.wasPressedThisFrame ||
                (Keyboard.current.iKey.wasPressedThisFrame && !ChatInput.IsFocused))
            {
                ToggleInventory();
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(0);
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(1);
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(2);
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(3);
            }
            else if (Keyboard.current.digit5Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(4);
            }
            else if (Keyboard.current.digit6Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(5);
            }
            else if (Keyboard.current.digit7Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(6);
            }
            else if (Keyboard.current.digit8Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(7);
            }
            else if (Keyboard.current.digit9Key.wasPressedThisFrame)
            {
                _model!.SelectSlot(8);
            }
            else if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                // Enter применяет выбранный предмет и имеет приоритет над открытием
                // чата: чат не должен перехватывать Enter и красть предмет у узла.
                // Если слот не выбран — Enter ведёт себя обычным образом
                // (в том числе открывает чат в GlobalChatUI).
                // Пустой Enter (нет выбранного предмета) передаёт управление чату,
                // чтобы открытие чата не конфликтовало с применением предмета.
                _model!.UseSelectedItem();
            }
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            if (_doc.rootVisualElement == null)
            {
                throw new InvalidOperationException(
                    "[InventoryView] Injected UIDocument has no root visual element.");
            }

            IInventoryModel model = _model ?? throw new InvalidOperationException(
                "[InventoryView] IInventoryModel injection is required before initialization.");
            _model = model;

            if (_inputBlocker == null)
            {
                throw new InvalidOperationException(
                    "[InventoryView] IInputBlocker injection is required before initialization.");
            }

            if (_loc == null)
            {
                throw new InvalidOperationException(
                    "[InventoryView] ILocalizationService injection is required before initialization.");
            }

            _model.OnSlotChanged += RefreshSlot;
            _model.OnSlotSelected += OnModelSlotSelected;

            CreateTooltip(_doc.rootVisualElement);
            BuildUI();
            _initialized = true;

            _loc.RegisterLocalizable(this);
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(InventoryView));
            UILocalizer.Apply(_doc.rootVisualElement, _loc);
            if (_capacityLabel != null)
            {
                _capacityLabel.text = _loc.Get("inventory.capacity", InventoryModel.TOTALSLOTS);
            }

            UILocalizer.AssertLocalized(_doc.rootVisualElement, _loc);
        }

        private void OnModelSlotSelected(int slotIndex)
        {
            if (_lastSelectedSlot >= 0 && _slotElements.ContainsKey(_lastSelectedSlot))
            {
                foreach (var cell in _slotElements[_lastSelectedSlot])
                {
                    cell.RemoveFromClassList("inv-cell--selected");
                }
            }

            _lastSelectedSlot = slotIndex;

            if (slotIndex >= 0 && _slotElements.ContainsKey(slotIndex))
            {
                foreach (var cell in _slotElements[slotIndex])
                {
                    cell.AddToClassList("inv-cell--selected");
                }
            }

            if (slotIndex >= 0)
            {
                var item = _model!.GetSlot(slotIndex);
                if (item != null)
                {
                    _tooltipName.text = item.Name;
                    _tooltipDesc.text = item.Description ?? string.Empty;
                    _tooltipWrapper.style.display = DisplayStyle.Flex;
                    return;
                }
            }

            _tooltipWrapper.style.display = DisplayStyle.None;
        }

        private void CreateTooltip(VisualElement root)
        {
            _tooltipWrapper = new VisualElement();
            _tooltipWrapper.AddToClassList("inv-tooltip-wrapper");
            _tooltipWrapper.style.display = DisplayStyle.None;

            _tooltipBg = new VisualElement();
            _tooltipBg.AddToClassList("inv-tooltip-bg");

            _tooltipName = new Label();
            _tooltipName.AddToClassList("inv-tooltip-name");
            _tooltipBg.Add(_tooltipName);

            _tooltipDesc = new Label();
            _tooltipDesc.AddToClassList("inv-tooltip-desc");
            _tooltipBg.Add(_tooltipDesc);

            _tooltipWrapper.Add(_tooltipBg);
            root.Add(_tooltipWrapper);
        }

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;

            var uxml = Resources.Load<VisualTreeAsset>("UI/Inventory");
            if (uxml != null)
            {
                TemplateContainer tree = uxml.Instantiate();
                tree.AddToClassList("ui-fullscreen");
                tree.pickingMode = PickingMode.Ignore;
                root.Add(tree);

                if (_loc != null)
                {
                    UILocalizer.Apply(tree, _loc);
                }

                _hotbarContainer = tree.Q<VisualElement>("HotbarContainer");
                var hotbarSlots = tree.Q<VisualElement>("HotbarSlots") ?? _hotbarContainer;
                for (int i = 0; i < HOTBAR_COLS; i++)
                {
                    var cell = CreateCell(i, $"Hotbar_{i}");
                    hotbarSlots.Add(cell);
                }

                _inventoryButton = tree.Q<Button>("InventoryToggleBtn");
                if (_inventoryButton != null)
                {
                    _inventoryButton.clicked += ToggleInventory;
                    if (_loc != null)
                    {
                        _inventoryButton.tooltip = _loc.Get("inventory.open");
                        Label? toggleLabel = _inventoryButton.Q<Label>();
                        if (toggleLabel != null)
                        {
                            toggleLabel.text = _loc.Get("inventory.hotbar");
                        }
                    }
                }

                Label? inventoryTitle = tree.Q<Label>("InventoryTitle");
                if (inventoryTitle != null && _loc != null)
                {
                    inventoryTitle.text = _loc.Get("inventory.title");
                }

                _fullInventoryPanel = tree.Q<VisualElement>("FullInventoryPanel");
                var closeBtn = tree.Q<Button>("CloseInventoryBtn");
                if (closeBtn != null)
                {
                    closeBtn.clicked += ToggleInventory;
                }

                var inventoryGrid = tree.Q<VisualElement>("InventoryGrid");
                if (inventoryGrid != null)
                {
                    var grid = CreateGrid(0, InventoryModel.TOTALSLOTS - 1, "Inv");
                    inventoryGrid.Add(grid);
                }

                _capacityLabel = tree.Q<Label>("CapacityLabel");
                if (_capacityLabel != null && _loc != null)
                {
                    _capacityLabel.text = _loc.Get("inventory.capacity", InventoryModel.TOTALSLOTS);
                }

                Debug.Log($"[InventoryView] UI built: hotbar={(object?)_hotbarContainer ?? "NULL"}, " +
                    $"button={(object?)_inventoryButton ?? "NULL"}, treeChildren={tree.childCount}");
            }
            else
            {
                throw new InvalidOperationException("[InventoryView] Failed to load UI/Inventory.uxml");
            }
        }

        private VisualElement CreateGrid(int fromSlot, int toSlot, string prefix)
        {
            var grid = new VisualElement();
            grid.name = $"{prefix}_Grid";
            grid.AddToClassList("inv-grid");

            int slotIndex = fromSlot;
            int cols = (toSlot - fromSlot + 1 > 9) ? INVENTORY_COLS : (toSlot - fromSlot + 1);
            int rows = (toSlot - fromSlot + 1 + cols - 1) / cols;

            for (int row = 0; row < rows; row++)
            {
                var rowContainer = new VisualElement();
                rowContainer.AddToClassList("inv-grid-row");

                for (int col = 0; col < cols && slotIndex <= toSlot; col++, slotIndex++)
                {
                    rowContainer.Add(CreateCell(slotIndex, $"{prefix}_{slotIndex}"));
                }

                grid.Add(rowContainer);
            }

            return grid;
        }

        private VisualElement CreateCell(int slotIndex, string name)
        {
            var cell = new VisualElement();
            cell.name = name;
            cell.userData = slotIndex;
            cell.AddToClassList("inv-cell");
            // InventoryRoot стоит в picking-mode="Ignore" (клики пустого поля
            // уходят миру); ячейка обязана явно вернуть Position, иначе Ignore
            // наследуется на поддерево и слот не получает мышь вообще — хотбар
            // выглядит как мёртвый интерфейс.
            cell.pickingMode = PickingMode.Position;
            cell.style.width = CELLSIZE;
            cell.style.height = CELLSIZE;
            cell.style.minWidth = CELLSIZE;
            cell.style.minHeight = CELLSIZE;
            cell.style.flexShrink = 0;
            cell.style.flexGrow = 0;
            cell.style.marginRight = 3;
            cell.style.marginLeft = 3;
            cell.style.marginTop = 3;
            cell.style.marginBottom = 3;
            cell.style.backgroundColor = new Color(0.08f, 0.1f, 0.15f, 0.85f);
            cell.style.borderTopWidth = 1;
            cell.style.borderBottomWidth = 1;
            cell.style.borderLeftWidth = 1;
            cell.style.borderRightWidth = 1;
            cell.style.borderTopColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderBottomColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderLeftColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderRightColor = new Color(0.31f, 0.55f, 0.78f, 0.4f);
            cell.style.borderTopLeftRadius = 4;
            cell.style.borderTopRightRadius = 4;
            cell.style.borderBottomLeftRadius = 4;
            cell.style.borderBottomRightRadius = 4;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;

            var icon = new VisualElement();
            icon.name = "Icon";
            icon.AddToClassList("inv-icon");
            icon.style.display = DisplayStyle.None;
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            var qtyLabel = new Label();
            qtyLabel.name = "Quantity";
            qtyLabel.AddToClassList("inv-qty");
            qtyLabel.style.textShadow = new TextShadow
            {
                color = Color.black,
                offset = new Vector2(1, -1),
            };
            qtyLabel.pickingMode = PickingMode.Ignore;
            cell.Add(qtyLabel);

            cell.RegisterCallback<MouseEnterEvent>(_ => cell.AddToClassList("inv-cell--highlight"));
            cell.RegisterCallback<MouseLeaveEvent>(_ => cell.RemoveFromClassList("inv-cell--highlight"));

            cell.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _model!.SelectSlot(slotIndex);
                }
                else if (evt.button == 1)
                {
                    _dragAndContext.HideContextMenu(_doc.rootVisualElement);
                    _dragAndContext.ShowContextMenu(
                        evt.mousePosition,
                        slotIndex,
                        _doc.rootVisualElement,
                        _model!,
                        _loc!,
                        ShowItemInfo);
                    evt.StopPropagation();
                }
            });

            if (!_slotElements.ContainsKey(slotIndex))
            {
                _slotElements[slotIndex] = new List<VisualElement>();
            }

            _slotElements[slotIndex].Add(cell);

            RefreshSlot(slotIndex);
            return cell;
        }

        private void RefreshSlot(int slotIndex)
        {
            if (!_slotElements.ContainsKey(slotIndex))
            {
                return;
            }

            var item = _model!.GetSlot(slotIndex);

            foreach (var cell in _slotElements[slotIndex])
            {
                var icon = cell.Q<VisualElement>("Icon");
                var qty = cell.Q<Label>("Quantity");

                if (item != null)
                {
                    icon.style.display = DisplayStyle.Flex;
                    if (item.Icon != null)
                    {
                        icon.style.backgroundImage = new StyleBackground(item.Icon);
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
        }

        private void ToggleInventory()
        {
            _isInventoryOpen = !_isInventoryOpen;
            if (_fullInventoryPanel != null)
            {
                _fullInventoryPanel.style.display = _isInventoryOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public IInventoryModel? GetModel() => _model;

        private void ShowItemInfo(ItemData item)
        {
            _tooltipName.text = _loc!.Get("inventory.tooltip_item", item.Name ?? item.ItemType.ToString(), item.ItemType, item.Quantity);
            _tooltipDesc.text = _loc!.Get("inventory.tooltip_type", item.ItemType) + "\n" + (item.Description ?? _loc.Get("inventory.no_description"));
            _tooltipWrapper.style.display = DisplayStyle.Flex;
        }
    }
}
