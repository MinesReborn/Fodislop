#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.UI
{
    [ExecuteAlways]
    public class MenuSceneryController : MonoBehaviour
    {
        private const string ResolveShaderName = "Kern/UI/UnpremultiplyAlpha";

        private static readonly int _worldSpaceCameraPosID = Shader.PropertyToID("_WorldSpaceCameraPos");

        private CommandBuffer? _commandBuffer;
        private readonly List<(Mesh Mesh, Matrix4x4 Matrix, Material Material, int SubMesh, float Distance)> _draws = new();
        private OrbitRingRenderer? _ring;
        private bool _initialized;
        private OrbitalStationMotion? _station;
        private Transform? _planet;
        private Transform? _occluder;

        // Потолок стороны offscreen-кадра.
        //
        // 1024 давало мыло: вызывающий передаёт сюда уже физические пиксели
        // (умноженные на scaledPixelsPerPoint), а на Retina планета занимает
        // втрое больше, и кадр растягивался на элемент.
        //
        // Дорог был не размер, а мультисэмплинг поверх него: из 138 МБ той
        // версии 89 приходилось на MSAA 4x. Без него 3072 стоит 49 МБ, и кадр
        // при этом статичен — он пересчитывается на изменение размера, а не
        // каждый кадр. Опускать разрешение ради экономии, которой нет, значит
        // возвращать мыло: элемент на Retina шире 3000 физических пикселей, и
        // всё, что меньше, растягивается.
        private const int MaxTargetSize = 3072;

        private int _targetWidth = 1024;
        private int _targetHeight = 1024;

        private RenderTexture? _cameraTarget;
        private RenderTexture? _outputTexture;

        [SerializeField]
        private Material? _resolveMaterialAsset;

        private Material? _resolveMaterial;
        private bool _ownsResolveMaterial;
        private bool _renderDirty = true;

        // Последнее заданное кадрирование. Хранится, потому что его нужно уметь
        // пересчитать: угол отворота камеры выводится из соотношения сторон
        // кадра, а оно меняется при каждом пересоздании текстуры.
        private float _framingProgress;
        private Vector3 _framingDirection = Vector3.back;
        private MenuSceneryFraming.Placement _placement =
            new(Vector3.zero, Quaternion.identity);

        // Поза задаётся в пространстве рига, как раньше у дочерней камеры.
        private MenuSceneryViewpoint Viewpoint => new(
            transform.TransformPoint(_placement.LocalPosition),
            transform.rotation * _placement.LocalRotation,
            _targetWidth / (float)_targetHeight);

        public RenderTexture? OutputTexture => _outputTexture;

        public void SetDisplaySize(int width, int height)
        {
            int w = Mathf.Max(width, MenuSceneryDefaults.MinimumRenderTextureSide);
            int h = Mathf.Max(height, MenuSceneryDefaults.MinimumRenderTextureSide);

            float scale = Mathf.Min(1f, MaxTargetSize / (float)Mathf.Max(w, h));
            w = Mathf.Max(MenuSceneryDefaults.MinimumRenderTextureSide, Mathf.RoundToInt(w * scale));
            h = Mathf.Max(MenuSceneryDefaults.MinimumRenderTextureSide, Mathf.RoundToInt(h * scale));

            // Пересоздание пары RenderTexture — не бесплатная операция, а
            // размер приходит сюда из Update каждый кадр и дрожит на пиксель
            // от округлений раскладки. Точное сравнение размеров означало бы
            // перезалив на каждое такое дрожание: просадка кадра и пустая
            // планета до ближайшей отрисовки. Порог убирает это, оставаясь
            // много меньше видимой разницы в чёткости.
            if (_cameraTarget != null &&
                Mathf.Abs(_cameraTarget.width - w) <= MenuSceneryDefaults.RenderTextureResizeThresholdPixels &&
                Mathf.Abs(_cameraTarget.height - h) <= MenuSceneryDefaults.RenderTextureResizeThresholdPixels)
            {
                return;
            }

            _targetWidth = w;
            _targetHeight = h;

            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);

            EnsureTargets();

            // Свежая текстура пуста до ближайшего LateUpdate. Рисуем сразу,
            // но больше не перерисовываем статичный фон каждый кадр.
            RenderNow();
        }

        private void EnsureTargets()
        {
            if (_cameraTarget == null)
            {
                _cameraTarget = new RenderTexture(_targetWidth, _targetHeight, 16, RenderTextureFormat.ARGB32)
                {
                    name = "MenuSceneryRT_Premultiplied",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,

                    // MSAA здесь не нужен совсем.
                    //
                    // Сглаживание уже делает FXAA в блите разрешения
                    // (UnpremultiplyAlpha.shader) — причём по
                    // премультиплицированной RGBA, то есть вместе с альфой, так
                    // что силуэт получает частичное покрытие, ради которого
                    // MSAA обычно и держат. Мультисэмплинг поверх этого
                    // умножал бы всю площадь кадра на число выборок ради
                    // единственной дуги, которую уже сгладили дешевле.
                    antiAliasing = 1,
                };
                _cameraTarget.Create();

                if (_initialized)
                {
                    // Кадрирование пересчитывается ОБЯЗАТЕЛЬНО.
                    //
                    // Отворот камеры считается из соотношения сторон кадра, а
                    // здесь оно только что изменилось. В OnEnable текстура
                    // создаётся размером 512×512, то есть с аспектом 1.0, и
                    // угол выходит 13.9° вместо нужных 22.8° для 16:9. Без
                    // пересчёта планета оставалась стоять по углу для квадрата,
                    // и её положение зависело от того, успел ли кадр
                    // пересоздаться, — то есть выглядело случайным.
                    SetDescentFraming(_framingProgress, _framingDirection);
                }
            }

            if (_outputTexture == null)
            {
                _outputTexture = new RenderTexture(_targetWidth, _targetHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "MenuSceneryRT",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    autoGenerateMips = false,
                    anisoLevel = 0,
                };
                _outputTexture.Create();
            }
        }

        private void OnEnable()
        {
            EnsureInitialized();
            SetDescentFraming(0f, Vector3.back);
            RenderNow();
        }

        public void RenderNow()
        {
            EnsureInitialized();
            if (_cameraTarget == null)
            {
                return;
            }

            RenderScenery(_cameraTarget, Viewpoint);
            ResolveOutput();
            _renderDirty = false;
        }

        // То, что раньше делала камера рига: очистка в прозрачный, непрозрачное
        // спереди назад по очереди, прозрачное сзади вперёд по дальности.
        private void RenderScenery(RenderTexture target, MenuSceneryViewpoint viewpoint)
        {
            _draws.Clear();
            foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>())
            {
                if (!meshRenderer.enabled ||
                    !meshRenderer.TryGetComponent(out MeshFilter filter) ||
                    filter.sharedMesh == null)
                {
                    continue;
                }

                Material[] materials = meshRenderer.sharedMaterials;
                float distance = (meshRenderer.bounds.center - viewpoint.Position).sqrMagnitude;
                for (int subMesh = 0; subMesh < Mathf.Min(materials.Length, filter.sharedMesh.subMeshCount); subMesh++)
                {
                    if (materials[subMesh] != null)
                    {
                        _draws.Add((filter.sharedMesh, meshRenderer.localToWorldMatrix, materials[subMesh], subMesh, distance));
                    }
                }
            }

            if (_ring != null && _ring.isActiveAndEnabled && _ring.Material != null)
            {
                Mesh? ringMesh = _ring.BuildFacing(viewpoint.Position);
                if (ringMesh != null)
                {
                    float distance = (ringMesh.bounds.center - viewpoint.Position).sqrMagnitude;
                    _draws.Add((ringMesh, Matrix4x4.identity, _ring.Material, 0, distance));
                }
            }

            _draws.Sort(static (a, b) =>
            {
                int queue = a.Material.renderQueue.CompareTo(b.Material.renderQueue);
                if (queue != 0)
                {
                    return queue;
                }

                bool transparent = a.Material.renderQueue > (int)RenderQueue.GeometryLast;
                return transparent ? b.Distance.CompareTo(a.Distance) : a.Distance.CompareTo(b.Distance);
            });

            _commandBuffer ??= new CommandBuffer { name = "Kern.MenuScenery" };
            _commandBuffer.Clear();
            _commandBuffer.SetRenderTarget(target);
            _commandBuffer.ClearRenderTarget(clearDepth: true, clearColor: true, backgroundColor: Color.clear);
            _commandBuffer.SetViewProjectionMatrices(
                viewpoint.WorldToView,
                GL.GetGPUProjectionMatrix(viewpoint.Projection, renderIntoTexture: true));
            _commandBuffer.SetGlobalVector(_worldSpaceCameraPosID, viewpoint.Position);
            foreach ((Mesh mesh, Matrix4x4 matrix, Material material, int subMesh, _) in _draws)
            {
                _commandBuffer.DrawMesh(mesh, matrix, material, subMesh, 0);
            }

            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _draws.Clear();
        }

        public void ResolveOutput()
        {
            EnsureTargets();
            if (_cameraTarget == null || _outputTexture == null || _resolveMaterial == null)
            {
                return;
            }

            Graphics.Blit(_cameraTarget, _outputTexture, _resolveMaterial);
        }

        private void LateUpdate()
        {
            if (_renderDirty)
            {
                RenderNow();
            }
        }

        private void EnsureInitialized()
        {
            _ring ??= GetComponentInChildren<OrbitRingRenderer>(includeInactive: true);
            _station ??= GetComponentInChildren<OrbitalStationMotion>(includeInactive: true);
            _planet ??= transform.Find("PlanetSurface");

            if (_planet != null)
            {
                _planet.localPosition = Vector3.zero;
            }

            Transform? atmosphere = transform.Find("PlanetAtmosphere");
            if (atmosphere != null)
            {
                atmosphere.localPosition = Vector3.zero;
            }

            _occluder = _planet;
            _initialized = true;
            EnsureTargets();
            EnsureResolveMaterial();
        }

        private void EnsureResolveMaterial()
        {
            if (_resolveMaterial != null)
            {
                return;
            }

            if (_resolveMaterialAsset != null)
            {
                _resolveMaterial = _resolveMaterialAsset;
                return;
            }

            Shader? resolve = Shader.Find(ResolveShaderName);
            if (resolve == null)
            {
                Debug.LogWarning(
                    $"[MenuSceneryController] Resolve shader '{ResolveShaderName}' is unavailable; " +
                    "scenery compositing is disabled.");
                return;
            }

            _resolveMaterial = new Material(resolve) { hideFlags = HideFlags.HideAndDontSave };
            _ownsResolveMaterial = true;
        }

        private void OnDestroy()
        {
            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);
            _commandBuffer?.Release();
            _commandBuffer = null;

            // Only destroy the fallback instance this component created; the
            // serialized asset must not be destroyed.
            if (_resolveMaterial != null && _ownsResolveMaterial)
            {
                if (Application.isPlaying)
                {
                    Destroy(_resolveMaterial);
                }
                else
                {
                    DestroyImmediate(_resolveMaterial);
                }
            }

            _resolveMaterial = null;
            _ownsResolveMaterial = false;
        }

        private void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            if (ReferenceEquals(RenderTexture.active, texture))
            {
                RenderTexture.active = null;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }

            texture = null;
        }

        public void SetDescentFraming(float progress, Vector3 landingLocalDirection)
        {
            _framingProgress = Mathf.Clamp01(progress);
            _framingDirection = landingLocalDirection;

            // Радиус берётся из сцены, а не задаётся числом: масштаб шара уже
            // менялся, и зашитая дистанция однажды окажется внутри поверхности.
            float planetRadius = _planet != null ? 0.5f * _planet.lossyScale.x : 1f;

            MenuSceneryFraming.Placement placement = MenuSceneryFraming.Solve(
                _framingProgress,
                landingLocalDirection,
                planetRadius,
                _targetWidth / (float)_targetHeight);

            // Зум задаётся только дистанцией, а не сужением FOV: макет
            // увеличивает планету scale(1.18), оставляя угол обзора прежним.
            // Сужение FOV добавляло лишний ~1.21x и ломало пропорции спуска.
            _placement = placement;
            _renderDirty = true;
        }
        // Reports the orbiting station's on-screen position as a 0..1 viewport
        // fraction (origin bottom-left, matching MenuSceneryViewpoint.WorldToViewport),
        // so UI Toolkit callers can convert it into their own panel space.
        //
        // Returns false while the station is not actually visible, so a label
        // anchored to it can be hidden rather than left hovering over the disc
        // with nothing underneath.
        public bool TryGetStationViewportPosition(out Vector2 viewportPosition)
        {
            return MenuSceneryProjection.TryGetStationViewportPosition(
                Viewpoint,
                _station,
                _occluder,
                out viewportPosition);
        }

        public bool TryGetOrbitPointViewportPosition(float angleDegrees, out Vector2 viewportPosition)
        {
            Transform centerTransform = _planet != null ? _planet : transform;
            return MenuSceneryProjection.TryGetOrbitPointViewportPosition(
                Viewpoint,
                centerTransform,
                angleDegrees,
                out viewportPosition);
        }

        public bool TryGetPlanetSurfaceViewportPosition(Vector3 localSurfaceDir, out Vector2 viewportPosition)
        {
            return MenuSceneryProjection.TryGetSurfaceViewportPosition(
                Viewpoint,
                _planet,
                localSurfaceDir,
                out viewportPosition);
        }
    }
}
