#nullable enable

using System;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI.HUD.Player.Model;
using Kern.World;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class MissionArrowUI : MonoBehaviour
    {
        // Кольцо виртуальное: сама окружность не рисуется, это только линия,
        // по которой ставится указатель.
        //
        // Радиус — доля МЕНЬШЕЙ стороны вьюпорта, а не расстояние в мире.
        // Раньше он задавался в пикселях сетки и прогонялся через камеру, и
        // кольцо вело себя как объект террейна: росло и сжималось с зумом,
        // держалось за мировые единицы. Это указатель интерфейса, у него на
        // экране всегда одно место и один размер при любом зуме.
        //
        // Меньшая сторона, а не большая: иначе на широком экране кольцо
        // вылезало бы за верх и низ кадра.
        // Размер и положение теперь меняются только со сменой размера
        // вьюпорта, но писать стили каждый кадр всё равно нельзя: запись
        // держит панель в состоянии style-dirty. Ниже полпикселя разницы не
        // видно, поэтому такая запись пропускается.
        private const float PositionWriteEpsilon = 0.5f;

        [Inject] private UIDocument _doc = null!;
        [Inject] private PlayerStatsModel _playerStats = null!;
        [Inject] private MapManager _mapManager = null!;
        [Inject] private ILocalPlayerState _localPlayerState = null!;

        private MissionRingVisual? _ring;
        private VisualElement? _layoutRoot;
        private ushort? _targetX;
        private ushort? _targetY;
        private bool _initialized;
        private bool _layoutSubscriptionActive;

        // Флаг, а не NaN-часовой. Сравнение с NaN ложно в обе стороны, поэтому
        // условие `Abs(value - NaN) > epsilon` не выполняется никогда: с NaN в
        // качестве начального значения первая запись стиля не происходила
        // вовсе, и кольцо оставалось в месте, которое ему выдала раскладка, —
        // в левом верхнем углу HUD и размером с текстуру. Тем же флагом
        // пользуется WorldLabels.
        private bool _hasAppliedLayout;
        private float _lastAppliedLeft;
        private float _lastAppliedTop;
        private float _lastAppliedSize;

        protected void Start()
        {
            // Школа (одна дорога): зарегистрированные вьюхи инжектятся при
            // сборке scope (фаза Awake), панель UIDocument создаётся в OnEnable —
            // к Start и зависимости, и панель гарантированы. Один вызов, без
            // ретраев из Update.
            TryInitialize();
        }

        private void TryInitialize()
        {
            if (_initialized)
            {
                return;
            }

            // [Inject]-поля гарантируют зависимости и панель UIDocument к
            // моменту вызова; null здесь — дефект проводки, а не гонка.
            // Молчаливый пропуск оставил бы кольцо миссии вечно невидимым
            // без ошибки.
            if (_doc == null || _doc.rootVisualElement == null || _playerStats == null ||
                _mapManager == null || _localPlayerState == null)
            {
                throw new InvalidOperationException(
                    "[MissionArrowUI] Required injection missing: " +
                    $"{(_doc == null ? "UIDocument" : _playerStats == null ? "PlayerStatsModel" : _mapManager == null ? "MapManager" : _localPlayerState == null ? "ILocalPlayerState" : "UIDocument root")}. " +
                    "MissionArrowUI must be registered in the Game scope before Start.");
            }

            VisualElement root = _doc.rootVisualElement;
            VisualElement? layoutRoot = root.Q("PlayerHUDRoot");
            if (layoutRoot == null)
            {
                if (!_layoutSubscriptionActive)
                {
                    root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged, TrickleDown.TrickleDown);
                    _layoutSubscriptionActive = true;
                }

                return;
            }

            _layoutRoot = layoutRoot;

            if (_layoutSubscriptionActive)
            {
                root.UnregisterCallback<GeometryChangedEvent>(
                    OnRootGeometryChanged,
                    TrickleDown.TrickleDown);
                _layoutSubscriptionActive = false;
            }

            _ring = new MissionRingVisual
            {
                name = "MissionRing",
                pickingMode = PickingMode.Ignore,
            };
            _ring.AddToClassList("mission-ring");

            _layoutRoot.Insert(0, _ring);
            UIState.Hide(_ring);

            _playerStats.OnMissionChanged += OnMissionChanged;
            _playerStats.OnMissionArrowChanged += OnMissionArrowChanged;
            _targetX = _playerStats.MissionArrowX;
            _targetY = _playerStats.MissionArrowY;
            _initialized = true;
        }

        private void OnRootGeometryChanged(GeometryChangedEvent _)
        {
            // Событие геометрии приходит на каждую перекладку корня, и после
            // первой удачной сборки выходить отсюда — норма, а не сбой. Пока
            // панель не готова, попытка просто повторится со следующим
            // событием: подписка снимается только в Dispose.
            if (_initialized || _doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            TryInitialize();
        }

        protected void OnDestroy()
        {
            if (_layoutSubscriptionActive && _doc != null && _doc.rootVisualElement != null)
            {
                _doc.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(
                    OnRootGeometryChanged,
                    TrickleDown.TrickleDown);
                _layoutSubscriptionActive = false;
            }

            if (_playerStats != null)
            {
                _playerStats.OnMissionChanged -= OnMissionChanged;
                _playerStats.OnMissionArrowChanged -= OnMissionArrowChanged;
            }

            _ring?.RemoveFromHierarchy();
            _ring?.Dispose();
            _ring = null;
            _layoutRoot = null;
        }

        private void OnMissionChanged()
        {
            if (!_initialized || _playerStats == null)
            {
                return;
            }

            if (!_playerStats.IsMissionActive)
            {
                UIState.Hide(_ring);
            }
        }

        private void OnMissionArrowChanged()
        {
            if (!_initialized || _playerStats == null)
            {
                return;
            }

            _targetX = _playerStats.MissionArrowX;
            _targetY = _playerStats.MissionArrowY;

            if (!_targetX.HasValue || !_targetY.HasValue)
            {
                UIState.Hide(_ring);
            }
        }

        protected void LateUpdate()
        {
            if (!_initialized || _ring == null || _layoutRoot == null ||
                _doc == null || _doc.rootVisualElement == null)
            {
                return;
            }

            if (!_playerStats.IsMissionActive || !_targetX.HasValue || !_targetY.HasValue)
            {
                UIState.Hide(_ring);
                return;
            }

            // During reconnect the player state can be restored before the
            // WorldInit packet repopulates MapManager. Coordinates are not
            // convertible until the authoritative world height exists; keep
            // the arrow hidden for that transient state instead of calling
            // CoordinateUtils with an invalid dimension.
            if (_mapManager.WorldHeight <= 0)
            {
                UIState.Hide(_ring);
                return;
            }

            // Мир нужен ровно для одного — направления на цель. Положение и
            // размер кольца мир не спрашивают вовсе: это элемент экрана.
            //
            // Центр — центр вьюпорта. CameraFollow специально смещает игрока
            // относительно экрана, оставляя место под левую панель; если
            // центрировать кольцо по player.transform, оно уезжает вместе
            // с этим смещением.
            ILocalPlayer? player = _localPlayerState.Current;
            if (player == null || !player.isActiveAndEnabled || !player.IsGameplayVisible)
            {
                UIState.Hide(_ring);
                return;
            }

            Vector3 playerWorld = player.transform.position;
            Vector3 targetWorld = CoordinateUtils.ServerToUnityPos(
                _targetX.Value,
                _targetY.Value,
                _mapManager.WorldHeight);

            Vector2 toTarget = new(targetWorld.x - playerWorld.x, targetWorld.y - playerWorld.y);
            float distance = toTarget.magnitude;
            float opacity = Mathf.InverseLerp(
                MissionRingLook.CenterFadeDistance,
                MissionRingLook.FullOpacityDistance,
                distance);
            if (opacity <= MissionRingLook.MinimumVisibleOpacity)
            {
                UIState.Hide(_ring);
                return;
            }

            // Размер кольца — доля вьюпорта, и больше ничья. Ни камеры, ни
            // мировых единиц, ни зума в этой формуле нет.
            float viewportWidth = _layoutRoot.resolvedStyle.width;
            float viewportHeight = _layoutRoot.resolvedStyle.height;

            // До первой раскладки размеры приходят нулями или NaN. Сравнение с
            // NaN ложно в обе стороны, поэтому условие пишется через «годен»,
            // а не через «негоден»: иначе кадр с NaN проскочил бы проверку и
            // ушёл в стили.
            bool viewportResolved = viewportWidth > 1f && viewportHeight > 1f;
            if (!viewportResolved)
            {
                UIState.Hide(_ring);
                return;
            }

            float ringPanelRadius =
                Mathf.Min(viewportWidth, viewportHeight) * MissionRingLook.ViewportRadiusFraction;
            Vector2 centerLocal = new(viewportWidth * 0.5f, viewportHeight * 0.5f);

            // Элемент шире виртуального кольца ровно во столько, во сколько
            // кадр шейдера шире окружности, по которой он ставит указатель.
            float elementSize = ringPanelRadius * 2f / MissionRingLook.Radius;
            float left = centerLocal.x - (elementSize * 0.5f);
            float top = centerLocal.y - (elementSize * 0.5f);

            if (!_hasAppliedLayout || Mathf.Abs(left - _lastAppliedLeft) > PositionWriteEpsilon)
            {
                _ring.style.left = left;
                _lastAppliedLeft = left;
            }

            if (!_hasAppliedLayout || Mathf.Abs(top - _lastAppliedTop) > PositionWriteEpsilon)
            {
                _ring.style.top = top;
                _lastAppliedTop = top;
            }

            if (!_hasAppliedLayout || Mathf.Abs(elementSize - _lastAppliedSize) > PositionWriteEpsilon)
            {
                _ring.style.width = elementSize;
                _ring.style.height = elementSize;
                _lastAppliedSize = elementSize;
            }

            _hasAppliedLayout = true;

            // Пеленг — прямое мировое направление на цель, без разворотов.
            //
            // Шейдер считает угол в координатах СВОЕГО кадра (uv 0..1, ось Y
            // вверх), а не в координатах панели, и UI Toolkit показывает эту
            // текстуру той же стороной вверх. Поэтому переводить направление
            // в систему панели с Y вниз здесь нечего: прежний минус у y был
            // отражением по вертикали, а добавленный к нему пол-оборота
            // превращал отражение по вертикали в отражение по горизонтали —
            // указатель оставался развёрнутым, только уже по другой оси.
            float bearing = Mathf.Atan2(toTarget.y, toTarget.x);

            UIState.Show(_ring);
            _ring.Render(Mathf.Clamp01(opacity), bearing);
        }

        private sealed class MissionRingVisual : VisualElement, IDisposable
        {
            // Полградуса пеленга на кольце радиусом в сотню пикселей — это
            // меньше пикселя дуги, перерисовывать ради такого нечего.
            private const float BearingEpsilonDegrees = 0.5f;
            private const float OpacityEpsilon = 0.004f;

            private readonly Material _material;
            private readonly int _opacityId = Shader.PropertyToID("_MissionOpacity");
            private readonly int _angleId = Shader.PropertyToID("_MissionAngle");
            private readonly int _ringRadiusId = Shader.PropertyToID("_RingRadius");
            private readonly int _ringSquashId = Shader.PropertyToID("_RingSquash");
            private readonly int _arcHalfAngleId = Shader.PropertyToID("_ArcHalfAngle");
            private readonly int _arcThicknessId = Shader.PropertyToID("_ArcThickness");
            private readonly int _arcEndTaperId = Shader.PropertyToID("_ArcEndTaper");
            private readonly int _arcCoreWidthId = Shader.PropertyToID("_ArcCoreWidth");
            private readonly int _arcEnergyId = Shader.PropertyToID("_ArcEnergy");
            private readonly int _coreEnergyId = Shader.PropertyToID("_CoreEnergy");
            private readonly int _coreColorId = Shader.PropertyToID("_CoreColor");

            private float _lastOpacity = float.NaN;
            private float _lastBearing = float.NaN;

            public MissionRingVisual()
            {
                Shader? shader = Resources.Load<Shader>(
                    ProjectRuntimeContracts.ResourcePaths.MissionVirtualRingShader);
                if (shader == null || !shader.isSupported)
                {
                    throw new InvalidOperationException(
                        $"[MissionArrowUI] Required shader '{ProjectRuntimeContracts.ShaderNames.MissionVirtualRing}' is unavailable.");
                }

                _material = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _material.SetFloat(_ringRadiusId, MissionRingLook.Radius);
                _material.SetFloat(_ringSquashId, MissionRingLook.VerticalSquash);
                _material.SetFloat(_arcHalfAngleId, MissionRingLook.ArcHalfAngle);
                _material.SetFloat(_arcThicknessId, MissionRingLook.ArcThickness);
                _material.SetFloat(_arcEndTaperId, MissionRingLook.EndTaper);
                _material.SetFloat(_arcCoreWidthId, MissionRingLook.CoreWidth);
                _material.SetFloat(_arcEnergyId, MissionRingLook.ArcEnergy);
                _material.SetFloat(_coreEnergyId, MissionRingLook.CoreEnergy);
                _material.SetColor(_coreColorId, MissionRingLook.CoreColor);

                // Материал уходит прямо в стиль элемента. Ни RenderTexture,
                // ни Image здесь больше нет: кадр фиксированной стороны
                // растягивался на элемент фильтрацией и давал мыло, а так
                // дуга считается в разрешении самой панели.
                //
                // Геометрию элементу даёт фон: у прозрачного фона UI Toolkit
                // не выдаёт ни одного треугольника, и рисовать шейдеру было
                // бы нечего. Цвет фона задан в .mission-ring, шейдер берёт
                // из него только альфу (прозрачность элемента).
                style.unityMaterial = _material;
            }

            public void Render(float opacity, float bearing)
            {
                // Указатель неподвижен: пока пеленг и прозрачность те же,
                // новых чисел материалу давать нечего. Запись идёт только на
                // смену состояния, а не каждый кадр.
                if (Mathf.Abs(opacity - _lastOpacity) <= OpacityEpsilon &&
                    Mathf.Abs(Mathf.DeltaAngle(bearing * Mathf.Rad2Deg, _lastBearing * Mathf.Rad2Deg)) <= BearingEpsilonDegrees)
                {
                    return;
                }

                _lastOpacity = opacity;
                _lastBearing = bearing;
                _material.SetFloat(_opacityId, opacity);
                _material.SetFloat(_angleId, bearing);
                MarkDirtyRepaint();
            }

            public void Dispose()
            {
                style.unityMaterial = StyleKeyword.Null;
                UnityEngine.Object.Destroy(_material);
            }
        }
    }
}
