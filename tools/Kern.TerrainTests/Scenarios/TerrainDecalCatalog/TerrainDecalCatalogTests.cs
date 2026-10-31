#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.TerrainTests;

[TestFixture]
public sealed class TerrainDecalCatalogTests
{
    private const string GroundTexture = "Visual/World/Terrain/Decals/terrain-decals.png";
    private const string RockTexture = "Visual/World/Terrain/Decals/terrain-decals-rock.png";

    [SetUp]
    public void ConfigureCatalog() => TerrainDecalCatalog.Configure(new[] { GroundTexture, RockTexture });

    [TearDown]
    public void ClearCatalog() => TerrainDecalCatalog.Configure(Array.Empty<string>());

    [Test]
    public void PlacementValuesRemainExactInHalfPrecision()
    {
        for (int code = 0; code <= 2048; code++)
        {
            Assert.That((float)(System.Half)code, Is.EqualTo((float)code), $"placement {code}");
        }
    }

    [Test]
    public void CatalogRegistersOnlyConfiguredTextureNames()
    {
        Assert.That(TerrainDecalCatalog.GetTextureNames(), Is.EqualTo(new[] { GroundTexture, RockTexture }));
        Assert.That(TerrainDecalCatalog.TextureIndex(GroundTexture), Is.EqualTo(1));
        Assert.That(TerrainDecalCatalog.TextureIndex(RockTexture), Is.EqualTo(2));
        Assert.That(TerrainDecalCatalog.TextureIndex(null), Is.Zero);
        Assert.Throws<InvalidDataException>(() => TerrainDecalCatalog.TextureIndex("Visual/World/Terrain/Decals/terrain-decals-other.png"));
    }

    [Test]
    public void EachConfiguredAtlasHasOneExistingStagingSource()
    {
        DirectoryInfo? root = FindProjectRoot();
        Assert.That(root, Is.Not.Null, "Project root was not found.");
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root!.FullName, "Assets/Settings/Build/Textures/TextureStagingCatalog.json")));
        foreach (string name in TerrainDecalCatalog.GetTextureNames())
        {
            JsonElement[] entries = catalog.RootElement.GetProperty("Entries").EnumerateArray()
                .Where(entry => entry.GetProperty("Published").GetString() == name).ToArray();
            Assert.That(entries, Has.Length.EqualTo(1), name);
            string source = entries[0].GetProperty("Source").GetString()!;
            Assert.That(File.Exists(Path.Combine(root.FullName, "Assets/Textures", source)), Is.True, source);
        }
    }

    [TestCase(CellType.Empty, GroundTexture)]
    [TestCase(CellType.WorldEdge, RockTexture)]
    [TestCase(CellType.RedRock, RockTexture)]
    [TestCase(CellType.BlackRock, RockTexture)]
    [TestCase(CellType.Rock, null)]
    [TestCase(CellType.WhiteSand, null)]
    public void DecalTextureComesFromCellConfiguration(CellType cellType, string? expected)
    {
        Assert.That(BlockRegistry.Get(cellType).DecalTextureName, Is.EqualTo(expected));
    }

    [Test]
    public void Place_IsDeterministicAndSparse()
    {
        byte textureIndex = TerrainDecalCatalog.TextureIndex(RockTexture);
        int placedCount = 0;
        const int SampleSize = 4096;
        for (int i = 0; i < SampleSize; i++)
        {
            int first = TerrainDecalCatalog.Place(textureIndex, i, i * 17);
            Assert.That(TerrainDecalCatalog.Place(textureIndex, i, i * 17), Is.EqualTo(first));
            Assert.That(first, Is.InRange(0, 2048), "placement code exceeds the packed cell field");
            placedCount += first > 0 ? 1 : 0;
        }

        Assert.That(placedCount, Is.InRange(SampleSize * 20 / 100, SampleSize * 30 / 100));
    }

    [Test]
    public void Place_ZeroTextureNeverPlaces()
    {
        for (int i = 0; i < 1024; i++)
        {
            Assert.That(TerrainDecalCatalog.Place(0, i, -i), Is.Zero);
        }
    }

    [Test]
    public void Place_GroundUsesAllVariantsAndOffsets()
    {
        byte textureIndex = TerrainDecalCatalog.TextureIndex(GroundTexture);
        bool[] variants = new bool[TerrainDecalCatalog.VariantCount];
        bool[] offsetsX = new bool[4];
        bool[] offsetsY = new bool[4];

        for (int i = 0; i < 20000; i++)
        {
            int packed = TerrainDecalCatalog.Place(textureIndex, i, i * 37);
            if (packed == 0)
            {
                continue;
            }

            int code = packed - 1;
            variants[code & (TerrainDecalCatalog.VariantCount - 1)] = true;
            offsetsX[(code >> 7) & 3] = true;
            offsetsY[(code >> 9) & 3] = true;
        }

        Assert.That(variants.All(value => value), Is.True);
        Assert.That(offsetsX.All(value => value), Is.True);
        Assert.That(offsetsY.All(value => value), Is.True);
    }

    private static DirectoryInfo? FindProjectRoot()
    {
        DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Assets/Settings/Build/Textures/TextureStagingCatalog.json")))
        {
            root = root.Parent;
        }

        return root;
    }
}
