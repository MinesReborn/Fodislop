#nullable enable

namespace Kern.Tests.World;

using Kern.World.Terrain;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class TerrainVertexDistortionCalculatorTests
{
    [Test]
    public void EnsureCapacity_AllocatesCorrectGridDimensions()
    {
        var calculator = new TerrainVertexDistortionCalculator();
        calculator.EnsureCapacity(10, 20);

        Assert.IsNotNull(calculator.GridVertexOffsets);
        Assert.AreEqual(11, calculator.GridVertexOffsets.GetLength(0));
        Assert.AreEqual(21, calculator.GridVertexOffsets.GetLength(1));
    }

    [Test]
    public void ComputeOffset_WorldBounds_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };

        TerrainVertexOffset minX = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 0, 10, 100, 100);
        TerrainVertexOffset maxX = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 100, 10, 100, 100);
        TerrainVertexOffset minY = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 10, 0, 100, 100);
        TerrainVertexOffset maxY = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, cause, 10, 100, 100, 100);

        Assert.AreEqual(TerrainVertexOffset.Zero, minX);
        Assert.AreEqual(TerrainVertexOffset.Zero, maxX);
        Assert.AreEqual(TerrainVertexOffset.Zero, minY);
        Assert.AreEqual(TerrainVertexOffset.Zero, maxY);
    }

    [Test]
    public void ComputeOffset_WhenAnyNeighborIsBlock_ReturnsZero()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        var block = new CachedCellData { Distortion = CellDistortionType.Block };

        TerrainVertexOffset tlBlock = TerrainVertexDistortionCalculator.ComputeOffset(block, cause, cause, cause, 10, 10, 100, 100);
        TerrainVertexOffset trBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, block, cause, cause, 10, 10, 100, 100);
        TerrainVertexOffset blBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, block, cause, 10, 10, 100, 100);
        TerrainVertexOffset brBlock = TerrainVertexDistortionCalculator.ComputeOffset(cause, cause, cause, block, 10, 10, 100, 100);

        Assert.AreEqual(TerrainVertexOffset.Zero, tlBlock);
        Assert.AreEqual(TerrainVertexOffset.Zero, trBlock);
        Assert.AreEqual(TerrainVertexOffset.Zero, blBlock);
        Assert.AreEqual(TerrainVertexOffset.Zero, brBlock);
    }

    [Test]
    public void ComputeOffset_RoundableLooseCell_UsesServerCause()
    {
        var cause = new CachedCellData
        {
            Distortion = CellDistortionType.Cause,
            Type = CellType.Rock,
        };
        var lava = new CachedCellData
        {
            Distortion = CellDistortionType.Cause,
            Type = CellType.Lava,
        };

        TerrainVertexOffset result = TerrainVertexDistortionCalculator.ComputeOffset(
            lava,
            cause,
            cause,
            cause,
            10,
            10,
            100,
            100);

        TerrainVertexOffset serverCauseResult = TerrainVertexDistortionCalculator.ComputeOffset(
            cause,
            cause,
            cause,
            cause,
            10,
            10,
            100,
            100);

        Assert.That(TerrainVertexDistortionCalculator.IsCause(lava), Is.True);
        Assert.That(result, Is.EqualTo(serverCauseResult));
        Assert.That(result, Is.Not.EqualTo(TerrainVertexOffset.Zero));
    }

    // Узел внутри сплошного массива. Оригинал Mines (TerrainRenderer.GetDistortion,
    // первая ветка) двигает его свободно в обе стороны; именно эта ветка делает
    // кристалл цельным камнем, а не плиткой. Раньше здесь стоял ноль, и
    // внутренность любого массива оставалась идеальной решёткой.
    [Test]
    public void ComputeOffset_AllFourAreCause_JittersFreely()
    {
        var cause = new CachedCellData { Distortion = CellDistortionType.Cause };
        int limit = 3 * TerrainVertexDistortionCalculator.DistortionStrengthSteps;
        int moved = 0;
        bool negativeX = false;
        bool positiveX = false;
        bool negativeY = false;
        bool positiveY = false;

        for (int worldX = 1; worldX <= 40; worldX++)
        {
            for (int worldY = 1; worldY <= 40; worldY++)
            {
                TerrainVertexOffset result = TerrainVertexDistortionCalculator.ComputeOffset(
                    cause, cause, cause, cause, worldX, worldY, 100, 100);

                Assert.That(result.XSteps, Is.InRange(-limit, limit), $"X at {worldX},{worldY}");
                Assert.That(result.YSteps, Is.InRange(-limit, limit), $"Y at {worldX},{worldY}");
                Assert.That(result.ZSteps, Is.Zero, $"Z at {worldX},{worldY}");

                if (result != TerrainVertexOffset.Zero)
                {
                    moved++;
                }

                negativeX |= result.XSteps < 0;
                positiveX |= result.XSteps > 0;
                negativeY |= result.YSteps < 0;
                positiveY |= result.YSteps > 0;
            }
        }

        Assert.That(moved, Is.GreaterThan(0), "Ни один узел внутри массива не сдвинулся");

        // Джиттер обязан быть центрирован. Потеряется вычитание середины —
        // и весь массив уедет вправо-вверх целиком вместо того, чтобы
        // колыхаться на месте; диапазон при этом останется прежним, поэтому
        // одной проверки границ мало.
        Assert.That(negativeX && positiveX, Is.True, "Джиттер по X только в одну сторону");
        Assert.That(negativeY && positiveY, Is.True, "Джиттер по Y только в одну сторону");
    }

    [Test]
    public void RandMath_StaysWithinSeven()
    {
        for (int x = 1; x <= 50; x++)
        {
            for (int y = 1; y <= 50; y++)
            {
                float rx = TerrainVertexDistortionCalculator.RandXd(x, y);
                float ry = TerrainVertexDistortionCalculator.RandYd(x, y);

                Assert.IsTrue(rx >= 0 && rx < 7);
                Assert.IsTrue(ry >= 0 && ry < 7);
            }
        }
    }

    [Test]
    public void TerrainVertexOffset_ConvertsStepsToWorldOffset()
    {
        Vector3 result = new TerrainVertexOffset(1, -3, 6).ToVector3();

        Assert.That(result.x, Is.EqualTo(1f / 32f).Within(0.000001f));
        Assert.That(result.y, Is.EqualTo(-3f / 32f).Within(0.000001f));
        Assert.That(result.z, Is.EqualTo(6f / 32f).Within(0.000001f));
    }
    [TestCase(CellType.BlackBoulder1)]
    [TestCase(CellType.BlackBoulder2)]
    [TestCase(CellType.BlackBoulder3)]
    [TestCase(CellType.MetalBoulder1)]
    [TestCase(CellType.MetalBoulder2)]
    [TestCase(CellType.MetalBoulder3)]
    [TestCase(CellType.Boulder1)]
    [TestCase(CellType.Boulder2)]
    [TestCase(CellType.Boulder3)]
    [TestCase(CellType.DeepMagmaBoulder)]
    [TestCase(CellType.AliveCyan)]
    [TestCase(CellType.AliveRed)]
    [TestCase(CellType.AliveViol)]
    [TestCase(CellType.AliveBlack)]
    [TestCase(CellType.AliveWhite)]
    [TestCase(CellType.AliveRainbow)]
    [TestCase(CellType.AliveBlue)]
    [TestCase(CellType.QuadBlock)]
    [TestCase(CellType.Support)]
    [TestCase(CellType.MilitaryBlockFrame)]
    [TestCase(CellType.MilitaryBlock)]
    [TestCase(CellType.GreenBlock)]
    [TestCase(CellType.YellowBlock)]
    [TestCase(CellType.FedBlock)]
    [TestCase(CellType.RedBlock)]
    [TestCase(CellType.BuildingWall)]
    [TestCase(CellType.BuildingDoor)]
    [TestCase(CellType.BuildingCorner)]
    [TestCase(CellType.BuildingRoad)]
    [TestCase(CellType.Gate)]
    [TestCase(CellType.TeleportBlock)]
    [TestCase(CellType.Box)]
    public void ServerBlockPinsEverySharedCorner(CellType type)
    {
        var causeCell = new CachedCellData { Type = type, Distortion = CellDistortionType.Cause };
        Assert.That(TerrainVertexDistortionCalculator.IsCause(causeCell), Is.True);

        var blockCell = new CachedCellData { Type = type, Distortion = CellDistortionType.Block };
        Assert.That(TerrainVertexDistortionCalculator.IsBlock(blockCell), Is.True);
        for (int corner = 0; corner < 4; corner++)
        {
            var cells = new CachedCellData[4];
            cells[corner] = blockCell;
            cells[(corner + 1) % 4] = new CachedCellData
            {
                Type = CellType.Green,
                Distortion = CellDistortionType.Cause,
            };
            for (int seed = 1; seed <= 16; seed++)
            {
                TerrainVertexOffset offset = TerrainVertexDistortionCalculator.ComputeOffset(
                    cells[0], cells[1], cells[2], cells[3], seed * 17, seed * 29);
                Assert.That(offset, Is.EqualTo(TerrainVertexOffset.Zero),
                    $"{type}, shared corner {corner}, seed {seed}");
            }
        }
    }

}
