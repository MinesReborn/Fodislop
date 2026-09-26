#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI;

public sealed class MapInteractionController
{
    private bool _isDragging;
    private Vector2 _lastMousePos;
    private int _dragPointerId = -1;

    public void HandlePointerDown(
        PointerDownEvent evt,
        Image? mapImage)
    {
        if (evt.button != 0 || mapImage == null)
        {
            return;
        }

        Vector2 localPosition = new(evt.localPosition.x, evt.localPosition.y);
        if (!mapImage.localBound.Contains(localPosition))
        {
            return;
        }

        _isDragging = true;
        _dragPointerId = evt.pointerId;
        _lastMousePos = localPosition;
        mapImage.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    public void HandlePointerMove(
        PointerMoveEvent evt,
        Image? mapImage,
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
        if (!_isDragging || evt.pointerId != _dragPointerId || mapImage == null ||
            !mapImage.HasPointerCapture(evt.pointerId))
        {
            return;
        }

        Vector2 localPosition = new(evt.localPosition.x, evt.localPosition.y);
        Vector2 delta = localPosition - _lastMousePos;
        _lastMousePos = localPosition;
        ApplyDragDelta(
            mapImage,
            delta,
            texWidth,
            texHeight,
            cellsPerPixel,
            dragSpeed,
            ref viewCenterX,
            ref viewCenterY,
            ref followPlayer,
            ref renderRequested,
            clampViewCenter);
        evt.StopPropagation();
    }

    public void HandlePointerUp(PointerUpEvent evt, Image? mapImage)
    {
        if (!_isDragging || evt.pointerId != _dragPointerId)
        {
            return;
        }

        _isDragging = false;
        _dragPointerId = -1;
        if (mapImage != null && mapImage.HasPointerCapture(evt.pointerId))
        {
            mapImage.ReleasePointer(evt.pointerId);
        }

        evt.StopPropagation();
    }

    private static void ApplyDragDelta(
        Image mapImage,
        Vector2 delta,
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
        // Preserve sub-pixel movement across frames so slow drags still pan.
        if (delta.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        followPlayer = false;
        Rect mapRect = mapImage.localBound;
        if (mapRect.width <= 0f || mapRect.height <= 0f)
        {
            return;
        }

        if (texWidth <= 0 || texHeight <= 0)
        {
            return;
        }

        Vector2 dragWorldDelta = MapProjection.MapPixelDeltaToServer(
            delta.x * texWidth / mapRect.width * dragSpeed,
            delta.y * texHeight / mapRect.height * dragSpeed,
            cellsPerPixel);
        viewCenterX -= dragWorldDelta.x;
        viewCenterY -= dragWorldDelta.y;
        clampViewCenter();
        renderRequested = true;
    }

    public void HandleMouseScroll(
        VisualElement? mapOverlay,
        Image? mapImage,
        float delta,
        Vector2 panelPosition,
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
            !mapImage.worldBound.Contains(panelPosition))
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
            panelPosition,
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
                panelPosition,
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
        Vector2 serverPosition = MapProjection.MapPixelToServer(
            pixelX,
            pixelY,
            viewCenterX,
            viewCenterY,
            cellsPerPixel,
            texWidth,
            texHeight);
        worldX = serverPosition.x;
        worldY = serverPosition.y;
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
        Vector2 cursorOffset = MapProjection.MapPixelToServer(
            pixelX,
            pixelY,
            0f,
            0f,
            cellsPerPixel,
            texWidth,
            texHeight);
        viewCenterX = cursorWorldX - cursorOffset.x;
        viewCenterY = cursorWorldY - cursorOffset.y;
    }
}
