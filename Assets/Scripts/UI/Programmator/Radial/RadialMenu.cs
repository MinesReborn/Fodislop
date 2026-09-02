#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Localization;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.Programmator
{
    public class RadialMenu
    {
        private readonly ILocalizationService _loc;
        private readonly IProgrammatorTextureCatalog _textures;
        private readonly VisualElement _root;
        private readonly VisualElement _innerContainer;
        private readonly VisualElement _outerContainer;
        private readonly VisualElement _outerRingBg;
        private readonly VisualElement _backButton;

        private int[] _innerIds = Array.Empty<int>();
        private int _innerCount;
        private Color[]? _innerItemColors;

        private int[] _outerIds = Array.Empty<int>();
        private int _outerCount;

        private readonly float _innerRadius = 55f;
        private readonly float _outerRadius = 100f;
        private readonly float _itemSize = 36f;
        private readonly float _center = 130f;

        private int _hoveredInnerIndex = -1;
        private int _hoveredOuterIndex = -1;
        private Vector2 _centerPosition;

        public event Action<int>? OnCategoryClicked; // inner ring item clicked
        public event Action<int>? OnItemClicked;      // outer ring item clicked (actual operator)
        public event Action? OnBackClicked;

        public VisualElement Root => _root;
        public bool IsShown => _root.parent != null;

        private static readonly Color DefaultBorder = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color HoverBorder = new Color(1f, 0.84f, 0f, 1f);

        public RadialMenu(ILocalizationService loc, IProgrammatorTextureCatalog textures)
        {
            _loc = loc ?? throw new ArgumentNullException(nameof(loc));
            _textures = textures ?? throw new ArgumentNullException(nameof(textures));

            // Static skeleton (rings, containers, back button) lives in
            // RadialMenu.uxml; items of both rings are positioned dynamically
            // in code, so only the skeleton is cloned here.
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.RadialMenuUxml) ??
                throw new InvalidOperationException(
                    "[RadialMenu] Resources/UI/RadialMenu.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            tree.AddToClassList("prog-radial-root");
            tree.pickingMode = PickingMode.Ignore;
            _root = tree;

            // Статические ключи UXML резолвятся сразу при сборке (контракт
            // един для всех экранов; у радиального меню их почти нет).
            UILocalizer.Apply(tree, _loc);

            _outerRingBg = _root.Q<VisualElement>("OuterRingBg") ??
                throw new InvalidOperationException("[RadialMenu] OuterRingBg is missing from RadialMenu.uxml.");
            _outerContainer = _root.Q<VisualElement>("OuterContainer") ??
                throw new InvalidOperationException("[RadialMenu] OuterContainer is missing from RadialMenu.uxml.");
            _innerContainer = _root.Q<VisualElement>("InnerContainer") ??
                throw new InvalidOperationException("[RadialMenu] InnerContainer is missing from RadialMenu.uxml.");
            _backButton = _root.Q<VisualElement>("RadialBackButton") ??
                throw new InvalidOperationException("[RadialMenu] RadialBackButton is missing from RadialMenu.uxml.");
            _backButton.RegisterCallback<PointerDownEvent>(_ => OnBackClicked?.Invoke());
        }

        public void SetInnerItems(int[] ids, Color[]? colors = null)
        {
            _innerContainer.Clear();
            _innerIds = ids ?? Array.Empty<int>();
            _innerCount = _innerIds.Length;
            _innerItemColors = colors;

            for (int i = 0; i < _innerCount; i++)
            {
                float angle = ((float)i / _innerCount * Mathf.PI * 2f) - (Mathf.PI / 2f);
                float x = _center + (_innerRadius * Mathf.Cos(angle)) - (_itemSize / 2f);
                float y = _center + (_innerRadius * Mathf.Sin(angle)) - (_itemSize / 2f);

                int itemIdx = i;
                var item = new VisualElement();
                item.style.position = Position.Absolute;
                item.style.left = x;
                item.style.top = y;
                item.style.width = _itemSize;
                item.style.height = _itemSize;
                item.style.borderTopLeftRadius = 18;
                item.style.borderTopRightRadius = 18;
                item.style.borderBottomLeftRadius = 18;
                item.style.borderBottomRightRadius = 18;
                item.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
                item.style.borderTopWidth = 2;
                item.style.borderBottomWidth = 2;
                item.style.borderLeftWidth = 2;
                item.style.borderRightWidth = 2;

                item.AddToClassList("prog-radial-item");

                Color borderColor = (colors != null && i < colors.Length) ? colors[i] : DefaultBorder;
                item.style.borderTopColor = borderColor;
                item.style.borderBottomColor = borderColor;
                item.style.borderLeftColor = borderColor;
                item.style.borderRightColor = borderColor;

                item.pickingMode = PickingMode.Position;
                item.name = $"radial_inner_{i}";

                // Categories use negative IDs — show name label
                string catName = ProgrammatorData.CATEGORY_NAMES.TryGetValue(_innerIds[i], out var cn) ? _loc.Get(cn) : _innerIds[i].ToString();
                var label = new Label(catName);
                label.AddToClassList("prog-radial-item-label");
                label.pickingMode = PickingMode.Ignore;
                item.Add(label);

                item.RegisterCallback<PointerEnterEvent>(_ => OnInnerPointerEnter(itemIdx));
                item.RegisterCallback<PointerLeaveEvent>(_ => OnInnerPointerLeave(itemIdx));
                item.RegisterCallback<PointerDownEvent>(_ => OnCategoryClicked?.Invoke(_innerIds[itemIdx]));

                _innerContainer.Add(item);
            }
        }

        public void SetOuterItems(int[] ids, Color[]? colors = null)
        {
            _outerContainer.Clear();
            _outerIds = ids ?? Array.Empty<int>();
            _outerCount = _outerIds.Length;

            for (int i = 0; i < _outerCount; i++)
            {
                float angle = ((float)i / _outerCount * Mathf.PI * 2f) - (Mathf.PI / 2f);
                float x = _center + (_outerRadius * Mathf.Cos(angle)) - (_itemSize / 2f);
                float y = _center + (_outerRadius * Mathf.Sin(angle)) - (_itemSize / 2f);

                int itemIdx = i;
                var item = new VisualElement();
                item.style.position = Position.Absolute;
                item.style.left = x;
                item.style.top = y;
                item.style.width = _itemSize;
                item.style.height = _itemSize;
                item.style.borderTopLeftRadius = 18;
                item.style.borderTopRightRadius = 18;
                item.style.borderBottomLeftRadius = 18;
                item.style.borderBottomRightRadius = 18;
                item.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f);
                item.style.borderTopWidth = 2;
                item.style.borderBottomWidth = 2;
                item.style.borderLeftWidth = 2;
                item.style.borderRightWidth = 2;

                item.AddToClassList("prog-radial-item");

                item.pickingMode = PickingMode.Position;
                item.name = $"radial_outer_{i}";

                int id = _outerIds[i];
                var action = (ProgAction)id;
                var tex = _textures.GetTexture(action);
                if (tex != null)
                {
                    item.style.backgroundImage = new StyleBackground(tex);
                    item.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                }
                else
                {
                    string labelText = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? _loc.Get(n) : id.ToString();
                    var label = new Label(labelText);
                    label.AddToClassList("prog-radial-item-label");
                    label.pickingMode = PickingMode.Ignore;
                    item.Add(label);
                }

                item.RegisterCallback<PointerEnterEvent>(_ => OnOuterPointerEnter(itemIdx));
                item.RegisterCallback<PointerLeaveEvent>(_ => OnOuterPointerLeave(itemIdx));
                item.RegisterCallback<PointerDownEvent>(_ => OnItemClicked?.Invoke(_outerIds[itemIdx]));

                _outerContainer.Add(item);
            }

            _outerRingBg.style.display = DisplayStyle.Flex;
            _backButton.style.display = DisplayStyle.Flex;
        }

        public void ClearOuterItems()
        {
            _outerContainer.Clear();
            _outerCount = 0;
            _outerIds = Array.Empty<int>();
            _hoveredOuterIndex = -1;
            _outerRingBg.style.display = DisplayStyle.None;
            _backButton.style.display = DisplayStyle.None;
        }

        private void OnInnerPointerEnter(int index)
        {
            _hoveredInnerIndex = index;
            for (int i = 0; i < _innerCount; i++)
            {
                var item = _innerContainer[i] as VisualElement;
                if (item == null)
                {
                    continue;
                }

                Color bc = (i == index) ? HoverBorder
                    : (_innerItemColors != null && i < _innerItemColors.Length) ? _innerItemColors[i] : DefaultBorder;
                item.style.borderTopColor = bc;
                item.style.borderBottomColor = bc;
                item.style.borderLeftColor = bc;
                item.style.borderRightColor = bc;
            }
        }

        private void OnInnerPointerLeave(int index)
        {
            if (_hoveredInnerIndex == index)
            {
                _hoveredInnerIndex = -1;
            }

            var item = _innerContainer[index] as VisualElement;
            if (item != null)
            {
                Color bc = (_innerItemColors != null && index < _innerItemColors.Length) ? _innerItemColors[index] : DefaultBorder;
                item.style.borderTopColor = bc;
                item.style.borderBottomColor = bc;
                item.style.borderLeftColor = bc;
                item.style.borderRightColor = bc;
            }
        }

        private void OnOuterPointerEnter(int index)
        {
            _hoveredOuterIndex = index;
            for (int i = 0; i < _outerCount; i++)
            {
                var item = _outerContainer[i] as VisualElement;
                if (item == null)
                {
                    continue;
                }

                Color bc = (i == index) ? HoverBorder : DefaultBorder;
                item.style.borderTopColor = bc;
                item.style.borderBottomColor = bc;
                item.style.borderLeftColor = bc;
                item.style.borderRightColor = bc;
            }
        }

        private void OnOuterPointerLeave(int index)
        {
            if (_hoveredOuterIndex == index)
            {
                _hoveredOuterIndex = -1;
            }

            var item = _outerContainer[index] as VisualElement;
            if (item != null)
            {
                item.style.borderTopColor = DefaultBorder;
                item.style.borderBottomColor = DefaultBorder;
                item.style.borderLeftColor = DefaultBorder;
                item.style.borderRightColor = DefaultBorder;
            }
        }

        public void ShowAt(VisualElement parent, Vector2 screenPos)
        {
            _hoveredInnerIndex = -1;
            _hoveredOuterIndex = -1;
            _centerPosition = screenPos;
            _root.style.left = screenPos.x - _center;
            _root.style.top = screenPos.y - _center;
            parent.Add(_root);
        }

        public Vector2 GetCenter()
        {
            return _centerPosition;
        }

        public void Hide()
        {
            _hoveredInnerIndex = -1;
            _hoveredOuterIndex = -1;
            ClearOuterItems();
            if (_root.parent != null)
            {
                _root.RemoveFromHierarchy();
            }
        }
    }
}
