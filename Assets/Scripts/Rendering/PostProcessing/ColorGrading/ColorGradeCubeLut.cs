#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public enum ColorGradeLutType
{
    OneDimensional = 1,
    ThreeDimensional = 3,
}

public enum ColorGradeLutColorSpace
{
    LinearRec709 = 0,
    SrgbRec709 = 1,
}

public sealed class ColorGradeCubeLut : IDisposable
{
    private readonly Color[] _values;

    private ColorGradeCubeLut(
        ColorGradeLutType type,
        int size,
        Vector3 domainMin,
        Vector3 domainMax,
        Color[] values,
        string path)
    {
        Type = type;
        Size = size;
        DomainMin = domainMin;
        DomainMax = domainMax;
        _values = values;
        Path = path;
        Texture1D = type == ColorGradeLutType.OneDimensional
            ? Create1DTexture(size, values, path)
            : null;
        Texture3D = type == ColorGradeLutType.ThreeDimensional
            ? Create3DTexture(size, values, path)
            : null;
    }

    public ColorGradeLutType Type { get; }

    public int Size { get; }

    public Vector3 DomainMin { get; }

    public Vector3 DomainMax { get; }

    public string Path { get; }

    public Texture2D? Texture1D { get; }

    public Texture3D? Texture3D { get; }

    public static bool TryLoad(string path, out ColorGradeCubeLut? lut, out string error)
    {
        lut = null;
        error = string.Empty;
        try
        {
            if (!File.Exists(path))
            {
                error = $"Lut file does not exist: {path}";
                return false;
            }

            ColorGradeLutType? type = null;
            int size = 0;
            Vector3 domainMin = Vector3.zero;
            Vector3 domainMax = Vector3.one;
            var values = new List<Color>();
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                int comment = line.IndexOf('#');
                if (comment >= 0)
                {
                    line = line[..comment].Trim();
                }

                if (line.Length == 0)
                {
                    continue;
                }

                string[] tokens = line.Split(
                    [' ', '\t'],
                    StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0].ToUpperInvariant())
                {
                    case "TITLE":
                        break;
                    case "LUT_1D_SIZE":
                        type = ColorGradeLutType.OneDimensional;
                        size = ParseSize(tokens, line);
                        break;
                    case "LUT_3D_SIZE":
                        type = ColorGradeLutType.ThreeDimensional;
                        size = ParseSize(tokens, line);
                        break;
                    case "DOMAIN_MIN":
                        domainMin = ParseVector(tokens, line);
                        break;
                    case "DOMAIN_MAX":
                        domainMax = ParseVector(tokens, line);
                        break;
                    default:
                        if (tokens.Length != 3)
                        {
                            throw new FormatException($"Invalid Lut row: {line}");
                        }

                        values.Add(new Color(
                            ParseFloat(tokens[0]),
                            ParseFloat(tokens[1]),
                            ParseFloat(tokens[2]),
                            1f));
                        break;
                }
            }

            if (!type.HasValue || size < 2)
            {
                throw new FormatException(
                    "Lut requires a valid type and a size of at least 2.");
            }

            int maximumSize = type.Value == ColorGradeLutType.OneDimensional ? 4096 : 256;
            if (size > maximumSize)
            {
                throw new FormatException(
                    $"Lut size {size} exceeds the safe maximum of {maximumSize} for {type.Value}.");
            }

            if (values.Count != ExpectedValueCount(type.Value, size))
            {
                throw new FormatException(
                    $"Lut requires exactly the expected number of rows; got {values.Count}.");
            }

            if (!IsFinite(domainMin) ||
                !IsFinite(domainMax) ||
                domainMax.x <= domainMin.x ||
                domainMax.y <= domainMin.y ||
                domainMax.z <= domainMin.z)
            {
                throw new FormatException("Lut DOMAIN_MAX must be greater than DOMAIN_MIN on every channel.");
            }

