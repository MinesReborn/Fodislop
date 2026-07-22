using UnityEngine;

namespace Fodinae.Scripts.World
{
    [ExecuteAlways]
    public class WorldBackgroundSetup : MonoBehaviour
    {
        [Header("Background Renderer Settings")]
        [SerializeField]
        private SingleMeshTerrainRenderer _backgroundRendererPrefab;
        [SerializeField]
        private Transform _backgroundParent;

        private SingleMeshTerrainRenderer _backgroundRenderer;

        protected void Awake()
        {
            SetupBackgroundRenderer();
        }

        protected void Update()
        {
            if (_backgroundRenderer != null)
            {
                EnsureBackgroundConfiguration();
            }
        }

        private void SetupBackgroundRenderer()
        {
            _backgroundRenderer = FindAnyObjectByType<SingleMeshTerrainRenderer>();

            if (_backgroundRenderer == null)
            {
                if (_backgroundRendererPrefab != null)
                {
                    _backgroundRenderer = Instantiate(_backgroundRendererPrefab, transform);
                    _backgroundRenderer.name = "SingleMeshTerrainRenderer";
                }
                else
                {
                    var backgroundGO = new GameObject("SingleMeshTerrainRenderer");
                    _backgroundRenderer = backgroundGO.AddComponent<SingleMeshTerrainRenderer>();

                    if (_backgroundRenderer.TryGetComponent<MeshRenderer>(out var meshRenderer))
                    {
                        meshRenderer.sortingOrder = -1000;
                    }

                    if (_backgroundRenderer.TryGetComponent<Transform>(out var transformComp))
                    {
                        transformComp.position = new Vector3(0, 0, 0); // FIX: Z=0
                    }
                }

                if (_backgroundParent != null)
                {
                    _backgroundRenderer.transform.SetParent(_backgroundParent);
                }
            }
        }

        private void EnsureBackgroundConfiguration()
        {
            if (_backgroundRenderer == null)
            {
                return;
            }

            var renderer = _backgroundRenderer.GetComponent<MeshRenderer>();
            var trans = _backgroundRenderer.transform;

            if (renderer != null && renderer.sortingOrder != -1000)
            {
                renderer.sortingOrder = -1000;
            }

            // FIX: Ensure it stays at Z=0 (visible), not Z=-10 (clipped/invisible)
            if (trans != null && trans.position.z != 0f)
            {
                var pos = trans.position;
                pos.z = 0f;
                trans.position = pos;
                Debug.Log("[WorldBackgroundSetup] Fixed Z position to 0 for visibility");
            }
        }

        public SingleMeshTerrainRenderer GetBackgroundRenderer()
        {
            return _backgroundRenderer;
        }
    }
}
