#nullable enable

using System.Collections.Generic;
using Kern.Core.Lifecycle;
using Kern.World;
using UnityEngine;

using Kern.Game.Visual.Effects.Pooling;
using Kern.Game.Visual.Effects.Sprites;

namespace Kern.Game.Visual.Effects.Weapons;

// GunShot.Start/Update: 0x19B5B84 / 0x19B5DB4. Inputs are captured at fire time.
internal sealed class GunShotWorldEffect : IServerWorldEffect
{
    private const int LifetimeTicks = 20;
    private const float TicksPerSecond = 30f;
    private const int SparkSegmentCapacity = LifetimeTicks + 1;
    private const float SparkLineWidth = 0.07f;
    private const float SparkGlowWidth = 0.2f;
    private const int GlowSortingOrder = RenderingConstants.BUILDING_ROOF_SORTING_ORDER + 11;
    private const int LineSortingOrder = GlowSortingOrder + 6;
    private readonly GameObject _object;
    private readonly LineRenderer _line;
    private readonly LineRenderer _glow;
    private readonly Vector3[] _points = new Vector3[LifetimeTicks + 1];
    private readonly List<Vector3> _sparkVertices = new(4 * SparkSegmentCapacity);
    private readonly List<Vector3> _sparkGlowVertices = new(4 * SparkSegmentCapacity);
    private readonly List<Vector2> _sparkUvs = new(4 * SparkSegmentCapacity);
    private readonly List<Color> _sparkColors = new(4 * SparkSegmentCapacity);
    private readonly List<int> _sparkTriangles = new(6 * SparkSegmentCapacity);
    private readonly Mesh _sparkMesh;
    private readonly Mesh _sparkGlowMesh;
    private Vector3 _target;
    private double _createdAt;
    private uint _random;
    private int _tick;
    private int _count = 1;

    internal GunShotWorldEffect(Vector3 origin, Vector3 target,
        SpriteAnimationLibrary library, ISceneObjectFactory objects, uint seed)
    {
        _object = objects.Create("SpriteAnimations/GunShot", RuntimeOwner.VisualEffects);
        _line = CreateLine("Line", 0.14f, LineSortingOrder, library.GunLineMaterial, objects);
        _glow = CreateLine("Glow", 1.25f, GlowSortingOrder, library.GunGlowMaterial, objects);
        _glow.startColor = _glow.endColor = new Color(1f, Mathf.GammaToLinearSpace(68f / 255f), Mathf.GammaToLinearSpace(68f / 255f), 1f);
        _sparkMesh = CreateSparkBatch("SparkBatch", library.GunLineMaterial, LineSortingOrder, objects);
        _sparkGlowMesh = CreateSparkBatch("SparkGlowBatch", library.GunGlowMaterial, GlowSortingOrder, objects);
        Activate(origin, target, seed);
    }

    internal void Activate(Vector3 origin, Vector3 target, uint seed)
    {
        _target = target;
        _points[0] = origin;
        _random = seed;
        _createdAt = Time.timeAsDouble;
        _tick = 0;
        _count = 1;
        _line.positionCount = 0;
        _glow.positionCount = 0;
        _line.startColor = _line.endColor = Color.white;
        _sparkVertices.Clear();
        _sparkGlowVertices.Clear();
        _sparkUvs.Clear();
        _sparkColors.Clear();
        _sparkTriangles.Clear();
        _sparkMesh.Clear();
        _sparkGlowMesh.Clear();
        IsDisposed = false;
        _object.SetActive(true);
    }

    public bool IsDisposed { get; private set; }

    public void Update()
    {
        if (IsDisposed)
        {
            return;
        }

        int targetTick = (int)((Time.timeAsDouble - _createdAt) * TicksPerSecond);
        if (targetTick > LifetimeTicks)
        {
            Dispose();
            return;
        }

        while (_tick < targetTick)
        {
            _tick++;
            _sparkVertices.Clear();
            _sparkGlowVertices.Clear();
            _sparkUvs.Clear();
            _sparkColors.Clear();
            _sparkTriangles.Clear();
            _points[_count] = (_points[_count - 1] + _target) * 0.5f;
            _count++;
            for (int index = 1; index < _count; index++)
            {
                Vector3 noise = new((0.5f - NextRandom()) * 1.25f, (0.5f - NextRandom()) * 1.25f, 0f);
                _points[index] = (_points[index] + noise) * 0.8f + _points[index - 1] * 0.2f;
            }

            Color color = new(NextRandom(), NextRandom(), NextRandom(), 1f);
            _line.startColor = _line.endColor = color.linear;
            _line.positionCount = _glow.positionCount = _count;
            for (int index = 0; index < _count; index++)
            {
                _line.SetPosition(index, _points[index]);
                _glow.SetPosition(index, _points[index]);
                if (NextRandom() < (float)index / _count)
                {
                    Vector3 end = _points[index] + new Vector3((0.5f - NextRandom()) * 1.875f, (0.5f - NextRandom()) * 1.875f, 0f);
                    AppendSparkSegment(_points[index], end);
                }
            }

            UploadSparkMeshes();
        }

    }

