using UnityEngine;
using UnityEngine.UI;

public class FPSCounter : MonoBehaviour
{
    private const int SampleSize = 30;
    private readonly float[] _frameTimes = new float[SampleSize];
    private int _frameIndex;

    private Text _fpsText;
    private int _pingMs;
    private int _onlinePlayers;
    private int _onlineProgrammator;

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

        _fpsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_fpsText.font == null)
            _fpsText.font = Font.CreateDynamicFontFromOSFont("Arial", 14);

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

    private void Update()
    {
        _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
        _frameIndex = (_frameIndex + 1) % SampleSize;
        float sum = 0f;
        for (int i = 0; i < SampleSize; i++)
            sum += _frameTimes[i];
        float avg = sum / SampleSize;
        float fps = avg > 0f ? 1f / avg : 0f;
        _fpsText.text = $"FPS: {fps:F1}  Ping: {_pingMs}ms  Online: {_onlinePlayers}  Prg: {_onlineProgrammator}";
    }

    public void SetPing(int ms) => _pingMs = ms;
    public void SetOnline(int players, int programmator) { _onlinePlayers = players; _onlineProgrammator = programmator; }
}
