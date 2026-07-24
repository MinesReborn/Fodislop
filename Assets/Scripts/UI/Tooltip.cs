using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    public class Tooltip
    {
        private VisualElement _tooltipElement;
        private Label _tooltipLabel;
        private bool _isVisible;

        public void Initialize(UIDocument doc)
        {
            _tooltipElement = new VisualElement();
            _tooltipElement.name = "Tooltip";
            _tooltipElement.style.position = Position.Absolute;
            _tooltipElement.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            _tooltipElement.style.borderTopWidth = 1;
            _tooltipElement.style.borderBottomWidth = 1;
            _tooltipElement.style.borderLeftWidth = 1;
            _tooltipElement.style.borderRightWidth = 1;
            _tooltipElement.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            _tooltipElement.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            _tooltipElement.style.borderLeftColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            _tooltipElement.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f, 1f);
            _tooltipElement.style.paddingTop = 6;
            _tooltipElement.style.paddingBottom = 6;
            _tooltipElement.style.paddingLeft = 10;
            _tooltipElement.style.paddingRight = 10;
            _tooltipElement.style.maxWidth = 250;
            _tooltipElement.style.display = DisplayStyle.None;
            _tooltipElement.pickingMode = PickingMode.Ignore;

            _tooltipLabel = new Label();
            _tooltipLabel.style.fontSize = 12;
            _tooltipLabel.style.color = Color.white;
            _tooltipLabel.style.whiteSpace = WhiteSpace.Normal;
            _tooltipElement.Add(_tooltipLabel);

            doc.rootVisualElement.Add(_tooltipElement);
        }

        public void Show(string text, Vector2 screenPos)
        {
            if (_tooltipElement == null)
            {
                return;
            }

            _tooltipLabel.text = text;
            _tooltipElement.style.display = DisplayStyle.Flex;
            _tooltipElement.style.left = screenPos.x + 12;
            _tooltipElement.style.top = screenPos.y + 12;
            _isVisible = true;
        }

        public void Hide()
        {
            if (_tooltipElement == null || !_isVisible)
            {
                return;
            }

            _tooltipElement.style.display = DisplayStyle.None;
            _isVisible = false;
        }

        public void UpdatePosition(Vector2 screenPos)
        {
            if (!_isVisible || _tooltipElement == null)
            {
                return;
            }

            _tooltipElement.style.left = screenPos.x + 12;
            _tooltipElement.style.top = screenPos.y + 12;
        }

        public static void AttachTo(VisualElement element, string text, Tooltip tooltip)
        {
            element.RegisterCallback<MouseEnterEvent>(evt =>
            {
                var screenPos = evt.mousePosition;
                tooltip.Show(text, screenPos);
            });

            element.RegisterCallback<MouseMoveEvent>(evt =>
            {
                tooltip.UpdatePosition(evt.mousePosition);
            });

            element.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                tooltip.Hide();
            });
        }
    }
}
