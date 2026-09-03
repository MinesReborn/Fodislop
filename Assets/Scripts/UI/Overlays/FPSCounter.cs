#nullable enable

using System.Text;
using Fodinae.Core;
using Fodinae.Networking;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace Fodinae.UI
{
    /// <summary>
    /// Displays the current frames‑per‑second in the top‑center of the screen using UI Toolkit.
    /// Updates each frame and formats the value with one decimal place.
    /// </summary>
    public class FPSCounter : MonoBehaviour
    {
        private const int SAMPLE_SIZE = 30;
        private readonly float[] _frameTimes = new float[SAMPLE_SIZE];
        private int _frameIndex;
        private float _runningSum;

        [Inject]
        private UIDocument _injectedDoc = null!;
        [Inject]
        private LightingEngine _lightingEngine = null!;
        [Inject]
        private NetworkStatusModel _networkStatus = null!;

        private UIDocument? _doc;
        private VisualElement? _rootElement;
        private Label? _fpsLabel;
        private float _nextDisplayUpdate;
        private int _currentDebugViewIndex;
        private readonly StringBuilder _displaySb = new(512);

        public float CurrentFps { get; private set; }

        public int PingMs => _networkStatus.PingMs;

        public int OnlinePlayers => _networkStatus.OnlinePlayers;

        public int OnlineProgrammator => _networkStatus.OnlineProgrammator;

        protected void Awake()
        {
            float initialDelta = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 1f / 60f;
            for (int i = 0; i < SAMPLE_SIZE; i++)
            {
                _frameTimes[i] = initialDelta;
            }

            _runningSum = initialDelta * SAMPLE_SIZE;
            CurrentFps = 1f / initialDelta;
        }

        protected void Start()
        {
            EnsureUI();
        }

        protected void OnEnable()
        {
            if (_fpsLabel == null)
            {
                EnsureUI();
            }
            else if (_rootElement != null)
            {
                UIState.Show(_rootElement);
            }
        }

        protected void OnDisable()
        {
            if (_rootElement != null)
            {
                UIState.Hide(_rootElement);
            }
        }

        protected void OnDestroy()
        {
            if (_rootElement != null && _doc != null && _doc.rootVisualElement != null)
            {
                _doc.rootVisualElement.Remove(_rootElement);
            }
            _rootElement = null;
            _fpsLabel = null;
        }

        private void EnsureUI()
        {
            if (_fpsLabel != null)
            {
                return;
            }

            // Никогда не резолвим из текущего контейнера здесь: во время
            // GameLifetimeScope.Configure он ещё указывает на родительский (Bootstrap)
            // скоуп, а AddComponent на только что созданном менеджере немедленно дёргает
            // OnEnable — Resolve<UIDocument> бросил бы VContainerException. [Inject]-поле
            // заполняется при завершении сборки scope; до этого момента EnsureUI
            // ретраится из Update каждый кадр.
            _doc = _injectedDoc;
            if (_doc == null || _doc.rootVisualElement == null)
            {
                return;
            }


            _rootElement = new VisualElement
            {
                name = "fps-counter-container",
                pickingMode = PickingMode.Ignore,
            };

            _rootElement.style.position = Position.Absolute;
            _rootElement.style.top = 8;
            _rootElement.style.left = new Length(50, LengthUnit.Percent);
            _rootElement.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _rootElement.style.alignItems = Align.Center;

            _fpsLabel = new Label
            {
                name = "fps-counter-label",
                pickingMode = PickingMode.Ignore,
            };

            _fpsLabel.style.color = Color.white;
            _fpsLabel.style.fontSize = 13;
            _fpsLabel.style.unityTextAlign = TextAnchor.UpperCenter;
            _fpsLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            _fpsLabel.style.borderTopLeftRadius = 4;
            _fpsLabel.style.borderTopRightRadius = 4;
            _fpsLabel.style.borderBottomLeftRadius = 4;
            _fpsLabel.style.borderBottomRightRadius = 4;
            _fpsLabel.style.paddingLeft = 8;
            _fpsLabel.style.paddingRight = 8;
            _fpsLabel.style.paddingTop = 2;
            _fpsLabel.style.paddingBottom = 2;

            _rootElement.Add(_fpsLabel);
            _doc.rootVisualElement.Add(_rootElement);
        }

        protected void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
            {
                LightingEngine? engine = _lightingEngine;
                if (engine != null)
                {
                    _currentDebugViewIndex = (_currentDebugViewIndex + 1) % 6;
                    var view = (LightingEngine.DebugView)_currentDebugViewIndex;
                    engine.SetDebugView(view);
                }
            }

            _runningSum -= _frameTimes[_frameIndex];
            _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
            _runningSum += _frameTimes[_frameIndex];
            _frameIndex = (_frameIndex + 1) % SAMPLE_SIZE;
            float avg = _runningSum / SAMPLE_SIZE;
            float fps = avg > 0f ? 1f / avg : 0f;
            CurrentFps = fps;

            if (_fpsLabel == null)
            {
                EnsureUI();
            }

            if (_fpsLabel != null && Time.unscaledTime >= _nextDisplayUpdate)
            {
                _nextDisplayUpdate = Time.unscaledTime + 0.25f;
                float frameTimeMs = avg * 1000f;
                int pingMs = _networkStatus.PingMs;
                int online = _networkStatus.OnlinePlayers;

                _displaySb.Clear();
                _displaySb.Append("FPS: ").Append((int)fps)
                    .Append(" (").Append(frameTimeMs.ToString("F1")).Append("ms)  Ping: ")
                    .Append(pingMs).Append("ms  Online: ").Append(online).Append("  [F3]");

                string newText = _displaySb.ToString();
                if (_fpsLabel.text != newText)
                {
                    _fpsLabel.text = newText;
                }
            }
        }

    }
}
