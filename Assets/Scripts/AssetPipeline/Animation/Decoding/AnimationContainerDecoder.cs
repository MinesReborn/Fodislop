#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Kern;
using Kern.World;
using UnityEngine;

namespace Kern.AssetPipeline.Animation;
public static class AnimationContainerDecoder
{
    public enum ContainerType
    {
        None,
        PNG,
    }

    private const string FrameLayoutsResourcePath = "Visual/Sprites/Configuration/AnimationFrameLayouts";

    [Serializable]
    private sealed class FrameLayoutEntry
    {
        public string Name = string.Empty;
        // Zero derives the width from the loaded atlas.
        public int FrameWidth;
        public int FrameHeightRatio = 1;
        public int FrameCount;
        public float Fps;
        public float PixelsPerUnit;
        public bool PixelsPerUnitFromFrameWidth;
        public bool ComputeEmission;
    }

    [Serializable]
    private sealed class FrameLayoutDocument
    {
        public FrameLayoutEntry[] Layouts = Array.Empty<FrameLayoutEntry>();
    }

    private static readonly Lazy<Dictionary<string, FrameLayoutEntry>> s_layouts =
        new(LoadFrameLayouts);

    private static Dictionary<string, FrameLayoutEntry> LoadFrameLayouts()
    {
        TextAsset? json = Resources.Load<TextAsset>(FrameLayoutsResourcePath);
        if (json == null)
        {
            throw new InvalidOperationException(
                $"Animation frame layouts are missing at Resources/{FrameLayoutsResourcePath}.");
        }

        FrameLayoutDocument document = JsonUtility.FromJson<FrameLayoutDocument>(json.text) ??
            throw new InvalidDataException(
                $"Animation frame layouts at Resources/{FrameLayoutsResourcePath} are invalid.");
        var layouts = new Dictionary<string, FrameLayoutEntry>(StringComparer.Ordinal);
        foreach (FrameLayoutEntry layout in document.Layouts)
        {
            if (!string.IsNullOrWhiteSpace(layout.Name))
            {
                layouts[layout.Name] = layout;
            }
        }

        return layouts;
    }

    private static bool TryGetFrameLayout(
        string filename,
        [NotNullWhen(true)] out FrameLayoutEntry? layout)
    {
        Dictionary<string, FrameLayoutEntry> layouts = s_layouts.Value;
        if (layouts.TryGetValue(filename, out layout))
        {
            return true;
        }

        // Runtime asset requests use extensionless IDs (for example Visual/bz),
        // while authored layouts identify the concrete PNG asset.
        if (Path.GetExtension(filename).Length == 0)
        {
            return layouts.TryGetValue($"{filename}.png", out layout);
        }

        layout = null;
        return false;
    }

    public static ContainerType DetectType(byte[] data)
    {
        if (data == null || data.Length < 8)
        {
            return ContainerType.None;
        }

        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return ContainerType.PNG;
        }

        return ContainerType.None;
    }

    public static bool TryGetAnimationConfig(
        string filename,
        int width,
        int height,
        out int frameWidth,
        out int frameHeight,
        out int frameCount,
        out float fps)
    {
        if (width <= 0 || height <= 0)
        {
            frameWidth = 0;
            frameHeight = 0;
            frameCount = 0;
            fps = 0f;
            return false;
        }

        if (TryGetFrameLayout(filename, out FrameLayoutEntry? layout) && layout is not null)
        {
            if (layout.FrameCount <= 0)
            {
                throw new InvalidDataException(
                    $"Animation frame layout '{layout.Name}' declares frame count {layout.FrameCount}.");
            }

            frameWidth = layout.FrameWidth > 0 ? layout.FrameWidth : width;
            frameHeight = checked(frameWidth * Math.Max(1, layout.FrameHeightRatio));
            frameCount = layout.FrameCount;
            fps = layout.Fps;
            // A derived width follows the loaded atlas, so a mismatched sheet
            // is an authoring error rather than a silently wrong layout.
            if (layout.FrameWidth <= 0 && height != checked(frameHeight * frameCount))
            {
                throw new InvalidDataException(
                    $"Animation atlas {width}x{height} does not match layout '{layout.Name}': " +
                    $"expected {frameWidth}x{checked(frameHeight * frameCount)}.");
            }

            return true;
        }

        if (height > width && height % width == 0)
        {
            frameWidth = width;
            frameHeight = width;
            frameCount = height / width;
            fps = 5f;
            return true;
        }

        frameWidth = width;
        frameHeight = height;
        frameCount = 1;
        fps = 0f;
        return true;
    }

    public static bool HasFrameEmission(string filename) =>
        TryGetFrameLayout(filename, out FrameLayoutEntry? layout) && layout?.ComputeEmission == true;

    public static float ResolvePixelsPerUnit(string filename, int frameWidth)
    {
        if (!TryGetFrameLayout(filename, out FrameLayoutEntry? layout) || layout is null)
        {
            return RenderingConstants.PIXELS_PER_UNIT;
        }

        if (layout.PixelsPerUnitFromFrameWidth)
        {
            return frameWidth;
        }

        return layout.PixelsPerUnit > 0f
            ? layout.PixelsPerUnit
            : RenderingConstants.PIXELS_PER_UNIT;
    }

    public static Sprite[] Decode(Texture2D atlas, int width, int height, int frameCount,
        float pixelsPerUnit = RenderingConstants.PIXELS_PER_UNIT)
    {
        if (atlas == null)
        {
            throw new ArgumentNullException(nameof(atlas));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Sprite frame dimensions must be positive.");
        }

        if (frameCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount),
                "Sprite frame count must be positive.");
        }

        if (atlas.width < width || atlas.height < height)
        {
            throw new InvalidDataException(
                $"Sprite atlas {atlas.width}x{atlas.height} is smaller than frame {width}x{height}.");
        }

        Sprite[] frames = new Sprite[frameCount];
        int framesPerRow = atlas.width / width;
        if (framesPerRow <= 0 ||
            (int)Math.Ceiling(frameCount / (double)framesPerRow) * height > atlas.height)
        {
            throw new InvalidDataException(
                $"Sprite atlas {atlas.width}x{atlas.height} cannot contain " +
                $"{frameCount} frames of {width}x{height}.");
        }

        for (int i = 0; i < frameCount; i++)
        {
            int x = (i % framesPerRow) * width;
            int y = (i / framesPerRow) * height;

            frames[i] = Sprite.Create(
                atlas,
                new Rect(x, y, width, height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
        }

        return frames;
    }
}
