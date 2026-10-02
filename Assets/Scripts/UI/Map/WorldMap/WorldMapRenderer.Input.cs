#nullable enable

using System;
using UnityEngine.UIElements;

namespace Kern.UI
{
    internal sealed class WorldMapInputDispatcher(
        MapInteractionController interaction,
        WorldMapPanel panel,
        MapTextureController textureController,
        Action clampViewCenter,
        Action requestRender)
    {
        public void HandleWheel(
            WheelEvent evt,
            float maxCellsPerPixel,
            ref float cellsPerPixel,
            ref float viewCenterX,
            ref float viewCenterY)
        {
            bool renderRequested = false;
            interaction.HandleMouseScroll(
                panel.Overlay,
                panel.Image,
                evt.delta.y,
                evt.mousePosition,
                textureController.TexWidth,
                textureController.TexHeight,
                maxCellsPerPixel,
                ref cellsPerPixel,
                ref viewCenterX,
                ref viewCenterY,
                ref renderRequested,
                clampViewCenter);

            if (renderRequested)
            {
                requestRender();
            }
        }

        public void HandlePointerDown(PointerDownEvent evt) =>
            interaction.HandlePointerDown(evt, panel.Image);

        public void HandlePointerMove(
            PointerMoveEvent evt,
            float cellsPerPixel,
            float dragSpeed,
            ref float viewCenterX,
            ref float viewCenterY,
            ref bool followPlayer)
        {
            bool renderRequested = false;
            interaction.HandlePointerMove(
                evt,
                panel.Image,
                textureController.TexWidth,
                textureController.TexHeight,
                cellsPerPixel,
                dragSpeed,
                ref viewCenterX,
                ref viewCenterY,
                ref followPlayer,
                ref renderRequested,
                clampViewCenter);

            if (renderRequested)
            {
                requestRender();
            }
        }

        public void HandlePointerUp(PointerUpEvent evt) =>
            interaction.HandlePointerUp(evt, panel.Image);
    }
}
