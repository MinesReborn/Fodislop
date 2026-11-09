#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kern.Core.Interfaces;
using Kern.UI;
using UnityEngine;
using UnityEngine.UIElements;

using Kern.Diagnostics.Artifacts;
using Kern.UI.Screens;

namespace Kern.Tests.PlayMode.Mobile;

// Снимок настоящего документа игры на экране устройства из матрицы.
//
// Документ остаётся тем же, что строит игра (сцена, DI, контроллеры, тема);
// подменяются только экран — UIScreen получает снимок устройства и сам
// выставляет масштаб, классы формы и безопасную область, — и цель вывода:
// панель рисуется в RenderTexture размера экрана устройства. PNG кладётся
// в общем каталоге диагностических артефактов для просмотра глазами.
//
// Аудит — независимый оракул: безопасная область пересчитывается из
// паспортных данных устройства, а не берётся у UISafeArea.
internal sealed class MobileLayoutProbe : IDisposable
{
    public const float MinTouchTargetDp = 44f;
    private const float Tolerance = 0.75f;

    private readonly PanelSettings _settings;
    private readonly RenderTexture? _originalTarget;
    private readonly PanelScaleMode _originalScaleMode;
    private readonly float _originalScale;
    private readonly bool _originalClearColor;
    private readonly Color _originalClearValue;
    private RenderTexture? _target;

    public MobileLayoutProbe(UIDocument document)
    {
        Document = document;
        _settings = document.panelSettings ?? throw new InvalidOperationException("Document has no PanelSettings.");
        _originalTarget = _settings.targetTexture;
        _originalScaleMode = _settings.scaleMode;
        _originalScale = _settings.scale;
        _originalClearColor = _settings.clearColor;
        _originalClearValue = _settings.colorClearValue;
    }

    public UIDocument Document { get; }

    public static string OutputRoot => DiagnosticArtifactPaths.GetDirectory("MobileLayout");

    public IEnumerator Show(MobileDevice device)
    {
        ReleaseTarget();
        _target = new RenderTexture(device.Pixels.x, device.Pixels.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = $"MobileLayout {device.Name}",
        };
        _target.Create();
        _settings.targetTexture = _target;
        _settings.clearColor = true;
        _settings.colorClearValue = new Color(0.10f, 0.12f, 0.14f, 1f);

        UIScreen.OverrideForTests(Resolve(device));

        // Раскладка, стили, потом отрисовка: три кадра хватает на смену
        // классов, перевод токенов и GeometryChanged у безопасной области.
        for (int i = 0; i < 4; i++)
        {
            yield return null;
        }
    }

    public static ScreenShape Resolve(MobileDevice device) =>
        ScreenShapeRules.Resolve(device.Pixels, device.Dpi, device.SafeArea, usesPhysicalScale: true, isTouch: true);

