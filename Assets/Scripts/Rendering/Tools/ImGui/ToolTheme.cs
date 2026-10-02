#nullable enable

using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Tools.ImGui;

public static class ToolTheme
{
    public const float HeaderHeight = 28f;

    public static Color Accent => ToolPalette.Accent;
    public static Color Warning => ToolPalette.Warning;
    public static Color Success => ToolPalette.Success;
    public static Color Error => ToolPalette.Error;
    public static Color FrameGraphColor => ToolPalette.Data;
    public static Color AllocationGraphColor => ToolPalette.Warning;

    private static GUISkin? s_sourceSkin;
    private static GUISkin? s_skin;
    private static GUIStyle? s_sectionLabel;
    private static GUIStyle? s_windowTitle;
    private static GUIStyle? s_richLabel;
    private static GUIStyle? s_wrappedLabel;
    private static GUIStyle? s_mutedLabel;
    private static GUIStyle? s_metricLabel;
    private static GUIStyle? s_unitLabel;
    private static GUIStyle? s_fieldLabel;
    private static GUIStyle? s_activeButton;
    private static GUIStyle? s_secondaryButton;
    private static GUIStyle? s_dangerButton;
    private static GUIStyle? s_segmentedButton;
    private static GUIStyle? s_closeButton;
    private static GUIStyle? s_warningLabel;
    private static GUIStyle? s_errorLabel;
    private static GUIStyle? s_card;
    private static GUIStyle? s_graph;
    private static GUIStyle? s_scope;

    public static GUIStyle SectionLabel => s_sectionLabel!;
    public static GUIStyle WindowTitle => s_windowTitle!;
    public static GUIStyle RichLabel => s_richLabel!;
    public static GUIStyle WrappedLabel => s_wrappedLabel!;
    public static GUIStyle MutedLabel => s_mutedLabel!;
    public static GUIStyle MetricLabel => s_metricLabel!;

    public static GUIStyle UnitLabel => s_unitLabel!;
    public static GUIStyle FieldLabel => s_fieldLabel!;
    public static GUIStyle ActiveButton => s_activeButton!;
    public static GUIStyle SecondaryButton => s_secondaryButton!;
    public static GUIStyle DangerButton => s_dangerButton!;
    public static GUIStyle SegmentedButton => s_segmentedButton!;
    public static GUIStyle CloseButton => s_closeButton!;
    public static GUIStyle WarningLabel => s_warningLabel!;
    public static GUIStyle ErrorLabel => s_errorLabel!;
    public static GUIStyle Card => s_card!;
    public static GUIStyle Graph => s_graph!;
    public static GUIStyle Scope => s_scope!;

    public static GUISkin ResolveSkin(GUISkin source)
    {
        if (s_skin != null && ReferenceEquals(s_sourceSkin, source))
        {
            return s_skin;
        }

        ReleaseResources();
        s_sourceSkin = source;
        Build(source);
        return s_skin!;
    }

    public static void Reset()
    {
        ReleaseResources();
        s_sourceSkin = null;
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
        s_skin = Object.Instantiate(source);
        s_skin.name = "Kern Runtime Tools";

        ConfigureWindow(s_skin.window);
        ConfigureButton(s_skin.button);
        ConfigureLabel(s_skin.label);
        ConfigureTextField(s_skin.textField, ToolPalette.Field, ToolPalette.FieldFocused);
        ConfigureTextField(s_skin.textArea, ToolPalette.Field, ToolPalette.FieldFocused, fixedHeight: 0f);
        ConfigureToggle(s_skin.toggle);
        ConfigureBox(s_skin.box);
        ConfigureSlider(s_skin.horizontalSlider, s_skin.horizontalSliderThumb);
        ConfigureScrollbars(s_skin);
        BuildLabels();
        BuildButtons();
        BuildSurfaces();
    }

