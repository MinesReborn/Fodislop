#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Номера отладочных видов едут в шейдер как есть, и обе стороны обязаны
// совпадать поимённо.
//
// Проверка появилась после того, как удалённый вид оставил дыру в нумерации:
// глобаль шейдера пережила доменную перезагрузку с номером, которого в
// перечислении уже не было, ветка не нашлась, и функция провалилась в
// последнюю — мир залило её цветом. Расхождение нумерации нельзя ловить
// глазами по кадру.
[TestFixture]
public sealed class TerrainDebugViewContractTests
{
    private static string ShaderSource() => File.ReadAllText(
        Path.Combine(Application.dataPath, "Shaders", "Terrain", "TerrainDebugView.hlsl"));

    private static Dictionary<string, int> ShaderConstants()
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
            ShaderSource(),
            @"static const int KERN_TERRAIN_DEBUG_(\w+) = (\d+);"))
        {
            found[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
        }

        return found;
    }

    private static string ShaderName(TerrainDebugView view)
    {
        var builder = new StringBuilder();
        string name = view.ToString();
        for (int index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(name[index]));
        }

        return builder.ToString();
    }

    [Test]
    public void EveryViewHasAShaderConstantWithTheSameNumber()
    {
        Dictionary<string, int> constants = ShaderConstants();
        Assert.That(constants, Is.Not.Empty, "В шейдере не найдено ни одной константы вида.");

        foreach (TerrainDebugView view in Enum.GetValues(typeof(TerrainDebugView)))
        {
            string name = ShaderName(view);
            Assert.That(
                constants.ContainsKey(name),
                Is.True,
                $"Вид {view} есть в C#, но не объявлен в шейдере как KERN_TERRAIN_DEBUG_{name}.");
            Assert.That(
                constants[name],
                Is.EqualTo((int)view),
                $"Номер вида {view} разошёлся между C# и шейдером.");
        }
    }

    [Test]
    public void ShaderDeclaresNoViewThatCSharpDoesNotKnow()
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (TerrainDebugView view in Enum.GetValues(typeof(TerrainDebugView)))
        {
            known.Add(ShaderName(view));
        }

        foreach (KeyValuePair<string, int> constant in ShaderConstants())
        {
            Assert.That(
                known.Contains(constant.Key),
                Is.True,
                $"Шейдер объявляет KERN_TERRAIN_DEBUG_{constant.Key}, которого нет в TerrainDebugView.");
        }
    }

    // Осиротевший номер обязан быть безвредным: проверка активности идёт по
    // диапазону, а не по «не ноль», и дыра в нумерации не должна его открыть.
    [Test]
    public void ActivityCheckIsBoundedByTheDeclaredRange()
    {
        Assert.That(
            ShaderSource(),
            Does.Contain("> KERN_TERRAIN_DEBUG_OFF").And
                .Contain("<= KERN_TERRAIN_DEBUG_BACKGROUND_TILE_IDENTITY"),
            "Активность отладочного вида обязана проверяться диапазоном объявленных номеров.");
    }

    [Test]
    public void BackgroundTileIdentityViewIsolatedAndUsesResolvedAtlasTile()
    {
        string terrainShader = File.ReadAllText(
            Path.Combine(Application.dataPath, "Shaders", "Terrain", "Terrain.shader"));
        string debugShader = ShaderSource();
        string cellDataShader = File.ReadAllText(
            Path.Combine(Application.dataPath, "Shaders", "Terrain", "TerrainCellData.hlsl"));
        string samplingShader = File.ReadAllText(
            Path.Combine(Application.dataPath, "Shaders", "Terrain", "TerrainSampling.hlsl"));

        Assert.That(
            terrainShader,
            Does.Contain("clip(0.5 - input.isForeground)").And
                .Contain("debugTile.identityTileOffsetUV").And
                .Contain("KernTerrainUniqueTileColor("),
            "Режим тайлов фона обязан отсечь передний план и классифицировать фактически выбранный atlas tile.");
        Assert.That(
            debugShader,
            Does.Contain("atlasTexelSize.zw").And
                .Contain("tileOriginPixels").And
                .Contain("(atlasSlot << 14u)").And
                .Contain("(tileCoordinate.y << 7u)").And
                .Contain("tileCoordinate.x"),
            "Цвет классификации обязан напрямую кодировать уникальные координаты atlas tile.");
        Assert.That(
            terrainShader,
            Does.Contain("#define KERN_TERRAIN_DEBUG_BACKGROUND_TILE_VIEW"),
            "Только экранный проход может показывать подложку под полностью закрытыми клетками.");
        Assert.That(
            cellDataShader,
            Does.Contain("occludedBackground = occludedBackground && _TerrainDebugBackgroundTileIdentity == 0"),
            "Проходы поля освещения не должны менять occupancy из-за отладки фона.");
        Assert.That(
            samplingShader,
            Does.Contain("float2 geometryCellPosition").And
                .Contain("+ geometryCellPosition").And
                .Contain("TerrainResolveGeometryTileUV(").And
                .Contain("TerrainSetResolvedTileIdentity(res, baseUV, tileSizeUV)").And
                .Contain("tile.finalUV - baseUV").And
                .Contain("subAtlasSizeUV.y <= 0.0").And
                .Contain("tileSizeUV.y <= 0.0"),
            "Сплошные листы обязаны получать непрерывные UV из фактической деформированной позиции; клеточная UV должна восстанавливаться отдельно для тайловой выборки.");
    }
}
