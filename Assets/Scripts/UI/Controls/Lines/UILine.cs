#nullable enable

using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.UI.Controls
{
    public class UILine : VisualElement
    {
        public Color LineColor
        {
            get => _color; set
            {
                _color = value;
                MarkDirtyRepaint();
            }
        }

        private Color _color = Color.white;

        public float Thickness
        {
            get => _thickness; set
            {
                _thickness = value;
                MarkDirtyRepaint();
            }
        }

        private float _thickness = 1f;

        public LineDirection Direction
        {
            get => _direction; set
            {
                _direction = value;
                MarkDirtyRepaint();
            }
        }

        private LineDirection _direction = LineDirection.Horizontal;

        private Vector2 _start;
        private Vector2 _end;

        public Vector2 Start
        {
            get => _start; set
            {
                _start = value;
                MarkDirtyRepaint();
            }
        }

        public Vector2 End
        {
            get => _end; set
            {
                _end = value;
                MarkDirtyRepaint();
            }
        }

        public UILine()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var paint2D = mgc.painter2D;
            paint2D.strokeColor = _color;
            paint2D.lineWidth = _thickness;

            Rect bounds = contentRect;
            if (_direction == LineDirection.Horizontal)
            {
                _start = new Vector2(bounds.xMin, bounds.center.y);
                _end = new Vector2(bounds.xMax, bounds.center.y);
            }
            else if (_direction == LineDirection.Vertical)
            {
                _start = new Vector2(bounds.center.x, bounds.yMin);
                _end = new Vector2(bounds.center.x, bounds.yMax);
            }
            else if (_direction == LineDirection.Diagonal)
            {
                _start = bounds.min;
                _end = bounds.max;
            }
            else if (_direction == LineDirection.ReverseDiagonal)
            {
                _start = new Vector2(bounds.xMin, bounds.yMax);
                _end = new Vector2(bounds.xMax, bounds.yMin);
            }

            paint2D.BeginPath();
            paint2D.MoveTo(_start);
            paint2D.LineTo(_end);
            paint2D.Stroke();
        }
    }
}
