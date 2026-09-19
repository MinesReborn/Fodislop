#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
[Category("FuzzPure")]
public class MapCellConfigCatalogFuzzTests
{
    [Test]
    public void LoadThenGetConfig_ReturnsEqualValue()
    {
        var configs = new CellConfigurationPacket[10];
        for (int i = 0; i < 10; i++)
            configs[i] = MakeConfig((CellType)i, CellAnimationType.None, 0);

        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(configs, null);

        for (int i = 0; i < 10; i++)
            Assert.That(catalog.GetCellConfig((CellType)i), Is.EqualTo(configs[i]), $"i={i}");
    }

    [Test]
    public void GetCellConfig_OutOfRange_Throws()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[] { MakeConfig(CellType.Empty, CellAnimationType.None, 0) }, null);
        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig((CellType)1));
        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig((CellType)255));
    }

    [Test]
    public void LoadConfigurations_NullInput_Throws()
    {
        var catalog = new MapCellConfigCatalog();
        Assert.Throws<InvalidDataException>(() => catalog.LoadConfigurations(null, null));
    }

    [Test]
    public void LoadConfigurations_EmptyArray_Throws()
    {
        var catalog = new MapCellConfigCatalog();
        Assert.Throws<InvalidDataException>(() => catalog.LoadConfigurations(Array.Empty<CellConfigurationPacket>(), null));
    }

    [Test]
    public void Reset_ThrowsOnGetConfig()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[] { MakeConfig(CellType.Empty, CellAnimationType.None, 0) }, null);
        catalog.Reset();
        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig(CellType.Empty));
    }

    [Test]
    public void MoveCooldown_Zero_Throws()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[] { MakeConfig(CellType.Empty, CellAnimationType.None, 0) }, null);
        catalog.UpdateMovementSpeeds(new MovementSpeedPacket(new Dictionary<CellType, ushort> { [CellType.Empty] = 0 }));
        Assert.Throws<InvalidDataException>(() => catalog.GetMoveCooldown(CellType.Empty));
    }

    [Test]
    public void MoveCooldown_MissingEntry_Throws()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[] { MakeConfig(CellType.Empty, CellAnimationType.None, 0) }, null);
        Assert.Throws<InvalidOperationException>(() => catalog.GetMoveCooldown(CellType.Rock));
    }

    [Test]
    public void MoveCooldown_NullPacket_NoEffect()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[] { MakeConfig(CellType.Empty, CellAnimationType.None, 0) }, null);
        catalog.UpdateMovementSpeeds(new MovementSpeedPacket((IDictionary<CellType, ushort>?)null!));
        Assert.Throws<InvalidOperationException>(() => catalog.GetMoveCooldown(CellType.Empty));
    }

    [Test]
    public void AnimatedConfig_ZeroAnimationSpeed_Throws()
    {
        var catalog = new MapCellConfigCatalog();
        var configs = new[] { MakeConfig(CellType.Empty, CellAnimationType.Blinking, 0) };
        Assert.Throws<InvalidDataException>(() => catalog.LoadConfigurations(configs, null));
    }

    [Test]
    public void HasAnimation_TrueOnlyWhenAnimationNotNone()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[]
        {
            new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.None, 0, 0, 0, 0),
            new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.Blinking, 5, 0, 0, 0),
        }, null);
        Assert.That(catalog.HasAnimation(CellType.Empty), Is.False);
        Assert.That(catalog.HasAnimation(CellType.Rock), Is.True);
    }

    [Test]
    public void GetAnimationFrameHeight_FrameOffsetTimesCellSize()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[]
        {
            new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.Blinking, 5, 3, 0, 0),
        }, null);
        int frameHeight = catalog.GetAnimationFrameHeight(CellType.Empty);
        Assert.That(frameHeight, Is.EqualTo(3 * 32));
    }

    [Test]
    public void GetAnimationSpeed_ReturnsConfigValue()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[]
        {
            new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.Blinking, 7, 0, 0, 0),
        }, null);
        Assert.That(catalog.GetAnimationSpeed(CellType.Empty), Is.EqualTo((byte)7));
    }

    [Test]
    public void GetCellMinimapColor_ColorZero_FallsBackToDefault()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations(new[] { MakeConfig(CellType.Empty, CellAnimationType.None, 0) }, null);
        Color fallback = MapBlockColors.GetColor(CellType.Empty);
        Color c = catalog.GetCellMinimapColor(CellType.Empty);
        Assert.That(c, Is.EqualTo(fallback));
    }

    [Test]
    public void GetCellMinimapColor_NonZeroColor_ProducesColor()
    {
        var catalog = new MapCellConfigCatalog();
        const int argb = unchecked((int)0xFF804020);
        catalog.LoadConfigurations(new[]
        {
            new CellConfigurationPacket(CellConfigProperties.None, CellDistortionType.Neutral, CellAnimationType.None, 0, 0, argb, 0),
        }, null);
        Color c = catalog.GetCellMinimapColor(CellType.Empty);
        Assert.That(c.r, Is.EqualTo(0x80 / 255f).Within(0.001f));
        Assert.That(c.g, Is.EqualTo(0x40 / 255f).Within(0.001f));
        Assert.That(c.b, Is.EqualTo(0x20 / 255f).Within(0.001f));
        Assert.That(c.a, Is.EqualTo(1f));
    }

    [TestCase(CellType.WhiteSand, true)]
    [TestCase(CellType.Lava, true)]
    [TestCase(CellType.Empty, false)]
    [TestCase(CellType.Rock, false)]
    public void IsRoundableLoose_DocumentedTypes(CellType type, bool expected)
    {
        Assert.That(MapCellConfigCatalog.IsRoundableLoose(type), Is.EqualTo(expected));
    }

    [TestCase(CellType.Road, true)]
    [TestCase(CellType.GoldenRoad, true)]
    [TestCase(CellType.BuildingRoad, true)]
    [TestCase(CellType.PolymerRoad, true)]
    [TestCase(CellType.Empty, false)]
    [TestCase(CellType.Rock, false)]
    public void IsRoad_DocumentedTypes(CellType type, bool expected)
    {
        Assert.That(MapCellConfigCatalog.IsRoad(type), Is.EqualTo(expected));
    }


    private static CellConfigurationPacket MakeConfig(CellType type, CellAnimationType anim, byte animSpeed)
    {
        return new CellConfigurationPacket(
            CellConfigProperties.None,
            CellDistortionType.Neutral,
            anim,
            animSpeed,
            0,
            0,
            0);
    }
}
