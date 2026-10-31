#nullable enable

using System;
using Kern.World;
using UnityEngine;

using Kern.AssetPipeline.Loading;

namespace Kern.Game.Visual.Effects.Cells;

// One shared import of the original sprite sheet, polygon meshes and timeline.
public sealed class CellDestructionClip : IDisposable
{
    private readonly ClipData _data;
    private readonly Texture2D _texture;
    private readonly Sprite[] _frames;
    private bool _disposed;

    public CellDestructionClip()
    {
        TextAsset timeline = Resources.Load<TextAsset>("Visual/Cells/Destruction/CellDestruction")
            ?? throw new InvalidOperationException("Cell destruction timeline is missing.");
        // Resources names must be unique; the image has its own basename.
        TextAsset image = Resources.Load<TextAsset>("Visual/Cells/Destruction/CellDestructionImage")
            ?? throw new InvalidOperationException("Cell destruction sprite sheet is missing.");
        _data = JsonUtility.FromJson<ClipData>(timeline.text)
            ?? throw new InvalidOperationException("Cell destruction timeline is invalid.");
        if (_data.Frames.Length != 8 || _data.Duration != 0.25f ||
            _data.PixelsPerUnit != 16 || _data.FrameSize != 32 || _data.SortingOrder != 5)
        {
            throw new InvalidOperationException("Cell destruction data differs from the original clip contract.");
        }

        Texture2D source = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            image.bytes, "CellDestruction", RuntimeTextureColorSpace.Srgb,
            FilterMode.Point, TextureWrapMode.Clamp, makeNoLongerReadable: false)
            ?? throw new InvalidOperationException("Cell destruction sprite sheet cannot be decoded.");
        if (source.width != 256 || source.height != 32)
        {
            UnityEngine.Object.Destroy(source);
            throw new InvalidOperationException("Cell destruction sprite sheet must be 256 by 32 pixels.");
        }

        try
        {
            // The reference's Gamma Sprites/Default writes encoded RGB * alpha.
            // Decode that authored contribution once at import, rather than
            // decoding straight RGB and premultiplying afterward. Those two
            // operations do not commute. Keep coverage alpha unchanged.
            Color32[] encoded = source.GetPixels32();
            var linearPremultiplied = new Color[encoded.Length];
            for (int pixel = 0; pixel < encoded.Length; pixel++)
            {
                Color32 value = encoded[pixel];
                float alpha = value.a / 255f;
                linearPremultiplied[pixel] = new Color(
                    Mathf.GammaToLinearSpace(value.r / 255f * alpha),
                    Mathf.GammaToLinearSpace(value.g / 255f * alpha),
                    Mathf.GammaToLinearSpace(value.b / 255f * alpha), alpha);
            }

            _texture = RuntimeTextureFactory.CreateRGBAHalfNoMip(256, 32,
                "CellDestructionLinearEmission", RuntimeTextureColorSpace.Linear,
                FilterMode.Point, TextureWrapMode.Clamp);
            _texture.SetPixels(linearPremultiplied);
            _texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        }
        finally
        {
            UnityEngine.Object.Destroy(source);
        }

        _frames = new Sprite[_data.Frames.Length];
        for (int index = 0; index < _frames.Length; index++)
        {
            FrameData frame = _data.Frames[index];
            if (frame.Emission.a != 1f || frame.Emission.maxColorComponent <= 0f ||
                !float.IsFinite(frame.Emission.r) || !float.IsFinite(frame.Emission.g) ||
                !float.IsFinite(frame.Emission.b))
            {
                throw new InvalidOperationException($"Cell destruction frame {index} has no valid emission integral.");
            }

            var triangles = new ushort[frame.Triangles.Length];
            for (int vertex = 0; vertex < triangles.Length; vertex++)
            {
                triangles[vertex] = checked((ushort)frame.Triangles[vertex]);
            }

            Sprite sprite = Sprite.Create(_texture,
                new Rect(index * _data.FrameSize, 0, _data.FrameSize, _data.FrameSize),
                new Vector2(0.5f, 0.5f), _data.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = $"CellDestruction_{index}";
            // Serialized Sprite.vertices are pivot-relative world units, but
            // OverrideGeometry takes pixels measured from the rect's bottom left.
            var pixelVertices = new Vector2[frame.Vertices.Length];
            Vector2 pivotPixels = Vector2.one * (_data.FrameSize * 0.5f);
            for (int vertex = 0; vertex < pixelVertices.Length; vertex++)
            {
                pixelVertices[vertex] = frame.Vertices[vertex] * _data.PixelsPerUnit + pivotPixels;
            }

            sprite.OverrideGeometry(pixelVertices, triangles);
            _frames[index] = sprite;
        }

        // These authored pixels are emission, not white diffuse albedo.
        // They illuminate the world without being multiplied by incoming light.
        Shader shader = Resources.Load<Shader>("Shaders/Effects/Cells/Destruction/CellDestruction")
            ?? throw new InvalidOperationException("The cell destruction shader is missing.");
        Material = new Material(shader) { name = "CellDestruction" };
        Material.SetVector("_SourceTextureSize", new Vector4(_texture.width, _texture.height, 0, 0));
    }

    public float DurationSeconds => _data.Duration;
    public int SortingOrder => _data.SortingOrder;
    public Material Material { get; }
    internal Sprite[] Frames => _frames;
    // Dynamic-light authoring color: integral of linear source RGB * coverage
    // over cell area. Lighting applies its normal GlowScale; the sprite
    // independently preserves the reference Gamma compositing contribution.
    internal Color EmissionAt(int frameIndex) => _data.Frames[frameIndex].Emission;

    internal int FrameIndexAt(float elapsedSeconds)
    {
        if (elapsedSeconds >= DurationSeconds)
        {
            return -1;
        }

        for (int index = _frames.Length - 1; index >= 0; index--)
        {
            if (elapsedSeconds >= _data.Frames[index].Time)
            {
                return index;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Sprite frame in _frames)
        {
            UnityEngine.Object.Destroy(frame);
        }

        UnityEngine.Object.Destroy(_texture);
        UnityEngine.Object.Destroy(Material);
    }

    [Serializable]
    private sealed class ClipData
    {
        public float Duration = 0f;
        public int PixelsPerUnit = 0;
        public int FrameSize = 0;
        public int SortingOrder = 0;
        public FrameData[] Frames = Array.Empty<FrameData>();
    }

    [Serializable]
    private sealed class FrameData
    {
        public float Time = 0f;
        public Vector2[] Vertices = Array.Empty<Vector2>();
        public int[] Triangles = Array.Empty<int>();
        public Color Emission = Color.black;
    }
}
