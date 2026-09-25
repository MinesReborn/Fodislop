#nullable enable

using Kern.World.Terrain;
using NUnit.Framework;

namespace Kern.Tests.World.Terrain;

public sealed class TerrainContentRevisionJournalTests
{
    [Test]
    public void BoundedRegionChangesDoNotAdvanceTheGlobalRequestedRevision()
    {
        var journal = new TerrainContentRevisionJournal();

        Assert.That(journal.RecordBoundedRegionChange(), Is.EqualTo(1));
        Assert.That(journal.RequestedRevision, Is.EqualTo(1));
    }

    [Test]
    public void ConfigurationAndWorldChangesAdvanceRequestedRevision()
    {
        var journal = new TerrainContentRevisionJournal();

        Assert.That(journal.RecordConfigurationChange(), Is.EqualTo(2));
        Assert.That(journal.RecordWorldLoad(), Is.EqualTo(3));
        Assert.That(journal.RecordUnboundedGeometryChange(), Is.EqualTo(4));
        Assert.That(journal.RecordLightingVisibleTextureChange(), Is.EqualTo(5));
    }
}