    private LineRenderer CreateLine(string name, float width, int order, Material material, ISceneObjectFactory objects)
    {
        GameObject child = objects.Create(name, RuntimeOwner.VisualEffects);
        child.transform.SetParent(_object.transform, worldPositionStays: false);
        LineRenderer line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.startWidth = line.endWidth = width;
        line.sortingOrder = order;
        line.positionCount = 0;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.textureMode = LineTextureMode.Stretch;
        return line;
    }

    private Mesh CreateSparkBatch(
        string name,
        Material material,
        int sortingOrder,
        ISceneObjectFactory objects)
    {
        GameObject sparkObject = objects.Create(name, RuntimeOwner.VisualEffects);
        sparkObject.transform.SetParent(_object.transform, worldPositionStays: false);
        Mesh mesh = new() { name = name };
        mesh.MarkDynamic();
        sparkObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = sparkObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
        return mesh;
    }

    private void AppendSparkSegment(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        float length = new Vector2(direction.x, direction.y).magnitude;
        Vector3 perpendicular = length > 0f
            ? new Vector3(-direction.y / length, direction.x / length, 0f)
            : Vector3.up;

        int firstVertex = _sparkVertices.Count;
        AppendSparkQuad(_sparkVertices, start, end, perpendicular, SparkLineWidth * 0.5f);
        AppendSparkQuad(_sparkGlowVertices, start, end, perpendicular, SparkGlowWidth * 0.5f);
        _sparkUvs.Add(new Vector2(0f, 0f));
        _sparkUvs.Add(new Vector2(0f, 1f));
        _sparkUvs.Add(new Vector2(1f, 0f));
        _sparkUvs.Add(new Vector2(1f, 1f));
        Color sparkColor = _glow.startColor;
        for (int vertex = 0; vertex < 4; vertex++)
        {
            _sparkColors.Add(sparkColor);
        }

        _sparkTriangles.Add(firstVertex);
        _sparkTriangles.Add(firstVertex + 1);
        _sparkTriangles.Add(firstVertex + 2);
        _sparkTriangles.Add(firstVertex + 1);
        _sparkTriangles.Add(firstVertex + 3);
        _sparkTriangles.Add(firstVertex + 2);
    }

    private void AppendSparkQuad(List<Vector3> vertices, Vector3 start, Vector3 end, Vector3 perpendicular, float halfWidth)
    {
        Transform sparkTransform = _object.transform;
        Vector3 offset = perpendicular * halfWidth;
        vertices.Add(sparkTransform.InverseTransformPoint(start - offset));
        vertices.Add(sparkTransform.InverseTransformPoint(start + offset));
        vertices.Add(sparkTransform.InverseTransformPoint(end - offset));
        vertices.Add(sparkTransform.InverseTransformPoint(end + offset));
    }

    private void UploadSparkMeshes()
    {
        if (_sparkVertices.Count == 0)
        {
            _sparkMesh.Clear();
            _sparkGlowMesh.Clear();
            return;
        }

        UploadSparkMesh(_sparkMesh, _sparkVertices);
        UploadSparkMesh(_sparkGlowMesh, _sparkGlowVertices);
    }

    private void UploadSparkMesh(Mesh mesh, List<Vector3> vertices)
    {
        mesh.Clear(keepVertexLayout: true);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, _sparkUvs);
        mesh.SetColors(_sparkColors);
        mesh.SetTriangles(_sparkTriangles, 0);
    }

    private float NextRandom()
    {
        _random = unchecked(_random * 1664525u + 1013904223u);
        return (_random >> 8) * (1f / 16777216f);
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _object.SetActive(false);
    }

    internal void Destroy()
    {
        Object.Destroy(_sparkMesh);
        Object.Destroy(_sparkGlowMesh);
        Object.Destroy(_object);
    }
}
