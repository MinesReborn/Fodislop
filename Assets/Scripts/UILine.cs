using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts
{
    [UxmlElement]
    public partial class UILine : VisualElement
    {
        [UxmlAttribute("line-color")]
        private Color _color = Color.white;
        
        [UxmlAttribute("thickness")]
        private float _thickness = 1f;

        private Vector2 _start;
        private Vector2 _end;

        public Vector2 Start { get => _start; set { _start = value; MarkDirtyRepaint(); } }
        public Vector2 End { get => _end; set { _end = value; MarkDirtyRepaint(); } }

        public UILine()
        {
            generateVisualContent += OnGenerateVisualContent;
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var paint2D = mgc.painter2D;
            paint2D.strokeColor = _color;
            paint2D.lineWidth = _thickness;
            paint2D.BeginPath();
            paint2D.MoveTo(_start);
            paint2D.LineTo(_end);
            paint2D.Stroke();
        }
    }
}
