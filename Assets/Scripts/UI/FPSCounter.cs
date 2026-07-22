using UnityEngine;
using UnityEngine.UI;

namespace Fodinae.Scripts.UI
{
    /// <summary>
    /// Displays the current frames‑per‑second in the top‑right corner of the screen.
    /// Attach this component to a GameObject that has a Canvas (or create a new Canvas
    /// automatically if none exists). The script creates a UI Text element, updates it
    /// each frame and formats the value with one decimal place.
    /// </summary>
    public class FPSCounter : MonoBehaviour
    {
        public static FPSCounter Instance { get; private set; }

        private const int SAMPLE_SIZE = 30;
        private readonly float[] _frameTimes = new float[SAMPLE_SIZE];
        private int _frameIndex;
        private float _runningSum;

        private Text _fpsText;
        private int _pingMs;
        private int _onlinePlayers;
        private int _onlineProgrammator;

        protected void Awake()
        {
            Instance = this;

            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasGO = new GameObject("FPSCanvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
            }

            GameObject textGO = new GameObject("FPSLabel");
            textGO.transform.SetParent(canvas.transform, false);
            _fpsText = textGO.AddComponent<Text>();

            _fpsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_fpsText.font == null)
            {
                _fpsText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }

            _fpsText.fontSize = 14;
            _fpsText.alignment = TextAnchor.UpperCenter;
            _fpsText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _fpsText.color = Color.white;
            _fpsText.raycastTarget = false;

            RectTransform rt = _fpsText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 1);
            rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -10);
        }

        protected void Update()
        {
            _runningSum -= _frameTimes[_frameIndex];
            _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
            _runningSum += _frameTimes[_frameIndex];
            _frameIndex = (_frameIndex + 1) % SAMPLE_SIZE;
            float avg = _runningSum / SAMPLE_SIZE;
            float fps = avg > 0f ? 1f / avg : 0f;
            _fpsText.text = $"FPS: {fps:F1}  Ping: {_pingMs}ms  Online: {_onlinePlayers}  Prg: {_onlineProgrammator}";
        }

        public void SetPing(int ms) => _pingMs = ms;
        public void SetOnline(int players, int programmator)
        {
            _onlinePlayers = players;
            _onlineProgrammator = programmator;
        }
    }
}
