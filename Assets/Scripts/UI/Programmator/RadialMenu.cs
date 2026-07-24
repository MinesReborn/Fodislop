using System;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Programmator
{
    public class RadialMenu
    {
        private readonly VisualElement _root;
        private readonly VisualElement _innerContainer;
        private readonly VisualElement _outerContainer;
        private readonly VisualElement _outerRingBg;
        private readonly VisualElement _backButton;

        private int[] _innerIds;
        private int _innerCount;
        private Color[] _innerItemColors;

        private int[] _outerIds;
        private int _outerCount;
        private Color[] _outerItemColors;

        private readonly float _innerRadius = 55f;
        private readonly float _outerRadius = 100f;
        private readonly float _itemSize = 36f;
        private readonly float _center = 130f;

        private int _hoveredInnerIndex = -1;
        private int _hoveredOuterIndex = -1;
        private Vector2 _centerPosition;

        public event Action<int> OnCategoryClicked; // inner ring item clicked
        public event Action<int> OnItemClicked;      // outer ring item clicked (actual operator)
        public event Action OnBackClicked;

        public VisualElement Root => _root;
        public bool IsShown => _root.parent != null;

        private static readonly Color DefaultBorder = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color HoverBorder = new Color(1f, 0.84f, 0f, 1f);

        public RadialMenu()
        {
            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.width = 260;
            _root.style.height = 260;
            _root.pickingMode = PickingMode.Ignore;

            // Outer ring background — hidden until SetOuterItems()
            _outerRingBg = AddRing(_outerRadius, _itemSize, new Color(0.08f, 0.08f, 0.08f, 0.45f));
            _outerRingBg.style.display = DisplayStyle.None;

            // Outer container for outer ring items
            _outerContainer = new VisualElement();
            _outerContainer.style.position = Position.Absolute;
            _outerContainer.style.left = 0;
            _outerContainer.style.top = 0;
            _outerContainer.style.right = 0;
            _outerContainer.style.bottom = 0;
            _outerContainer.pickingMode = PickingMode.Ignore;
            _root.Add(_outerContainer);

            // Inner ring background (rendered on top of outer items)
            AddRing(_innerRadius, _itemSize, new Color(0.12f, 0.12f, 0.12f, 0.5f));

            // Inner container for inner ring items
            _innerContainer = new VisualElement();
            _innerContainer.style.position = Position.Absolute;
            _innerContainer.style.left = 0;
            _innerContainer.style.top = 0;
            _innerContainer.style.right = 0;
            _innerContainer.style.bottom = 0;
            _innerContainer.pickingMode = PickingMode.Ignore;
            _root.Add(_innerContainer);

            // Back button — centered, hidden by default
            _backButton = new VisualElement();
            _backButton.style.position = Position.Absolute;
            float bbPos = _center - (_itemSize / 2f);
            _backButton.style.left = bbPos;
            _backButton.style.top = bbPos;
            _backButton.style.width = _itemSize;
            _backButton.style.height = _itemSize;
            _backButton.style.borderTopLeftRadius = 18;
            _backButton.style.borderTopRightRadius = 18;
            _backButton.style.borderBottomLeftRadius = 18;
            _backButton.style.borderBottomRightRadius = 18;
            _backButton.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            _backButton.style.borderTopWidth = 2;
            _backButton.style.borderBottomWidth = 2;
            _backButton.style.borderLeftWidth = 2;
            _backButton.style.borderRightWidth = 2;
            _backButton.style.borderTopColor = DefaultBorder;
            _backButton.style.borderBottomColor = DefaultBorder;
            _backButton.style.borderLeftColor = DefaultBorder;
            _backButton.style.borderRightColor = DefaultBorder;
            _backButton.pickingMode = PickingMode.Position;
            _backButton.style.display = DisplayStyle.None;

            var backLabel = new Label("\u2190");
            backLabel.style.color = Color.white;
            backLabel.style.fontSize = 18;
            backLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            backLabel.pickingMode = PickingMode.Ignore;
            _backButton.Add(backLabel);

            _backButton.RegisterCallback<PointerDownEvent>(_ => OnBackClicked?.Invoke());
            _backButton.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _backButton.style.borderTopColor = HoverBorder;
                _backButton.style.borderBottomColor = HoverBorder;
                _backButton.style.borderLeftColor = HoverBorder;
                _backButton.style.borderRightColor = HoverBorder;
            });
            _backButton.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _backButton.style.borderTopColor = DefaultBorder;
                _backButton.style.borderBottomColor = DefaultBorder;
                _backButton.style.borderLeftColor = DefaultBorder;
                _backButton.style.borderRightColor = DefaultBorder;
            });

            _root.Add(_backButton);
        }

        /// <summary>
        /// Adds a donut-shaped background ring at the given radius with given thickness.
        /// Uses border-radius/border-width trick to render a ring.
        /// </summary>
        private VisualElement AddRing(float ringRadius, float thickness, Color color)
        {
            float ringSize = (ringRadius + (thickness / 2f)) * 2f;
            var ring = new VisualElement();
            ring.style.position = Position.Absolute;
            ring.style.left = _center - (ringSize / 2f);
            ring.style.top = _center - (ringSize / 2f);
            ring.style.width = ringSize;
            ring.style.height = ringSize;
            ring.style.borderTopLeftRadius = ringSize / 2f;
            ring.style.borderTopRightRadius = ringSize / 2f;
            ring.style.borderBottomLeftRadius = ringSize / 2f;
            ring.style.borderBottomRightRadius = ringSize / 2f;
            ring.style.borderTopWidth = thickness;
            ring.style.borderBottomWidth = thickness;
            ring.style.borderLeftWidth = thickness;
            ring.style.borderRightWidth = thickness;
            ring.style.borderTopColor = color;
            ring.style.borderBottomColor = color;
            ring.style.borderLeftColor = color;
            ring.style.borderRightColor = color;
            ring.pickingMode = PickingMode.Ignore;
            ring.name = $"radial_ring_{ringRadius}";
            _root.Add(ring);
            return ring;
        }

        public void SetInnerItems(int[] ids, Color[] colors = null)
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

                Color borderColor = (colors != null && i < colors.Length) ? colors[i] : DefaultBorder;
                item.style.borderTopColor = borderColor;
                item.style.borderBottomColor = borderColor;
                item.style.borderLeftColor = borderColor;
                item.style.borderRightColor = borderColor;

                item.pickingMode = PickingMode.Position;
                item.name = $"radial_inner_{i}";

                // Categories use negative IDs — show name label
                string catName = ProgrammatorData.CATEGORY_NAMES.TryGetValue(_innerIds[i], out var cn) ? cn : _innerIds[i].ToString();
                var label = new Label(catName);
                label.style.color = Color.white;
                label.style.fontSize = 8;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.whiteSpace = WhiteSpace.Normal;
                label.pickingMode = PickingMode.Ignore;
                item.Add(label);

                item.RegisterCallback<PointerEnterEvent>(_ => OnInnerPointerEnter(itemIdx));
                item.RegisterCallback<PointerLeaveEvent>(_ => OnInnerPointerLeave(itemIdx));
                item.RegisterCallback<PointerDownEvent>(_ => OnCategoryClicked?.Invoke(_innerIds[itemIdx]));

                _innerContainer.Add(item);
            }
        }

        public void SetOuterItems(int[] ids, Color[] colors = null)
        {
            _outerContainer.Clear();
            _outerIds = ids ?? Array.Empty<int>();
            _outerCount = _outerIds.Length;
            _outerItemColors = colors;

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

                item.pickingMode = PickingMode.Position;
                item.name = $"radial_outer_{i}";

                int id = _outerIds[i];
                var action = (ProgAction)id;
                var tex = ProgrammatorTextureRegistry.GetTexture(action);
                if (tex != null)
                {
                    item.style.backgroundImage = new StyleBackground(tex);
                    item.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                }
                else
                {
                    string labelText = ProgrammatorData.OPERATOR_NAMES.TryGetValue(action, out var n) ? n : id.ToString();
                    var label = new Label(labelText);
                    label.style.color = Color.white;
                    label.style.fontSize = 8;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.style.whiteSpace = WhiteSpace.Normal;
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
                if (item == null) continue;
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
                if (item == null) continue;
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
