#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Unity.Profiling;
using UnityEngine;

namespace Kern.World;

/// <summary>Scope-owned presentation. Tick changes no terrain facts or lighting revisions.</summary>
public sealed class SurfaceSceneRenderer : IDisposable
{
    private readonly ISceneObjectFactory _sceneObjects;
    private readonly MapManager _map;
    private readonly IGameplayCamera _gameplayCamera;
    private static readonly ProfilerMarker s_tickMarker = new("Kern.SurfaceScene.Tick");
    private static readonly ProfilerMarker s_rebuildMarker = new("Kern.SurfaceScene.RebuildCoverage");
    private static readonly int s_surfaceYId = Shader.PropertyToID("_SurfaceY");
    private static readonly int s_cameraXId = Shader.PropertyToID("_CameraX");
    private readonly List<Band> _bands = new();
    private readonly List<Material> _materials = new();
    private readonly Dictionary<(int Layer, int Index), Band> _segments = new();
    private readonly List<(int Layer, int Index)> _expired = new();
    private SurfaceSceneProfile? _profile;
    private int _sceneSeed;
    private float _segmentWidthCells;
    private Transform? _root;
    private Mesh? _quad;
    private int _lastMin = int.MaxValue;
    private int _lastMax = int.MinValue;
    private int _lastFarMin = int.MaxValue;
    private int _lastFarMax = int.MinValue;
    private int _lastMiddleMin = int.MaxValue;
    private int _lastMiddleMax = int.MinValue;
    private int _worldHeight = int.MinValue;
    private int _worldWidth = int.MinValue;
    private float _lastCameraX;
    private float _lastCameraY;
    private float _lastHalfWidth;
    private float _lastHalfHeight;

    public SurfaceSceneRenderer(ISceneObjectFactory sceneObjects, MapManager map, IGameplayCamera gameplayCamera)
    {
        _sceneObjects = sceneObjects;
        _map = map;
        _gameplayCamera = gameplayCamera;
    }

    public bool IsInitialized => _root != null;
    public int RebuildCount { get; private set; }
    public string LastRebuildReason { get; private set; } = "none";
    public int ActiveSegmentCount => _segments.Count;
    public int TriangleCount { get; private set; }

    public void Initialize(Transform parent, MeshRenderer[] legacyVisibleBands)
    {
        SurfaceSceneProfile profile = Resources.Load<SurfaceSceneProfile>(SurfaceSceneProfile.ResourcePath) ??
            throw new InvalidOperationException("Required Resources/World/Surface/Scene/SurfaceScene profile is missing. Run Kern/World/Author Surface Assets.");
        Initialize(parent, profile);
        foreach (MeshRenderer renderer in legacyVisibleBands)
        {
            // Preserve the gameplay surface and its transition; only the old
            // full-screen sky is replaced by the atmospheric background.
            renderer.enabled = renderer.gameObject.name != SurfaceRenderer.HorizonObjectName;
        }
    }

    public void Initialize(Transform parent, SurfaceSceneProfile profile)
    {
        if (IsInitialized)
        {
            throw new InvalidOperationException("Surface scene already initialized.");
        }

        profile.Validate();
        _profile = profile;
        _sceneSeed = profile.Seed;
        _segmentWidthCells = profile.SegmentWidthCells;
        GameObject root = _sceneObjects.Create("SurfaceScene", RuntimeOwner.General);
        root.transform.SetParent(parent, false);
        _root = root.transform;
        root.layer = parent.gameObject.layer;
        _quad = CreateQuad();
        _materials.Add(CreateMaterial("Chemical sky", 0, profile.Basalt, 0f, 0f));
        _materials.Add(CreateMaterial("Rocky satellite", 1, profile.Moon, 0.75f, 0f));
        foreach (Texture2D ridge in profile.DistantRidges)
        {
            _materials.Add(CreateMaterial($"Distant ridge {ridge.name}", 2, ridge, 0.77f, 0f));
        }

        _materials.Add(CreateMaterial("Middle basalt", 3, profile.Basalt, 0.38f, 0f));
        _materials.Add(CreateMaterial("Near basalt", 4, profile.Basalt, 0.05f, profile.CrackRadiance));
        _materials.Add(CreateMaterial("Distant basalt masses", 3, profile.Basalt, 0.65f, 0f));
        _bands.Add(CreateBand("Sky", _quad, _materials[0], -1250, ownsMesh: false));
        _bands.Add(CreateBand("Satellite", _quad, _materials[1], -1240, ownsMesh: false));
    }

