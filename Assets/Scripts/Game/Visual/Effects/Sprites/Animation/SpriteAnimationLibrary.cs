#nullable enable

using System;
using System.Collections.Generic;
using Kern.World;
using UnityEngine;

using Kern.AssetPipeline.Loading;
using Kern.AssetPipeline.Animation;

namespace Kern.Game.Visual.Effects.Sprites;

// Shared native sprite geometry, UVs and Animator timelines. Loaded once at world entry.
public sealed class SpriteAnimationLibrary : IDisposable
{
    private readonly Dictionary<string, Clip> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Material> _materials = new(StringComparer.Ordinal);
    private readonly List<Texture2D> _textures = new();
    private readonly List<Sprite> _sprites = new();
    // Indexed by encoded channel and alpha. Conversion is deterministic and
    // identical for every pixel sharing the same pair across sprite sheets.
    private readonly float[] _premultipliedLinear = new float[256 * 256];
    private readonly Mesh[] _meshes;
    private readonly Material[] _meshMaterials;
    private readonly Sprite[] _crystalCollectionSprites;
    private readonly SpriteEmissionFrame[] _meshEmission;

    public SpriteAnimationLibrary()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        TextAsset asset = Resources.Load<TextAsset>("Visual/Sprites/Catalog/Catalog")
            ?? throw new InvalidOperationException("The sprite animation catalog is missing.");
        Catalog data = JsonUtility.FromJson<Catalog>(asset.text)
            ?? throw new InvalidOperationException("The sprite animation catalog is invalid.");
        Shader shader = Resources.Load<Shader>("Shaders/Effects/Cells/Destruction/CellDestruction")
            ?? throw new InvalidOperationException("The world-grid effect shader is missing.");
        var emittingMeshes = new HashSet<int>();
        foreach (Clip clip in data.Clips)
        {
            if (clip.Id is "Heal" or "Hurt")
            {
                foreach (Frame frame in clip.Frames) { emittingMeshes.Add(frame.Mesh); }
            }
        }
        var emittingTextures = new HashSet<string>(StringComparer.Ordinal);
        foreach (int mesh in emittingMeshes) { emittingTextures.Add(data.Meshes[mesh].Texture); }
        var emissionPixels = new Dictionary<string, (Color32[] Pixels, int Width, int Height)>(StringComparer.Ordinal);
        _meshEmission = new SpriteEmissionFrame[data.Meshes.Length];
        _meshes = new Mesh[data.Meshes.Length];
        _meshMaterials = new Material[data.Meshes.Length];
        for (int index = 0; index < data.Meshes.Length; index++)
        {
            MeshData source = data.Meshes[index];
            if (!_materials.TryGetValue(source.Texture, out Material material))
            {
                Texture2D texture = ImportTexture(source.Texture,
                    emittingTextures.Contains(source.Texture) ? emissionPixels : null);
                material = new Material(shader) { name = source.Texture, mainTexture = texture };
                material.SetVector("_SourceTextureSize", new Vector4(texture.width, texture.height, 0, 0));
                _materials.Add(source.Texture, material);
            }

            var colors = new Color32[source.Vertices.Length];
            for (int vertex = 0; vertex < colors.Length; vertex++) { colors[vertex] = new Color32(255, 255, 255, 255); }
            var mesh = new Mesh { name = source.Name, vertices = source.Vertices, uv = source.Uv, colors32 = colors, triangles = source.Triangles };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: true);
            _meshes[index] = mesh;
            _meshMaterials[index] = material;
        }

        foreach (int index in emittingMeshes)
        {
            MeshData mesh = data.Meshes[index];
            var pixels = emissionPixels[mesh.Texture];
            Vector2 uvMin = mesh.Uv[0], uvMax = mesh.Uv[0];
            Vector3 min = mesh.Vertices[0], max = mesh.Vertices[0];
            if (mesh.Vertices.Length != 4 || mesh.Triangles.Length != 6)
            {
                throw new InvalidOperationException("Heal/Hurt emission requires the authored rectangular quad.");
            }
            foreach (Vector2 uv in mesh.Uv) { uvMin = Vector2.Min(uvMin, uv); uvMax = Vector2.Max(uvMax, uv); }
            foreach (Vector3 vertex in mesh.Vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
            int x = Mathf.RoundToInt(uvMin.x * pixels.Width), y = Mathf.RoundToInt(uvMin.y * pixels.Height);
            var rect = new RectInt(x, y, Mathf.RoundToInt(uvMax.x * pixels.Width) - x,
                Mathf.RoundToInt(uvMax.y * pixels.Height) - y);
            SpriteEmissionFrame emission = SpriteEmissionIntegrator.IntegrateFrame(pixels.Pixels, pixels.Width, rect,
                new Vector2(max.x - min.x, max.y - min.y));
            _meshEmission[index] = emission with { Centroid = new Vector2(
                (min.x + max.x) * 0.5f + emission.Centroid.x,
                (min.y + max.y) * 0.5f + emission.Centroid.y) };
        }

        foreach (Clip clip in data.Clips)
        {
            if (clip.Duration <= 0f || clip.Frames.Length == 0) { throw new InvalidOperationException($"Invalid sprite animation clip: {clip.Id}."); }
            _clips.Add(clip.Id, clip);
        }

        _crystalCollectionSprites = LoadCrystalCollectionSprites();
        // A first gunshot can arrive inside NetworkService's synchronous packet
        // drain. Load its texture and both materials with the rest of the
        // visual library so the packet handler only activates an effect.
        _ = GunLineMaterial;
        _ = GunGlowMaterial;
        double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency;
        if (elapsedMs >= 4.0)
        {
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            string allocation = allocatedBytes > 0 ? $"{allocatedBytes / 1024} КБ" : "учёт выделений недоступен";
            Kern.Diagnostics.Frames.FrameEventLog.Record(
                $"библиотека визуальных эффектов: {data.Meshes.Length} мешей, {_textures.Count} текстур, " +
                $"{elapsedMs:F1} мс, {allocation}");
        }
    }

    internal Material GunLineMaterial => GetGunMaterial("GunLine", Texture2D.whiteTexture);
    internal Material GunGlowMaterial
    {
        get
        {
            if (_materials.TryGetValue("GunGlow", out Material material))
            {
                return material;
            }

            return GetGunMaterial("GunGlow", ImportTexture("Visual/Sprites/Textures/GunGlow/GunGlow"));
        }
    }

    private Material GetGunMaterial(string key, Texture2D texture)
    {
        if (_materials.TryGetValue(key, out Material material))
        {
            return material;
        }

        Shader shader = Resources.Load<Shader>("Shaders/Effects/Cells/Destruction/CellDestruction")
            ?? throw new InvalidOperationException("The world-grid effect shader is missing.");
        material = new Material(shader) { name = key, mainTexture = texture };
        material.SetVector("_SourceTextureSize", new Vector4(texture.width, texture.height, 0f, 0f));
        _materials.Add(key, material);
        return material;
    }

    internal Clip Get(string id) => _clips[id];
    internal Mesh MeshAt(int index) => _meshes[index];
    internal Material MaterialAt(int index) => _meshMaterials[index];
    internal SpriteEmissionFrame EmissionAt(int index) => _meshEmission[index];

    internal Sprite GetCrystalCollectionSprite(string colorLetter)
    {
        int index = colorLetter switch
        {
            "g" => 0,
            "r" => 1,
            "v" => 2,
            "b" => 3,
            "w" => 4,
            "c" => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(colorLetter), colorLetter, "Unknown crystal color."),
        };
        return _crystalCollectionSprites[index];
    }

    private Sprite[] LoadCrystalCollectionSprites()
    {
        const string resource = "Visual/Sprites/Textures/MineralsPanel/MineralsPanelImage";
        TextAsset asset = Resources.Load<TextAsset>(resource)
            ?? throw new InvalidOperationException("The source crystal collection atlas is missing.");
        Texture2D atlas = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            asset.bytes, resource, RuntimeTextureColorSpace.Srgb, FilterMode.Point, TextureWrapMode.Clamp,
            makeNoLongerReadable: true)
            ?? throw new InvalidOperationException("Cannot decode the source crystal collection atlas.");
        if (atlas.width != 200 || atlas.height != 12)
        {
            UnityEngine.Object.Destroy(atlas);
            throw new InvalidOperationException($"Crystal collection atlas must be 200x12, got {atlas.width}x{atlas.height}.");
        }

        _textures.Add(atlas);
        var sprites = new Sprite[6];
        for (int index = 0; index < sprites.Length; index++)
        {
            sprites[index] = Sprite.Create(atlas, new Rect(index * 12, 0, 12, 12),
                new Vector2(0.5f, 0.5f), 16f);
            _sprites.Add(sprites[index]);
        }

        return sprites;
    }

    private Texture2D ImportTexture(string resource,
        Dictionary<string, (Color32[] Pixels, int Width, int Height)>? emissionPixels = null)
    {
        TextAsset image = Resources.Load<TextAsset>(resource)
            ?? throw new InvalidOperationException($"Missing sprite animation texture: {resource}.");
        Texture2D source = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            image.bytes, resource, RuntimeTextureColorSpace.Srgb, FilterMode.Point, TextureWrapMode.Clamp,
            makeNoLongerReadable: false)
            ?? throw new InvalidOperationException($"Cannot decode sprite animation texture: {resource}.");
        try
        {
            Color32[] encoded = source.GetPixels32();
            emissionPixels?.Add(resource, (encoded, source.width, source.height));
            var pixels = new Color[encoded.Length];
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 value = encoded[index];
                float alpha = value.a / 255f;
                pixels[index] = new Color(PremultipliedLinear(value.r, value.a),
                    PremultipliedLinear(value.g, value.a), PremultipliedLinear(value.b, value.a), alpha);
            }

            Texture2D texture = RuntimeTextureFactory.CreateRGBAHalfNoMip(source.width, source.height,
                resource, RuntimeTextureColorSpace.Linear, FilterMode.Point, TextureWrapMode.Clamp);
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _textures.Add(texture);
            return texture;
        }
        finally { UnityEngine.Object.Destroy(source); }
    }

    private float PremultipliedLinear(byte channel, byte alpha)
    {
        if (channel == 0 || alpha == 0)
        {
            return 0f;
        }
        int key = (channel << 8) | alpha;
        float value = _premultipliedLinear[key];
        if (value == 0f)
        {
            value = Mathf.GammaToLinearSpace(channel / 255f * (alpha / 255f));
            _premultipliedLinear[key] = value;
        }
        return value;
    }

    public void Dispose()
    {
        foreach (Mesh mesh in _meshes) { UnityEngine.Object.Destroy(mesh); }
        foreach (Material material in _materials.Values) { UnityEngine.Object.Destroy(material); }
        foreach (Sprite sprite in _sprites) { UnityEngine.Object.Destroy(sprite); }
        foreach (Texture2D texture in _textures) { UnityEngine.Object.Destroy(texture); }
    }

    [Serializable]
    internal sealed class Clip
    {
        public string Id = string.Empty;
        public float Duration = 0f;
        public bool Loop = false;
        public int SortingOrder = 0;
        public Color Color = Color.white;
        public Vector3 Scale = Vector3.one;
        public Frame[] Frames = Array.Empty<Frame>();
    }

    [Serializable]
    internal sealed class Frame
    {
        public float Time = 0f;
        public int Mesh = 0;
    }

    [Serializable]
    private sealed class MeshData
    {
        public string Name = string.Empty;
        public string Texture = string.Empty;
        public Vector3[] Vertices = Array.Empty<Vector3>();
        public Vector2[] Uv = Array.Empty<Vector2>();
        public int[] Triangles = Array.Empty<int>();
    }

    [Serializable]
    private sealed class Catalog
    {
        public MeshData[] Meshes = Array.Empty<MeshData>();
        public Clip[] Clips = Array.Empty<Clip>();
    }
}
