using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.Scripts
{
    public class UILine : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<UILine, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlColorAttributeDescription _color = new UxmlColorAttributeDescription { name = "line-color", defaultValue = Color.white };
            UxmlFloatAttributeDescription _thickness = new UxmlFloatAttributeDescription { name = "thickness", defaultValue = 1f };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var line = ve as UILine;
                line._color = _color.GetValueFromBag(bag, cc);
                line._thickness = _thickness.GetValueFromBag(bag, cc);
                line.MarkDirtyRepaint();
            }
        }

        private Color _color = Color.white;
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
