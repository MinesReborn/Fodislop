#nullable enable

namespace Kern.Tests.World;

using System;
using System.Collections.Generic;
using System.IO;
using Kern.Core;
using Kern.World;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using MinesServer.Networking.Server.Packets.Information;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class MapCellConfigCatalogTests
{
    private static CellConfigurationPacket CreateConfig(
        CellAnimationType animation = CellAnimationType.None,
        byte animationSpeed = 0,
        byte frameOffset = 0,
        int color = 0,
        ushort properties = 0)
    {
        return new CellConfigurationPacket(
            (MinesServer.Networking.Server.Packets.Connection.CellConfigProperties)properties,
            (CellDistortionType)0,
            animation,
            animationSpeed,
            frameOffset,
            color,
            0);
    }

    [Test]
    public void LoadConfigurations_ValidConfigurations_PopulatesState()
    {
        var catalog = new MapCellConfigCatalog();
        var configs = new[]
        {
            CreateConfig(),
            CreateConfig(color: unchecked((int)0xFF00FF00)), // Green
        };
        byte[][] tileGroups =
        [
            [0],
            [1],
        ];

        catalog.LoadConfigurations(configs, tileGroups);

        Assert.AreEqual(configs[0], catalog.GetCellConfig((CellType)0));
        Assert.AreEqual(configs[1], catalog.GetCellConfig((CellType)1));

        Assert.IsTrue(catalog.TryGetTileGroup((CellType)0, out int group0));
        Assert.AreEqual(0, group0);

        Assert.IsTrue(catalog.TryGetTileGroup((CellType)1, out int group1));
        Assert.AreEqual(1, group1);
    }

    [Test]
    public void LoadConfigurations_NullOrEmpty_ThrowsInvalidDataException()
    {
        var catalog = new MapCellConfigCatalog();

        Assert.Throws<InvalidDataException>(() => catalog.LoadConfigurations(null, null));
        Assert.Throws<InvalidDataException>(() => catalog.LoadConfigurations([], null));
    }

    [Test]
    public void LoadConfigurations_AnimatedTextureWithZeroSpeed_ThrowsInvalidDataException()
    {
        var catalog = new MapCellConfigCatalog();
        var configs = new[]
        {
            CreateConfig(animation: CellAnimationType.Blinking, animationSpeed: 0),
        };

        var ex = Assert.Throws<InvalidDataException>(() => catalog.LoadConfigurations(configs, null));
        StringAssert.Contains("AnimationSpeed=0", ex.Message);
    }

    [Test]
    public void GetCellConfig_BeforeLoading_ThrowsInvalidOperationException()
    {
        var catalog = new MapCellConfigCatalog();

        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig((CellType)0));
    }

    [Test]
    public void GetCellConfig_OutOfBounds_ThrowsInvalidOperationException()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations([CreateConfig()], null);

        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig((CellType)5));
        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig(unchecked((CellType)(-1))));
    }

    [Test]
    public void UpdateMovementSpeeds_And_GetMoveCooldown_ValidSpeed_ReturnsSeconds()
    {
        var catalog = new MapCellConfigCatalog();
        var packet = new MovementSpeedPacket(new Dictionary<CellType, ushort>
        {
            { (CellType)1, 500 },
            { (CellType)2, 1000 },
        });

        catalog.UpdateMovementSpeeds(packet);

        Assert.AreEqual(0.5f, catalog.GetMoveCooldown((CellType)1), 0.0001f);
        Assert.AreEqual(1.0f, catalog.GetMoveCooldown((CellType)2), 0.0001f);
    }

    [Test]
    public void GetMoveCooldown_MissingCellType_ThrowsInvalidOperationException()
    {
        var catalog = new MapCellConfigCatalog();

        Assert.Throws<InvalidOperationException>(() => catalog.GetMoveCooldown((CellType)99));
    }

    [Test]
    public void GetMoveCooldown_ZeroSpeed_ThrowsInvalidDataException()
    {
        var catalog = new MapCellConfigCatalog();
        var packet = new MovementSpeedPacket(new Dictionary<CellType, ushort>
        {
            { (CellType)1, 0 },
        });

        catalog.UpdateMovementSpeeds(packet);

        Assert.Throws<InvalidDataException>(() => catalog.GetMoveCooldown((CellType)1));
    }

    [Test]
    public void GetCellMinimapColor_AuthoredType_IgnoresServerColor()
    {
        var catalog = new MapCellConfigCatalog();
        int argb = unchecked((int)0xFF112233);
        catalog.LoadConfigurations([CreateConfig(color: argb)], null);

        Color color = catalog.GetCellMinimapColor((CellType)0);

        // (CellType)0 описан в палитре, поэтому серверное значение не читается.
        Color32 expected = MapBlockColors.GetColor32((CellType)0);
        Assert.AreEqual(expected.r / 255f, color.r, 0.001f);
        Assert.AreEqual(expected.g / 255f, color.g, 0.001f);
        Assert.AreEqual(expected.b / 255f, color.b, 0.001f);
    }

    [Test]
    public void GetCellMinimapColor_AuthoredType_IgnoresSaturatedWhiteServerColor()
    {
        // Сервер кладёт 0xFFFFFFFF в Color для всех 256 типов. Если бы пакет был
        // источником правды, карта и мини-карта были бы полностью белыми.
        var catalog = new MapCellConfigCatalog();
        var configs = new CellConfigurationPacket[256];
        for (int index = 0; index < configs.Length; index++)
        {
            configs[index] = CreateConfig(color: unchecked((int)0xFFFFFFFF));
        }

        catalog.LoadConfigurations(configs, null);

        var values = Enum.GetValues(typeof(CellType));
        foreach (CellType type in values)
        {
            Color32 color = catalog.GetCellMinimapColor32(type);
            Assert.AreEqual(
                MapBlockColors.GetColor32(type),
                color,
                $"Cell type '{type}' must not take the server's white colour.");
        }
    }

    [Test]
    public void GetCellMinimapColor_UnauthoredType_UnpacksServerColor()
    {
        var catalog = new MapCellConfigCatalog();
        var configs = new CellConfigurationPacket[16];
        for (int index = 0; index < configs.Length; index++)
        {
            configs[index] = CreateConfig();
        }

        configs[5] = CreateConfig(color: unchecked((int)0xFF804020));
        catalog.LoadConfigurations(configs, null);

        Color color = catalog.GetCellMinimapColor((CellType)5);

        Assert.AreEqual(0x80 / 255f, color.r, 0.001f);
        Assert.AreEqual(0x40 / 255f, color.g, 0.001f);
        Assert.AreEqual(0x20 / 255f, color.b, 0.001f);
        Assert.AreEqual(1f, color.a, 0.001f);
    }

    [Test]
    public void GetCellMinimapColor_UnauthoredType_ZeroServerColor_UsesGreyPlaceholder()
    {
        var catalog = new MapCellConfigCatalog();
        var configs = new CellConfigurationPacket[16];
        for (int index = 0; index < configs.Length; index++)
        {
            configs[index] = CreateConfig(color: 0);
        }

        catalog.LoadConfigurations(configs, null);

        Color32 color = catalog.GetCellMinimapColor32((CellType)5);

        Assert.AreEqual(MapBlockColors.MissingServerColor, color);
    }

    [Test]
    public void GetCellMinimapColor_WithZeroColor_UsesFallback()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations([CreateConfig(color: 0)], null);

        Color color = catalog.GetCellMinimapColor((CellType)0);
        Color expectedFallback = MapBlockColors.GetColor((CellType)0);

        Assert.AreEqual(expectedFallback.r, color.r, 0.001f);
        Assert.AreEqual(expectedFallback.g, color.g, 0.001f);
        Assert.AreEqual(expectedFallback.b, color.b, 0.001f);
    }

    [Test]
    public void AnimationProperties_QueriesCorrectly()
    {
        var catalog = new MapCellConfigCatalog();
        var configs = new[]
        {
            CreateConfig(animation: CellAnimationType.None, animationSpeed: 0, frameOffset: 0),
            CreateConfig(animation: CellAnimationType.Blinking, animationSpeed: 5, frameOffset: 3),
        };

        catalog.LoadConfigurations(configs, null);

        Assert.IsFalse(catalog.HasAnimation((CellType)0));
        Assert.AreEqual(0, catalog.GetAnimationSpeed((CellType)0));
        Assert.AreEqual(0, catalog.GetAnimationFrameHeight((CellType)0));

        Assert.IsTrue(catalog.HasAnimation((CellType)1));
        Assert.AreEqual(5, catalog.GetAnimationSpeed((CellType)1));
        Assert.AreEqual(3 * RenderingConstants.CELL_SIZE, catalog.GetAnimationFrameHeight((CellType)1));
    }

    [Test]
    public void Reset_ClearsAllState()
    {
        var catalog = new MapCellConfigCatalog();
        catalog.LoadConfigurations([CreateConfig()], [[0]]);
        catalog.UpdateMovementSpeeds(new MovementSpeedPacket(
            new Dictionary<CellType, ushort> { { (CellType)0, 100 } }));

        catalog.Reset();

        Assert.IsFalse(catalog.TryGetTileGroup((CellType)0, out _));
        Assert.Throws<InvalidOperationException>(() => catalog.GetMoveCooldown((CellType)0));
        Assert.Throws<InvalidOperationException>(() => catalog.GetCellConfig((CellType)0));
    }

    [Test]
    public void IsRoundableLoose_CorrectlyIdentifiesCellTypes()
    {
        Assert.IsTrue(MapCellConfigCatalog.IsRoundableLoose(CellType.WhiteSand));
        Assert.IsTrue(MapCellConfigCatalog.IsRoundableLoose(CellType.Lava));
        Assert.IsFalse(MapCellConfigCatalog.IsRoundableLoose(CellType.Empty));
        Assert.IsFalse(MapCellConfigCatalog.IsRoundableLoose(CellType.Rock));
    }

    [Test]
    public void IsRoad_CorrectlyIdentifiesCellTypes()
    {
        Assert.IsTrue(MapCellConfigCatalog.IsRoad(CellType.Road));
        Assert.IsTrue(MapCellConfigCatalog.IsRoad(CellType.GoldenRoad));
        Assert.IsTrue(MapCellConfigCatalog.IsRoad(CellType.BuildingRoad));
        Assert.IsTrue(MapCellConfigCatalog.IsRoad(CellType.PolymerRoad));
        Assert.IsFalse(MapCellConfigCatalog.IsRoad(CellType.Empty));
        Assert.IsFalse(MapCellConfigCatalog.IsRoad(CellType.Rock));
    }
}
