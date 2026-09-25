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

namespace Kern.UI.Inventory
{
    public class InventoryView : MonoBehaviour, ILocalizableUI
    {

        private const int ROWCOUNT = 4;

        // Компактный режим — восемь предметов (два столбца по четыре), как
        // согласовано: старый клиент держал в верхней панели короткий список,
        // полный раскрывался тем же треугольником.
        private const int COMPACT_SIZE = 8;

        // Цифровые клавиши 1–9 выбирают предмет по позиции в OrderedTypes.
        private const int NUM_ORDERED_SELECT_KEYS = 9;

        [Inject]
        private UIDocument _doc = null!;
        [Inject]
        private IInventoryModel _model = null!;
        [Inject]
        private IItemCatalog _catalog = null!;
        [Inject]
        private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject]
        private ILocalizationService _loc = null!;
        [Inject]
        private UIInputManager _uiInput = null!;

        private readonly Dictionary<ItemType, List<VisualElement>> _cellElements = new();
        private Button? _inventoryButton;
        private VisualElement? _hotbarSlots;
        private VisualElement? _fullSlots;
        private Label? _toggleGlyph;
        private bool _isInventoryOpen;
        private InventoryTooltipController? _tooltipController;
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
                _model.OnItemsChanged -= RebuildSlots;
                _model.OnSelectedChanged -= OnModelSelectedChanged;
            }

            _tooltipController?.HideTooltip();
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
                (Keyboard.current.iKey.wasPressedThisFrame && !_uiInput.IsChatFocused))
            {
                ToggleInventory();
            }

            if (_inputBlocker != null && _inputBlocker.IsInputBlocked)
            {
                return;
            }

            if (TrySelectFromDigitKey())
            {
                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                // Enter применяет выбранный предмет и имеет приоритет над
                // открытием чата: пустой Enter (ничего не выбрано) ведёт себя
                // обычным образом и позволяет чату открыться (GlobalChatUI).
                _model!.UseSelectedItem();
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame && _model!.HasSelectedItem)
            {
                // Escape снимает предмет и не должен заодно открывать меню
                // паузы — одно нажатие, один владелец (см. UIInputManager).
                _model.Deselect();
                _uiInput.ConsumeEscape();
            }
        }

        private bool TrySelectFromDigitKey()
        {
            IReadOnlyList<ItemType> types = _model!.OrderedTypes;
            for (int i = 0; i < NUM_ORDERED_SELECT_KEYS && i < types.Count; i++)
            {
                if (WasOrderedSelectKeyPressed(i))
                {
                    _model.Select(types[i]);
                    return true;
                }
            }

            return false;
        }

        private static bool WasOrderedSelectKeyPressed(int index)
        {
            if (Keyboard.current == null)
            {
                return false;
            }

            return index switch
            {
                0 => Keyboard.current.digit1Key.wasPressedThisFrame,
                1 => Keyboard.current.digit2Key.wasPressedThisFrame,
                2 => Keyboard.current.digit3Key.wasPressedThisFrame,
                3 => Keyboard.current.digit4Key.wasPressedThisFrame,
                4 => Keyboard.current.digit5Key.wasPressedThisFrame,
                5 => Keyboard.current.digit6Key.wasPressedThisFrame,
                6 => Keyboard.current.digit7Key.wasPressedThisFrame,
                7 => Keyboard.current.digit8Key.wasPressedThisFrame,
                8 => Keyboard.current.digit9Key.wasPressedThisFrame,
                _ => false,
            };
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

            IItemCatalog catalog = _catalog ?? throw new InvalidOperationException(
                "[InventoryView] IItemCatalog injection is required before initialization.");
            _catalog = catalog;

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

            BuildUI();
            _tooltipController = new InventoryTooltipController(_doc.rootVisualElement, _loc);

            _model.OnItemsChanged += RebuildSlots;
            _model.OnSelectedChanged += OnModelSelectedChanged;
            _initialized = true;

            _loc.RegisterLocalizable(this);
        }

        public void ApplyLocalizedText()
        {
            UILocalizer.AssertLocalizationServiceAvailable(_loc, nameof(InventoryView));
            UILocalizer.Apply(_doc.rootVisualElement, _loc);
            UILocalizer.AssertLocalized(_doc.rootVisualElement, _loc);
        }

        private void OnModelSelectedChanged(ItemType? selectedType)
        {
            ApplySelection();

            if (selectedType is { } type)
            {
                var item = _model!.GetItem(type);
                if (item != null)
                {
                    _tooltipController?.ShowItemInfo(item, _catalog.GetIcon(type));
                    return;
                }
            }

            _tooltipController?.HideTooltip();
        }

        private void BuildUI()
        {
            var root = _doc.rootVisualElement;

            var uxml = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.InventoryUxml);
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

                _hotbarSlots = tree.Q<VisualElement>("HotbarSlots");

                _inventoryButton = tree.Q<Button>("InventoryToggleBtn");
                if (_inventoryButton != null)
                {
                    _inventoryButton.clicked += ToggleFullInventory;
                    if (_loc != null)
                    {
                        _inventoryButton.tooltip = $"{_loc.Get("inventory.open")} — {_loc.Get("inventory.hotbar")}";

                        // Кнопка — узкая полоса с треугольником, как в старом
                        // клиенте: название туда не влезает, внутри остаётся
                        // только стрелка, разворачивающаяся при сворачивании.
                        _toggleGlyph = _inventoryButton.Q<Label>();
                    }

                    ApplyInventoryMode();
                }

                _fullSlots = tree.Q<VisualElement>("InventoryGrid");
                RebuildSlots();

            }
            else
            {
                throw new InvalidOperationException("[InventoryView] Failed to load UI/Gameplay/Inventory.uxml");
            }
        }

        private VisualElement CreateCell(ItemType type)
        {
            var cell = new VisualElement();
            cell.name = "Cell_" + type;
            cell.userData = type;
            cell.AddToClassList("inv-cell");
            // InventoryRoot стоит в picking-mode="Ignore"; ячейка обязана явно
            // вернуть Position, иначе Ignore наследуется на поддерево и слот
            // не получает мышь вообще.
            cell.pickingMode = PickingMode.Position;

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

            // ЛКМ выбирает предмет (контекстное меню из новой модели убрано).
            cell.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _model!.Select(type);
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

        private void RefreshCell(VisualElement cell, ItemType type)
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

        private void RebuildSlots()
        {
            _cellElements.Clear();
            FillSlots(_hotbarSlots, COMPACT_SIZE);
            FillSlots(_fullSlots, int.MaxValue);

            if (_inventoryButton != null)
            {
                _inventoryButton.style.display = DisplayStyle.Flex;
            }

            ApplySelection();
            ApplyInventoryMode();
        }

        private void FillSlots(VisualElement? container, int maxCount)
        {
            if (container == null)
            {
                return;
            }

            container.Clear();

            IReadOnlyList<ItemType> types = _model!.OrderedTypes;
            int count = Math.Min(maxCount, types.Count);
            if (count <= 0)
            {
                return;
            }

            // Четыре строки, столбцы прирастают влево — как FixedRowCount = 4
            // со StartAxis = Vertical в старом клиенте. Создаются только
            // ячейки реально держимых предметов: номер слота больше не имеет
            // смысла, у каждого типа одна ячейка на панель.
            VisualElement? column = null;
            for (int i = 0; i < count; i++)
            {
                if (i % ROWCOUNT == 0)
                {
                    column = new VisualElement();
                    column.AddToClassList("inv-slot-column");
                    container.Add(column);
                }

                column!.Add(CreateCell(types[i]));
            }
        }

        private void ApplySelection()
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

        private void ToggleFullInventory()
        {
            _isInventoryOpen = !_isInventoryOpen;
            ApplyInventoryMode();
        }

        private void ApplyInventoryMode()
        {
            if (_hotbarSlots != null)
            {
                _hotbarSlots.style.display =
                    _isInventoryOpen ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_fullSlots != null)
            {
                _fullSlots.style.display =
                    _isInventoryOpen ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_toggleGlyph != null)
            {
                _toggleGlyph.text = _isInventoryOpen ? "\u25B6" : "\u25C0";
            }
        }

        // Клавиша делает ровно то же, что полоса: отдельного окна больше нет.
        private void ToggleInventory() => ToggleFullInventory();
    }
}
