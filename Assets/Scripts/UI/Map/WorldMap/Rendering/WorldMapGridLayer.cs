#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Map;

/// <summary>
/// Сетка карты мира по 10 клеток — с крупного масштаба (макет js/map.js: z ≥ 6, цвет --map-grid). Слой поверх
/// картинки карты; положение линий — из вида карты (центр, клеток на пиксель текстуры, размер текстуры).
/// </summary>
internal sealed class WorldMapGridLayer
{
    private const float MinPixelsPerCell = 6f;
    private const int Step = 10;
    private static readonly Color s_line = new(228f / 255f, 232f / 255f, 234f / 255f, 0.08f);

    private float _centerX;
    private float _centerY;
    private float _cellsPerPixel;
    private int _texWidth;
    private int _texHeight;

    public WorldMapGridLayer()
    {
        Element = new VisualElement { name = "WorldMapGrid", pickingMode = PickingMode.Ignore };
        Element.style.position = Position.Absolute;
        Element.style.left = 0f;
        Element.style.top = 0f;
        Element.style.width = Length.Percent(100f);
        Element.style.height = Length.Percent(100f);
        Element.generateVisualContent += Draw;
    }

    public VisualElement Element { get; }

    public void SetView(float centerX, float centerY, float cellsPerPixel, int texWidth, int texHeight)
    {
        if (Mathf.Approximately(centerX, _centerX) && Mathf.Approximately(centerY, _centerY) &&
            Mathf.Approximately(cellsPerPixel, _cellsPerPixel) && texWidth == _texWidth && texHeight == _texHeight)
        {
            return;
        }

        _centerX = centerX;
        _centerY = centerY;
        _cellsPerPixel = cellsPerPixel;
        _texWidth = texWidth;
        _texHeight = texHeight;
        Element.MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext context)
    {
        Rect r = Element.contentRect;
        if (_cellsPerPixel <= 0f || _texWidth <= 0 || _texHeight <= 0 || r.width <= 0f || r.height <= 0f)
        {
            return;
        }

        // Экранных пикселей на клетку: ширина слоя / ширина текстуры / клеток на пиксель текстуры
        float perCellX = r.width / _texWidth / _cellsPerPixel;
        float perCellY = r.height / _texHeight / _cellsPerPixel;
        if (perCellX < MinPixelsPerCell)
        {
            return;
        }

        float leftCell = _centerX - _texWidth * 0.5f * _cellsPerPixel;
        float topCell = _centerY - _texHeight * 0.5f * _cellsPerPixel;
        float rightCell = leftCell + r.width / perCellX;
        float bottomCell = topCell + r.height / perCellY;

        Painter2D p = context.painter2D;
        p.fillColor = s_line;
        for (int x = Mathf.CeilToInt(leftCell / Step) * Step; x <= rightCell; x += Step)
        {
            float px = Mathf.Round((x - leftCell) * perCellX);
            FillRect(p, px, 0f, 1f, r.height);
        }

        for (int y = Mathf.CeilToInt(topCell / Step) * Step; y <= bottomCell; y += Step)
        {
            float py = Mathf.Round((y - topCell) * perCellY);
            FillRect(p, 0f, py, r.width, 1f);
        }
    }

    private static void FillRect(Painter2D p, float x, float y, float w, float h)
    {
        p.BeginPath();
        p.MoveTo(new Vector2(x, y));
        p.LineTo(new Vector2(x + w, y));
        p.LineTo(new Vector2(x + w, y + h));
        p.LineTo(new Vector2(x, y + h));
        p.ClosePath();
        p.Fill();
    }
}
