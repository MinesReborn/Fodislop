#nullable enable

using System;
using UnityEngine;

namespace Kern.World;

public sealed class SurfaceGeometryBuilder
{
    public const float TransitHeight = 2f;
    public const float PerspectiveHeight = 2f;
    public const float TransitTileWidth = 32f;
    public const float PerspectiveTileWidth = 5f;
    public const float BoundaryOverscan = 2f;
    public const float GeometryCacheQuantum = 32f;
    public const float GeometryCachePadding = 16f;

    private static readonly int[] _QuadTriangles =
    [
        0, 1, 2, 3, 2, 1,
    ];

    private readonly Vector3[] _boundaryVertices = new Vector3[12];
    private readonly Vector2[] _boundaryUv = new Vector2[12];
    private readonly Vector2[] _boundaryLightingData = new Vector2[12];
    private readonly int[] _boundaryTriangles = new int[18];
    private readonly Vector3[] _quadVertices = new Vector3[4];
    private readonly Vector2[] _quadUv = new Vector2[4];
    private readonly Vector2[] _quadLightingData = new Vector2[4];

    public static Rect GetVisibleRect(Camera camera)
    {
        float halfHeight = camera.orthographicSize + BoundaryOverscan;
        float halfWidth = (camera.orthographicSize * camera.aspect) + BoundaryOverscan;
        Vector3 position = camera.transform.position;
        return Rect.MinMaxRect(
            position.x - halfWidth,
            position.y - halfHeight,
            position.x + halfWidth,
            position.y + halfHeight);
    }

