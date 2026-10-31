#nullable enable

using System.Collections.Generic;
using System.IO;
using Kern.AssetPipeline.Loading;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.AssetPipeline;

public sealed class PublishedTextureSourceIndexTests
{
    [Test]
    public void PublishedIdentityUsesExactNameAndFormatPriority()
    {
        var index = new PublishedTextureSourceIndex(new Dictionary<string, string>
        {
            ["Cells/Crystals/Blue/Blue.exr"] = "Cells/Blue.exr",
            ["Cells/Crystals/Blue/Blue.png"] = "Cells/Blue.png",
        });
        Assert.That(index.Resolve("Cells/Blue.png"), Is.EqualTo("Cells/Crystals/Blue/Blue.png"));
        Assert.That(index.Resolve("Cells/Blue.exr"), Is.EqualTo("Cells/Crystals/Blue/Blue.exr"));
        Assert.That(index.Resolve("cells/BLUE"), Is.Null);
        Assert.That(index.Resolve("Cells/Absent.png"), Is.Null);
    }

    [Test]
    public void ProductionCatalogReferencesExistingAuthoredSources()
    {
        foreach (KeyValuePair<string, string> entry in AuthoredTextureCatalog.Read(AuthoredTextureCatalog.AssetPath))
        {
            string path = Path.Combine(
                Application.dataPath,
                "Textures",
                entry.Key.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, entry.Value);
        }
    }
}
