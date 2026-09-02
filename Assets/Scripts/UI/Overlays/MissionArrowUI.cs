#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using Fodinae.UI.HUD.Player.Model;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    public class MissionArrowUI : MonoBehaviour
    {
        [Inject]
        private UIDocument _doc = null!;
        private VisualElement? _arrow;
        private Camera? _camera;
        private ushort? _targetX;
        private ushort? _targetY;
        private bool _initialized;
        [Inject]
        private PlayerStatsModel _playerStats = null!;

        private const float PositionWriteEpsilon = 0.5f;
        private float _lastAppliedLeft = float.NaN;
        private float _lastAppliedTop = float.NaN;
        private float _lastAppliedRotate = float.NaN;
        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private IGameplayCamera _gameplayCamera = null!;

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

            // [Inject]-метод гарантирует зависимости и панель UIDocument к
            // моменту вызова; null здесь — дефект проводки, а не гонка.
            // Молчаливый пропуск оставил бы стрелку миссии вечно невидимой
            // без ошибки.
            if (_doc == null || _doc.rootVisualElement == null || _playerStats == null || _mapManager == null)
            {
                throw new InvalidOperationException(
                    "[MissionArrowUI] Required injection missing: " +
                    $"{(_doc == null ? "UIDocument" : _playerStats == null ? "IPlayerStats" : _mapManager == null ? "IMapDataProvider" : "UIDocument root")}. " +
                    "MissionArrowUI must be registered in the Game scope before Start.");
            }

            _camera = _gameplayCamera.Camera;

            _arrow = new VisualElement();
            _arrow.name = "MissionArrow";
            _arrow.AddToClassList("mission-arrow");

            // Видимость — рантайм-состояние. Вставляем в индекс 0: метка не должна
            // перекрывать текст UI (раньше добавлялась последней — рисовалась поверх).
            _arrow.style.display = DisplayStyle.None;
            // _doc is guarded by the throw above; the compiler cannot narrow it
            // across the conditional 'missing' expression, so null-forgive here.
            _doc!.rootVisualElement.Insert(0, _arrow);

            PlayerStatsModel stats = _playerStats;
            if (stats != null)
            {
                stats.OnMissionArrowChanged += OnArrowChanged;
                if (stats.MissionArrowX.HasValue && stats.MissionArrowY.HasValue)
                {
                    _targetX = stats.MissionArrowX;
                    _targetY = stats.MissionArrowY;
                    _arrow.style.display = DisplayStyle.Flex;
                }
            }

            _initialized = true;
        }

        protected void OnDestroy()
        {
            if (_playerStats != null)
            {
                _playerStats.OnMissionArrowChanged -= OnArrowChanged;
            }

            _arrow?.RemoveFromHierarchy();
            _arrow = null;
        }

        private void OnArrowChanged()
        {
            if (!isActiveAndEnabled || !_initialized || _arrow == null || _playerStats == null)
            {
                return;
            }

            PlayerStatsModel stats = _playerStats;
            if (!stats.MissionArrowX.HasValue || !stats.MissionArrowY.HasValue)
            {
                if (!_targetX.HasValue && !_targetY.HasValue &&
                    _arrow.style.display == DisplayStyle.None)
                {
                    return;
                }

                _targetX = null;
                _targetY = null;

                if (_arrow != null)
                {
                    _arrow.style.display = DisplayStyle.None;
                }

                return;
            }

            if (_targetX == stats.MissionArrowX && _targetY == stats.MissionArrowY &&
                _arrow.style.display == DisplayStyle.Flex)
            {
                return;
            }

            _targetX = stats.MissionArrowX;
            _targetY = stats.MissionArrowY;
            _lastAppliedLeft = float.NaN;
            _lastAppliedTop = float.NaN;
            _lastAppliedRotate = float.NaN;
            if (_arrow != null)
            {
                _arrow.style.display = DisplayStyle.Flex;
            }
        }

        protected void LateUpdate()
        {
            if (!_targetX.HasValue || !_targetY.HasValue || _camera == null)
            {
                return;
            }

            var worldPos = CoordinateUtils.ServerToUnityPos(
                _targetX.Value,
                _targetY.Value,
                _mapManager.WorldHeight);
            var screenPos = _camera.WorldToScreenPoint(worldPos);

            if (_doc == null || _doc.rootVisualElement == null || _doc.rootVisualElement.panel == null || _arrow == null)
            {
                // Per-frame: панель или стрелка могут отсутствовать в этом кадре —
                // на следующем кадре обновление повторится.
                return;
            }

            if (screenPos.z < 0f)
            {
                if (_arrow.style.display != DisplayStyle.None)
                {
                    _arrow!.style.display = DisplayStyle.None;
                }

                return;
            }

            if (_arrow.style.display != DisplayStyle.Flex)
            {
                _arrow.style.display = DisplayStyle.Flex;
            }

            var panelPos = RuntimePanelUtils.ScreenToPanel(
                _doc.rootVisualElement.panel,
                screenPos);

            float halfW = _doc.rootVisualElement.resolvedStyle.width / 2f;
            float halfH = _doc.rootVisualElement.resolvedStyle.height / 2f;

            float posX = panelPos.x - (_arrow.resolvedStyle.width / 2f);
            float posY = panelPos.y - (_arrow.resolvedStyle.height / 2f);

            float maxX = _doc.rootVisualElement.resolvedStyle.width - _arrow.resolvedStyle.width;
            float maxY = _doc.rootVisualElement.resolvedStyle.height - _arrow.resolvedStyle.height;

            bool offScreen = posX < 0 || posX > maxX || posY < 0 || posY > maxY;

            float targetLeft;
            float targetTop;
            float targetRotate;
            if (offScreen)
            {
                var dir = new Vector2(panelPos.x - halfW, panelPos.y - halfH);
                if (dir.magnitude < 0.001f)
                {
                    dir = Vector2.up;
                }

                dir.Normalize();

                const float margin = 40f;
                targetLeft = Mathf.Clamp(panelPos.x, margin, _doc.rootVisualElement.resolvedStyle.width - margin) - (_arrow.resolvedStyle.width / 2f);
                targetTop = Mathf.Clamp(panelPos.y, margin, _doc.rootVisualElement.resolvedStyle.height - margin) - (_arrow.resolvedStyle.height / 2f);

                float targetAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                targetRotate = targetAngle - 45f;
            }
            else
            {
                targetLeft = posX;
                targetTop = posY;
                targetRotate = 45f;
            }

            if (Mathf.Abs(targetLeft - _lastAppliedLeft) > PositionWriteEpsilon)
            {
                _arrow.style.left = targetLeft;
                _lastAppliedLeft = targetLeft;
            }

            if (Mathf.Abs(targetTop - _lastAppliedTop) > PositionWriteEpsilon)
            {
                _arrow.style.top = targetTop;
                _lastAppliedTop = targetTop;
            }

            if (Mathf.Abs(targetRotate - _lastAppliedRotate) > 0.5f)
            {
                _arrow.style.rotate = new Rotate(Angle.Degrees(targetRotate));
                _lastAppliedRotate = targetRotate;
            }
        }
    }
}
