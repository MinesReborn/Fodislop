#nullable enable

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Kern.UI;

public sealed class MapInteractionController
{
    private bool _isDragging;
    private Vector2 _lastMousePos;

    public void HandleDrag(
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

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _isDragging = true;
            followPlayer = false;
            _lastMousePos = Mouse.current.position.ReadValue();
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            _isDragging = false;
        }
        else if (_isDragging && Mouse.current.leftButton.isPressed)
        {
            Vector2 currentPos = Mouse.current.position.ReadValue();
            Vector2 delta = currentPos - _lastMousePos;
            _lastMousePos = currentPos;

            if (delta.sqrMagnitude > 1f)
            {
                // Screen-space: +X right, +Y up. Server world: +X right, +Y down.
                // Dragging right moves view left (decrease centerX).
                // Dragging up moves view down towards deeper cells (increase centerY).
                viewCenterX -= delta.x * cellsPerPixel * dragSpeed;
                viewCenterY += delta.y * cellsPerPixel * dragSpeed;
                clampViewCenter();
                renderRequested = true;
            }
        }
    }

    public void HandleMouseScroll(
        VisualElement? mapOverlay,
        Image? mapImage,
        UIDocument? document,
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
            Mouse.current == null)
        {
            return;
        }

        float delta = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(delta) < 0.01f)
        {
            return;
        }

        float oldCellsPerPixel = cellsPerPixel;
        bool hasCursorAnchor = TryGetCursorWorldPosition(
            mapImage,
            document,
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
                document,
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
        UIDocument? document,
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
        if (Mouse.current == null || mapImage == null || document?.rootVisualElement.panel == null ||
            texWidth <= 0 || texHeight <= 0)
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

        Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(
            document.rootVisualElement.panel,
            Mouse.current.position.ReadValue());
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
        UIDocument? document,
        int texWidth,
        int texHeight,
        float cellsPerPixel,
        float cursorWorldX,
        float cursorWorldY,
        ref float viewCenterX,
        ref float viewCenterY)
    {
        if (Mouse.current == null || mapImage == null || document?.rootVisualElement.panel == null ||
            texWidth <= 0 || texHeight <= 0)
        {
            return;
        }

        Rect rect = mapImage.worldBound;
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(
            document.rootVisualElement.panel,
            Mouse.current.position.ReadValue());
        float pixelX = ((panelPoint.x - rect.xMin) / rect.width) * texWidth;
        float pixelY = ((panelPoint.y - rect.yMin) / rect.height) * texHeight;
        viewCenterX = cursorWorldX -
            ((pixelX - (texWidth * 0.5f)) * cellsPerPixel);
        viewCenterY = cursorWorldY -
            ((pixelY - (texHeight * 0.5f)) * cellsPerPixel);
    }
}
