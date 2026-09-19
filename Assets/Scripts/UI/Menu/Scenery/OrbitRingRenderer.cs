#nullable enable

using UnityEngine;

namespace Kern.UI
{
    // A real 3D orbit ring drawn into the menu scenery target alongside the
    // planet, so it wraps around the sphere with proper depth occlusion (the far
    // side of the loop passes behind the planet) instead of a flat 2D ellipse
    // drawn on top of everything.
    //
    // Лента строится своим мешем, а не LineRenderer: тот разворачивает ленту к
    // рисующей камере, а риг меню камеры не имеет — MenuSceneryController рисует
    // его в текстуру сам и передаёт сюда точку обзора.
    public class OrbitRingRenderer : MonoBehaviour
    {
        [SerializeField]
        private Transform? _center;
        [SerializeField]
        private float _radius = MenuSceneryDefaults.OrbitRadius;
        [SerializeField]
        private Vector3 _orbitPlaneEulerAngles = MenuSceneryDefaults.OrbitPlaneEulerAngles;
        [SerializeField]
        private int _segments = 128;
        [SerializeField]
        private float _lineWidth = 0.03f;
        [SerializeField]
        private Material? _material;

        private Mesh? _mesh;
        private Vector3[] _points = System.Array.Empty<Vector3>();
        private Vector3[] _vertices = System.Array.Empty<Vector3>();

        public Material? Material => _material;

        // Лента в мировых координатах, развёрнутая шириной поперёк взгляда из
        // viewPosition — так же, как LineRenderer с выравниванием View.
        public Mesh? BuildFacing(Vector3 viewPosition)
        {
            if (_center == null || _segments < 3)
            {
                return null;
            }

            EnsureMesh();
            Quaternion orbitPlane = Quaternion.Euler(_orbitPlaneEulerAngles);
            for (int i = 0; i < _segments; i++)
            {
                float t = (float)i / _segments * Mathf.PI * 2f;
                var localOffset = new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * _radius;
                _points[i] = _center.position + (orbitPlane * localOffset);
            }

            float halfWidth = _lineWidth * 0.5f;
            for (int i = 0; i < _segments; i++)
            {
                Vector3 point = _points[i];
                Vector3 tangent = _points[(i + 1) % _segments] - _points[(i + _segments - 1) % _segments];
                Vector3 side = Vector3.Cross(tangent, viewPosition - point).normalized * halfWidth;
                _vertices[2 * i] = point - side;
                _vertices[(2 * i) + 1] = point + side;
            }

            _mesh!.vertices = _vertices;
            _mesh.RecalculateBounds();
            return _mesh;
        }

        private void EnsureMesh()
        {
            if (_mesh != null && _points.Length == _segments)
            {
                return;
            }

            _points = new Vector3[_segments];
            _vertices = new Vector3[_segments * 2];
            var colors = new Color32[_segments * 2];
            var indices = new int[_segments * 6];
            for (int i = 0; i < _segments; i++)
            {
                int next = (i + 1) % _segments;
                int k = i * 6;
                indices[k] = 2 * i;
                indices[k + 1] = (2 * next) + 1;
                indices[k + 2] = (2 * i) + 1;
                indices[k + 3] = 2 * i;
                indices[k + 4] = 2 * next;
                indices[k + 5] = (2 * next) + 1;
                colors[2 * i] = new Color32(255, 255, 255, 255);
                colors[(2 * i) + 1] = new Color32(255, 255, 255, 255);
            }

            ReleaseMesh();
            _mesh = new Mesh { name = "OrbitRing", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.colors32 = colors;
            _mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        }

        private void OnDestroy()
        {
            ReleaseMesh();
        }

        private void ReleaseMesh()
        {
            if (_mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }

            _mesh = null;
        }
    }
}