    public void Tick()
    {
        using var marker = s_tickMarker.Auto();
        if (!IsInitialized || !_map.IsWorldInitialized)
        {
            return;
        }

        Camera camera = _gameplayCamera.Camera;
        // Bootstrap camera teardown can precede content-scene unloading. No view exists in that interval.
        if (camera == null)
        {
            return;
        }

        UpdateView(camera, _map.WorldWidth, _map.WorldHeight);
    }

    internal void UpdateView(Camera camera, int worldWidth, int worldHeight)
    {
        _ = _profile ?? throw new InvalidOperationException("Surface profile is not initialized.");
        if (!camera.orthographic || worldWidth <= 0 || worldHeight <= 0)
        {
            throw new InvalidOperationException("Surface scene requires an orthographic camera and positive world dimensions.");
        }

        float cameraX = camera.transform.position.x;
        float halfHeight = camera.orthographicSize;
        float halfWidth = halfHeight * camera.aspect;
        bool worldChanged = _worldHeight != worldHeight || _worldWidth != worldWidth;
        bool viewChanged = worldChanged || _lastCameraX != cameraX || _lastCameraY != camera.transform.position.y ||
            _lastHalfWidth != halfWidth || _lastHalfHeight != halfHeight;
        if (worldChanged)
        {
            _root!.position = new Vector3(0f, worldHeight, 0f);
            foreach (Material material in _materials)
            {
                material.SetFloat(s_surfaceYId, worldHeight);
                material.SetFloat("_WorldWidth", worldWidth);
            }
        }

        if (viewChanged)
        {
            foreach (Material material in _materials)
            {
                material.SetFloat(s_cameraXId, cameraX);
            }

            float top = Mathf.Max(100f, camera.transform.position.y - worldHeight + halfHeight + 4f);
            _bands[0].Renderer.transform.localPosition = new Vector3(cameraX, 0f, 90f);
            _bands[0].Renderer.transform.localScale = new Vector3(halfWidth * 2f + 4f, top, 1f);
            _bands[1].Renderer.transform.localPosition = new Vector3(cameraX + 24f, 48f, 80f);
            _bands[1].Renderer.transform.localScale = new Vector3(7f, 7f, 1f);
        }

        float width = _segmentWidthCells;
        int min = Mathf.FloorToInt((cameraX - halfWidth) / width) - 1;
        int max = Mathf.FloorToInt((cameraX + halfWidth) / width) + 1;
        float farWidth = width * 4f;
        int farMin = Mathf.FloorToInt((cameraX * 0.08f - halfWidth) / farWidth) - 1;
        int farMax = Mathf.FloorToInt((cameraX * 0.08f + halfWidth) / farWidth) + 1;
        int middleMin = Mathf.FloorToInt((cameraX * 0.25f - halfWidth) / width) - 1;
        int middleMax = Mathf.FloorToInt((cameraX * 0.25f + halfWidth) / width) + 1;
        if (worldChanged || min != _lastMin || max != _lastMax || farMin != _lastFarMin || farMax != _lastFarMax ||
            middleMin != _lastMiddleMin || middleMax != _lastMiddleMax)
        {
            using var rebuild = s_rebuildMarker.Auto();
            LastRebuildReason = worldChanged ? "world dimensions" : "visible segment range";
            RebuildCoverage(cameraX, halfWidth, worldWidth);
            _lastMin = min;
            _lastMax = max;
            _lastFarMin = farMin;
            _lastFarMax = farMax;
            _lastMiddleMin = middleMin;
            _lastMiddleMax = middleMax;
            RebuildCount++;
        }

        if (viewChanged)
        {
            foreach (var entry in _segments)
            {
                int layer = entry.Key.Layer;
                float factor = layer == 0 ? 0.08f : layer == 1 ? 0.25f : 1f;
                float segmentWidth = layer == 0 ? farWidth : width;
                entry.Value.Renderer.transform.localPosition = new Vector3(
                    entry.Key.Index * segmentWidth + cameraX * (1f - factor),
                    SurfaceGeometryBuilder.PresentationHeight, layer == 0 ? 40f : layer == 1 ? 20f : 10f);
            }
        }

        _worldHeight = worldHeight;
        _worldWidth = worldWidth;
        _lastCameraX = cameraX;
        _lastCameraY = camera.transform.position.y;
        _lastHalfHeight = halfHeight;
        _lastHalfWidth = halfWidth;
    }

