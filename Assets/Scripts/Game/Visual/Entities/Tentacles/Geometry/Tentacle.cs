#nullable enable

using UnityEngine;

using Kern.Game.Rendering;
using Kern.Game.Visual.Entities.Tails;

namespace Kern.Game.Visual.Entities.Tentacles;

public sealed class Tentacle
{
    private readonly WorldEntityBatchRenderer _renderer;
    private readonly Texture2D _texture;
    private readonly TailChain _chain;
    private readonly Vector3[] _renderPoints = new Vector3[TailChain.PointCount];
    private Vector3 _simulationOffset;
    private bool _isActive = true;

    public Tentacle(
        WorldEntityBatchRenderer renderer,
        Texture2D texture,
        Vector3 startPosition,
        int strandIndex)
    {
        _renderer = renderer;
        _texture = texture;
        _chain = new TailChain(strandIndex, startPosition);

        CopyChainToRenderPoints();
        _renderer.Register(this, _texture);
    }

    public bool IsActive => _isActive;

    public Vector3 RootPosition => _chain[0] - _simulationOffset;

    public Vector3 TipPosition => _chain[TailChain.PointCount - 1] - _simulationOffset;

    public bool IsSettled => _chain.IsSettled;

    internal Texture2D Texture => _texture;

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
        _simulationOffset = Vector3.zero;
        _chain.Snap(position);
        CopyChainToRenderPoints();
        _renderer.MarkDirty(_texture);
    }

    public float TipDistanceTo(Vector3 rootPosition) => Vector3.Distance(TipPosition, rootPosition);

    public void Update(
        Vector3 rootPosition,
        float movementFactor,
        float deltaTime,
        float sharedStretch,
        Vector3 simulationOffset)
    {
        if (!_isActive)
        {
            return;
        }

        _simulationOffset = simulationOffset;
        _chain.Step(rootPosition + simulationOffset, movementFactor, deltaTime, sharedStretch);
        CopyChainToRenderPoints();
        _renderer.MarkDirty(_texture);
    }

    public void WriteGeometry(Vector3[] verts, Vector2[] uvs, int vertBase, Rect atlasRect)
    {
        TentacleGeometry.WriteVertices(
            _renderPoints,
            atlasRect,
            verts,
            uvs,
            vertBase,
            _simulationOffset);
    }

    public void Destroy() => _renderer.Unregister(this, _texture);

    private void CopyChainToRenderPoints()
    {
        for (int i = 0; i < _renderPoints.Length; i++)
        {
            _renderPoints[i] = _chain[i];
        }
    }
}
