#nullable enable

using System.Collections.Generic;
using Kern.World.Terrain;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Окно не имеет права собираться по частично приехавшим чанкам, но не имеет
// права и стоять на месте: стояние кончается прыжком во всё окно, а это самая
// дорогая пересборка из возможных. Здесь проверяется, что догон выбирает
// только резидентные положения и при этом действительно двигается вперёд.
[TestFixture]
public sealed class TerrainWindowAdvanceTests
{
    private const int Width = 192;
    private const int Height = 160;

    [TestCase(8, 0)]
    [TestCase(-8, 0)]
    [TestCase(0, 8)]
    [TestCase(0, -8)]
    [TestCase(8, -8)]
    public void WindowMovementPublishesArrivingAndDepartingCoverage(int deltaX, int deltaY)
    {
        var journal = new TerrainWindowChangeJournal();
        RectInt previous = new(-32, -48, 32, 32);
        RectInt current = new(previous.x + deltaX, previous.y + deltaY, 32, 32);
        journal.RecordPublishedCoverageChange(previous, current);
        var regions = new List<RectInt>();
        journal.TakePublishedChangedRegions(regions);
        for (int y = -64; y < 0; y++)
        {
            for (int x = -48; x < 16; x++)
            {
                Vector2Int cell = new(x, y);
                if (previous.Contains(cell) != current.Contains(cell))
                {
                    Assert.That(regions.Exists(region => region.Contains(cell)), Is.True,
                        $"Changed contributor coverage at {cell} was not published.");
                }
            }
        }

        regions.Clear();
        journal.TakePublishedChangedRegions(regions);
        Assert.That(regions, Is.Empty, "Coverage must be acknowledged once.");
    }

    [Test]
    public void NothingToDoWhenTheWindowIsAlreadyWhereItWasAsked()
    {
        var origin = new Vector2Int(10, 20);

        Vector2Int resolved = TerrainWindowAdvance.Resolve(
            origin, origin, Width, Height, _ => false);

        Assert.That(resolved, Is.EqualTo(origin));
    }

    [Test]
    public void NothingResidentMeansStayingPut()
    {
        var committed = new Vector2Int(0, 0);
        var requested = new Vector2Int(128, 0);

        Vector2Int resolved = TerrainWindowAdvance.Resolve(
            committed, requested, Width, Height, _ => false);

        Assert.That(resolved, Is.EqualTo(committed));
    }

    [Test]
    public void TheWholeWayResidentMeansGoingAllTheWay()
    {
        var committed = new Vector2Int(0, 0);
        var requested = new Vector2Int(128, 0);

        Vector2Int resolved = TerrainWindowAdvance.Resolve(
            committed, requested, Width, Height, _ => true);

        Assert.That(resolved.x, Is.GreaterThan(committed.x));
        Assert.That(resolved.x, Is.LessThanOrEqualTo(requested.x));
    }

    // Главное свойство: выбранное положение обязано быть резидентным. Иначе
    // окно соберётся по дырам, и это увидит игрок, а не тест.
    [Test]
    public void TheChosenOriginIsAlwaysResident()
    {
        var committed = new Vector2Int(0, 0);
        for (int reach = 1; reach <= 256; reach++)
        {
            var requested = new Vector2Int(256, 0);
            int residentLimit = reach;
            Vector2Int resolved = TerrainWindowAdvance.Resolve(
                committed,
                requested,
                Width,
                Height,
                origin => origin.x <= residentLimit);

            Assert.That(
                resolved.x,
                Is.LessThanOrEqualTo(residentLimit),
                $"выбрано нерезидентное положение при границе {residentLimit}");
            Assert.That(resolved.x, Is.GreaterThanOrEqualTo(committed.x));
        }
    }

    // Догон не обязан быть оптимальным, но обязан не топтаться: если вперёд
    // есть куда, кадр сдвигается.
    [Test]
    public void ProgressIsMadeWheneverAnyStepIsResident()
    {
        var committed = new Vector2Int(0, 0);
        var requested = new Vector2Int(256, 0);

        Vector2Int resolved = TerrainWindowAdvance.Resolve(
            committed, requested, Width, Height, origin => origin.x <= 200);

        Assert.That(resolved.x, Is.GreaterThan(0));
    }

    [Test]
    public void ProbingIsBoundedNoMatterHowFarTheWindowIsBehind()
    {
        var probed = new List<Vector2Int>();
        TerrainWindowAdvance.Resolve(
            new Vector2Int(0, 0),
            new Vector2Int(1_000_000, 1_000_000),
            Width,
            Height,
            origin =>
            {
                probed.Add(origin);
                return false;
            });

        Assert.That(probed.Count, Is.LessThanOrEqualTo(5));
    }

    [Test]
    public void DiagonalCatchUpMovesOnBothAxes()
    {
        Vector2Int resolved = TerrainWindowAdvance.Resolve(
            new Vector2Int(0, 0), new Vector2Int(64, -64), Width, Height, _ => true);

        Assert.That(resolved.x, Is.GreaterThan(0));
        Assert.That(resolved.y, Is.LessThan(0));
    }
}
