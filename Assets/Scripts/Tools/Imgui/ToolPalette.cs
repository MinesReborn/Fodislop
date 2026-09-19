#nullable enable

using System.Collections.Generic;
using Kern;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Tools.Imgui;

public static class ToolPalette
{
    // ── Подложки ──────────────────────────────────────────────────────────
    public static readonly Color32 Panel = new(11, 15, 19, 246);
    public static readonly Color32 Raised = new(19, 25, 32, 255);
    public static readonly Color32 RaisedHover = new(28, 36, 45, 255);
    public static readonly Color32 Sunken = new(4, 6, 9, 255);

    // ── Сигнальные ────────────────────────────────────────────────────────
    public static readonly Color Accent = new(0.988f, 0.933f, 0.039f, 1f);

    public static readonly Color Data = new(0f, 0.878f, 1f, 1f);
    public static readonly Color Warning = new(1f, 0.58f, 0.11f, 1f);
    public static readonly Color Success = new(0.22f, 1f, 0.53f, 1f);
    public static readonly Color Error = new(1f, 0.176f, 0.333f, 1f);

    public static readonly Color Text = new(0.843f, 0.890f, 0.925f, 1f);
    public static readonly Color MutedText = new(0.431f, 0.498f, 0.549f, 1f);
    public static readonly Color Hairline = new(0.988f, 0.933f, 0.039f, 0.22f);

    private static readonly Color32 _AccentSolid = new(252, 238, 10, 255);
    private static readonly Color32 _AccentDim = new(96, 90, 12, 255);
    private static readonly Color32 _DataSolid = new(0, 224, 255, 255);
    private static readonly Color32 _ErrorSolid = new(255, 45, 85, 255);
    private static readonly Color32 _EdgeDim = new(44, 56, 66, 255);

    private static readonly List<Texture2D> _Textures = [];

    public const int NotchSize = 9;

    public const int FrameSize = 20;

    public static Texture2D White { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D WindowFrame { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D CardFrame { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D GraphFrame { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D Control { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D ControlHover { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D ControlPressed { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D Selected { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D Danger { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D DangerHover { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D Field { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D FieldFocused { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D SliderTrack { get; private set; } = Texture2D.whiteTexture;

    public static Texture2D SliderThumb { get; private set; } = Texture2D.whiteTexture;

    public static RectOffset FrameBorder => new(NotchSize, NotchSize, NotchSize, NotchSize);

    public static RectOffset FlatBorder => new(1, 1, 1, 1);

    public static void Build()
    {
        Release();
        White = CreateSolid("Tool.White", new Color32(255, 255, 255, 255));

        WindowFrame = CreateNotched("Tool.WindowFrame", Panel, _AccentDim);
        CardFrame = CreateNotched("Tool.CardFrame", Raised, _EdgeDim);
        GraphFrame = CreateNotched("Tool.GraphFrame", Sunken, _EdgeDim);

        Control = CreateFlat("Tool.Control", Raised, _EdgeDim);
        ControlHover = CreateFlat("Tool.ControlHover", RaisedHover, new Color32(96, 92, 34, 255));
        ControlPressed = CreateFlat("Tool.ControlPressed", new Color32(58, 54, 8, 255), _AccentSolid);
        Selected = CreateFlat("Tool.Selected", new Color32(46, 43, 6, 255), _AccentSolid);
        Danger = CreateFlat("Tool.Danger", new Color32(48, 12, 22, 255), new Color32(150, 30, 56, 255));
        DangerHover = CreateFlat("Tool.DangerHover", new Color32(74, 17, 32, 255), _ErrorSolid);
        Field = CreateFlat("Tool.Field", Sunken, _EdgeDim);
        FieldFocused = CreateFlat("Tool.FieldFocused", new Color32(6, 12, 15, 255), _DataSolid);
        SliderTrack = CreateFlat("Tool.SliderTrack", Sunken, _EdgeDim);
        SliderThumb = CreateFlat("Tool.SliderThumb", _AccentSolid, new Color32(255, 255, 190, 255));
    }

    public static void Release()
    {
        foreach (Texture2D texture in _Textures)
        {
            CoreUtils.Destroy(texture);
        }

        _Textures.Clear();

        // Перезагрузка домена стирает статический список, а сами текстуры с
        // HideAndDontSave живут дальше: перепись нашла по 32 копии каждой.
        // Сироты прошлых доменов узнаются по имени.
        foreach (Texture2D orphan in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (orphan.hideFlags == HideFlags.HideAndDontSave &&
                orphan.name.StartsWith("Tool.", System.StringComparison.Ordinal))
            {
                CoreUtils.Destroy(orphan);
            }
        }

        White = Texture2D.whiteTexture;
    }

    public static Color Fade(Color color, float alpha) =>
        new(color.r, color.g, color.b, color.a * alpha);

    private static Texture2D Allocate(string name, int width, int height)
    {
        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            width,
            height,
            name,
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        texture.hideFlags = HideFlags.HideAndDontSave;
        _Textures.Add(texture);
        return texture;
    }

    private static Texture2D CreateSolid(string name, Color32 color)
    {
        Texture2D texture = Allocate(name, 1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    private static Texture2D CreateFlat(string name, Color32 fill, Color32 border)
    {
        Texture2D texture = Allocate(name, 3, 3);
        Color32[] pixels =
        [
            border, border, border,
            border, fill, border,
            border, border, border,
        ];
        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }

    private static Texture2D CreateNotched(string name, Color32 fill, Color32 border)
    {
        Texture2D texture = Allocate(name, FrameSize, FrameSize);
        var pixels = new Color32[FrameSize * FrameSize];
        Color32 clear = new(0, 0, 0, 0);

        for (int y = 0; y < FrameSize; y++)
        {
            for (int x = 0; x < FrameSize; x++)
            {
                // Координаты текстуры идут снизу вверх, срез нужен сверху справа.
                int fromRight = FrameSize - 1 - x;
                int fromTop = FrameSize - 1 - y;
                int diagonal = fromRight + fromTop;

                Color32 value;
                if (diagonal < NotchSize - 1)
                {
                    value = clear;
                }
                else if (diagonal < NotchSize + 1)
                {
                    value = border;
                }
                else if (x == 0 || y == 0 || x == FrameSize - 1 || y == FrameSize - 1)
                {
                    value = border;
                }
                else
                {
                    value = fill;
                }

                pixels[y * FrameSize + x] = value;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return texture;
    }
}