    private static void BuildLabels()
    {
        s_windowTitle = new GUIStyle(s_skin!.label)
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
        s_sectionLabel = new GUIStyle(s_skin!.label)
        {
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ToolPalette.Accent },
            margin = new RectOffset(0, 0, 5, 4),
        };
        s_richLabel = new GUIStyle(s_skin.label) { richText = true };
        s_wrappedLabel = new GUIStyle(s_skin.label) { wordWrap = true };
        s_mutedLabel = new GUIStyle(s_wrappedLabel)
        {
            fontSize = 10,
            normal = { textColor = ToolPalette.MutedText },
        };
        s_metricLabel = new GUIStyle(s_skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            normal = { textColor = ToolPalette.Text },
            margin = new RectOffset(0, 0, 1, 0),
        };
        s_unitLabel = new GUIStyle(s_skin.label)
        {
            fontSize = 9,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.LowerLeft,
            normal = { textColor = ToolPalette.MutedText },
            margin = new RectOffset(2, 0, 0, 6),
        };
        s_fieldLabel = new GUIStyle(s_skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fixedHeight = 24f,
            normal = { textColor = ToolPalette.Text },
        };
        s_warningLabel = CreateSemanticLabel(ToolPalette.Warning);
        s_errorLabel = CreateSemanticLabel(ToolPalette.Error);
    }

    private static void BuildButtons()
    {
        s_activeButton = new GUIStyle(s_skin!.button);
        SetButtonBackgrounds(
            s_activeButton,
            ToolPalette.Selected,
            ToolPalette.ControlPressed,
            ToolPalette.ControlPressed,
            ToolPalette.Selected);
        SetAllTextColors(s_activeButton, ToolPalette.Accent);
        s_secondaryButton = new GUIStyle(s_skin.button) { fontStyle = FontStyle.Normal };
        s_dangerButton = new GUIStyle(s_skin.button);
        SetButtonBackgrounds(
            s_dangerButton,
            ToolPalette.Danger,
            ToolPalette.DangerHover,
            ToolPalette.DangerHover,
            ToolPalette.Danger);
        SetAllTextColors(s_dangerButton, ToolPalette.Error);
        s_segmentedButton = new GUIStyle(s_skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 8, 4, 5),
        };
        SetButtonBackgrounds(
            s_segmentedButton,
            ToolPalette.Control,
            ToolPalette.ControlHover,
            ToolPalette.ControlPressed,
            ToolPalette.Selected);
        SetOnTextColors(s_segmentedButton, ToolPalette.Accent);
        s_closeButton = new GUIStyle(s_skin.button)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(0, 0, 0, 2),
            fixedWidth = 24f,
            fixedHeight = 20f,
        };
        SetButtonBackgrounds(
            s_closeButton,
            ToolPalette.Control,
            ToolPalette.DangerHover,
            ToolPalette.Danger,
            ToolPalette.Danger);
    }

    private static void BuildSurfaces()
    {
        s_card = new GUIStyle(s_skin!.box);
        s_graph = new GUIStyle(s_skin.box)
        {
            normal = { background = ToolPalette.GraphFrame },
            border = ToolPalette.FrameBorder,
            padding = new RectOffset(5, 5, 5, 5),
        };
        s_scope = new GUIStyle(s_graph) { padding = new RectOffset(7, 7, 7, 7) };
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

    private static GUIStyle CreateSemanticLabel(Color color) => new(s_wrappedLabel!)
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
        if (s_skin != null)
        {
            CoreUtils.Destroy(s_skin);
            s_skin = null;
        }

        ToolPalette.Release();
        s_sectionLabel = null;
        s_windowTitle = null;
        s_richLabel = null;
        s_wrappedLabel = null;
        s_mutedLabel = null;
        s_metricLabel = null;
        s_unitLabel = null;
        s_fieldLabel = null;
        s_activeButton = null;
        s_secondaryButton = null;
        s_dangerButton = null;
        s_segmentedButton = null;
        s_closeButton = null;
        s_warningLabel = null;
        s_errorLabel = null;
        s_card = null;
        s_graph = null;
        s_scope = null;
    }
}
