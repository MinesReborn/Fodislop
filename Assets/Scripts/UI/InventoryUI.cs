using System.Collections.Generic;
using Fodinae.Scripts.Networking;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class InventoryUI : MonoBehaviour
    {
        private const int HOTBAR_COLS = 9;
        private const int INVENTORY_ROWS = 6;
        private const int INVENTORY_COLS = 9;
        private const int CELL_SIZE = 50;
        private const int CELL_GAP = 10;
        private const int ICON_SIZE = 36;

        private Color _cellBgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        private Color _cellBorderColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        private Color _cellHighlightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private Color _selectedBorderColor = new Color(1f, 0.84f, 0f, 1f);
        private Color _inventoryButtonColor = new Color(0.7f, 0.65f, 0.5f, 1f);
        private Color _inventoryButtonHoverColor = new Color(0.8f, 0.75f, 0.6f, 1f);
        private Color _panelBgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        private Color _panelBorderColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        private UIDocument _doc;
        private InventoryModel _model;
        private Dictionary<int, List<VisualElement>> _slotElements = new Dictionary<int, List<VisualElement>>();
        private VisualElement _hotbarContainer;
        private Button _inventoryButton;
        private VisualElement _fullInventoryPanel;
        private bool _isInventoryOpen = false;

        // Drag-and-drop
        private VisualElement _floatingItem;
        private int _dragFromSlot = -1;
        private ItemData _draggedItem;

        // Selection
        private int _lastSelectedSlot = -1;
        private VisualElement _tooltipWrapper;
        private VisualElement _tooltipBg;
        private Label _tooltipName;
        private Label _tooltipDesc;

        void Start()
        {
            InitializeInventory();
        }

        void Update()
        {
            if (Keyboard.current == null) return;
            if (PacketHandler.IsInputBlocked) return;

            if (Keyboard.current.digit1Key.wasPressedThisFrame) _model.SelectSlot(0);
            else if (Keyboard.current.digit2Key.wasPressedThisFrame) _model.SelectSlot(1);
            else if (Keyboard.current.digit3Key.wasPressedThisFrame) _model.SelectSlot(2);
            else if (Keyboard.current.digit4Key.wasPressedThisFrame) _model.SelectSlot(3);
            else if (Keyboard.current.digit5Key.wasPressedThisFrame) _model.SelectSlot(4);
            else if (Keyboard.current.digit6Key.wasPressedThisFrame) _model.SelectSlot(5);
            else if (Keyboard.current.digit7Key.wasPressedThisFrame) _model.SelectSlot(6);
            else if (Keyboard.current.digit8Key.wasPressedThisFrame) _model.SelectSlot(7);
            else if (Keyboard.current.digit9Key.wasPressedThisFrame) _model.SelectSlot(8);
            else if (Keyboard.current.enterKey.wasPressedThisFrame) _model.UseSelectedItem();
        }

        private void InitializeInventory()
        {
            _doc = FindObjectOfType<UIDocument>();
            if (_doc == null)
            {
                Debug.LogError("[InventoryUI] UIDocument not found on scene");
                return;
            }

            _model = InventoryModel.Instance;
            _model.OnSlotChanged += RefreshSlot;
            _model.OnSlotSelected += OnModelSlotSelected;

            CreateTooltip(_doc.rootVisualElement);
            BuildUI();
        }

        private void OnModelSlotSelected(int slotIndex)
        {
            // Сбросить рамку у старого слота
            if (_lastSelectedSlot >= 0 && _slotElements.ContainsKey(_lastSelectedSlot))
            {
                foreach (var cell in _slotElements[_lastSelectedSlot])
                {
                    cell.style.borderTopWidth = 2;
                    cell.style.borderBottomWidth = 2;
                    cell.style.borderLeftWidth = 2;
                    cell.style.borderRightWidth = 2;
                    cell.style.borderTopColor = _cellBorderColor;
                    cell.style.borderBottomColor = _cellBorderColor;
                    cell.style.borderLeftColor = _cellBorderColor;
                    cell.style.borderRightColor = _cellBorderColor;
                }
            }

            _lastSelectedSlot = slotIndex;

            // Поставить рамку новому слоту
            if (slotIndex >= 0 && _slotElements.ContainsKey(slotIndex))
            {
                foreach (var cell in _slotElements[slotIndex])
                {
                    cell.style.borderTopWidth = 3;
                    cell.style.borderBottomWidth = 3;
                    cell.style.borderLeftWidth = 3;
                    cell.style.borderRightWidth = 3;
                    cell.style.borderTopColor = _selectedBorderColor;
                    cell.style.borderBottomColor = _selectedBorderColor;
                    cell.style.borderLeftColor = _selectedBorderColor;
                    cell.style.borderRightColor = _selectedBorderColor;
                }
            }

            if (slotIndex >= 0)
            {
                var item = _model.GetSlot(slotIndex);
                if (item != null)
                {
                    _tooltipName.text = item.Name;
                    _tooltipDesc.text = item.Description ?? "";
                    _tooltipWrapper.style.display = DisplayStyle.Flex;
                    return;
                }
            }
            _tooltipWrapper.style.display = DisplayStyle.None;
        }

        private void CreateTooltip(VisualElement root)
        {
            _tooltipWrapper = new VisualElement();
            _tooltipWrapper.style.position = Position.Absolute;
            _tooltipWrapper.style.left = 0;
            _tooltipWrapper.style.right = 0;
            _tooltipWrapper.style.top = 48;
            _tooltipWrapper.style.height = 36;
            _tooltipWrapper.style.flexDirection = FlexDirection.Row;
            _tooltipWrapper.style.justifyContent = Justify.Center;
            _tooltipWrapper.style.display = DisplayStyle.None;

            _tooltipBg = new VisualElement();
            _tooltipBg.style.flexDirection = FlexDirection.Row;
            _tooltipBg.style.alignItems = Align.Center;
            _tooltipBg.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
            _tooltipBg.style.borderTopWidth = 2;
            _tooltipBg.style.borderBottomWidth = 2;
            _tooltipBg.style.borderLeftWidth = 2;
            _tooltipBg.style.borderRightWidth = 2;
            _tooltipBg.style.borderTopColor = _panelBorderColor;
            _tooltipBg.style.borderBottomColor = _panelBorderColor;
            _tooltipBg.style.borderLeftColor = _panelBorderColor;
            _tooltipBg.style.borderRightColor = _panelBorderColor;
            _tooltipBg.style.paddingLeft = 8;
            _tooltipBg.style.paddingRight = 8;
            _tooltipBg.style.paddingTop = 4;
            _tooltipBg.style.paddingBottom = 4;

            _tooltipName = new Label();
            _tooltipName.style.fontSize = 14;
            _tooltipName.style.unityFontStyleAndWeight = FontStyle.Bold;
            _tooltipName.style.color = _selectedBorderColor;
            _tooltipName.style.marginRight = 10;

            _tooltipDesc = new Label();
            _tooltipDesc.style.fontSize = 12;
            _tooltipDesc.style.color = Color.white;

            _tooltipBg.Add(_tooltipName);
            _tooltipBg.Add(_tooltipDesc);
            _tooltipWrapper.Add(_tooltipBg);
            root.Add(_tooltipWrapper);
        }

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;
            CreateFullInventoryPanel(root);
            CreateHotbar(root);
        }

        private void CreateHotbar(VisualElement root)
        {
            _hotbarContainer = new VisualElement();
            _hotbarContainer.name = "HotbarContainer";
            _hotbarContainer.style.position = Position.Absolute;
            _hotbarContainer.style.bottom = 10;
            _hotbarContainer.style.flexDirection = FlexDirection.Row;
            _hotbarContainer.style.alignItems = Align.Center;

            for (int i = 0; i < HOTBAR_COLS; i++)
            {
                var cell = CreateCell(i, $"Hotbar_{i}");
                _hotbarContainer.Add(cell);
            }

            _inventoryButton = CreateInventoryButton();
            _hotbarContainer.Add(_inventoryButton);

            root.Add(_hotbarContainer);

            root.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                int w = HOTBAR_COLS * CELL_SIZE + (HOTBAR_COLS - 1) * CELL_GAP + CELL_SIZE + CELL_GAP;
                _hotbarContainer.style.left = (root.resolvedStyle.width - w) / 2;
            });
        }

        private void CreateFullInventoryPanel(VisualElement root)
        {
            _fullInventoryPanel = new VisualElement();
            _fullInventoryPanel.name = "FullInventoryPanel";
            _fullInventoryPanel.style.position = Position.Absolute;
            _fullInventoryPanel.style.left = 0;
            _fullInventoryPanel.style.top = 0;
            _fullInventoryPanel.style.right = 0;
            _fullInventoryPanel.style.bottom = 0;
            _fullInventoryPanel.style.justifyContent = Justify.Center;
            _fullInventoryPanel.style.alignItems = Align.Center;
            _fullInventoryPanel.style.display = DisplayStyle.None;

            var panelBg = new VisualElement();
            panelBg.name = "PanelBackground";
            panelBg.style.backgroundColor = _panelBgColor;
            panelBg.style.borderTopWidth = 4;
            panelBg.style.borderBottomWidth = 4;
            panelBg.style.borderLeftWidth = 4;
            panelBg.style.borderRightWidth = 4;
            panelBg.style.borderTopColor = _panelBorderColor;
            panelBg.style.borderBottomColor = _panelBorderColor;
            panelBg.style.borderLeftColor = _panelBorderColor;
            panelBg.style.borderRightColor = _panelBorderColor;
            panelBg.style.paddingTop = 20;
            panelBg.style.paddingBottom = 20;
            panelBg.style.paddingLeft = 20;
            panelBg.style.paddingRight = 20;
            panelBg.style.flexDirection = FlexDirection.Column;
            panelBg.style.alignItems = Align.Center;

            var closeBtn = new Button();
            closeBtn.name = "CloseButton";
            closeBtn.style.position = Position.Absolute;
            closeBtn.style.top = 5;
            closeBtn.style.right = 5;
            closeBtn.style.width = 24;
            closeBtn.style.height = 24;
            closeBtn.style.backgroundColor = Color.clear;
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.paddingTop = 0;
            closeBtn.style.paddingBottom = 0;
            closeBtn.style.paddingLeft = 0;
            closeBtn.style.paddingRight = 0;

            var closeLabel = new Label("×");
            closeLabel.style.fontSize = 20;
            closeLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            closeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            closeLabel.style.alignSelf = Align.Center;
            closeLabel.style.justifyContent = Justify.Center;
            closeLabel.style.flexGrow = 1;
            closeLabel.pickingMode = PickingMode.Ignore;
            closeBtn.Add(closeLabel);

            closeBtn.clicked += ToggleInventory;
            panelBg.Add(closeBtn);

            var titleLabel = new Label("Inventory");
            titleLabel.style.fontSize = 18;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = Color.white;
            titleLabel.style.marginBottom = 15;
            titleLabel.style.alignSelf = Align.FlexStart;
            panelBg.Add(titleLabel);

            var inventoryGrid = CreateGrid(9, InventoryModel.TOTAL_SLOTS - 1, "Inv");
            panelBg.Add(inventoryGrid);

            var separator = new VisualElement();
            separator.style.height = 2;
            separator.style.backgroundColor = _panelBorderColor;
            separator.style.marginTop = 15;
            separator.style.marginBottom = 15;
            separator.style.width = INVENTORY_COLS * CELL_SIZE + (INVENTORY_COLS - 1) * CELL_GAP;
            panelBg.Add(separator);

            var hotbarInPanel = CreateGrid(0, 8, "PanelHotbar");
            panelBg.Add(hotbarInPanel);

            _fullInventoryPanel.Add(panelBg);

            root.Add(_fullInventoryPanel);
        }

        private VisualElement CreateGrid(int fromSlot, int toSlot, string prefix)
        {
            var grid = new VisualElement();
            grid.name = $"{prefix}_Grid";
            grid.style.flexDirection = FlexDirection.Column;
            grid.style.alignItems = Align.Center;

            int slotIndex = fromSlot;
            int cols = (toSlot - fromSlot + 1 > 9) ? INVENTORY_COLS : (toSlot - fromSlot + 1);
            int rows = (toSlot - fromSlot + 1 + cols - 1) / cols;

            for (int row = 0; row < rows; row++)
            {
                var rowContainer = new VisualElement();
                rowContainer.style.flexDirection = FlexDirection.Row;

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
            cell.style.width = CELL_SIZE;
            cell.style.height = CELL_SIZE;
            cell.style.justifyContent = Justify.Center;
            cell.style.alignItems = Align.Center;
            cell.style.marginRight = CELL_GAP;
            cell.style.marginBottom = CELL_GAP;
            cell.style.backgroundColor = _cellBgColor;
            cell.style.borderTopWidth = 2;
            cell.style.borderBottomWidth = 2;
            cell.style.borderLeftWidth = 2;
            cell.style.borderRightWidth = 2;
            cell.style.borderTopColor = _cellBorderColor;
            cell.style.borderBottomColor = _cellBorderColor;
            cell.style.borderLeftColor = _cellBorderColor;
            cell.style.borderRightColor = _cellBorderColor;

            // Иконка-кружок
            var icon = new VisualElement();
            icon.name = "Icon";
            icon.style.width = ICON_SIZE;
            icon.style.height = ICON_SIZE;
            icon.style.alignSelf = Align.Center;
            icon.style.justifyContent = Justify.Center;
            icon.style.display = DisplayStyle.None;
            icon.pickingMode = PickingMode.Ignore;
            cell.Add(icon);

            // Количество
            var qtyLabel = new Label();
            qtyLabel.name = "Quantity";
            qtyLabel.style.position = Position.Absolute;
            qtyLabel.style.right = 3;
            qtyLabel.style.bottom = 2;
            qtyLabel.style.fontSize = 12;
            qtyLabel.style.color = Color.white;
            qtyLabel.style.textShadow = new TextShadow
            {
                color = Color.black,
                offset = new Vector2(1, -1)
            };
            qtyLabel.pickingMode = PickingMode.Ignore;
            cell.Add(qtyLabel);

            // Hover
            cell.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (_dragFromSlot < 0)
                    cell.style.backgroundColor = _cellHighlightColor;
            });
            cell.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (_dragFromSlot < 0)
                    cell.style.backgroundColor = _cellBgColor;
            });

            // Выбор по клику
            cell.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                _model.SelectSlot(slotIndex);
                var item = _model.GetSlot(slotIndex);

                if (item == null) return;

                _dragFromSlot = slotIndex;
                _draggedItem = item;
                cell.style.backgroundColor = _cellBgColor;

                _floatingItem = new VisualElement();
                _floatingItem.style.position = Position.Absolute;
                _floatingItem.style.width = ICON_SIZE;
                _floatingItem.style.height = ICON_SIZE;
                _floatingItem.style.borderTopLeftRadius = ICON_SIZE / 2;
                _floatingItem.style.borderTopRightRadius = ICON_SIZE / 2;
                _floatingItem.style.borderBottomLeftRadius = ICON_SIZE / 2;
                _floatingItem.style.borderBottomRightRadius = ICON_SIZE / 2;
                if (item.Icon != null)
                {
                    _floatingItem.style.backgroundImage = new StyleBackground(item.Icon);
                    _floatingItem.style.backgroundColor = Color.clear;
                }
                else
                {
                    _floatingItem.style.backgroundColor = Color.gray;
                }
                _floatingItem.style.opacity = 0.8f;
                _floatingItem.pickingMode = PickingMode.Ignore;

                var root = _doc.rootVisualElement;
                root.Add(_floatingItem);
                UpdateFloatingPosition(evt.mousePosition);

                root.RegisterCallback<MouseMoveEvent>(OnDragMove);
                root.RegisterCallback<MouseUpEvent>(OnDragDrop);
                evt.StopPropagation();
            });

            if (!_slotElements.ContainsKey(slotIndex))
                _slotElements[slotIndex] = new List<VisualElement>();
            _slotElements[slotIndex].Add(cell);

            RefreshSlot(slotIndex);
            return cell;
        }

        private void OnDragMove(MouseMoveEvent evt)
        {
            if (_floatingItem != null)
                UpdateFloatingPosition(evt.mousePosition);
        }

        private void OnDragDrop(MouseUpEvent evt)
        {
            if (_dragFromSlot < 0 || _floatingItem == null)
                return;

            var root = _doc.rootVisualElement;
            root.UnregisterCallback<MouseMoveEvent>(OnDragMove);
            root.UnregisterCallback<MouseUpEvent>(OnDragDrop);

            var target = FindSlotUnderMouse(evt.mousePosition);
            if (target >= 0 && target != _dragFromSlot)
            {
                if (_model.CanStack(_draggedItem, _model.GetSlot(target)))
                    _model.TryStackSlots(_dragFromSlot, target);
                else
                    _model.SwapSlots(_dragFromSlot, target);
            }

            root.Remove(_floatingItem);
            _floatingItem = null;
            _dragFromSlot = -1;
            _draggedItem = null;
        }

        private int FindSlotUnderMouse(Vector2 mousePos)
        {
            foreach (var kvp in _slotElements)
            {
                foreach (var cell in kvp.Value)
                {
                    if (cell.worldBound.Contains(mousePos))
                        return kvp.Key;
                }
            }
            return -1;
        }

        private void UpdateFloatingPosition(Vector2 mousePos)
        {
            _floatingItem.style.left = mousePos.x - ICON_SIZE / 2;
            _floatingItem.style.top = mousePos.y - ICON_SIZE / 2;
        }

        private void RefreshSlot(int slotIndex)
        {
            if (!_slotElements.ContainsKey(slotIndex)) return;
            var item = _model.GetSlot(slotIndex);

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
                    qty.text = item.Quantity > 1 ? item.Quantity.ToString() : "";
                }
                else
                {
                    icon.style.display = DisplayStyle.None;
                    qty.text = "";
                }
            }
        }

        private Button CreateInventoryButton()
        {
            var btn = new Button();
            btn.name = "InventoryButton";
            btn.style.width = CELL_SIZE;
            btn.style.height = CELL_SIZE;
            btn.style.marginBottom = CELL_GAP;
            btn.style.backgroundColor = _inventoryButtonColor;
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;
            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopColor = _cellBorderColor;
            btn.style.borderBottomColor = _cellBorderColor;
            btn.style.borderLeftColor = _cellBorderColor;
            btn.style.borderRightColor = _cellBorderColor;
            btn.text = "";
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;

            btn.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (btn.enabledSelf)
                    btn.style.backgroundColor = _inventoryButtonHoverColor;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (btn.enabledSelf)
                    btn.style.backgroundColor = _inventoryButtonColor;
            });

            btn.clicked += ToggleInventory;
            return btn;
        }

        private void ToggleInventory()
        {
            _isInventoryOpen = !_isInventoryOpen;
            _fullInventoryPanel.style.display = _isInventoryOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _hotbarContainer.style.display = _isInventoryOpen ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public InventoryModel GetModel() => _model;
    }
}