    public string Capture(string screen, MobileDevice device)
    {
        if (_target == null)
        {
            throw new InvalidOperationException("Show(device) must run before Capture.");
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = _target;
        var texture = new Texture2D(_target.width, _target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, _target.width, _target.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;

        string directory = Path.Combine(OutputRoot, screen);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{device.Name.Replace(' ', '_')}.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.Destroy(texture);
        return path;
    }

    // Нарушения раскладки на текущем экране. Пустой список — экран годен.
    public List<string> Audit(MobileDevice device)
    {
        var violations = new List<string>();
        VisualElement tree = Document.rootVisualElement.panel.visualTree;
        Rect panel = tree.worldBound;
        Rect safe = SafeRectInPanel(device, panel.size);

        var interactive = new List<VisualElement>();
        tree.Query<VisualElement>().ForEach(element =>
        {
            if (!IsInteractive(element) || !IsShown(element))
            {
                return;
            }

            Rect box = element.worldBound;
            string id = Describe(element);

            // Палец достаёт только верхний элемент. Цель под модальным слоем
            // (пауза, окно, затемнение) недостижима намеренно и не проверяется;
            // цель под чем-то другим — дефект: её видно, а нажать нельзя.
            VisualElement? hit = tree.panel.Pick(box.center);
            if (hit != element && (hit == null || !element.Contains(hit)))
            {
                if (hit != null && !IsModalLayer(hit))
                {
                    violations.Add($"{id}: закрыта элементом {Describe(hit)}");
                }

                return;
            }

            interactive.Add(element);

            if (!InsideScrollViewport(element) &&
                (box.xMin < safe.xMin - Tolerance || box.yMin < safe.yMin - Tolerance ||
                 box.xMax > safe.xMax + Tolerance || box.yMax > safe.yMax + Tolerance))
            {
                violations.Add($"{id}: вне безопасной области {Format(box)} ⊄ {Format(safe)}");
            }

            if (Mathf.Min(box.width, box.height) < MinTouchTargetDp - Tolerance)
            {
                violations.Add($"{id}: цель касания {box.width:F0}×{box.height:F0} dp < {MinTouchTargetDp:F0}");
            }
        });

        for (int i = 0; i < interactive.Count; i++)
        {
            for (int j = i + 1; j < interactive.Count; j++)
            {
                VisualElement a = interactive[i];
                VisualElement b = interactive[j];
                if (a.Contains(b) || b.Contains(a))
                {
                    continue;
                }

                Rect overlap = Intersection(VisibleBox(a), VisibleBox(b));
                if (overlap.width > 2f && overlap.height > 2f)
                {
                    violations.Add($"{Describe(a)} перекрывает {Describe(b)} на {overlap.width:F0}×{overlap.height:F0}");
                }
            }
        }

        tree.Query<TextElement>().ForEach(text =>
        {
            if (string.IsNullOrEmpty(text.text) || !IsShown(text))
            {
                return;
            }

            Rect content = text.contentRect;
            if (content.width <= 0f || content.height <= 0f)
            {
                return;
            }

            IResolvedStyle style = text.resolvedStyle;
            bool wraps = style.whiteSpace == WhiteSpace.Normal || style.whiteSpace == WhiteSpace.PreWrap;
            Vector2 needed = text.MeasureTextSize(
                text.text,
                wraps ? content.width : 0f,
                wraps ? VisualElement.MeasureMode.Exactly : VisualElement.MeasureMode.Undefined,
                0f,
                VisualElement.MeasureMode.Undefined);
            bool clippedOnPurpose = style.textOverflow == TextOverflow.Ellipsis;
            if (!clippedOnPurpose && (needed.x > content.width + 1.5f || needed.y > content.height + 1.5f))
            {
                violations.Add($"{Describe(text)}: текст «{Short(text.text)}» не помещается — {needed.x:F0}×{needed.y:F0} в {content.width:F0}×{content.height:F0}");
            }

            Rect box = text.worldBound;
            if (!InsideScrollViewport(text) &&
                (box.xMax > panel.xMax + Tolerance || box.yMax > panel.yMax + Tolerance ||
                 box.xMin < panel.xMin - Tolerance || box.yMin < panel.yMin - Tolerance))
            {
                violations.Add($"{Describe(text)}: «{Short(text.text)}» за краем экрана {Format(box)}");
            }
        });

        return violations;
    }

    // Координаты всех видимых целей — для правки раскладки по числам.
    public void DumpLayout(string screen, MobileDevice device)
    {
        var builder = new StringBuilder();
        VisualElement tree = Document.rootVisualElement.panel.visualTree;
        builder.Append("panel ").AppendLine(Format(tree.worldBound));
        tree.Query<VisualElement>().ForEach(element =>
        {
            if (IsInteractive(element) && IsShown(element))
            {
                builder.Append(Describe(element)).Append(' ').AppendLine(Format(element.worldBound));
            }
        });

        string directory = Path.Combine(OutputRoot, screen);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{device.Name.Replace(' ', '_')}.txt"), builder.ToString());
    }

    public void Dispose()
    {
        UIScreen.OverrideForTests(null);
        _settings.targetTexture = _originalTarget;
        _settings.scaleMode = _originalScaleMode;
        _settings.scale = _originalScale;
        _settings.clearColor = _originalClearColor;
        _settings.colorClearValue = _originalClearValue;
        ReleaseTarget();
    }

    // Оракул безопасной области: паспортный прямоугольник устройства
    // (пиксели, начало внизу слева) переводится в пиксели панели.
    private static Rect SafeRectInPanel(MobileDevice device, Vector2 panelSize)
    {
        float sx = panelSize.x / device.Pixels.x;
        float sy = panelSize.y / device.Pixels.y;
        float left = device.SafeArea.xMin * sx;
        float right = device.SafeArea.xMax * sx;
        float top = (device.Pixels.y - device.SafeArea.yMax) * sy;
        float bottom = (device.Pixels.y - device.SafeArea.yMin) * sy;
        return Rect.MinMaxRect(left, top, right, bottom);
    }

    // Полоса прокрутки — не цель пальца: на сенсорном экране листают само
    // содержимое, поэтому всё внутри Scroller в аудит не входит.
    private static bool IsInteractive(VisualElement element) =>
        (element is Button or Toggle or TextField or DropdownField or Slider or SliderInt or IntegerField &&
         element.GetFirstAncestorOfType<Scroller>() == null) ||
        element.name == "MobileMovePad";

    // Частично прокрученный элемент списка обрезан окном прокрутки, а не
    // краем экрана: палец докрутит его, это не дефект раскладки.
    private static bool InsideScrollViewport(VisualElement element) =>
        element.GetFirstAncestorOfType<ScrollView>() != null;

    private static bool IsModalLayer(VisualElement hit)
    {
        for (VisualElement? current = hit; current != null; current = current.parent)
        {
            foreach (string cls in current.GetClasses())
            {
                if (cls.Contains("overlay") || cls.Contains("popup") || cls.Contains("modal") || cls.Contains("dimmer"))
                {
                    return true;
                }
            }

            if (current.name.Contains("Overlay") || current.name.Contains("Popup") || current.name.Contains("Modal"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShown(VisualElement element)
    {
        for (VisualElement? current = element; current != null; current = current.parent)
        {
            IResolvedStyle style = current.resolvedStyle;
            if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden || style.opacity < 0.01f)
            {
                return false;
            }
        }

        Rect box = element.worldBound;
        if (box.width <= 0.5f || box.height <= 0.5f || float.IsNaN(box.width))
        {
            return false;
        }

        // Содержимое, прокрученное за край списка, не видно и не нажимается:
        // окно прокрутки обрезает его целиком.
        for (VisualElement? current = element.parent; current != null; current = current.parent)
        {
            if (current is ScrollView scroll &&
                Intersection(box, scroll.contentViewport.worldBound).width <= 0f)
            {
                return false;
            }
        }

        return true;
    }

    // Видимая часть цели: элемент списка обрезан окном прокрутки, и палец
    // достаёт только то, что в окне.
    private static Rect VisibleBox(VisualElement element)
    {
        Rect box = element.worldBound;
        ScrollView? scroll = element.GetFirstAncestorOfType<ScrollView>();
        return scroll != null ? Intersection(box, scroll.contentViewport.worldBound) : box;
    }

    private static Rect Intersection(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : Rect.zero;
    }

    private static string Describe(VisualElement element)
    {
        var builder = new StringBuilder(element.GetType().Name);
        if (!string.IsNullOrEmpty(element.name))
        {
            builder.Append('#').Append(element.name);
        }
        else
        {
            foreach (string cls in element.GetClasses())
            {
                builder.Append('.').Append(cls);
                break;
            }

            if (element.parent != null && !string.IsNullOrEmpty(element.parent.name))
            {
                builder.Append(" в #").Append(element.parent.name);
            }
        }

        return builder.ToString();
    }

    private static string Short(string text) => text.Length <= 24 ? text : text.Substring(0, 24) + "…";

    private static string Format(Rect rect) => $"[{rect.xMin:F0},{rect.yMin:F0} {rect.width:F0}×{rect.height:F0}]";

    private void ReleaseTarget()
    {
        if (_target == null)
        {
            return;
        }

        if (_settings.targetTexture == _target)
        {
            _settings.targetTexture = _originalTarget;
        }

        _target.Release();
        UnityEngine.Object.Destroy(_target);
        _target = null;
    }
}
