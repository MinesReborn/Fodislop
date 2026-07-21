using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts.UI
{
    [UxmlElement]
    public partial class UILine : VisualElement
    {
        [UxmlAttribute("line-color")]
        public Color LineColor
        {
            get => _color; set
            {
                _color = value;
                MarkDirtyRepaint();
            }
        }

        private Color _color = Color.white;

        [UxmlAttribute("thickness")]
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

            // If direction is set, we use simple line drawing based on element size
            if (_direction == LineDirection.Horizontal)
            {
                _start = new Vector2(0, layout.height / 2);
                _end = new Vector2(layout.width, layout.height / 2);
            }
            else if (_direction == LineDirection.Vertical)
            {
                _start = new Vector2(layout.width / 2, 0);
                _end = new Vector2(layout.width / 2, layout.height);
            }

            paint2D.BeginPath();
            paint2D.MoveTo(_start);
            paint2D.LineTo(_end);
            paint2D.Stroke();
        }
    }
}
