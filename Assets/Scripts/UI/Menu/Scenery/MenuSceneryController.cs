#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    [ExecuteAlways]
    public class MenuSceneryController : MonoBehaviour
    {
        private const string ResolveShaderName = "Fodinae/UI/UnpremultiplyAlpha";

        private Camera? _sceneryCamera;
        private OrbitalStationMotion? _station;
        private Transform? _planet;
        private Transform? _occluder;

        /// <summary>
        /// На сколько пикселей должен измениться размер, чтобы имело смысл
        /// пересоздавать текстуры.
        /// </summary>
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

        /// <summary>
        /// Действующий риг задника меню.
        ///
        /// Раньше потребители искали его опросом FindAnyObjectByType раз в
        /// секунду. Если первая попытка приходилась на момент, когда сцена ещё
        /// грузится, планета появлялась на секунду позже всего остального —
        /// ровно на длину интервала опроса. Риг заявляет о себе сам, и ждать
        /// больше нечего.
        /// </summary>
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

                if (_sceneryCamera != null)
                {
                    _sceneryCamera.targetTexture = _cameraTarget;
                    _sceneryCamera.ResetAspect();
                    _sceneryCamera.ResetProjectionMatrix();

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
            if (!EnsureInitialized() || _sceneryCamera == null)
            {
                return;
            }

            _sceneryCamera.allowHDR = false;
            _sceneryCamera.fieldOfView = MenuSceneryFraming.FieldOfView;
            _sceneryCamera.ResetAspect();
            _sceneryCamera.ResetProjectionMatrix();
            SetDescentFraming(0f, Vector3.back);
            RenderNow();
        }

        /// <summary>
        /// Принудительно обновляет статичный offscreen-кадр.
        /// </summary>
        public void RenderNow()
        {
            if (!EnsureInitialized() || _sceneryCamera == null || _cameraTarget == null)
            {
                return;
            }

            _sceneryCamera.targetTexture = _cameraTarget;
            _sceneryCamera.Render();
            ResolveOutput();
            _renderDirty = false;
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

        private bool EnsureInitialized()
        {
            _sceneryCamera ??= GetComponentInChildren<Camera>(includeInactive: true);
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
            if (_sceneryCamera == null)
            {
                return false;
            }

            EnsureTargets();
            EnsureResolveMaterial();
            return true;
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

        private void OnDisable()
        {
            if (_sceneryCamera != null)
            {
                _sceneryCamera.targetTexture = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);

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

        /// <summary>
        /// Освобождает текстуру, предварительно отцепив её от камеры.
        ///
        /// Порядок значим. Уничтожение RenderTexture, которая ещё назначена в
        /// Camera.targetTexture, даёт «Releasing render texture that is set as
        /// Camera.targetTexture!» со стеком на каждое изменение размера окна:
        /// камера остаётся с висячей ссылкой, и Unity вынуждена чинить это за
        /// нас. Метод перестал быть статическим именно ради доступа к камере.
        /// </summary>
        private void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            if (_sceneryCamera != null && ReferenceEquals(_sceneryCamera.targetTexture, texture))
            {
                _sceneryCamera.targetTexture = null;
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

        /// <summary>
        /// Кадрирование спуска: камера подъезжает от обзорной точки к точке
        /// высадки. Параметр — доля пройденной загрузки, 0 = обзор, 1 = вплотную.
        ///
        /// Планету при этом никто не вращает: точка высадки закреплена за
        /// поверхностью, и разворачивать шар под камеру означало бы, что метка
        /// на поверхности переезжает вместе с ним. Двигается камера — как и
        /// должно быть при подлёте.
        /// </summary>
        public void SetDescentFraming(float progress, Vector3 landingLocalDirection)
        {
            if (_sceneryCamera == null)
            {
                return;
            }

            _framingProgress = Mathf.Clamp01(progress);
            _framingDirection = landingLocalDirection;

            // Радиус берётся из сцены, а не задаётся числом: масштаб шара уже
            // менялся, и зашитая дистанция однажды окажется внутри поверхности.
            float planetRadius = _planet != null ? 0.5f * _planet.lossyScale.x : 1f;

            MenuSceneryFraming.Placement placement = MenuSceneryFraming.Solve(
                _framingProgress,
                landingLocalDirection,
                planetRadius,
                Mathf.Max(_sceneryCamera.aspect, 0.1f));

            _sceneryCamera.transform.localPosition = placement.LocalPosition;
            _sceneryCamera.transform.localRotation = placement.LocalRotation;

            // Зум задаётся только дистанцией, а не сужением FOV: макет
            // увеличивает планету scale(1.18), оставляя угол обзора прежним.
            // Сужение FOV добавляло лишний ~1.21x и ломало пропорции спуска.
            _sceneryCamera.ResetProjectionMatrix();
            _renderDirty = true;
        }

        /// <summary>Возвращает камеру в обзорное положение меню.</summary>
        public void ResetFraming() => SetDescentFraming(0f, Vector3.back);

        // Reports the orbiting station's on-screen position as a 0..1 viewport
        // fraction (origin bottom-left, matching Camera.WorldToViewportPoint),
        // so UI Toolkit callers can convert it into their own panel space.
        //
        // Returns false while the station is not actually visible, so a label
        // anchored to it can be hidden rather than left hovering over the disc
        // with nothing underneath.
        public bool TryGetStationViewportPosition(out Vector2 viewportPosition)
        {
            return MenuSceneryProjection.TryGetStationViewportPosition(
                _sceneryCamera,
                _station,
                _occluder,
                out viewportPosition);
        }

        /// <summary>
        /// Calculates the on-screen viewport position for a fixed point along the orbital ring.
        /// </summary>
        public bool TryGetOrbitPointViewportPosition(float angleDegrees, out Vector2 viewportPosition)
        {
            Transform centerTransform = _planet != null ? _planet : transform;
            return MenuSceneryProjection.TryGetOrbitPointViewportPosition(
                _sceneryCamera,
                centerTransform,
                angleDegrees,
                out viewportPosition);
        }

        /// <summary>
        /// Calculates the on-screen viewport position for a fixed landing point on the planet's surface.
        /// </summary>
        public bool TryGetPlanetSurfaceViewportPosition(Vector3 localSurfaceDir, out Vector2 viewportPosition)
        {
            return MenuSceneryProjection.TryGetSurfaceViewportPosition(
                _sceneryCamera,
                _planet,
                localSurfaceDir,
                out viewportPosition);
        }
    }
}
