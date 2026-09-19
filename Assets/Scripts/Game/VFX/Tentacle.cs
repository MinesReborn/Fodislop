#nullable enable

using UnityEngine;

namespace Kern.Game;

public class Tentacle
{
    private const float MAX_SEGMENT_DIST = 0.2f;
    private const float SMOOTH_TIME = 0.08f;
    private const float START_WIDTH = 0.15f;
    private const float END_WIDTH = 0.02f;

    private readonly WorldEntityBatchRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly float _wiggleOffset;
    private readonly float _sliceOffsetV;
    private readonly float _sliceScaleV;
    private readonly Vector3[] _positions;
    private readonly Vector3[] _velocities;
    private readonly Vector3[] _renderPoints;
    private readonly float[] _segmentLengths;
    private bool _isActive = true;

    public Tentacle(
        WorldEntityBatchRenderer renderer,
        Texture2D texture,
        Vector3 startPosition,
        float wiggleOffset,
        int sliceIndex,
        int totalSlices)
    {
        _renderer = renderer;
        _texture = texture;
        _wiggleOffset = wiggleOffset;

        const int count = WorldEntityBatchRenderer.POINT_COUNT;
        _positions = new Vector3[count];
        _velocities = new Vector3[count];
        _renderPoints = new Vector3[count];
        _segmentLengths = new float[count];

        _sliceScaleV = 1.0f / totalSlices;
        _sliceOffsetV = sliceIndex * _sliceScaleV;

        for (int i = 0; i < count; i++)
        {
            _positions[i] = startPosition;
            _renderPoints[i] = startPosition;
        }

        _renderer.Register(this, _texture);
    }
    public bool IsActive => _isActive;
    public Vector3 RootPosition => _positions.Length > 0 ? _positions[0] : Vector3.zero;

    internal Texture2D Texture => _texture;

    public bool IsSettled
    {
        get
        {
            for (int i = 1; i < _positions.Length; i++)
            {
                if (_velocities[i].sqrMagnitude > 1e-6f)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void SetActive(bool active)
    {
        if (_isActive == active)
        {
            return;
        }

        _isActive = active;
        _renderer.MarkDirty(_texture);
    }

    public void Snap(Vector3 position)
    {
        for (int i = 0; i < _positions.Length; i++)
        {
            _positions[i] = position;
            _velocities[i] = Vector3.zero;
            _renderPoints[i] = position;
        }

        _renderer.MarkDirty(_texture);
    }

    public void Update(Vector3 rootPosition, float rotationAngle, float movementFactor, float deltaTime)
    {
        if (!_isActive)
        {
            return;
        }

        // This is the motion model from 54b48bd (2026-06-27, "Стабилизация FPS"),
        // adapted to the current shared-mesh renderer. Unlike the later Verlet
        // version it has no perpetual idle wave, so a stationary tail settles
        // and stops invalidating the entity batch.
        _positions[0] = rootPosition;
        _renderPoints[0] = rootPosition;
        _segmentLengths[0] = 0f;

        float angleRad = rotationAngle * Mathf.Deg2Rad;
        Vector3 backwardDirection = new(-Mathf.Cos(angleRad), -Mathf.Sin(angleRad), 0f);
        Vector3 baseOffset = backwardDirection * (0.2f * movementFactor);
        float spreadAngle = (rotationAngle + _wiggleOffset) * Mathf.Deg2Rad;
        baseOffset += new Vector3(Mathf.Cos(spreadAngle), Mathf.Sin(spreadAngle), 0f) *
            (0.15f * movementFactor);

        Vector3 lastPosition = rootPosition;
        Vector3 targetPosition = rootPosition + baseOffset;
        for (int i = 1; i < _positions.Length; i++)
        {
            _positions[i] = Vector3.SmoothDamp(
                _positions[i],
                targetPosition,
                ref _velocities[i],
                SMOOTH_TIME,
                50f,
                deltaTime);

            float wiggle = Mathf.Sin((Time.time * 15f) + (i * 1.5f) + _wiggleOffset) *
                (0.1f * movementFactor);
            Vector3 direction = _positions[i] - lastPosition;
            if (direction.sqrMagnitude < 1e-6f)
            {
                direction = backwardDirection;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f);
            _renderPoints[i] = _positions[i] + (perpendicular * wiggle);
            _segmentLengths[i] = Vector3.Distance(_renderPoints[i], _renderPoints[i - 1]);
            lastPosition = _positions[i];
            targetPosition = _positions[i] + (direction * MAX_SEGMENT_DIST * movementFactor);
        }

        _renderer.MarkDirty(_texture);
    }

    public void WriteGeometry(
        Vector3[] verts,
        Vector2[] uvs,
        int vertBase,
        Rect atlasRect)
    {
        const int count = WorldEntityBatchRenderer.POINT_COUNT;

        float totalLength = 0f;
        for (int i = 1; i < count; i++)
        {
            totalLength += _segmentLengths[i];
        }

        float accumLength = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 direction;
            if (i == 0)
            {
                direction = _renderPoints[1] - _renderPoints[0];
            }
            else if (i == count - 1)
            {
                direction = _renderPoints[count - 1] - _renderPoints[count - 2];
            }
            else
            {
                direction = _renderPoints[i + 1] - _renderPoints[i - 1];
            }

            if (direction.sqrMagnitude < 1e-10f)
            {
                direction = Vector3.down;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0);
            float t = (float)i / (count - 1);
            float halfWidth = Mathf.Lerp(START_WIDTH, END_WIDTH, t) * 0.5f;

            float u = totalLength > 1e-6f ? accumLength / totalLength : t;
            if (i + 1 < count)
            {
                accumLength += _segmentLengths[i + 1];
            }

            int vi = vertBase + (i * 2);
            Vector3 p = _renderPoints[i];
            verts[vi] = p - (perpendicular * halfWidth);
            verts[vi + 1] = p + (perpendicular * halfWidth);

            float atlasU = atlasRect.xMin + (u * atlasRect.width);
            uvs[vi] = new Vector2(
                atlasU,
                atlasRect.yMin + (_sliceOffsetV * atlasRect.height));
            uvs[vi + 1] = new Vector2(
                atlasU,
                atlasRect.yMin + ((_sliceOffsetV + _sliceScaleV) * atlasRect.height));
        }
    }

    public void Destroy()
    {
        if (_renderer != null)
        {
            _renderer.Unregister(this, _texture);
        }
    }
}
