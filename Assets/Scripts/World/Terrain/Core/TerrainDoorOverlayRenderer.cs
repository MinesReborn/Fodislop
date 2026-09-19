#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

public sealed class TerrainDoorOverlayRenderer : IDisposable
{
    private const MeshUpdateFlags UploadFlags =
        MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

    private readonly List<TerrainVertex> _vertices = [];
    private List<int>[] _compactSubMeshIndices = Array.Empty<List<int>>();
    private GameObject? _gameObject;
    private Mesh? _mesh;
    private MeshRenderer? _renderer;

    public void Rebuild(
        Transform parent,
        ISceneObjectFactory sceneObjects,
        List<TerrainVertex> vertices,
        List<int>[] subMeshIndices,
        Material[] materials,
        string sortingLayerName,
        int sortingOrder,
        int meshWidth,
        int meshHeight,
        float cellSize)
    {
        EnsureObjects(parent, sceneObjects);
        _gameObject!.transform.localPosition = Vector3.zero;
        // Вершины и индексы дверей приходят уже компактными из сборщика клеток.
        EnsureSubMeshLists(subMeshIndices.Length);
        _vertices.Clear();
        _vertices.AddRange(vertices);
        for (int atlasIndex = 0; atlasIndex < subMeshIndices.Length; atlasIndex++)
        {
            _compactSubMeshIndices[atlasIndex].Clear();
            _compactSubMeshIndices[atlasIndex].AddRange(subMeshIndices[atlasIndex]);
        }

        if (_gameObject == null || _mesh == null || _renderer == null)
        {
            return;
        }

        if (_vertices.Count == 0)
        {
            _gameObject.SetActive(false);
            return;
        }

        _gameObject.SetActive(true);

        // Переописывать буфер надо только когда изменилась его длина или
        // число подсеток. Раньше Clear + SetVertexBufferParams шли каждый
        // раз: это перевыделение на GPU, а состав дверей в кадре почти
        // всегда тот же самый, и менялись только их вершины.
        bool layoutChanged =
            _mesh.vertexCount != _vertices.Count ||
            _mesh.subMeshCount != subMeshIndices.Length;
        if (layoutChanged)
        {
            _mesh.Clear();
            _mesh.SetVertexBufferParams(_vertices.Count, TerrainMeshManager.VertexLayout);
            _mesh.subMeshCount = subMeshIndices.Length;
        }

        _mesh.SetVertexBufferData(
            _vertices,
            0,
            0,
            _vertices.Count,
            0,
            UploadFlags);

        // Индексы переписываются всегда. Одинаковая длина буфера НЕ значит
        // одинаковый состав: одна дверь сменилась другой — счёт тот же, а
        // треугольники другие, и пропуск оставил бы на экране прошлый кадр.
        for (int atlasIndex = 0; atlasIndex < _compactSubMeshIndices.Length; atlasIndex++)
        {
            _mesh.SetIndices(
                _compactSubMeshIndices[atlasIndex],
                MeshTopology.Triangles,
                atlasIndex,
                calculateBounds: false,
                baseVertex: 0);
        }

        _mesh.bounds = new Bounds(
            new Vector3(meshWidth * cellSize * 0.5f, meshHeight * cellSize * 0.5f, 0f),
            new Vector3(
                (meshWidth * cellSize) + (cellSize * 2f),
                (meshHeight * cellSize) + (cellSize * 2f),
                2f));
        _renderer.sharedMaterials = materials;
        _renderer.sortingLayerName = sortingLayerName;
        _renderer.sortingOrder = sortingOrder;
    }

    public void Hide()
    {
        if (_gameObject != null)
        {
            _gameObject.SetActive(false);
        }
    }

    public void CompensateParentTranslation(Vector3 parentDelta)
    {
        if (_gameObject == null || !_gameObject.activeSelf || parentDelta == Vector3.zero)
        {
            return;
        }

        _gameObject.transform.localPosition -= parentDelta;
    }

    public void Dispose()
    {
        if (_gameObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(_gameObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        _gameObject = null;
        _mesh = null;
        _renderer = null;
    }

    private void EnsureObjects(Transform parent, ISceneObjectFactory sceneObjects)
    {
        if (_gameObject != null)
        {
            return;
        }

        _gameObject = sceneObjects.Create("TerrainDoorOverlay");
        _gameObject.transform.SetParent(parent, worldPositionStays: false);
        var meshFilter = _gameObject.AddComponent<MeshFilter>();
        _renderer = _gameObject.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _mesh = new Mesh
        {
            name = "TerrainDoorOverlayMesh",
            indexFormat = IndexFormat.UInt32,
        };
        _mesh.MarkDynamic();
        meshFilter.sharedMesh = _mesh;
    }

    private void EnsureSubMeshLists(int atlasCount)
    {
        if (_compactSubMeshIndices.Length == atlasCount)
        {
            return;
        }

        _compactSubMeshIndices = new List<int>[atlasCount];
        for (int atlasIndex = 0; atlasIndex < atlasCount; atlasIndex++)
        {
            _compactSubMeshIndices[atlasIndex] = [];
        }
    }
}