            lut = new ColorGradeCubeLut(type.Value, size, domainMin, domainMax, values.ToArray(), path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or
            FormatException or
            UnauthorizedAccessException or
            OverflowException)
        {
            error = exception.Message;
            return false;
        }
    }

    public Color Sample(Vector3 color)
    {
        Vector3 normalized = new(
            Mathf.InverseLerp(DomainMin.x, DomainMax.x, color.x),
            Mathf.InverseLerp(DomainMin.y, DomainMax.y, color.y),
            Mathf.InverseLerp(DomainMin.z, DomainMax.z, color.z));
        return Type == ColorGradeLutType.OneDimensional
            ? Sample1D(normalized)
            : Sample3D(normalized);
    }

    public void Dispose()
    {
        if (Texture1D != null)
        {
            DestroyRuntimeTexture(Texture1D);
        }

        if (Texture3D != null)
        {
            DestroyRuntimeTexture(Texture3D);
        }
    }

    private static void DestroyRuntimeTexture(UnityEngine.Object texture)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private Color Sample1D(Vector3 input)
    {
        return new Color(
            SampleLinear(input.x, 0),
            SampleLinear(input.y, 1),
            SampleLinear(input.z, 2),
            1f);
    }

    private float SampleLinear(float coordinate, int channel)
    {
        float position = Mathf.Clamp01(coordinate) * (Size - 1);
        int lower = Mathf.FloorToInt(position);
        int upper = Mathf.Min(lower + 1, Size - 1);
        float weight = position - lower;
        float left = channel switch
        {
            0 => _values[lower].r,
            1 => _values[lower].g,
            _ => _values[lower].b,
        };
        float right = channel switch
        {
            0 => _values[upper].r,
            1 => _values[upper].g,
            _ => _values[upper].b,
        };
        return Mathf.Lerp(left, right, weight);
    }

    private Color Sample3D(Vector3 input)
    {
        Vector3 position = new(
            Mathf.Clamp01(input.x) * (Size - 1),
            Mathf.Clamp01(input.y) * (Size - 1),
            Mathf.Clamp01(input.z) * (Size - 1));
        int x = Mathf.FloorToInt(position.x);
        int y = Mathf.FloorToInt(position.y);
        int z = Mathf.FloorToInt(position.z);
        int x1 = Mathf.Min(x + 1, Size - 1);
        int y1 = Mathf.Min(y + 1, Size - 1);
        int z1 = Mathf.Min(z + 1, Size - 1);
        Vector3 fraction = position - new Vector3(x, y, z);

        Color c000 = Value(x, y, z);
        Color c100 = Value(x1, y, z);
        Color c010 = Value(x, y1, z);
        Color c001 = Value(x, y, z1);
        Color c110 = Value(x1, y1, z);
        Color c101 = Value(x1, y, z1);
        Color c011 = Value(x, y1, z1);
        Color c111 = Value(x1, y1, z1);

        // Tetrahedral interpolation: select one of six tetrahedra in the cell
        // from the ordering of the fractional coordinates.
        if (fraction.x >= fraction.y)
        {
            return fraction.y >= fraction.z
                ? Tetra(c000, c100, c110, c111, fraction.x, fraction.y, fraction.z)
                : fraction.x >= fraction.z
                    ? Tetra(c000, c100, c101, c111, fraction.x, fraction.z, fraction.y)
                    : Tetra(c000, c001, c101, c111, fraction.z, fraction.x, fraction.y);
        }

        return fraction.x >= fraction.z
            ? Tetra(c000, c010, c110, c111, fraction.y, fraction.x, fraction.z)
            : fraction.y >= fraction.z
                ? Tetra(c000, c010, c011, c111, fraction.y, fraction.z, fraction.x)
                : Tetra(c000, c001, c011, c111, fraction.z, fraction.y, fraction.x);
    }

    // The .cube specification writes blue fastest, then green, with red as
    // the outer loop. Keep that source ordering for CPU interpolation.
    private Color Value(int x, int y, int z) => _values[z + y * Size + x * Size * Size];

    private static Color Tetra(Color c0, Color c1, Color c2, Color c3, float a, float b, float c) =>
        c0 + (c1 - c0) * a + (c2 - c1) * b + (c3 - c2) * c;

    private static int ExpectedValueCount(ColorGradeLutType type, int size) =>
        type == ColorGradeLutType.OneDimensional ? size : size * size * size;

    private static int ParseSize(string[] tokens, string line)
    {
        if (tokens.Length != 2 || !int.TryParse(tokens[1], out int size))
        {
            throw new FormatException($"Invalid Lut size: {line}");
        }

        return size;
    }

    private static Vector3 ParseVector(string[] tokens, string line)
    {
        if (tokens.Length != 4)
        {
            throw new FormatException($"Invalid Lut vector: {line}");
        }

        return new Vector3(ParseFloat(tokens[1]), ParseFloat(tokens[2]), ParseFloat(tokens[3]));
    }

    private static float ParseFloat(string value)
    {
        float parsed = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!float.IsFinite(parsed))
        {
            throw new FormatException($"Lut value must be finite: {value}");
        }

        return parsed;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static Texture2D Create1DTexture(int size, Color[] values, string path)
    {
        Texture2D texture = Kern.RuntimeTextureFactory.CreateRGBAFloatNoMip(
            size,
            1,
            $"LUT_1D_{System.IO.Path.GetFileName(path)}",
            Kern.RuntimeTextureColorSpace.Linear,
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        texture.SetPixels(values);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture3D Create3DTexture(int size, Color[] values, string path)
    {
        Texture3D texture = Kern.RuntimeTextureFactory.CreateRGBAFloat3DNoMip(
            size,
            $"LUT_3D_{System.IO.Path.GetFileName(path)}",
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);
        // Unity's Texture3D array is x-fastest, while .cube rows are
        // blue-fastest. Reorder once at load time so shader coordinates remain
        // the intuitive (R, G, B) lookup vector.
        var reordered = new Color[values.Length];
        for (int red = 0; red < size; red++)
        {
            for (int green = 0; green < size; green++)
            {
                for (int blue = 0; blue < size; blue++)
                {
                    int unityIndex = red + green * size + blue * size * size;
                    int cubeIndex = blue + green * size + red * size * size;
                    reordered[unityIndex] = values[cubeIndex];
                }
            }
        }

        texture.SetPixels(reordered);
        texture.Apply(false, true);
        return texture;
    }
}
