#nullable enable

using System.Text;
using Kern.Networking;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace Kern.UI
{
    public class FPSCounter : MonoBehaviour
    {
        private const int SampleSize = 30;

        private readonly float[] _frameTimes = new float[SampleSize];
        private readonly StringBuilder _displayBuilder = new(128);
        private int _frameIndex;
        private float _runningSum;
        private float _nextDisplayUpdate;
        private Label? _fpsLabel;
        private Label? _versionLabel;

        [Inject]
        private UIDocument _document = null!;
        [Inject]
        private NetworkStatusModel _networkStatus = null!;

        public float CurrentFPS { get; private set; }

        public int PingMs => _networkStatus.PingMs;

        public int OnlinePlayers => _networkStatus.OnlinePlayers;

        public int OnlineProgrammator => _networkStatus.OnlineProgrammator;

        protected void Awake()
        {
            float initialDelta = Time.unscaledDeltaTime > 0f
                ? Time.unscaledDeltaTime
                : 1f / 60f;
            for (int index = 0; index < SampleSize; index++)
            {
                _frameTimes[index] = initialDelta;
            }

            _runningSum = initialDelta * SampleSize;
            CurrentFPS = 1f / initialDelta;
        }

        protected void Start()
        {
            FindLabel();
        }

        protected void OnEnable()
        {
            FindLabel();
            if (_fpsLabel != null)
            {
                UIState.Show(_fpsLabel);
            }
        }

        protected void OnDisable()
        {
            if (_fpsLabel != null)
            {
                UIState.Hide(_fpsLabel);
            }
        }

        protected void OnDestroy()
        {
            _fpsLabel = null;
            _versionLabel = null;
        }

        protected void Update()
        {
            float delta = Time.unscaledDeltaTime;
            if (delta <= 0f)
            {
                return;
            }

            _runningSum -= _frameTimes[_frameIndex];
            _runningSum += delta;
            _frameTimes[_frameIndex] = delta;
            _frameIndex = (_frameIndex + 1) % SampleSize;

            float averageDelta = _runningSum / SampleSize;
            CurrentFPS = averageDelta > 0f ? 1f / averageDelta : 0f;

            if (_document != null && !_document.enabled)
            {
                return;
            }

            if (_fpsLabel == null)
            {
                FindLabel();
            }

            if (_fpsLabel == null || Time.unscaledTime < _nextDisplayUpdate)
            {
                return;
            }

            _nextDisplayUpdate = Time.unscaledTime + 0.25f;
            _displayBuilder.Clear();
            _displayBuilder.Append("FPS: ").Append((int)CurrentFPS)
                .Append(" (").Append((averageDelta * 1000f).ToString("F1"))
                .Append("ms)  Ping: ").Append(_networkStatus.PingMs)
                .Append("ms  Robots: ").Append(_networkStatus.OnlinePlayers)
                .Append("  Programmator: ").Append(_networkStatus.OnlineProgrammator)
                .Append("  [F1]");

            string text = _displayBuilder.ToString();
            if (_fpsLabel.text != text)
            {
                _fpsLabel.text = text;
                HoldWidth(_fpsLabel, ref _widestLabel);
            }
        }

        // Ширина подписи зависит от цифр: «FPS: 9» уже, чем «FPS: 48», и каждая
        // смена значения меняла размер и перераскладывала HUD (в журнале
        // раскладки это главный источник — 10–12 % кадров). Минимальная ширина
        // держится на самом широком тексте и только растёт: раскладка
        // случается, лишь пока строка расширяется до своего предела.
        private float _widestLabel;

        internal static void HoldWidth(VisualElement element, ref float widest)
        {
            float width = element.resolvedStyle.width;
            if (float.IsNaN(width) || width <= widest)
            {
                return;
            }

            widest = width;
            element.style.minWidth = width;
        }

        private void FindLabel()
        {
            if (_fpsLabel != null || _document == null)
            {
                return;
            }

            VisualElement? root = _document.rootVisualElement;
            _fpsLabel = root?.Q<Label>("FPSCounterLabel");
            _versionLabel = root?.Q<Label>("BuildVersionLabel");
            if (_versionLabel != null)
            {
                _versionLabel.text = $"v{Application.version}";
            }
        }
    }
}
