#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Tools.Imgui;

public static class ToolTheme
{
    public const float HeaderHeight = 28f;

    public static Color Accent => ToolPalette.Accent;
    public static Color Warning => ToolPalette.Warning;
    public static Color Success => ToolPalette.Success;
    public static Color Error => ToolPalette.Error;
    public static Color FrameGraphColor => ToolPalette.Data;
    public static Color AllocationGraphColor => ToolPalette.Warning;

    private static GUISkin? _sourceSkin;
    private static GUISkin? _skin;
    private static GUIStyle? _sectionLabel;
    private static GUIStyle? _windowTitle;
    private static GUIStyle? _richLabel;
    private static GUIStyle? _wrappedLabel;
    private static GUIStyle? _mutedLabel;
    private static GUIStyle? _metricLabel;
    private static GUIStyle? _unitLabel;
    private static GUIStyle? _fieldLabel;
    private static GUIStyle? _activeButton;
    private static GUIStyle? _secondaryButton;
    private static GUIStyle? _dangerButton;
    private static GUIStyle? _segmentedButton;
    private static GUIStyle? _closeButton;
    private static GUIStyle? _warningLabel;
    private static GUIStyle? _successLabel;
    private static GUIStyle? _errorLabel;
    private static GUIStyle? _card;
    private static GUIStyle? _graph;
    private static GUIStyle? _scope;

    public static GUIStyle SectionLabel => _sectionLabel!;
    public static GUIStyle WindowTitle => _windowTitle!;
    public static GUIStyle RichLabel => _richLabel!;
    public static GUIStyle WrappedLabel => _wrappedLabel!;
    public static GUIStyle MutedLabel => _mutedLabel!;
    public static GUIStyle MetricLabel => _metricLabel!;

    public static GUIStyle UnitLabel => _unitLabel!;
    public static GUIStyle FieldLabel => _fieldLabel!;
    public static GUIStyle ActiveButton => _activeButton!;
    public static GUIStyle SecondaryButton => _secondaryButton!;
    public static GUIStyle DangerButton => _dangerButton!;
    public static GUIStyle SegmentedButton => _segmentedButton!;
    public static GUIStyle CloseButton => _closeButton!;
    public static GUIStyle WarningLabel => _warningLabel!;
    public static GUIStyle SuccessLabel => _successLabel!;
    public static GUIStyle ErrorLabel => _errorLabel!;
    public static GUIStyle Card => _card!;
    public static GUIStyle Graph => _graph!;
    public static GUIStyle Scope => _scope!;

    public static GUISkin ResolveSkin(GUISkin source)
    {
        if (_skin != null && ReferenceEquals(_sourceSkin, source))
        {
            return _skin;
        }

        ReleaseResources();
        _sourceSkin = source;
        Build(source);
        return _skin!;
    }

    public static void Reset()
    {
        ReleaseResources();
        _sourceSkin = null;
    }

    public static void Separator(float spaceBefore = 8f, float spaceAfter = 8f)
    {
        GUILayout.Space(spaceBefore);
        Rect rect = GUILayoutUtility.GetRect(1f, 1f, ToolLayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            Color previousColor = GUI.color;
            GUI.color = ToolPalette.Hairline;
            GUI.DrawTexture(rect, ToolPalette.White);
            GUI.color = previousColor;
        }

        GUILayout.Space(spaceAfter);
    }

    private static void Build(GUISkin source)
    {
        ToolPalette.Build();
        _skin = Object.Instantiate(source);
        _skin.name = "Kern Runtime Tools";

        ConfigureWindow(_skin.window);
        ConfigureButton(_skin.button);
        ConfigureLabel(_skin.label);
        ConfigureTextField(_skin.textField, ToolPalette.Field, ToolPalette.FieldFocused);
        ConfigureTextField(_skin.textArea, ToolPalette.Field, ToolPalette.FieldFocused, fixedHeight: 0f);
        ConfigureToggle(_skin.toggle);
        ConfigureBox(_skin.box);
        ConfigureSlider(_skin.horizontalSlider, _skin.horizontalSliderThumb);
        ConfigureScrollbars(_skin);
        BuildLabels();
        BuildButtons();
        BuildSurfaces();
    }

