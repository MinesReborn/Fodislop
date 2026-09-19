#nullable enable

using System;
using Kern;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Tools.Imgui;

public sealed class ToolGraph : IDisposable
{
    private const int TextureHeight = 48;

    private readonly float[] _samples;
    private int _cursor;
    private int _count;

    private Texture2D? _texture;
    private Color32[]? _pixelBuffer;
    private bool _dirty;
    private Color _lastColor;
    private float _lastTop;

    public ToolGraph(int capacity)
    {
        _samples = new float[Mathf.Max(2, capacity)];
    }

    public float Last { get; private set; }

    public float Minimum { get; private set; }

    public float Maximum { get; private set; }

    public float Average { get; private set; }

    public void Push(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            value = 0f;
        }

        value = Mathf.Max(0f, value);
        _samples[_cursor] = value;
        _cursor = (_cursor + 1) % _samples.Length;
        _count = Mathf.Min(_count + 1, _samples.Length);
        Last = value;
        _dirty = true;

        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        float sum = 0f;
        for (int i = 0; i < _count; i++)
        {
            float sample = _samples[i];
            minimum = Mathf.Min(minimum, sample);
            maximum = Mathf.Max(maximum, sample);
            sum += sample;
        }

        Minimum = _count > 0 ? minimum : 0f;
        Maximum = _count > 0 ? maximum : 0f;
        Average = _count > 0 ? sum / _count : 0f;
    }

    public void Clear()
    {
        Array.Clear(_samples, 0, _samples.Length);
        _cursor = 0;
        _count = 0;
        Last = 0f;
        Minimum = 0f;
        Maximum = 0f;
        Average = 0f;
        _dirty = true;
    }

    public void DestroyTexture()
    {
        if (_texture != null)
        {
            CoreUtils.Destroy(_texture);
            _texture = null;
            _pixelBuffer = null;
            _dirty = true;
        }
    }

    public void Dispose()
    {
        DestroyTexture();
    }

    public void Draw(Rect area, Color color, float scaleHint)
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        GUI.Box(area, GUIContent.none, ToolTheme.Graph);
        if (_count == 0)
        {
            return;
        }

        float top = Mathf.Max(scaleHint, Maximum);
        if (top <= 0f)
        {
            return;
        }

        EnsureTextureUpdated(color, top);
        if (_texture == null)
        {
            return;
        }

        // Отступ от края: рамка теперь со срезанным углом, и растянутая на всю
        // площадь текстура закрыла бы и срез, и границу — график выглядел бы
        // приклеенным поверх окна, а не вставленным в него.
        const float inset = 5f;
        var plot = new Rect(
            area.x + inset,
            area.y + inset,
            Mathf.Max(1f, area.width - inset * 2f),
            Mathf.Max(1f, area.height - inset * 2f));
        GUI.DrawTexture(plot, _texture, ScaleMode.StretchToFill, true);
    }

    private void EnsureTextureUpdated(Color color, float top)
    {
        int width = _samples.Length;
        int height = TextureHeight;

        if (_texture == null)
        {
            _texture = RuntimeTextureFactory.CreateRGBA32NoMip(
                width,
                height,
                "ToolGraph",
                RuntimeTextureColorSpace.Linear,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _pixelBuffer = new Color32[width * height];
            _dirty = true;
        }

        if (!_dirty && color == _lastColor && Mathf.Approximately(top, _lastTop))
        {
            return;
        }

        PaintGrid(width, height, color);
        PaintBars(width, height, top, color);

        _texture.SetPixels32(_pixelBuffer!);
        _texture.Apply(false, false);
        _dirty = false;
        _lastColor = color;
        _lastTop = top;
    }

    private void PaintGrid(int width, int height, Color color)
    {
        Color32 empty = new(0, 0, 0, 0);
        Color32 grid = new(
            (byte)(color.r * 255f),
            (byte)(color.g * 255f),
            (byte)(color.b * 255f),
            30);

        int quarter = height / 4;
        for (int y = 0; y < height; y++)
        {
            bool isGridRow = quarter > 0 && y % quarter == 0 && y > 0;
            Color32 rowColor = isGridRow ? grid : empty;
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                // Пунктир, а не сплошная: сплошная линия на уровне столбца
                // сливается с ним и читается как часть данных.
                _pixelBuffer![rowStart + x] = isGridRow && (x & 3) == 0 ? rowColor : empty;
            }
        }
    }

    private void PaintBars(int width, int height, float top, Color color)
    {
        byte red = (byte)(color.r * 255f);
        byte green = (byte)(color.g * 255f);
        byte blue = (byte)(color.b * 255f);
        Color32 crest = new(
            (byte)Mathf.Min(255f, color.r * 255f + 90f),
            (byte)Mathf.Min(255f, color.g * 255f + 90f),
            (byte)Mathf.Min(255f, color.b * 255f + 90f),
            255);

        for (int x = 0; x < width; x++)
        {
            if (x >= _count)
            {
                continue;
            }

            int index = (_cursor - _count + x + width * 2) % width;
            float sample = _samples[index];
            if (sample <= 0f)
            {
                continue;
            }

            float normalized = Mathf.Clamp01(sample / top);
            int barHeight = Mathf.Max(1, Mathf.RoundToInt(normalized * height));

            for (int y = 0; y < barHeight; y++)
            {
                float rise = barHeight > 1 ? y / (float)(barHeight - 1) : 1f;
                byte alpha = (byte)Mathf.Lerp(46f, 210f, rise * rise);
                _pixelBuffer![y * width + x] = new Color32(red, green, blue, alpha);
            }

            _pixelBuffer![(barHeight - 1) * width + x] = crest;
        }
    }
}
