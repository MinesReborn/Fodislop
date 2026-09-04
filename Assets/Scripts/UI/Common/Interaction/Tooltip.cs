#nullable enable

using System;
using Fodinae.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI
{
    public class Tooltip
    {
        private VisualElement? _tooltipElement;
        private Label? _tooltipLabel;
        private bool _isVisible;

        public void Initialize(UIDocument doc)
        {
            // Статическая структура (панель + лейбл) живёт в Tooltip.uxml;
            // здесь только клон и биндинг. Позиция и видимость — рантайм-состояние.
            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                ProjectRuntimeContracts.ResourcePaths.TooltipUxml) ??
                throw new InvalidOperationException(
                    "[Tooltip] Resources/UI/Tooltip.uxml is required.");
            TemplateContainer tree = template.Instantiate();
            _tooltipElement = tree;

            _tooltipLabel = tree.Q<Label>("TooltipLabel") ??
                throw new InvalidOperationException(
                    "[Tooltip] TooltipLabel is missing from Tooltip.uxml.");

            doc.rootVisualElement.Add(_tooltipElement);
        }

        public void Show(string text, Vector2 screenPos)
        {
            if (_tooltipElement == null || _tooltipLabel == null)
            {
                return;
            }

            _tooltipLabel.text = text;
            UIState.Show(_tooltipElement);
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

            UIState.Hide(_tooltipElement);
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

        public static void AttachTo(VisualElement element, Func<string> textProvider, Tooltip? tooltip)
        {
            if (tooltip == null)
            {
                return;
            }

            element.RegisterCallback<MouseEnterEvent>(evt =>
            {
                var screenPos = evt.mousePosition;
                tooltip.Show(textProvider(), screenPos);
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
