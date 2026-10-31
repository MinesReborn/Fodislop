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

namespace Kern.UI.Inventory
{
    // Раньше PauseMenu в том же кадре: порядок Update между ними не задан,
    // и Escape с выбранным предметом успевал открыть меню паузы. Явный
    // порядок ставит инвентарь раньше меню — ConsumeEscape в его
    // ESC-ветке успевает отработать до проверки в PauseMenu.
    [DefaultExecutionOrder(-50)]
    public class InventoryView : MonoBehaviour, ILocalizableUI
    {

        private const int ROWCOUNT = 4;

        // Компактный режим — одна колонка из четырёх предметов (FixedRowCount
        // старого клиента): полоса-треугольник раскрывает полный список и
        // прячет обратно все предметы после четвёртого.

        // Цифровые клавиши 1–9 выбирают предмет по позиции в OrderedTypes.
        private const int NUM_ORDERED_SELECT_KEYS = 9;

        [Inject] private UIDocument _doc = null!;
        [Inject] private IInventoryModel _model = null!;
        [Inject] private IItemCatalog _catalog = null!;
        [Inject] private Kern.Core.Interfaces.IInputBlocker _inputBlocker = null!;
        [Inject] private ILocalizationService _loc = null!;
        [Inject] private UIInputManager _uiInput = null!;
        private InventorySlotCollection _slots = null!;
        private TemplateContainer? _inventoryRoot;
        private Button? _inventoryButton;
        private VisualElement? _hotbarContainer;
        private VisualElement? _fullScroll;
        private VisualElement? _hotbarSlots;
        private VisualElement? _fullSlots;
        private Label? _toggleGlyph;
        private bool _isInventoryOpen;
        private InventoryTooltipController? _tooltipController;
        private bool _initialized;

        public void SetVisible(bool visible)
        {
            if (_inventoryRoot != null)
            {
                _inventoryRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

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

            if (!Application.isFocused || _uiInput.IsKeyCaptureInProgress || Keyboard.current == null)
            {
                return;
            }

            // TAB отдан глобальному чату (GlobalChatUI); инвентарь открывается на I.
            if (Keyboard.current.iKey.wasPressedThisFrame && !_uiInput.IsChatFocused)
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

            if (Keyboard.current.escapeKey.wasPressedThisFrame && !_uiInput.IsChatFocused && _model!.HasSelectedItem)
            {
                // Escape снимает предмет и не должен заодно открывать меню
                // паузы — одно нажатие, один владелец (см. UIInputManager).
                // В открытом чате Escape принадлежит чату: инвентарь молчит.
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

            _slots = new InventorySlotCollection(_model, _catalog);
            BuildUI();
            _tooltipController = new InventoryTooltipController(_doc.rootVisualElement, _catalog);

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
            _slots.ApplySelection();

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
                ResourcePaths.InventoryUxml);
            if (uxml != null)
            {
                TemplateContainer tree = uxml.Instantiate();
                _inventoryRoot = tree;
                tree.Q<VisualElement>("InventoryRoot")?.RegisterCallback<PointerDownEvent>(OnBackdropPointerDown);
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
                _hotbarContainer = tree.Q<VisualElement>("HotbarContainer");
                _fullScroll = tree.Q<ScrollView>("InventoryScroll");
                RebuildSlots();

            }
            else
            {
                throw new InvalidOperationException("[InventoryView] Failed to load UI/Inventory/Templates/Inventory.uxml");
            }
        }

        private void RebuildSlots()
        {
            _slots.Clear();
            FillSlots(_hotbarSlots, ROWCOUNT);
            FillSlots(_fullSlots, int.MaxValue);

            ApplyToggleButtonVisibility();
            RefreshTooltipFromSelection();

            _slots.ApplySelection();
            ApplyInventoryMode();
        }

        // Название и описание предмета приходят с сервера MinesServer пакетом
        // SelectItemPacket уже ПОСЛЕ локального выбора: без этой перерисовки
        // карточка оставалась с пустым описанием до повторного клика.
        private void RefreshTooltipFromSelection()
        {
            if (_model!.SelectedItem is not { } type)
            {
                return;
            }

            ItemData? item = _model.GetItem(type);
            if (item != null)
            {
                _tooltipController?.ShowItemInfo(item, _catalog.GetIcon(type));
            }
        }

        // Полоса раскрытия появляется только когда предметов БОЛЬШЕ четырёх:
        // короткий список из четырёх и меньше помещается целиком, разворачивать
        // нечего — кнопка не показывается вовсе. При обратном уменьшении панель
        // сворачивается в компактный режим: без полосы полный режим недостижим
        // мышью, да и содержимое при ≤4 предметах в обоих режимах одинаково.
        private void ApplyToggleButtonVisibility()
        {
            if (_inventoryButton == null)
            {
                return;
            }

            bool showToggle = _model!.OrderedTypes.Count > ROWCOUNT;
            _inventoryButton.style.display = showToggle
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            if (!showToggle)
            {
                _isInventoryOpen = false;
            }
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

                column!.Add(_slots.CreateCell(types[i]));
            }
        }

        // Касание затемнения вокруг раскрытого списка закрывает его.
        private void OnBackdropPointerDown(PointerDownEvent evt)
        {
            if (_isInventoryOpen && evt.target is VisualElement { name: "InventoryRoot" })
            {
                ToggleFullInventory();
                evt.StopPropagation();
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

            if (_fullScroll != null)
            {
                _fullScroll.style.display =
                    _isInventoryOpen ? DisplayStyle.Flex : DisplayStyle.None;
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

            // Раскрытый список на сенсорном экране — окно по центру (Touch.uss):
            // полосой вдоль края полсотни предметов закрыли бы весь HUD.
            _hotbarContainer?.EnableInClassList("inv-hotbar--popup", _isInventoryOpen);

            // На сенсорном экране раскрытый список модален: корень инвентаря
            // ловит касания мимо окна (затемнение в Touch.uss) и закрывает его,
            // а HUD под ним не нажимается сквозь окно. Мышью — как раньше.
            bool modal = _isInventoryOpen && UIScreen.Current.IsTouch;
            if (_inventoryRoot != null)
            {
                VisualElement? inventoryRoot = _inventoryRoot.Q<VisualElement>("InventoryRoot");
                if (inventoryRoot != null)
                {
                    inventoryRoot.pickingMode = modal ? PickingMode.Position : PickingMode.Ignore;
                    inventoryRoot.EnableInClassList("inv-root--modal", modal);
                }

                if (_isInventoryOpen)
                {
                    _inventoryRoot.BringToFront();
                }
            }
        }

        // Клавиша делает ровно то же, что полоса: отдельного окна больше нет.
        private void ToggleInventory() => ToggleFullInventory();
    }
}
