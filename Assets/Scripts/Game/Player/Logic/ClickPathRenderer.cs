#nullable enable

using System.Collections.Generic;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using UnityEngine;

namespace Kern.Player.Logic
{
    // Пунктирная лаймовая линия клик-маршрута: точки-квадратики в центрах клеток
    // маршрута, цель - крупный маркер. Все точки - один общий меш, который
    // перерисовывается при смене маршрута.
    public class ClickPathRenderer : MonoBehaviour
    {
        private const float DotSize = 0.3f;
        private const float TargetSize = 0.55f;
        // Камера смотрит вдоль +Z: чуть меньший Z = чуть ближе к камере,
        // точки гарантированно рисуются поверх тайлов местности.
        private const float ZOffset = -0.05f;

        private GameObject? _view;
        private Mesh? _mesh;
        private readonly List<Vector3> _vertices = new();
        private readonly List<int> _triangles = new();

        private Material? _material;

        // Вью линии создаётся при первом показе маршрута через фабрику объектов
        // сцены: компонент вешается на игрока в рантайме, до инъекции.
        public void EnsureView(ISceneObjectFactory sceneObjects)
        {
            if (_view != null)
            {
                return;
            }

            _view = sceneObjects.Create("ClickPathView");
            _view.transform.position = Vector3.zero; // вершины меша - в мировых координатах

            MeshFilter filter = _view.AddComponent<MeshFilter>();
            _mesh = new Mesh { name = "ClickPathMesh" };
            _mesh.MarkDynamic();
            filter.sharedMesh = _mesh;

            MeshRenderer meshRenderer = _view.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _material = new Material(shader)
                {
                    color = new Color(0.55f, 1f, 0.35f, 0.9f), // лаймовый
                };
                meshRenderer.sharedMaterial = _material;
            }

            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            _view.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
            }

            if (_material != null)
            {
                Destroy(_material);
            }

            if (_view != null)
            {
                Destroy(_view);
            }
        }

        // path - полный маршрут в серверных координатах (без текущей клетки
        // робота); startIndex - первая ещё не пройденная клетка: всё до неё робот
        // уже прошёл, и на линии эти точки не рисуются. null/пустой остаток
        // прячет линию.
        public void Show(IReadOnlyList<Vector2Int>? path, int startIndex, IMapDataProvider map, float z)
        {
            if (_view == null || _mesh == null || map == null ||
                path == null || startIndex >= path.Count)
            {
                Hide();
                return;
            }

            int height = map.WorldHeight;
            float viewZ = z + ZOffset;
            _vertices.Clear();
            _triangles.Clear();

            for (int i = startIndex; i < path.Count; i++)
            {
                Vector2Int cell = path[i];
                Vector3 center = CoordinateUtils.ServerToUnityPos(cell.x, cell.y, height, viewZ);
                float half = (i == path.Count - 1 ? TargetSize : DotSize) * 0.5f;

                int baseIndex = _vertices.Count;
                _vertices.Add(new Vector3(center.x - half, center.y - half, viewZ));
                _vertices.Add(new Vector3(center.x + half, center.y - half, viewZ));
                _vertices.Add(new Vector3(center.x + half, center.y + half, viewZ));
                _vertices.Add(new Vector3(center.x - half, center.y + half, viewZ));

                _triangles.Add(baseIndex);
                _triangles.Add(baseIndex + 1);
                _triangles.Add(baseIndex + 2);
                _triangles.Add(baseIndex);
                _triangles.Add(baseIndex + 2);
                _triangles.Add(baseIndex + 3);
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0);

            // Границы считаем сами: после Clear пустой bounds отсечёт меш.
            Bounds bounds = new Bounds(_vertices[0], Vector3.zero);
            for (int i = 1; i < _vertices.Count; i++)
            {
                bounds.Encapsulate(_vertices[i]);
            }

            bounds.Expand(1f);
            _mesh.bounds = bounds;

            _view.SetActive(true);
        }

        public void Hide()
        {
            if (_view != null)
            {
                _view.SetActive(false);
            }
        }
    }
}
