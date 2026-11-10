#nullable enable

using System;
using Kern.Core;
using Kern.Core.Lifecycle;
using Kern.World.Streaming;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

/// <summary>
/// Меш, которым террейн рисуется на экране, внутри сетки, которой он живёт.
/// </summary>
///
/// Сетка больше кадра: её размер задан освещением и сдвигом, а не камерой.
/// Рисовать её целиком значит гонять квады, которых на экране нет. Поэтому
/// экран рисует свой меш поменьше, а его положение внутри сетки едет смещением
/// в шейдере — сами тексели при этом не двигаются, у них кольцевой адрес.
///
/// Запас в четыре клетки по краям — физический: камера стоит между границами
/// клеток непрерывно, а адрес текселя целочисленный.
public sealed class TerrainPresentationWindow : IDisposable
{
    private const int PresentationMarginCells = 4;

    private readonly TerrainVolumeMesh _mesh = new();
    private Vector4 _viewOffset;
    private Vector4 _viewSize;
    private static readonly int s_volumeViewSizeId = Shader.PropertyToID("_TerrainVolumeViewSize");

    private GameObject? _blockObject;
    private MeshFilter? _blockFilter;
    private MeshRenderer? _blockRenderer;
    private Material? _blockMaterial;

    public Mesh? Mesh => _mesh.Mesh;

    public Mesh? BlockMesh => _mesh.BlockMesh;

    public Vector4 ViewOffset => _viewOffset;

    // Submission count per camera frame, not a count of surviving wall fragments.
    public int SubmittedDepthCarrierQuads => _mesh.WallQuadCount;

    internal void Update(
        StreamingPolicy policy,
        RectInt cameraViewport,
        Vector2Int windowOrigin,
        int meshWidth,
        int meshHeight,
        float cellSize,
        MeshFilter? meshFilter,
        MeshRenderer? meshRenderer,
        ISceneObjectFactory sceneObjects,
        TerrainVolumeTopology topology)
    {
        // Backing grid is sized for the maximum supported zoom, while the
        // presentation mesh must match the current viewport. Retaining a
        // high-water size here submits off-screen quads forever after zoom-out.
        int wantedWidth = policy.QuantizeDimension(
            cameraViewport.width + (PresentationMarginCells * 2));
        int wantedHeight = policy.QuantizeDimension(
            cameraViewport.height + (PresentationMarginCells * 2));
        int width = Mathf.Clamp(wantedWidth, 1, meshWidth);
        int height = Mathf.Clamp(wantedHeight, 1, meshHeight);
        int extraWidth = Mathf.Max(0, width - cameraViewport.width - (PresentationMarginCells * 2));
        int extraHeight = Mathf.Max(0, height - cameraViewport.height - (PresentationMarginCells * 2));
        int presentationMinX = cameraViewport.xMin - PresentationMarginCells - (extraWidth / 2);
        int presentationMinY = cameraViewport.yMin - PresentationMarginCells - (extraHeight / 2);
        int offsetX = Mathf.Clamp(presentationMinX - windowOrigin.x, 0, meshWidth - width);
        int offsetY = Mathf.Clamp(presentationMinY - windowOrigin.y, 0, meshHeight - height);

        _mesh.EnsureSize(width, height, cellSize, meshWidth, meshHeight, topology);
        var viewSize = new Vector4(width, height, 0f, 0f);
        if (_viewSize != viewSize)
        {
            _viewSize = viewSize;
            Shader.SetGlobalVector(s_volumeViewSizeId, viewSize);
        }
        var offset = new Vector4(offsetX, offsetY, 0f, 0f);
        if (offset != _viewOffset)
        {
            _viewOffset = offset;
            Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, offset);
        }

        if (meshFilter != null && meshFilter.sharedMesh != _mesh.Mesh)
        {
            meshFilter.sharedMesh = _mesh.Mesh;
            Shader.SetGlobalVector(TerrainCellBuffers.ViewOffsetId, _viewOffset);
        }

        EnsureBlockRenderer(meshFilter, meshRenderer, sceneObjects);
    }

    // Стенки и передний план блоков живут на своём рендерере над сущностями
    // мира (роботами и хвостами). Объект создаётся тем же адресным мешем и той
    // же мировой матрицей, что главный террейн, поэтому оба слоя совпадают
    // пиксель в пиксель.
    private void EnsureBlockRenderer(
        MeshFilter? meshFilter,
        MeshRenderer? meshRenderer,
        ISceneObjectFactory sceneObjects)
    {
        if (meshFilter == null || meshRenderer == null || sceneObjects == null)
        {
            return;
        }

        if (_blockObject == null)
        {
            _blockObject = sceneObjects.Create("TerrainBlockOverlay");
            _blockObject.transform.SetParent(meshFilter.transform, worldPositionStays: false);
            _blockObject.transform.localPosition = Vector3.zero;
            _blockObject.transform.localRotation = Quaternion.identity;
            _blockObject.transform.localScale = Vector3.one;
            _blockFilter = _blockObject.AddComponent<MeshFilter>();
            _blockRenderer = _blockObject.AddComponent<MeshRenderer>();
            _blockRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _blockRenderer.receiveShadows = false;
            _blockRenderer.lightProbeUsage = LightProbeUsage.Off;
            _blockRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        _blockFilter!.sharedMesh = _mesh.BlockMesh;
        _blockRenderer!.sortingLayerName = meshRenderer.sortingLayerName;
        _blockRenderer.sortingOrder = WorldRenderLayers.TerrainForegroundSortingOrder;
        _blockRenderer.enabled = meshRenderer.enabled;

        Material? material = meshRenderer.sharedMaterial;
        if (material != null && material != _blockMaterial)
        {
            _blockMaterial = material;
            _blockRenderer.sharedMaterials = new[] { material, material };
        }
    }

    public void SetBlockVisible(bool visible)
    {
        if (_blockRenderer != null)
        {
            _blockRenderer.enabled = visible;
        }
    }

    public void Dispose()
    {
        _mesh.Dispose();
        if (_blockObject != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_blockObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_blockObject);
            }

            _blockObject = null;
            _blockFilter = null;
            _blockRenderer = null;
            _blockMaterial = null;
        }
    }
}