    public static Rect BuildCoverageRect(Rect visibleRect)
    {
        float minX = Mathf.Floor(
            (visibleRect.xMin - GeometryCachePadding) / GeometryCacheQuantum) *
            GeometryCacheQuantum;
        float minY = Mathf.Floor(
            (visibleRect.yMin - GeometryCachePadding) / GeometryCacheQuantum) *
            GeometryCacheQuantum;
        float maxX = Mathf.Ceil(
            (visibleRect.xMax + GeometryCachePadding) / GeometryCacheQuantum) *
            GeometryCacheQuantum;
        float maxY = Mathf.Ceil(
            (visibleRect.yMax + GeometryCachePadding) / GeometryCacheQuantum) *
            GeometryCacheQuantum;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    public static bool Contains(Rect outer, Rect inner)
    {
        return inner.xMin >= outer.xMin && inner.xMax <= outer.xMax &&
            inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;
    }

    public void UpdateBoundaryMesh(
        Mesh mesh,
        Rect coverageRect,
        int worldWidth,
        int worldHeight)
    {
        int vertexCount = 0;
        int indexCount = 0;
        AppendBoundaryQuad(
            coverageRect.xMin,
            coverageRect.yMin,
            Mathf.Min(coverageRect.xMax, 0f),
            Mathf.Min(coverageRect.yMax, worldHeight),
            ref vertexCount,
            ref indexCount);
        AppendBoundaryQuad(
            Mathf.Max(coverageRect.xMin, worldWidth),
            coverageRect.yMin,
            coverageRect.xMax,
            Mathf.Min(coverageRect.yMax, worldHeight),
            ref vertexCount,
            ref indexCount);
        AppendBoundaryQuad(
            Mathf.Max(coverageRect.xMin, 0f),
            coverageRect.yMin,
            Mathf.Min(coverageRect.xMax, worldWidth),
            Mathf.Min(coverageRect.yMax, 0f),
            ref vertexCount,
            ref indexCount);

        mesh.Clear(keepVertexLayout: false);
        if (vertexCount == 0)
        {
            return;
        }

        mesh.SetVertices(_boundaryVertices, 0, vertexCount);
        mesh.SetUVs(channel: 0, _boundaryUv, 0, vertexCount);
        mesh.SetUVs(channel: 1, _boundaryLightingData, 0, vertexCount);
        mesh.SetTriangles(
            _boundaryTriangles,
            trianglesStart: 0,
            trianglesLength: indexCount,
            submesh: 0,
            calculateBounds: true);
    }

    public void UpdateTransitMesh(Mesh mesh, Rect coverageRect, int worldHeight)
    {
        UpdateBandMesh(
            mesh,
            coverageRect,
            bottom: worldHeight,
            top: worldHeight + TransitHeight,
            tileWidth: TransitTileWidth,
            uvProjectionHeight: TransitHeight,
            emissionMask: 1f);
    }

    public void UpdatePerspectiveMesh(Mesh mesh, Rect coverageRect, int worldHeight)
    {
        float bottom = worldHeight + TransitHeight;
        UpdateBandMesh(
            mesh,
            coverageRect,
            bottom,
            top: bottom + PerspectiveHeight,
            tileWidth: PerspectiveTileWidth,
            uvProjectionHeight: PerspectiveHeight,
            emissionMask: 1f);
    }

    private void UpdateBandMesh(
        Mesh mesh,
        Rect coverageRect,
        float bottom,
        float top,
        float tileWidth,
        float uvProjectionHeight,
        float emissionMask)
    {
        float clippedBottom = Mathf.Max(coverageRect.yMin, bottom);
        float clippedTop = Mathf.Min(coverageRect.yMax, top);
        if (coverageRect.xMax <= coverageRect.xMin ||
            clippedTop <= clippedBottom || tileWidth <= 0f ||
            uvProjectionHeight <= 0f)
        {
            mesh.Clear(keepVertexLayout: false);
            return;
        }

        float left = coverageRect.xMin;
        float right = coverageRect.xMax;
        _quadVertices[0] = new Vector3(left, clippedBottom, 0f);
        _quadVertices[1] = new Vector3(left, clippedTop, 0f);
        _quadVertices[2] = new Vector3(right, clippedBottom, 0f);
        _quadVertices[3] = new Vector3(right, clippedTop, 0f);
        float uLeft = left / tileWidth;
        float uRight = right / tileWidth;
        float vBottom = (clippedBottom - bottom) / uvProjectionHeight;
        float vTop = (clippedTop - bottom) / uvProjectionHeight;
        _quadUv[0] = new Vector2(uLeft, vBottom);
        _quadUv[1] = new Vector2(uLeft, vTop);
        _quadUv[2] = new Vector2(uRight, vBottom);
        _quadUv[3] = new Vector2(uRight, vTop);
        Vector2 lightingData = new(emissionMask, 0f);
        _quadLightingData[0] = lightingData;
        _quadLightingData[1] = lightingData;
        _quadLightingData[2] = lightingData;
        _quadLightingData[3] = lightingData;

        mesh.Clear(keepVertexLayout: false);
        mesh.SetVertices(_quadVertices);
        mesh.SetUVs(channel: 0, _quadUv);
        mesh.SetUVs(channel: 1, _quadLightingData);
        mesh.SetTriangles(_QuadTriangles, submesh: 0, calculateBounds: true);
    }

    private void AppendBoundaryQuad(
        float left,
        float bottom,
        float right,
        float top,
        ref int vertexCount,
        ref int indexCount)
    {
        if (right <= left || top <= bottom)
        {
            return;
        }

        int firstVertex = vertexCount;
        WriteBoundaryVertex(left, bottom, ref vertexCount);
        WriteBoundaryVertex(left, top, ref vertexCount);
        WriteBoundaryVertex(right, bottom, ref vertexCount);
        WriteBoundaryVertex(right, top, ref vertexCount);
        _boundaryTriangles[indexCount++] = firstVertex;
        _boundaryTriangles[indexCount++] = firstVertex + 1;
        _boundaryTriangles[indexCount++] = firstVertex + 2;
        _boundaryTriangles[indexCount++] = firstVertex + 3;
        _boundaryTriangles[indexCount++] = firstVertex + 2;
        _boundaryTriangles[indexCount++] = firstVertex + 1;
    }

    private void WriteBoundaryVertex(float x, float y, ref int vertexCount)
    {
        _boundaryVertices[vertexCount] = new Vector3(x, y, 0f);
        _boundaryUv[vertexCount] = new Vector2(x, y);
        _boundaryLightingData[vertexCount] = Vector2.zero;
        vertexCount++;
    }
}
