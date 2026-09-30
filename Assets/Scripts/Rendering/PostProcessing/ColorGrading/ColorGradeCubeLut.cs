#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Kern.Rendering.PostProcessing;

public enum ColorGradeLutColorSpace
{
    LinearRec709 = 0,
    SrgbRec709 = 1,
}

// 3D-таблица .cube. Одномерные таблицы не поддерживаются: эффект с сервера —
// это объёмный LUT.
public sealed class ColorGradeCubeLut : IDisposable
{
    private const int MaximumSize = 256;

    private ColorGradeCubeLut(int size, Vector3 domainMin, Vector3 domainMax, Texture3D texture)
    {
        Size = size;
        DomainMin = domainMin;
        DomainMax = domainMax;
        Texture3D = texture;
    }

    public int Size { get; }

    public Vector3 DomainMin { get; }

    public Vector3 DomainMax { get; }

    public Texture3D Texture3D { get; }

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
                        throw new FormatException("1D Lut is not supported; only LUT_3D_SIZE tables are accepted.");
                    case "LUT_3D_SIZE":
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

            if (size < 2)
            {
                throw new FormatException("Lut requires LUT_3D_SIZE of at least 2.");
            }

            if (size > MaximumSize)
            {
                throw new FormatException(
                    $"Lut size {size} exceeds the safe maximum of {MaximumSize}.");
            }

            if (values.Count != size * size * size)
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

            lut = new ColorGradeCubeLut(
                size,
                domainMin,
                domainMax,
                CreateTexture(size, values.ToArray(), path));
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

    public void Dispose()
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(Texture3D);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(Texture3D);
        }
    }

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

    private static Texture3D CreateTexture(int size, Color[] values, string path)
    {
        Texture3D texture = Kern.RuntimeTextureFactory.CreateRGBAFloat3DNoMip(
            size,
            $"LUT_3D_{System.IO.Path.GetFileName(path)}",
            FilterMode.Bilinear,
            TextureWrapMode.Clamp);

        // По спецификации .cube (Adobe Cube LUT 1.0) быстрее всех меняется
        // красный индекс, затем зелёный, синий — внешний цикл. Texture3D в
        // Unity тоже x-fastest, поэтому при x = красный строки ложатся без
        // перестановки. Прежняя перестановка считала быстрым синий и меняла R
        // и B местами: любой настоящий .cube применялся с перевёрнутыми
        // оттенками.
        texture.SetPixels(values);
        texture.Apply(false, true);
        return texture;
    }
}
