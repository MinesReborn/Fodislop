using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI.Programmator
{
    public class RadialMenu
    {
        private readonly VisualElement _root;
        private readonly VisualElement _itemContainer;
        private readonly int[] _ids;
        private readonly int _count;
        private readonly float _radius = 60f;
        private readonly float _itemSize = 36f;

        private int _hoveredIndex = -1;

        public event Action<int> OnItemClicked;

        public VisualElement Root => _root;
        public bool IsShown => _root.parent != null;
        public int HoveredIndex => _hoveredIndex;

        public RadialMenu(int[] instructionIds)
        {
            _ids = instructionIds;
            _count = instructionIds.Length;

            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.width = 180;
            _root.style.height = 180;
            _root.pickingMode = PickingMode.Ignore;

            var bg = new VisualElement();
            bg.style.position = Position.Absolute;
            bg.style.left = 0;
            bg.style.top = 0;
            bg.style.right = 0;
            bg.style.bottom = 0;
            bg.style.borderTopLeftRadius = 90;
            bg.style.borderTopRightRadius = 90;
            bg.style.borderBottomLeftRadius = 90;
            bg.style.borderBottomRightRadius = 90;
            bg.style.backgroundColor = Color.clear;
            bg.style.borderTopWidth = 60;
            bg.style.borderBottomWidth = 60;
            bg.style.borderLeftWidth = 60;
            bg.style.borderRightWidth = 60;
            bg.style.borderTopColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            bg.style.borderBottomColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            bg.style.borderLeftColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            bg.style.borderRightColor = new Color(0.08f, 0.08f, 0.08f, 0.92f);
            _root.Add(bg);

            _itemContainer = new VisualElement();
            _itemContainer.style.position = Position.Absolute;
            _itemContainer.style.left = 0;
            _itemContainer.style.top = 0;
            _itemContainer.style.right = 0;
            _itemContainer.style.bottom = 0;
            _itemContainer.pickingMode = PickingMode.Position;
            _root.Add(_itemContainer);

            const float centerX = 90f;
            const float centerY = 90f;

            for (int i = 0; i < _count; i++)
            {
                float angle = ((float)i / _count * Mathf.PI * 2f) - (Mathf.PI / 2f);
                float x = centerX + (_radius * Mathf.Cos(angle)) - (_itemSize / 2f);
                float y = centerY + (_radius * Mathf.Sin(angle)) - (_itemSize / 2f);

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
                item.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.pickingMode = PickingMode.Position;
                item.name = $"radial_item_{i}";

                int id = instructionIds[i];
                var tex = ProgrammatorTextureRegistry.GetTexture(id);
                if (tex != null)
                {
                    item.style.backgroundImage = new StyleBackground(tex);
                    item.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                }
                else
                {
                    var label = new Label(id.ToString());
                    label.style.color = Color.white;
                    label.style.fontSize = 11;
                    label.style.unityTextAlign = TextAnchor.MiddleCenter;
                    label.pickingMode = PickingMode.Ignore;
                    item.Add(label);
                }

                item.RegisterCallback<PointerEnterEvent>(_ => OnItemPointerEnter(itemIdx));
                item.RegisterCallback<PointerLeaveEvent>(_ => OnItemPointerLeave(itemIdx));
                item.RegisterCallback<PointerDownEvent>(_ => OnItemPointerDown(itemIdx));

                _itemContainer.Add(item);
            }
        }

        private void OnItemPointerEnter(int index)
        {
            _hoveredIndex = index;
            var prev = _hoveredIndex >= 0 ? _itemContainer[_hoveredIndex] as VisualElement : null;
            for (int i = 0; i < _count; i++)
            {
                var item = _itemContainer[i] as VisualElement;
                if (item == null)
                {
                    continue;
                }

                if (i == index)
                {
                    item.style.borderTopColor = new Color(1f, 0.84f, 0f, 1f);
                    item.style.borderBottomColor = new Color(1f, 0.84f, 0f, 1f);
                    item.style.borderLeftColor = new Color(1f, 0.84f, 0f, 1f);
                    item.style.borderRightColor = new Color(1f, 0.84f, 0f, 1f);
                }
                else
                {
                    item.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    item.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    item.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    item.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                }
            }
        }

        private void OnItemPointerLeave(int index)
        {
            if (_hoveredIndex == index)
            {
                _hoveredIndex = -1;
            }

            var item = _itemContainer[index] as VisualElement;
            if (item != null)
            {
                item.style.borderTopColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.style.borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                item.style.borderRightColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            }
        }

        private void OnItemPointerDown(int index)
        {
            OnItemClicked?.Invoke(_ids[index]);
        }

        public void ShowAt(VisualElement parent, Vector2 screenPos)
        {
            _hoveredIndex = -1;
            _root.style.left = screenPos.x - 90;
            _root.style.top = screenPos.y - 90;
            parent.Add(_root);
        }

        public void Hide()
        {
            _hoveredIndex = -1;
            if (_root.parent != null)
            {
                _root.RemoveFromHierarchy();
            }
        }
    }
}
