#nullable enable

using System.Collections.Generic;
using Kern.World;
using UnityEngine;

namespace Kern.Game
{
    /// <summary>
    /// Camera frustum tracking and visible-sprite collection for the world-entity batch.
    /// Reports whether the cached visible rect was outgrown so the owner can mark geometry dirty.
    /// </summary>
    internal sealed class WorldEntityVisibility
    {
        private readonly SpatialShardGrid<WorldEntityBatchRenderer.SpriteHandle> _grid;
        private readonly float _prefetchMargin;
        private readonly List<WorldEntityBatchRenderer.SpriteHandle> _candidates = [];

        public readonly List<WorldEntityBatchRenderer.SpriteHandle> UnderTentacles = [];
        public readonly List<WorldEntityBatchRenderer.SpriteHandle> OverTentacles = [];
        public readonly List<WorldEntityBatchRenderer.SpriteHandle> Overlay = [];

        private Vector3 _lastCameraPosition;
        private float _lastCameraOrthographicSize;
        private float _lastCameraAspect;
        private bool _hasCameraState;
        private Rect _cachedVisibleRect;
        private bool _hasCachedVisibleRect;

        public WorldEntityVisibility(
            SpatialShardGrid<WorldEntityBatchRenderer.SpriteHandle> grid,
            float prefetchMargin)
        {
            _grid = grid;
            _prefetchMargin = prefetchMargin;
        }

        /// <summary>
        /// Tracks camera motion. Returns true when the visible rect outgrew the cache
        /// and the batch must rebuild.
        /// </summary>
        public bool UpdateCameraState(Camera? camera)
        {
            if (camera == null)
            {
                return false;
            }

            Vector3 camPos = camera.transform.position;
            float orthoSize = camera.orthographicSize;
            float aspect = camera.aspect;
            bool cameraChanged = !_hasCameraState ||
                (camPos - _lastCameraPosition).sqrMagnitude > 0.0001f ||
                Mathf.Abs(orthoSize - _lastCameraOrthographicSize) > 0.001f ||
                Mathf.Abs(aspect - _lastCameraAspect) > 0.001f;
            if (cameraChanged &&
                (!TryGetVisibleRect(camera, out Rect currentVisibleRect) ||
                !_hasCachedVisibleRect ||
                !Contains(_cachedVisibleRect, currentVisibleRect)))
            {
                return true;
            }

            if (cameraChanged)
            {
                _lastCameraPosition = camPos;
                _lastCameraOrthographicSize = orthoSize;
                _lastCameraAspect = aspect;
                _hasCameraState = true;
            }

            return false;
        }

        public void Collect(
            List<WorldEntityBatchRenderer.SpriteHandle> sprites,
            bool hasCamera,
            Rect visibleRect,
            int overlaySortingOrder,
            int tentacleSortingOrder)
        {
            UnderTentacles.Clear();
            OverTentacles.Clear();
            Overlay.Clear();

            List<WorldEntityBatchRenderer.SpriteHandle> source;
            if (hasCamera)
            {
                _candidates.Clear();
                _grid.QueryRect(visibleRect, _candidates);
                _candidates.Sort(static (left, right) => left.SortingOrder.CompareTo(right.SortingOrder));
                source = _candidates;
            }
            else
            {
                source = sprites;
            }

            if (hasCamera)
            {
                _cachedVisibleRect = visibleRect;
                _hasCachedVisibleRect = true;
            }

            for (int i = 0; i < source.Count; i++)
            {
                WorldEntityBatchRenderer.SpriteHandle handle = source[i];
                if (!handle.Enabled || !handle.FrameAlive || handle.Sprite == null ||
                    (hasCamera && !IsInView(handle, true, visibleRect)))
                {
                    continue;
                }

                if (handle.SortingOrder >= overlaySortingOrder)
                {
                    Overlay.Add(handle);
                }
                else if (handle.SortingOrder < tentacleSortingOrder)
                {
                    UnderTentacles.Add(handle);
                }
                else
                {
                    OverTentacles.Add(handle);
                }
            }
        }

        public bool TryGetVisibleRect(Camera? camera, out Rect visibleRect)
        {
            if (camera == null)
            {
                visibleRect = default;
                return false;
            }

            Vector3 camPos = camera.transform.position;
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(camPos.z);
            float halfWidth = halfHeight * camera.aspect;

            visibleRect = new Rect(
                camPos.x - halfWidth - _prefetchMargin,
                camPos.y - halfHeight - _prefetchMargin,
                (halfWidth + _prefetchMargin) * 2f,
                (halfHeight + _prefetchMargin) * 2f);
            return true;
        }

        public static bool IsInView(WorldEntityBatchRenderer.SpriteHandle handle, bool hasCamera, in Rect visibleRect) =>
            !hasCamera || visibleRect.Contains((Vector2)handle.GetWorldPosition());

        public static bool IsTentacleInView(Tentacle tentacle, bool hasCamera, in Rect visibleRect) =>
            !hasCamera || visibleRect.Contains((Vector2)tentacle.RootPosition);

        private static bool Contains(in Rect outer, in Rect inner) =>
            inner.xMin >= outer.xMin &&
            inner.xMax <= outer.xMax &&
            inner.yMin >= outer.yMin &&
            inner.yMax <= outer.yMax;
    }
}
