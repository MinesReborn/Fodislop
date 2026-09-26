#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;

namespace Kern.Tests.World;

[TestFixture]
public sealed class TerrainCellTypeSetTests
{
    [Test]
    public void CaptureCopiesMembershipAcrossAllByteWords()
    {
        var source = new HashSet<CellType>
        {
            (CellType)0,
            (CellType)63,
            (CellType)64,
            (CellType)127,
            (CellType)128,
            (CellType)191,
            (CellType)192,
            (CellType)255,
        };

        TerrainCellTypeSet snapshot = TerrainCellTypeSet.Capture(source);
        source.Clear();

        Assert.That(snapshot.IsEmpty, Is.False);
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            bool expected = value is 0 or 63 or 64 or 127 or 128 or 191 or 192 or 255;
            Assert.That(snapshot.Contains((CellType)value), Is.EqualTo(expected), $"CellType={value}");
        }
    }

    [Test]
    public void DefaultSetIsEmpty()
    {
        TerrainCellTypeSet snapshot = default;

        Assert.That(snapshot.IsEmpty, Is.True);
        Assert.That(snapshot.Contains(CellType.Empty), Is.False);
    }
}
