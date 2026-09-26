#nullable enable

namespace Kern.Tests.World;

using System;
using System.Collections.Generic;
using Kern.World;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Контракт палитры цветов клеток.
///
/// Ожидаемые значения записаны литералами, а не берутся из
/// <see cref="MapBlockColors"/>: сравнение хелпера с самим собой не является
/// доказательством.
/// </summary>
[TestFixture]
public class MapBlockColorsTests
{
    [Test]
    public void EveryNamedCellType_HasAuthoredColor()
    {
        var missing = new List<string>();
        foreach (CellType type in NamedCellTypes())
        {
            if (!MapBlockColors.IsAuthored(type))
            {
                missing.Add($"{type} ({(int)type})");
            }
        }

        Assert.That(
            missing,
            Is.Empty,
            "Каждая порода клетки обязана иметь цвет в палитре карты. " +
            "Добавьте Set(...) в MapBlockColors.BuildPalette.");
    }

    [Test]
    public void EveryPaletteEntry_IsReachableFromNamedCellType()
    {
        var named = new HashSet<byte>();
        foreach (CellType type in NamedCellTypes())
        {
            named.Add((byte)type);
        }

        var orphaned = new List<int>();
        for (int index = 0; index < 256; index++)
        {
            if (MapBlockColors.IsAuthored((CellType)index) && !named.Contains((byte)index))
            {
                orphaned.Add(index);
            }
        }

        Assert.That(orphaned, Is.Empty, "В палитре есть цвета для несуществующих типов клеток.");
    }

    [Test]
    public void UnnamedNumericValues_AreNotAuthored_AndReportMissingData()
    {
        var named = new HashSet<byte>();
        foreach (CellType type in NamedCellTypes())
        {
            named.Add((byte)type);
        }

        for (int index = 0; index < 256; index++)
        {
            if (named.Contains((byte)index))
            {
                continue;
            }

            Assert.That(
                MapBlockColors.IsAuthored((CellType)index),
                Is.False,
                $"Значение {index} не является типом клетки и не должно быть помечено как описанное.");
            Assert.That(
                MapBlockColors.GetColor32((CellType)index),
                Is.EqualTo(MapBlockColors.UnknownColor),
                $"Значение {index} должно давать маркер «нет данных», а не произвольный цвет.");
        }
    }

    [TestCase((CellType)91, 255, 90, 0)]      // Lava
    [TestCase((CellType)32, 0, 0, 0)]          // Empty
    [TestCase((CellType)35, 68, 68, 68)]       // Road
    [TestCase((CellType)107, 8, 215, 100)]     // Green
    [TestCase((CellType)103, 133, 81, 166)]    // Rock
    [TestCase((CellType)62, 255, 204, 204)]    // RustySand
    public void AuthoredColors_AreByteExact(CellType type, int r, int g, int b)
    {
        // Регрессия на прежнее хранение в Color: Color(r / 256f) → byte(c * 255f)
        // срезал по единице в каждом ненулевом канале, и палитра темнела.
        Color32 color = MapBlockColors.GetColor32(type);
        Assert.That(color.r, Is.EqualTo((byte)r), $"red of '{type}'");
        Assert.That(color.g, Is.EqualTo((byte)g), $"green of '{type}'");
        Assert.That(color.b, Is.EqualTo((byte)b), $"blue of '{type}'");
    }

    [TestCase((CellType)29, 56, 52, 48)]      // BuildingRoad
    [TestCase((CellType)30, 68, 17, 17)]      // Gate
    [TestCase((CellType)31, 40, 12, 6)]       // VolcanoBackground
    [TestCase((CellType)39, 60, 72, 84)]      // PolymerRoad
    [TestCase((CellType)76, 46, 42, 54)]      // DeepObsidianRock
    [TestCase((CellType)77, 40, 160, 160)]    // DeepTurquoiseRock
    [TestCase((CellType)78, 200, 120, 220)]   // DeepRainbowRock
    [TestCase((CellType)79, 180, 90, 140)]    // DeepStripedRock
    [TestCase((CellType)80, 24, 72, 72)]      // MilitaryBlockFrame
    [TestCase((CellType)81, 17, 85, 85)]      // MilitaryBlock
    [TestCase((CellType)87, 255, 180, 255)]   // SuperRainbow
    [TestCase((CellType)88, 170, 165, 150)]   // Skull
    public void PreviouslyMissingColors_AreNowAuthored(CellType type, int r, int g, int b)
    {
        // Эти 12 типов не имели записи и молча попадали в градиентную формулу.
        Assert.That(MapBlockColors.IsAuthored(type), Is.True, $"'{type}' должен быть описан в палитре.");
        Color32 color = MapBlockColors.GetColor32(type);
        Assert.That(color.r, Is.EqualTo((byte)r), $"red of '{type}'");
        Assert.That(color.g, Is.EqualTo((byte)g), $"green of '{type}'");
        Assert.That(color.b, Is.EqualTo((byte)b), $"blue of '{type}'");
    }

    [Test]
    public void GetPackedColor_MatchesColor32ArgbLayout()
    {
        foreach (CellType type in NamedCellTypes())
        {
            if (!MapBlockColors.IsAuthored(type))
            {
                continue;
            }

            Color32 color = MapBlockColors.GetColor32(type);
            int expected = unchecked((int)(((uint)color.a << 24) |
                ((uint)color.r << 16) |
                ((uint)color.g << 8) |
                color.b));

            Assert.That(MapBlockColors.GetPackedColor(type), Is.EqualTo(expected), $"'{type}'");
        }
    }

    [Test]
    public void GetColor_ProjectsColor32WithoutShiftingChannels()
    {
        // Смысл проверки — ни один канал не смещается. Точность до бита здесь не
        // утверждается: правило округления при касте Color → Color32 задаёт Unity,
        // а не этот проект. Растеризаторы карты байты получают напрямую через
        // GetCellMinimapColor32 и в этот путь не ходят вовсе.
        foreach (CellType type in NamedCellTypes())
        {
            Color32 expected = MapBlockColors.GetColor32(type);
            Color actual = MapBlockColors.GetColor(type);

            Assert.That(actual.r, Is.EqualTo(expected.r / 255f).Within(1f / 512f), $"red of '{type}'");
            Assert.That(actual.g, Is.EqualTo(expected.g / 255f).Within(1f / 512f), $"green of '{type}'");
            Assert.That(actual.b, Is.EqualTo(expected.b / 255f).Within(1f / 512f), $"blue of '{type}'");
            Assert.That(actual.a, Is.EqualTo(expected.a / 255f).Within(1f / 512f), $"alpha of '{type}'");
        }
    }

    private static CellType[] NamedCellTypes()
    {
        var values = Enum.GetValues(typeof(CellType));
        var types = new CellType[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            types[index] = (CellType)values.GetValue(index)!;
        }

        return types;
    }
}
