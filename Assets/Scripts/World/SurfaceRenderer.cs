using System;
using Cysharp.Threading.Tasks;
using Fodinae.Scripts.Game.Managers;
using UnityEngine;

namespace Fodinae.Scripts.World
{
    public class SurfaceRenderer : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField] private Material _transitMaterial;
        [SerializeField] private Material _perspectiveMaterial;

        [Header("Settings")]
        [SerializeField] private string _transitTexturePath = "transit";
        [SerializeField] private string _perspectiveTexturePath = "perspective";
        [SerializeField] private int _transitSortingOrder = -501;
        [SerializeField] private int _perspectiveSortingOrder = -502;

        [Header("Perspective")]
        [SerializeField] private float _perspectiveShrink = 0.3f;

        private const float TRANSIT_HEIGHT = 2f;
        private const float PERSPECTIVE_HEIGHT = 2f;
        private const float TILE_SIZE = 32f;
        private const float PERSPECTIVE_OFFSET = 32f;

        private Mesh _transitMesh;
        private Mesh _perspectiveMesh;
        private MeshFilter _transitFilter;
        private MeshFilter _perspectiveFilter;
        private MeshRenderer _transitRenderer;
        private MeshRenderer _perspectiveRenderer;

        private readonly Vector2[] _uvTransit = new Vector2[4];
        private readonly Vector2[] _uvPers = new Vector2[4];
        private readonly Vector3[] _verticesTransit = new Vector3[4];
        private readonly Vector3[] _verticesPers = new Vector3[4];
        private static readonly int[] Triangles = { 0, 1, 2, 3, 2, 1 };

        private Camera _mainCamera;
        private bool _texturesLoading;

        private void Start()
        {
            _mainCamera = Camera.main;

            var transitGO = new GameObject("SurfaceTransit");
            transitGO.transform.SetParent(transform, false);
            _transitFilter = transitGO.AddComponent<MeshFilter>();
            _transitRenderer = transitGO.AddComponent<MeshRenderer>();
            _transitRenderer.sortingOrder = _transitSortingOrder;
            _transitMesh = new Mesh();
            _transitMesh.vertices = _verticesTransit;
            _transitMesh.uv = _uvTransit;
            _transitMesh.triangles = Triangles;
            _transitFilter.mesh = _transitMesh;

            var persGO = new GameObject("SurfacePerspective");
            persGO.transform.SetParent(transform, false);
            _perspectiveFilter = persGO.AddComponent<MeshFilter>();
            _perspectiveRenderer = persGO.AddComponent<MeshRenderer>();
            _perspectiveRenderer.sortingOrder = _perspectiveSortingOrder;
            _perspectiveMesh = new Mesh();
            _perspectiveMesh.vertices = _verticesPers;
            _perspectiveMesh.uv = _uvPers;
            _perspectiveMesh.triangles = Triangles;
            _perspectiveFilter.mesh = _perspectiveMesh;

            // Create materials
            if (_transitMaterial == null) _transitMaterial = CreateDefaultMaterial();
            if (_perspectiveMaterial == null) _perspectiveMaterial = CreateDefaultMaterial();
            _transitRenderer.material = _transitMaterial;
            _perspectiveRenderer.material = _perspectiveMaterial;

            LoadTexturesAsync().Forget();
        }

        private static Material CreateDefaultMaterial()
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            var tempTex = new Texture2D(1, 1);
            tempTex.SetPixel(0, 0, Color.white);
            tempTex.Apply();
            mat.mainTexture = tempTex;
            return mat;
        }

        private async UniTaskVoid LoadTexturesAsync()
        {
            if (_texturesLoading) return;
            _texturesLoading = true;

            try
            {
                var transitTex = await ClientAssetLoader.Instance.GetTextureAsync(_transitTexturePath);
                if (transitTex != null) _transitMaterial.mainTexture = transitTex;

                var persTex = await ClientAssetLoader.Instance.GetTextureAsync(_perspectiveTexturePath);
                if (persTex != null) _perspectiveMaterial.mainTexture = persTex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SurfaceRenderer] Failed to load textures: {ex.Message}");
            }
            finally
            {
                _texturesLoading = false;
            }
        }

        private void LateUpdate()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;
            if (MapManager.Instance == null) return;

            int worldHeight = MapManager.Instance.WorldHeight;
            float camX = _mainCamera.transform.position.x;
            float halfScreenW = _mainCamera.orthographicSize * _mainCamera.aspect;

            float left = camX - halfScreenW;
            float right = camX + halfScreenW;
            float baseY = worldHeight;

            UpdateTransit(left, right, baseY, camX);
            UpdatePerspective(left, right, baseY, camX);
        }

        private void UpdateTransit(float left, float right, float baseY, float camX)
        {
            float uLeft = -(left - Mathf.Floor(left / TILE_SIZE) * TILE_SIZE) / TILE_SIZE;
            float uRight = uLeft + (left - right) / TILE_SIZE;

            _uvTransit[0] = new Vector2(uLeft, 0f);
            _uvTransit[1] = new Vector2(uLeft, 1f);
            _uvTransit[2] = new Vector2(uRight, 0f);
            _uvTransit[3] = new Vector2(uRight, 1f);

            _verticesTransit[0] = new Vector3(left, baseY, 0f);
            _verticesTransit[1] = new Vector3(left, baseY + TRANSIT_HEIGHT, 0f);
            _verticesTransit[2] = new Vector3(right, baseY, 0f);
            _verticesTransit[3] = new Vector3(right, baseY + TRANSIT_HEIGHT, 0f);

            _transitMesh.vertices = _verticesTransit;
            _transitMesh.uv = _uvTransit;
            _transitMesh.bounds = new Bounds(new Vector3(camX, baseY + 1f, 0f), new Vector3(100f, 100f, 10f));
        }

        private void UpdatePerspective(float left, float right, float baseY, float camX)
        {
            const float persTileSize = 5f;
            float uLeft = -(left - Mathf.Floor(left / persTileSize) * persTileSize) / persTileSize;
            float uRight = uLeft + (left - right) / persTileSize;

            float uMid = 0.5f * (uLeft + uRight);
            float uWidth = uRight - uLeft;
            float persLeft = uMid - 0.5f * uWidth;
            float persRight = uMid + 0.5f * uWidth;

            _uvPers[0] = new Vector2(persLeft, 0f);
            _uvPers[1] = new Vector2(persLeft, 1f);
            _uvPers[2] = new Vector2(persRight, 0f);
            _uvPers[3] = new Vector2(persRight, 1f);

            _verticesPers[0] = new Vector3(left, baseY + TRANSIT_HEIGHT, 0f);
            _verticesPers[1] = new Vector3(left, baseY + TRANSIT_HEIGHT + PERSPECTIVE_HEIGHT, 0f);
            _verticesPers[2] = new Vector3(right, baseY + TRANSIT_HEIGHT, 0f);
            _verticesPers[3] = new Vector3(right, baseY + TRANSIT_HEIGHT + PERSPECTIVE_HEIGHT, 0f);

            _perspectiveMesh.vertices = _verticesPers;
            _perspectiveMesh.uv = _uvPers;
            _perspectiveMesh.bounds = new Bounds(new Vector3(camX, baseY + 3f, 0f), new Vector3(100f, 100f, 10f));
        }
    }
}
