using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the current frames‑per‑second in the top‑right corner of the screen.
/// Attach this component to a GameObject that has a Canvas (or create a new Canvas
/// automatically if none exists). The script creates a UI Text element, updates it
/// each frame and formats the value with one decimal place.
/// </summary>
public class FPSCounter : MonoBehaviour
{
    private const int SampleSize = 30; // number of frames to average
    private readonly float[] _frameTimes = new float[SampleSize];
    private int _frameIndex;
    private float _accumulatedTime;

    private Text _fpsText;
    private int _pingMs;

    private void Awake()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
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

        // Исправление: используем LegacyRuntime.ttf вместо Arial.ttf
        _fpsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Альтернативный вариант, если LegacyRuntime.ttf не работает:
        // _fpsText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        // если не работает, можно создать шрифт программно:
        if (_fpsText.font == null)
        {
            _fpsText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
        }

        _fpsText.fontSize = 14;
        _fpsText.alignment = TextAnchor.UpperRight;
        _fpsText.color = Color.white;
        _fpsText.raycastTarget = false;

        RectTransform rt = _fpsText.rectTransform;
        rt.anchorMin = new Vector2(1, 1); // Top-right corner
        rt.anchorMax = new Vector2(1, 1); // Top-right corner
        rt.pivot = new Vector2(1, 1); // Pivot at top-right
        rt.anchoredPosition = new Vector2(-10, -10); // Offset from top-right (negative X for right edge)
    }

    private void Update()
    {
        _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
        _frameIndex = (_frameIndex + 1) % SampleSize;
        _accumulatedTime = 0f;
        for (int i = 0; i < SampleSize; i++)
        {
            _accumulatedTime += _frameTimes[i];
        }
        float avgDelta = _accumulatedTime / SampleSize;
        float fps = avgDelta > 0f ? 1f / avgDelta : 0f;
        _fpsText.text = $"FPS: {fps:F1}\nPing: {_pingMs}ms";
    }

    public void SetPing(int ms) => _pingMs = ms;
}
