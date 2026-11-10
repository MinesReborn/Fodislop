#nullable enable

using System;
using Kern.Core.Interfaces;
using UnityEngine.UIElements;
using Kern.Player.Interfaces;
using UnityEngine;

namespace Kern.UI.Map
{
    internal sealed class WorldMapInputDispatcher
    {
        private readonly MapInteractionController _interaction;
        private readonly WorldMapPanel _panel;
        private readonly MapTextureController _textureController;
        private readonly Action _clampViewCenter;
        private readonly Action _requestRender;
        private readonly MapPinchGesture _pinch = new();

        public WorldMapInputDispatcher(
            MapInteractionController interaction,
            WorldMapPanel panel,
            MapTextureController textureController,
            Action clampViewCenter,
            Action requestRender)
        {
            _interaction = interaction;
            _panel = panel;
            _textureController = textureController;
            _clampViewCenter = clampViewCenter;
            _requestRender = requestRender;
        }

        public void HandleWheel(
            WheelEvent evt,
            float maxCellsPerPixel,
            ref float cellsPerPixel,
            ref float viewCenterX,
            ref float viewCenterY)
        {
            bool renderRequested = false;
            _interaction.HandleMouseScroll(
                _panel.Overlay,
                _panel.Image,
                evt.delta.y,
                evt.mousePosition,
                _textureController.TexWidth,
                _textureController.TexHeight,
                maxCellsPerPixel,
                ref cellsPerPixel,
                ref viewCenterX,
                ref viewCenterY,
                ref renderRequested,
                _clampViewCenter);

            if (renderRequested)
            {
                _requestRender();
            }
        }

        // Масштаб кнопкой: как колесо, с якорем в центре карты
        public void HandleZoomStep(
            float delta,
            float maxCellsPerPixel,
            ref float cellsPerPixel,
            ref float viewCenterX,
            ref float viewCenterY)
        {
            if (_panel.Image == null)
            {
                return;
            }

            bool renderRequested = false;
            _interaction.HandleMouseScroll(
                _panel.Overlay,
                _panel.Image,
                delta,
                _panel.Image.worldBound.center,
                _textureController.TexWidth,
                _textureController.TexHeight,
                maxCellsPerPixel,
                ref cellsPerPixel,
                ref viewCenterX,
                ref viewCenterY,
                ref renderRequested,
                _clampViewCenter);

            if (renderRequested)
            {
                _requestRender();
            }
        }

        public void HandleClick(
            ClickEvent evt,
            float viewCenterX,
            float viewCenterY,
            float cellsPerPixel,
            IClickPathWalker? pathWalker)
        {
            Image? image = _panel.Image;
            if (evt.button != 0 || _interaction.WasDragging || image == null)
            {
                return;
            }

            Rect rect = image.contentRect;
            Vector2 local = new(evt.localPosition.x, evt.localPosition.y);
            if (rect.width <= 0f || rect.height <= 0f || !rect.Contains(local))
            {
                return;
            }

            float pixelX = (local.x - rect.x) / rect.width * _textureController.TexWidth;
            float pixelY = (local.y - rect.y) / rect.height * _textureController.TexHeight;
            Vector2 server = MapProjection.MapPixelToServer(
                pixelX,
                pixelY,
                viewCenterX,
                viewCenterY,
                cellsPerPixel,
                _textureController.TexWidth,
                _textureController.TexHeight);
            var cell = new Vector2Int(Mathf.FloorToInt(server.x), Mathf.FloorToInt(server.y));
            if (_panel.IsMarking)
            {
                _panel.PlaceMark(cell);
                return;
            }

            pathWalker?.TryStartPath(cell);
        }

        public void HandlePointerDown(PointerDownEvent evt)
        {
            if (_panel.Image != null && _pinch.Down(evt, _panel.Image))
            {
                _interaction.CancelDragForPinch();
                evt.StopPropagation();
                return;
            }

            _interaction.HandlePointerDown(evt, _panel.Image);
        }

        public void HandlePointerMove(
            PointerMoveEvent evt,
            float maxCellsPerPixel,
            ref float cellsPerPixel,
            float dragSpeed,
            ref float viewCenterX,
            ref float viewCenterY,
            ref bool followPlayer)
        {
            bool renderRequested = false;
            if (_pinch.Move(evt, out float wheelDelta, out UnityEngine.Vector2 center) && _pinch.IsPinching)
            {
                _interaction.HandleMouseScroll(
                    _panel.Overlay,
                    _panel.Image,
                    wheelDelta,
                    center,
                    _textureController.TexWidth,
                    _textureController.TexHeight,
                    maxCellsPerPixel,
                    ref cellsPerPixel,
                    ref viewCenterX,
                    ref viewCenterY,
                    ref renderRequested,
                    _clampViewCenter);
                followPlayer = false;
                if (renderRequested)
                {
                    _requestRender();
                }

                evt.StopPropagation();
                return;
            }

            _interaction.HandlePointerMove(
                evt,
                _panel.Image,
                _textureController.TexWidth,
                _textureController.TexHeight,
                cellsPerPixel,
                dragSpeed,
                ref viewCenterX,
                ref viewCenterY,
                ref followPlayer,
                ref renderRequested,
                _clampViewCenter);

            if (renderRequested)
            {
                _requestRender();
            }
        }

        public void HandlePointerUp(PointerUpEvent evt)
        {
            _pinch.Up(evt, _panel.Image);
            _interaction.HandlePointerUp(evt, _panel.Image);
        }

        public void Reset()
        {
            _pinch.Reset(_panel.Image);
            _interaction.Reset(_panel.Image);
        }
    }
}
