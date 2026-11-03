#nullable enable

using System;
using System.IO;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainQuantizationContractTests
{
    private static readonly string s_repoRoot = FindRepoRoot();

    [Test]
    public void IdentityPolygonOccupiesEveryPixel()
    {
        bool[,] bitmap = TerrainQuantizationOracle.Rasterize(
            TerrainQuantizationOracle.FromSteps(0, 0, 0, 0, 0, 0, 0, 0));

        Assert.That(TerrainQuantizationOracle.CountOccupied(bitmap), Is.EqualTo(32 * 32));
    }

    [Test]
    public void OnePixelCornerStepsChangeOnlyTheExpectedRaster()
    {
        TerrainQuantizedPolygon identity = TerrainQuantizationOracle.FromSteps(0, 0, 0, 0, 0, 0, 0, 0);
        TerrainQuantizedPolygon inset = TerrainQuantizationOracle.FromSteps(1, 1, 0, 0, 0, 0, 0, 0);
        bool[,] full = TerrainQuantizationOracle.Rasterize(identity);
        bool[,] clipped = TerrainQuantizationOracle.Rasterize(inset);

        Assert.That(TerrainQuantizationOracle.CountOccupied(full), Is.EqualTo(1024));
        Assert.That(TerrainQuantizationOracle.CountOccupied(clipped), Is.LessThan(1024));
        Assert.That(clipped[0, 0], Is.False);
        Assert.That(clipped[31, 31], Is.True);
    }

    [Test]
    public void ClockwiseAndCounterClockwiseCornerOrderAgree()
    {
        TerrainQuantizedPolygon counterClockwise = TerrainQuantizationOracle.FromSteps(
            -5, 3, 4, -2, 6, 5, -3, 7);
        TerrainQuantizedPolygon clockwise = new(
            counterClockwise.Corner00,
            counterClockwise.Corner01,
            counterClockwise.Corner11,
            counterClockwise.Corner10);

        AssertBitmapsEqual(
            TerrainQuantizationOracle.Rasterize(counterClockwise),
            TerrainQuantizationOracle.Rasterize(clockwise));
    }

    [Test]
    public void DegeneratePolygonHasNoOccupancy()
    {
        TerrainQuantizedPolygon line = new(
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(2f, 0f),
            new Vector2(3f, 0f));

        Assert.That(TerrainQuantizationOracle.CountOccupied(TerrainQuantizationOracle.Rasterize(line)), Is.EqualTo(0));
    }

    [Test]
    public void EveryPackedGeometryCornerLiesOnThe32PixelGrid()
    {
        for (int step = -32; step <= 32; step++)
        {
            TerrainQuantizedPolygon polygon = TerrainQuantizationOracle.FromSteps(
                step, -step, -step, step, step, step, -step, -step);

            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 point = polygon.GetCorner(corner);
                Assert.That(point.x * 32f, Is.EqualTo(MathF.Round(point.x * 32f)).Within(0.00001f));
                Assert.That(point.y * 32f, Is.EqualTo(MathF.Round(point.y * 32f)).Within(0.00001f));
            }
        }
    }

    [Test]
    public void AdjacentIdentityCellsShareTheirBoundaryWithoutASeam()
    {
        bool[,] left = TerrainQuantizationOracle.Rasterize(
            TerrainQuantizationOracle.FromSteps(0, 0, 0, 0, 0, 0, 0, 0));
        bool[,] right = TerrainQuantizationOracle.Rasterize(
            TerrainQuantizationOracle.FromSteps(0, 0, 0, 0, 0, 0, 0, 0));

        for (int y = 0; y < TerrainQuantizationOracle.GridSize; y++)
        {
            Assert.That(left[31, y], Is.True, $"left boundary pixel y={y}");
            Assert.That(right[0, y], Is.True, $"right boundary pixel y={y}");
        }
    }

    [Test]
    public void FuzzedCornerSetsAreDeterministicAndBounded()
    {
        var random = new System.Random(0x5EED_32);
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            TerrainQuantizedPolygon polygon = TerrainQuantizationOracle.FromSteps(
                random.Next(-32, 33), random.Next(-32, 33),
                random.Next(-32, 33), random.Next(-32, 33),
                random.Next(-32, 33), random.Next(-32, 33),
                random.Next(-32, 33), random.Next(-32, 33));

            bool[,] first = TerrainQuantizationOracle.Rasterize(polygon);
            bool[,] second = TerrainQuantizationOracle.Rasterize(polygon);
            AssertBitmapsEqual(first, second);
            int occupied = TerrainQuantizationOracle.CountOccupied(first);
            Assert.That(occupied, Is.InRange(0, 1024), $"iteration {iteration}");
        }
    }

    [Test]
    public void GeometrySourcePreservesContinuousCornersForFinalShaderQuantization()
    {
        TerrainCellGeometry geometry = TerrainCellGeometry.FromOffsets(
            new Vector3(0.037f, -0.021f, 0f),
            new Vector3(-0.044f, 0.019f, 0f),
            new Vector3(0.028f, 0.046f, 0f),
            new Vector3(-0.031f, -0.038f, 0f));

        Assert.That(geometry.Corner00.x, Is.EqualTo(0.037f).Within(0.00001f));
        Assert.That(geometry.Corner00.y, Is.EqualTo(-0.021f).Within(0.00001f));
        Assert.That(geometry.Corner10.x, Is.EqualTo(1f - 0.044f).Within(0.00001f));
        Assert.That(geometry.Corner10.y, Is.EqualTo(0.019f).Within(0.00001f));
        Assert.That(geometry.Corner11.x, Is.EqualTo(1f + 0.028f).Within(0.00001f));
        Assert.That(geometry.Corner11.y, Is.EqualTo(1f + 0.046f).Within(0.00001f));
        Assert.That(geometry.Corner01.x, Is.EqualTo(-0.031f).Within(0.00001f));
        Assert.That(geometry.Corner01.y, Is.EqualTo(1f - 0.038f).Within(0.00001f));
    }

    [Test]
    public void ShaderSourceUsesOneCoverageContractAndPerCellGeometry()
    {
        string terrain = ReadRepoFile("Assets", "Shaders", "Terrain", "Rendering", "Shader", "Terrain.shader");
        string contour = ReadRepoFile("Assets", "Shaders", "Terrain", "Geometry", "TerrainContour.hlsl");
        string geometry = ReadRepoFile("Assets", "Shaders", "Terrain", "Geometry", "TerrainGeometry.hlsl");
        string geometryContract = ReadRepoFile("Assets", "Shaders", "Terrain", "Geometry", "TerrainGeometryContract.hlsl");
        string cellData = ReadRepoFile("Assets", "Shaders", "Terrain", "Cells", "Assembly", "TerrainCellData.hlsl");
        string cellGeometry = ReadRepoFile("Assets", "Scripts", "Tests", "Editor", "World", "Terrain", "Reference", "Cells", "TerrainCellGeometry.cs");
        string buffers = ReadRepoFile("Assets", "Scripts", "World", "Terrain", "GPU", "Cells", "TerrainCellBuffers.cs");

        Assert.That(CountOccurrences(terrain, "EvaluateTerrainCellCoverage("), Is.EqualTo(2));
        // The production path clips the visible pass, two lighting-debug
        // views, foreground identity diagnostics, the regular debug path, and the lighting field pass.
        Assert.That(CountOccurrences(terrain, "clip(cellCoverage - 0.5);"), Is.EqualTo(6));
        Assert.That(CountOccurrences(contour, "TerrainGeometryCoverage("), Is.EqualTo(1));
        // Geometry owns the polygon and distance rules; the shared contract
        // owns the cell grid quantization used by its corners.
        Assert.That(
            CountOccurrences(geometry, "QuantizeTerrainGeometryPoint(") +
            CountOccurrences(geometryContract, "QuantizeTerrainGeometryPoint("),
            Is.EqualTo(2));
        Assert.That(geometryContract, Does.Contain("KERN_TERRAIN_FACE_GRID_SIZE = 32.0"));
        Assert.That(geometry, Does.Contain("KERN_TERRAIN_GEOMETRY_EPSILON"));
        Assert.That(contour, Does.Contain("TerrainGeometryCoverage("));
        Assert.That(contour, Does.Not.Contain("edgeMargins"));
        Assert.That(cellData, Does.Contain("float2 node00 = TerrainNodeCells(TerrainNode(gridX, unityY,"));
        Assert.That(cellGeometry, Does.Not.Contain("Quantize("));
        Assert.That(geometryContract, Does.Contain("TerrainGeometryRawCorner"));
        Assert.That(cellData, Does.Contain("occluded = occluded && TerrainOpaqueMassCell(neighbourRows[m]);"));
        Assert.That(cellData, Does.Contain("bool anchored = foreground && foregroundAnchored;"));
        Assert.That(terrain, Does.Contain("applyGeometry = input.isForeground"));
        Assert.That(cellData, Does.Contain("v.atlasIndex = -1.0"));
        // Геометрия — часть того же uint, что и остальной слой клетки: одна
        // выгрузка и одна привязка, разойтись им негде.
        Assert.That(buffers, Does.Contain("_cellBuffer!.SetData(_words"));
        Assert.That(buffers, Does.Contain("Shader.SetGlobalBuffer(CellsId"));
        Assert.That(terrain, Does.Not.Contain("_TerrainGridOffsets"));
        Assert.That(terrain, Does.Not.Contain("packedGeometryCorners"));
        Assert.That(contour, Does.Not.Contain("PhysicalContour("));
    }

    private static string ReadRepoFile(params string[] parts)
    {
        string path = s_repoRoot;
        foreach (string part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Assets")) &&
                File.Exists(Path.Combine(directory.FullName, "ProjectSettings/ProjectVersion.txt")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Unity project root from the test process.");
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static void AssertBitmapsEqual(bool[,] expected, bool[,] actual)
    {
        string diff = TerrainQuantizationOracle.Diff(expected, actual);
        Assert.That(diff, Is.Empty);
    }
}