    private void RebuildCoverage(float cameraX, float halfWidth, int worldWidth)
    {
        _ = _profile ?? throw new InvalidOperationException("Surface profile is not initialized.");
        _expired.Clear();
        foreach (var entry in _segments)
        {
            GetRange(entry.Key.Layer, out int min, out int max);
            if (entry.Key.Index < min || entry.Key.Index > max)
            {
                _expired.Add(entry.Key);
            }
        }

        foreach (var key in _expired)
        {
            DestroyBand(_segments[key]);
            _segments.Remove(key);
        }

        for (int layer = 0; layer < 3; layer++)
        {
            GetRange(layer, out int min, out int max);
            for (int index = min; index <= max; index++)
            {
                if (_segments.ContainsKey((layer, index)))
                {
                    continue;
                }

                bool far = layer == 0;
                Mesh mesh = SurfaceMassifGeometry.Create(index, _sceneSeed, _segmentWidthCells * (far ? 4f : 1f), layer);
                int materialIndex = far ? 7 : layer == 1 ? 5 : 6;
                Band band = CreateBand($"Surface layer {layer} segment {index}", mesh, _materials[materialIndex],
                    far ? -1220 : layer == 1 ? -1200 : -503, ownsMesh: true);

                _segments.Add((layer, index), band);
            }
        }

        TriangleCount = 4;
        foreach (Band band in _segments.Values)
        {
            TriangleCount += band.Mesh.triangles.Length / 3;
        }

        void GetRange(int layer, out int min, out int max)
        {
            float factor = layer == 0 ? 0.08f : layer == 1 ? 0.25f : 1f;
            float width = _segmentWidthCells * (layer == 0 ? 4f : 1f);
            min = Mathf.FloorToInt((cameraX * factor - halfWidth) / width) - 1;
            max = Mathf.FloorToInt((cameraX * factor + halfWidth) / width) + 1;
            if (layer == 2)
            {
                min = Mathf.Max(0, min);
                max = Mathf.Min(Mathf.CeilToInt(worldWidth / width) - 1, max);
            }
        }
    }

    private Material CreateMaterial(string name, int kind, Texture2D texture, float fog, float emission)
    {
        SurfaceSceneProfile profile = _profile!;
        var material = new Material(profile.Shader) { name = name, hideFlags = HideFlags.DontSave };
        material.SetTexture("_BaseMap", texture);
        material.SetFloat("_LayerKind", kind);
        material.SetFloat("_GroundBandHeight", SurfaceGeometryBuilder.PresentationHeight);
        material.SetInt("_ZWrite", kind >= 3 ? 1 : 0);
        material.SetFloat("_Fog", fog);
        material.SetFloat("_Emission", emission);
        material.SetFloat("_Density", profile.AtmosphereDensity);
        material.SetFloat("_Seed", profile.Seed);
        material.SetColor("_AtmosphereColor", profile.AtmosphereColor.linear);
        material.SetColor("_RockColor", profile.RockColor.linear);
        material.SetColor("_CrackColor", profile.CrackColor.linear);
        return material;
    }

    private Band CreateBand(string name, Mesh mesh, Material material, int sortingOrder, bool ownsMesh)
    {
        MeshRenderer renderer = SurfaceBandBinding.BindBandObject(_sceneObjects, _root!, _root!.gameObject.layer,
            name, mesh, material, sortingOrder);
        return new Band(renderer, mesh, ownsMesh);
    }

    internal static Mesh CreateQuad()
    {
        var mesh = new Mesh { name = "Surface scene quad" };
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f),
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(1f, 0f), new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateNormals();
        return mesh;
    }

    public void Dispose()
    {
        foreach (Band band in _segments.Values)
        {
            DestroyBand(band);
        }

        _segments.Clear();
        TriangleCount = 0;
        LastRebuildReason = "none";
        _bands.Clear();
        foreach (Material material in _materials)
        {
            SurfaceResourceLifetime.DestroyOwned(material);
        }

        _materials.Clear();
        SurfaceResourceLifetime.DestroyOwned(_quad);
        if (_root != null)
        {
            SurfaceResourceLifetime.DestroyOwned(_root.gameObject);
        }

        _root = null;
        _quad = null;
        _profile = null;
        _worldHeight = int.MinValue;
        _lastMin = int.MaxValue;
    }

    private static void DestroyBand(Band band)
    {
        SurfaceResourceLifetime.DestroyOwned(band.Renderer.gameObject);
        if (band.OwnsMesh)
        {
            SurfaceResourceLifetime.DestroyOwned(band.Mesh);
        }
    }

    private readonly record struct Band(MeshRenderer Renderer, Mesh Mesh, bool OwnsMesh);
}