    private static void BuildLabels()
    {
        _windowTitle = new GUIStyle(_skin!.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            wordWrap = false,
            padding = new RectOffset(),
            margin = new RectOffset(),
            normal = { textColor = ToolPalette.Accent },
        };
        _sectionLabel = new GUIStyle(_skin!.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ToolPalette.Accent },
            margin = new RectOffset(0, 0, 5, 4),
        };
        _richLabel = new GUIStyle(_skin.label) { richText = true };
        _wrappedLabel = new GUIStyle(_skin.label) { wordWrap = true };
        _mutedLabel = new GUIStyle(_wrappedLabel)
        {
            fontSize = 10,
            normal = { textColor = ToolPalette.MutedText },
        };
        _metricLabel = new GUIStyle(_skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ToolPalette.Text },
            margin = new RectOffset(0, 0, 1, 0),
        };
        _unitLabel = new GUIStyle(_skin.label)
        {
            fontSize = 9,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.LowerLeft,
            normal = { textColor = ToolPalette.MutedText },
            margin = new RectOffset(2, 0, 0, 6),
        };
        _fieldLabel = new GUIStyle(_skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fixedHeight = 24f,
            normal = { textColor = ToolPalette.Text },
        };
        _warningLabel = CreateSemanticLabel(ToolPalette.Warning);
        _successLabel = CreateSemanticLabel(ToolPalette.Success);
        _errorLabel = CreateSemanticLabel(ToolPalette.Error);
    }

    private static void BuildButtons()
    {
        _activeButton = new GUIStyle(_skin!.button);
        SetButtonBackgrounds(
            _activeButton,
            ToolPalette.Selected,
            ToolPalette.ControlPressed,
            ToolPalette.ControlPressed,
            ToolPalette.Selected);
        SetAllTextColors(_activeButton, ToolPalette.Accent);
        _secondaryButton = new GUIStyle(_skin.button) { fontStyle = FontStyle.Normal };
        _dangerButton = new GUIStyle(_skin.button);
        SetButtonBackgrounds(
            _dangerButton,
            ToolPalette.Danger,
            ToolPalette.DangerHover,
            ToolPalette.DangerHover,
            ToolPalette.Danger);
        SetAllTextColors(_dangerButton, ToolPalette.Error);
        _segmentedButton = new GUIStyle(_skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 8, 4, 5),
        };
        SetButtonBackgrounds(
            _segmentedButton,
            ToolPalette.Control,
            ToolPalette.ControlHover,
            ToolPalette.ControlPressed,
            ToolPalette.Selected);
        SetOnTextColors(_segmentedButton, ToolPalette.Accent);
        _closeButton = new GUIStyle(_skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 2),
            fixedWidth = 24f,
            fixedHeight = 20f,
        };
        SetButtonBackgrounds(
            _closeButton,
            ToolPalette.Control,
            ToolPalette.DangerHover,
            ToolPalette.Danger,
            ToolPalette.Danger);
    }

    private static void BuildSurfaces()
    {
        _card = new GUIStyle(_skin!.box);
        _graph = new GUIStyle(_skin.box)
        {
            normal = { background = ToolPalette.GraphFrame },
            border = ToolPalette.FrameBorder,
            padding = new RectOffset(5, 5, 5, 5),
        };
        _scope = new GUIStyle(_graph) { padding = new RectOffset(7, 7, 7, 7) };
    }

    private static void ConfigureWindow(GUIStyle style)
    {
        SetAllBackgrounds(style, ToolPalette.WindowFrame);
        SetAllTextColors(style, ToolPalette.Accent);
        style.border = ToolPalette.FrameBorder;

        // Верхнее поле держит полосу заголовка, правое — срез угла и кнопку
        // закрытия: содержимое не должно заходить под диагональ.
        style.padding = new RectOffset(13, 13, 34, 13);
        style.alignment = TextAnchor.UpperLeft;
        style.contentOffset = Vector2.zero;
        style.fontSize = 11;
        style.fontStyle = FontStyle.Bold;
    }

    private static void ConfigureButton(GUIStyle style)
    {
        SetButtonBackgrounds(
            style,
            ToolPalette.Control,
            ToolPalette.ControlHover,
            ToolPalette.ControlPressed,
            ToolPalette.Selected);
        SetAllTextColors(style, ToolPalette.Text);
        SetOnTextColors(style, ToolPalette.Accent);
        style.border = ToolPalette.FlatBorder;
        style.padding = new RectOffset(9, 9, 4, 5);
        style.margin = new RectOffset(2, 2, 2, 2);
        style.fixedHeight = 24f;
        style.alignment = TextAnchor.MiddleCenter;
    }

    private static void ConfigureLabel(GUIStyle style)
    {
        SetAllTextColors(style, ToolPalette.Text);
        style.fontSize = 12;
        style.padding = new RectOffset(1, 1, 1, 1);
        style.margin = new RectOffset(1, 1, 1, 1);
    }

    private static void ConfigureTextField(
        GUIStyle style,
        Texture2D normal,
        Texture2D focused,
        float fixedHeight = 24f)
    {
        SetButtonBackgrounds(style, normal, normal, focused, focused);
        SetAllTextColors(style, ToolPalette.Data);
        style.border = ToolPalette.FlatBorder;
        style.padding = new RectOffset(7, 7, 4, 4);
        style.fixedHeight = fixedHeight;
    }

    private static void ConfigureToggle(GUIStyle style)
    {
        SetAllTextColors(style, ToolPalette.Text);
        SetOnTextColors(style, ToolPalette.Accent);
        style.fontSize = 12;
        style.fixedHeight = 22f;
    }

    private static void ConfigureBox(GUIStyle style)
    {
        SetAllBackgrounds(style, ToolPalette.CardFrame);
        SetAllTextColors(style, ToolPalette.Text);
        style.border = ToolPalette.FrameBorder;
        style.padding = new RectOffset(11, 11, 9, 10);
        style.margin = new RectOffset(1, 1, 4, 5);
    }

    private static void ConfigureSlider(GUIStyle track, GUIStyle thumb)
    {
        SetAllBackgrounds(track, ToolPalette.SliderTrack);
        track.border = ToolPalette.FlatBorder;
        track.fixedHeight = 6f;
        track.margin = new RectOffset(5, 5, 10, 8);

        SetAllBackgrounds(thumb, ToolPalette.SliderThumb);
        thumb.border = ToolPalette.FlatBorder;
        thumb.fixedWidth = 9f;
        thumb.fixedHeight = 18f;
    }

    private static void ConfigureScrollbars(GUISkin skin)
    {
        ConfigureScrollbar(skin.verticalScrollbar, vertical: true);
        ConfigureScrollbar(skin.horizontalScrollbar, vertical: false);
        ConfigureScrollbarThumb(skin.verticalScrollbarThumb, vertical: true);
        ConfigureScrollbarThumb(skin.horizontalScrollbarThumb, vertical: false);
        ConfigureScrollbarButton(skin.verticalScrollbarUpButton);
        ConfigureScrollbarButton(skin.verticalScrollbarDownButton);
        ConfigureScrollbarButton(skin.horizontalScrollbarLeftButton);
        ConfigureScrollbarButton(skin.horizontalScrollbarRightButton);
        skin.scrollView.normal.background = null;
    }

    private static void ConfigureScrollbar(GUIStyle style, bool vertical)
    {
        SetAllBackgrounds(style, ToolPalette.SliderTrack);
        style.border = ToolPalette.FlatBorder;
        style.fixedWidth = vertical ? 9f : 0f;
        style.fixedHeight = vertical ? 0f : 9f;
    }

    private static void ConfigureScrollbarThumb(GUIStyle style, bool vertical)
    {
        SetButtonBackgrounds(
            style,
            ToolPalette.Control,
            ToolPalette.ControlHover,
            ToolPalette.ControlHover,
            ToolPalette.Control);
        style.border = ToolPalette.FlatBorder;
        style.fixedWidth = vertical ? 9f : 0f;
        style.fixedHeight = vertical ? 0f : 9f;
    }

    private static void ConfigureScrollbarButton(GUIStyle style)
    {
        SetButtonBackgrounds(
            style,
            ToolPalette.SliderTrack,
            ToolPalette.Control,
            ToolPalette.Control,
            ToolPalette.SliderTrack);
        style.border = ToolPalette.FlatBorder;
        style.fixedWidth = 9f;
        style.fixedHeight = 9f;
    }

    private static GUIStyle CreateSemanticLabel(Color color) => new(_wrappedLabel!)
    {
        normal = { textColor = color },
    };

    private static void SetButtonBackgrounds(
        GUIStyle style,
        Texture2D normal,
        Texture2D hover,
        Texture2D active,
        Texture2D selected)
    {
        style.normal.background = normal;
        style.hover.background = hover;
        style.active.background = active;
        style.focused.background = hover;
        style.onNormal.background = selected;
        style.onHover.background = selected;
        style.onActive.background = active;
        style.onFocused.background = selected;
    }

    private static void SetAllBackgrounds(GUIStyle style, Texture2D background)
    {
        style.normal.background = background;
        style.hover.background = background;
        style.active.background = background;
        style.focused.background = background;
        style.onNormal.background = background;
        style.onHover.background = background;
        style.onActive.background = background;
        style.onFocused.background = background;
    }

    private static void SetAllTextColors(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
        SetOnTextColors(style, color);
    }

    private static void SetOnTextColors(GUIStyle style, Color color)
    {
        style.onNormal.textColor = color;
        style.onHover.textColor = color;
        style.onActive.textColor = color;
        style.onFocused.textColor = color;
    }

    private static void ReleaseResources()
    {
        if (_skin != null)
        {
            CoreUtils.Destroy(_skin);
            _skin = null;
        }

        ToolPalette.Release();
        _sectionLabel = null;
        _windowTitle = null;
        _richLabel = null;
        _wrappedLabel = null;
        _mutedLabel = null;
        _metricLabel = null;
        _unitLabel = null;
        _fieldLabel = null;
        _activeButton = null;
        _secondaryButton = null;
        _dangerButton = null;
        _segmentedButton = null;
        _closeButton = null;
        _warningLabel = null;
        _successLabel = null;
        _errorLabel = null;
        _card = null;
        _graph = null;
        _scope = null;
    }
}
