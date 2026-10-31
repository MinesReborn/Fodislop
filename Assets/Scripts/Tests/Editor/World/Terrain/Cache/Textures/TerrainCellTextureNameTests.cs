#nullable enable

using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellTextureNameTests
{
    [TestCase("Visual/Cells/Rock.png", CellType.Rock)]
    [TestCase("visual/cells/redrock", CellType.RedRock)]
    [TestCase("Visual/Cells/117.png", CellType.RedRock)]
    public void TryParseCellType_RecognizesEnumTextureNames(
        string filename,
        CellType expectedCellType)
    {
        bool success = TerrainCellTextureName.TryParseCellType(filename, out CellType cellType);

        Assert.That(success, Is.True);
        Assert.That(cellType, Is.EqualTo(expectedCellType));
    }

    [TestCase("Visual/Cells/NotACell.png")]
    [TestCase("Other/Rock.png")]
    [TestCase("Visual/Cells/12345.png")]
    public void TryParseCellType_RejectsUnknownCellTextureNames(string filename)
    {
        bool success = TerrainCellTextureName.TryParseCellType(filename, out _);

        Assert.That(success, Is.False);
    }
}
