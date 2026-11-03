#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

// Источник правды формата — TerrainCellFormat.hlsl. C#-зеркало
// (TerrainCellFormat.cs) обязано повторять каждую свою константу оттуда
// под тем же именем и с тем же значением: тест читает сам HLSL.
[TestFixture]
public sealed class TerrainCellFormatMirrorTests
{
    private const string FormatPath = "Assets/Shaders/Terrain/Cells/Format/TerrainCellFormat.hlsl";

    [Test]
    public void EveryMirrorConstantMatchesTheShader()
    {
        Dictionary<string, double> shader = ReadShaderConstants();
        var mismatches = new List<string>();
        foreach (FieldInfo field in typeof(TerrainCellFormat).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string name = ShaderName(field.Name);
            double value = Convert.ToDouble(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
            if (!shader.TryGetValue(name, out double expected))
            {
                mismatches.Add($"{field.Name}: в шейдере нет {name}");
            }
            else if (expected != value)
            {
                mismatches.Add($"{field.Name} = {value}, а {name} = {expected}");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    [Test]
    public void EnumsLandOnShaderCodes()
    {
        Dictionary<string, double> shader = ReadShaderConstants();
        AssertCodes<CellOutline>(shader, "OUTLINE");
        AssertCodes<CellTextureAnchor>(shader, "TEXTURE_ANCHOR");
        AssertCodes<CellAnimationType>(shader, "ANIMATION_TYPE");

        // Базовые коды и дверь проверяются явными ссылками на обе стороны контракта.
        Assert.That(shader["KERN_TERRAIN_OUTLINE_RIGID"], Is.EqualTo((double)CellOutline.Rigid));
        Assert.That(shader["KERN_TERRAIN_OUTLINE_DOOR"], Is.EqualTo((double)CellOutline.Door));
        Assert.That(shader["KERN_TERRAIN_ANIMATION_TYPE_NONE"], Is.EqualTo((double)CellAnimationType.None));
        Assert.That(shader["KERN_TERRAIN_TEXTURE_ANCHOR_CELL"], Is.EqualTo((double)CellTextureAnchor.Cell));
    }

    // Каждое значение перечисления — константа KERN_TERRAIN_<group>_<ИМЯ>
    // с тем же номером.
    private static void AssertCodes<T>(Dictionary<string, double> shader, string group)
        where T : struct, Enum
    {
        foreach (T value in Enum.GetValues(typeof(T)))
        {
            string name = $"KERN_TERRAIN_{group}_{value.ToString().ToUpperInvariant()}";
            Assert.That(shader.ContainsKey(name), $"в шейдере нет {name}");
            Assert.That(shader[name], Is.EqualTo(Convert.ToDouble(value, CultureInfo.InvariantCulture)), name);
        }
    }

    // TypePixelHighShift → KERN_TERRAIN_TYPE_PIXEL_HIGH_SHIFT, OffsetXShift → OFFSET_X_SHIFT.
    private static string ShaderName(string pascal) =>
        "KERN_TERRAIN_" + Regex.Replace(pascal, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "_").ToUpperInvariant();

    private static Dictionary<string, double> ReadShaderConstants()
    {
        string text = File.ReadAllText(Path.Combine(ProjectRoot(), FormatPath));
        var constants = new Dictionary<string, double>();
        foreach (Match match in Regex.Matches(
            text, @"static const (?:uint|int|float) (KERN_TERRAIN_\w+) = ([^;\[\{]+);"))
        {
            constants[match.Groups[1].Value] = Evaluate(match.Groups[2].Value.Trim());
        }

        return constants;
    }

    // Значения формата: целое (десятичное или 0x), с суффиксом u, сдвиг «a << b»
    // или десятичная дробь.
    private static double Evaluate(string expression)
    {
        string[] shift = expression.Split("<<");
        if (shift.Length == 2)
        {
            return (double)((ulong)Evaluate(shift[0].Trim()) << (int)Evaluate(shift[1].Trim()));
        }

        string literal = expression.TrimEnd('u', 'U');
        return literal.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt64(literal[2..], 16)
            : double.Parse(literal, CultureInfo.InvariantCulture);
    }

    private static string ProjectRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, FormatPath)))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException($"Не найден {FormatPath} выше рабочего каталога.");
    }
}
