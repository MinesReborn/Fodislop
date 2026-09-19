#nullable enable

using UnityEngine;

namespace Kern.UI.Backgrounds
{
    [ExecuteAlways]
    [AddComponentMenu("Kern/UI/Backgrounds/Fractal Background Quad")]
    public sealed class FractalBackgroundQuad : MonoBehaviour
    {
        [SerializeField] private Material? _material;
        [SerializeField] private float _speed = 1.0f;

        private Mesh? _mesh;
        private Material? _runtimeMaterial;

        private void OnEnable()
        {
            if (_material != null)
            {
                _runtimeMaterial = new Material(_material);
            }

            CreateFullscreenQuad();
            UpdateMaterialProperties();
        }

        private void Update()
        {
            if (_runtimeMaterial != null)
            {
                _runtimeMaterial.SetFloat("_Speed", _speed);
            }
        }

        private void OnDisable()
        {
            DestroyMesh();
            if (_runtimeMaterial != null)
            {
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }
        }

        private void OnRenderObject()
        {
            if (_runtimeMaterial == null || _mesh == null)
                return;

            _runtimeMaterial.SetPass(0);
            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
        }

        private void CreateFullscreenQuad()
        {
            _mesh = new Mesh
            {
                name = "FractalBackgroundQuad",
                hideFlags = HideFlags.HideAndDontSave
            };

            _mesh.SetVertices(new Vector3[] {
                new(-1f, -1f, 0f),
                new(1f, -1f, 0f),
                new(-1f, 1f, 0f),
                new(1f, 1f, 0f),
            });

            _mesh.SetUVs(0, new Vector2[] {
                new(0f, 0f),
                new(1f, 0f),
                new(0f, 1f),
                new(1f, 1f),
            });

            _mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            _mesh.RecalculateBounds();
        }

        private void DestroyMesh()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }
        }

        private void UpdateMaterialProperties()
        {
            if (_runtimeMaterial != null)
            {
                _runtimeMaterial.SetFloat("_Speed", _speed);
            }
        }

        private static new void Destroy(Object obj)
        {
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}
