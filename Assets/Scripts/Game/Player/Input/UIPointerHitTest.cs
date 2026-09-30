#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Player.Input
{
    // Есть ли под указателем видимый интерфейс. Одна проверка на оба пути
    // мыши — клик по клетке и удержание для движения: раньше удержание
    // смотрело только panel.Pick и не видело HUD, и тык по интерфейсу гнал
    // бота к курсору.
    //
    // Одного panel.Pick мало: корень HUD, карта мира, затемнения попапов и
    // подписи стоят в picking-mode="Ignore", Pick сквозь них не видит.
    // Поэтому интерфейсом считается и любой видимый элемент под курсором,
    // у которого что-то нарисовано: фон, картинка или текст. Пустые
    // контейнеры на весь экран (корень, TemplateContainer) клик пропускают.
    internal static class UIPointerHitTest
    {
        public static bool IsOverUI(UIDocument? document, Vector2 screenPosition)
        {
            if (document == null || !document.isActiveAndEnabled)
            {
                return false;
            }

            VisualElement? root = document.rootVisualElement;
            if (root?.panel == null)
            {
                return false;
            }

            // ScreenToPanel учитывает масштаб панели (ScaleWithScreenSize).
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, screenPosition);
            VisualElement? picked = root.panel.Pick(panelPosition);
            if (picked != null && picked != root && picked is not TemplateContainer)
            {
                return true;
            }

            return HasPaintedElementAt(root, panelPosition);
        }

        private static bool HasPaintedElementAt(VisualElement element, Vector2 panelPosition)
        {
            IResolvedStyle style = element.resolvedStyle;
            if (style.display == DisplayStyle.None ||
                style.visibility == Visibility.Hidden ||
                style.opacity <= 0f)
            {
                return false;
            }

            // Границы родителя не отсекают детей: абсолютно спозиционированные
            // панели выходят за пределы своих контейнеров.
            if (element.worldBound.Contains(panelPosition) && IsPainted(element, style))
            {
                return true;
            }

            for (int i = 0; i < element.hierarchy.childCount; i++)
            {
                if (HasPaintedElementAt(element.hierarchy[i], panelPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPainted(VisualElement element, IResolvedStyle style) =>
            style.backgroundColor.a > 0f ||
            style.backgroundImage.texture != null ||
            style.backgroundImage.sprite != null ||
            style.backgroundImage.vectorImage != null ||
            (element is Image image && (image.image != null || image.sprite != null || image.vectorImage != null)) ||
            (element is TextElement text && !string.IsNullOrEmpty(text.text));
    }
}
