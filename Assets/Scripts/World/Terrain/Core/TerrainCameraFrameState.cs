#nullable enable

using UnityEngine;

namespace Kern.World.Terrain
{
    /// <summary>Tracks the active terrain camera and its smoothed world-cell travel speed.</summary>
    internal sealed class TerrainCameraFrameState
    {
        private bool _hasSpeedSample;
        private Vector3 _lastCameraPosition;

        public Camera? Camera { get; private set; }

        public float SpeedCellsPerSecond { get; private set; }

        public void SetCamera(Camera? camera) => Camera = camera;

        public void SetCameraIfMissing(Camera? camera) => Camera ??= camera;

        public bool UseCameraCandidate(Camera? candidate)
        {
            if (candidate != null)
            {
                Camera = candidate;
            }

            return Camera != null;
        }

        public void UpdateSpeedEstimate(float deltaTime, float cellSize, int windowWidth, int windowHeight)
        {
            if (!_hasSpeedSample || deltaTime <= 0f || cellSize <= 0f)
            {
                _lastCameraPosition = Camera!.transform.position;
                _hasSpeedSample = true;
                SpeedCellsPerSecond = 0f;
                return;
            }

            Vector3 position = Camera!.transform.position;
            float distanceCells = Vector2.Distance(
                new Vector2(_lastCameraPosition.x, _lastCameraPosition.y),
                new Vector2(position.x, position.y)) /
                cellSize;
            _lastCameraPosition = position;

            // A jump beyond the current window is a teleport, not a velocity
            // sample; the terrain window must re-anchor before speed is useful.
            if (distanceCells >= Mathf.Max(windowWidth, windowHeight))
            {
                return;
            }

            // Smooth normal movement so frame-time noise does not make
            // preparation lookahead jump from frame to frame.
            SpeedCellsPerSecond = Mathf.Lerp(
                SpeedCellsPerSecond,
                distanceCells / deltaTime,
                0.2f);
        }
    }
}
