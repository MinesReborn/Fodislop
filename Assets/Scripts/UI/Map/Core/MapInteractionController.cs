#nullable enable

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI;

public sealed class MapInteractionController
{
    private bool _isDragging;
    private bool _panelUnavailableWarningReported;
    private Vector2 _lastMousePos;

    public void HandleDrag(
        Image? mapImage,
        UIDocument? document,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float dragSpeed,
        ref float viewCenterX,
        ref float viewCenterY,
        ref bool followPlayer,
        ref bool renderRequested,
        System.Action clampViewCenter)
    {
        if (Mouse.current == null)
        {
            return;
        }

        Vector2 screenPosition = Mouse.current.position.ReadValue();
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (TryGetPanelPosition(mapImage, document, screenPosition, out Vector2 panelPosition) &&
                mapImage!.worldBound.Contains(panelPosition))
            {
                _isDragging = true;
                _lastMousePos = panelPosition;
            }
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            _isDragging = false;
        }
        else if (_isDragging && Mouse.current.leftButton.isPressed)
        {
            if (!TryGetPanelPosition(mapImage, document, screenPosition, out Vector2 panelPosition) ||
                mapImage == null || texWidth <= 0 || texHeight <= 0)
            {
                return;
            }

            Vector2 delta = panelPosition - _lastMousePos;
            _lastMousePos = panelPosition;

            if (delta.sqrMagnitude > 1f)
            {
                followPlayer = false;
                Rect mapRect = mapImage.worldBound;
                if (mapRect.width <= 0f || mapRect.height <= 0f)
                {
                    return;
                }

                // Panel and server coordinates both increase downwards on Y.
                // Dragging the map right/down moves its viewed world left/up.
                viewCenterX -= delta.x * texWidth / mapRect.width * cellsPerPixel * dragSpeed;
                viewCenterY -= delta.y * texHeight / mapRect.height * cellsPerPixel * dragSpeed;
                clampViewCenter();
                renderRequested = true;
            }
        }
    }

    private bool TryGetPanelPosition(
        Image? mapImage,
        UIDocument? document,
        Vector2 screenPosition,
        out Vector2 panelPosition)
    {
        panelPosition = default;
        if (mapImage == null)
        {
            return false;
        }

        if (document == null || document.rootVisualElement.panel == null)
        {
            if (!_panelUnavailableWarningReported)
            {
                _panelUnavailableWarningReported = true;
                Debug.LogWarning(
                    "World map input was ignored because its UI Toolkit panel is not attached.");
            }

            return false;
        }

        _panelUnavailableWarningReported = false;

        panelPosition = RuntimePanelUtils.ScreenToPanel(
            document.rootVisualElement.panel,
            screenPosition);
        return true;
    }

    public void HandleMouseScroll(
        VisualElement? mapOverlay,
        Image? mapImage,
        UIDocument? document,
        float delta,
        Vector2 screenPosition,
        int texWidth,
        int texHeight,
        float maxCellsPerPixel,
        ref float cellsPerPixel,
        ref float viewCenterX,
        ref float viewCenterY,
        ref bool renderRequested,
        System.Action clampViewCenter)
    {
        if (mapOverlay == null ||
            mapOverlay.resolvedStyle.display == DisplayStyle.None ||
            mapImage == null ||
            !TryGetPanelPosition(mapImage, document, screenPosition, out Vector2 panelPoint) ||
            !mapImage.worldBound.Contains(panelPoint))
        {
            return;
        }

        if (Mathf.Abs(delta) < 0.01f)
        {
            return;
        }

        float oldCellsPerPixel = cellsPerPixel;
        bool hasCursorAnchor = TryGetCursorWorldPosition(
            mapImage,
            panelPoint,
            texWidth,
            texHeight,
            cellsPerPixel,
            viewCenterX,
            viewCenterY,
            out float cursorWorldX,
            out float cursorWorldY);

        float zoomSteps = Mathf.Clamp(delta, -4f, 4f);
        cellsPerPixel = Mathf.Clamp(
            oldCellsPerPixel * Mathf.Pow(0.85f, zoomSteps),
            0.25f,
            maxCellsPerPixel);

        if (hasCursorAnchor && oldCellsPerPixel > 0f)
        {
            ApplyCursorAnchor(
                mapImage,
                panelPoint,
                texWidth,
                texHeight,
                cellsPerPixel,
                cursorWorldX,
                cursorWorldY,
                ref viewCenterX,
                ref viewCenterY);
        }

        clampViewCenter();
        renderRequested = true;
    }

    private static bool TryGetCursorWorldPosition(
        Image? mapImage,
        Vector2 panelPoint,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float viewCenterX,
        float viewCenterY,
        out float worldX,
        out float worldY)
    {
        worldX = 0f;
        worldY = 0f;
        if (mapImage == null || texWidth <= 0 || texHeight <= 0)
        {
            return false;
        }

        Rect rect = mapImage.worldBound;
        if (rect.width <= 0f || rect.height <= 0f ||
            float.IsNaN(rect.width) || float.IsNaN(rect.height) ||
            float.IsInfinity(rect.width) || float.IsInfinity(rect.height))
        {
            return false;
        }

        float pixelX = ((panelPoint.x - rect.xMin) / rect.width) * texWidth;
        float pixelY = ((panelPoint.y - rect.yMin) / rect.height) * texHeight;
        worldX = viewCenterX +
            ((pixelX - (texWidth * 0.5f)) * cellsPerPixel);
        worldY = viewCenterY +
            ((pixelY - (texHeight * 0.5f)) * cellsPerPixel);
        return true;
    }

    private static void ApplyCursorAnchor(
        Image? mapImage,
        Vector2 panelPoint,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float cursorWorldX,
        float cursorWorldY,
        ref float viewCenterX,
        ref float viewCenterY)
    {
        if (mapImage == null || texWidth <= 0 || texHeight <= 0)
        {
            return;
        }

        Rect rect = mapImage.worldBound;
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        float pixelX = ((panelPoint.x - rect.xMin) / rect.width) * texWidth;
        float pixelY = ((panelPoint.y - rect.yMin) / rect.height) * texHeight;
        viewCenterX = cursorWorldX -
            ((pixelX - (texWidth * 0.5f)) * cellsPerPixel);
        viewCenterY = cursorWorldY -
            ((pixelY - (texHeight * 0.5f)) * cellsPerPixel);
    }
}
