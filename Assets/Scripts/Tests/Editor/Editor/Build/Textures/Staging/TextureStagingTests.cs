#nullable enable

using Kern.AssetPipeline.Loading;

using System;
using System.Collections.Generic;
using System.IO;
using Kern.Editor;
using NUnit.Framework;

namespace Kern.Tests.Editor.Build;

public sealed class TextureStagingTests
{
    [Test]
    public void AuthoringCategoriesPreservePublishedTextureIdentityAndBytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "Kern.TextureStaging", Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(root, "source");
            string authored = Path.Combine(source, "Cells/Crystals/Blue/Blue.png");
            Directory.CreateDirectory(Path.GetDirectoryName(authored)!);
            byte[] bytes = { 0, 1, 2, 3, 255 };
            File.WriteAllBytes(authored, bytes);
            File.WriteAllText(authored + ".meta", "metadata must not ship");
            string destination = Path.Combine(root, "published");

            TextureStagingWriter.CopyRuntimeFiles(source, destination, new Dictionary<string, string>
            {
                ["Cells/Crystals/Blue/Blue.png"] = "Cells/Blue.png",
            });

            Assert.That(File.ReadAllBytes(Path.Combine(destination, "Cells/Blue.png")), Is.EqualTo(bytes));
            Assert.That(Directory.GetFiles(destination, "*", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ProductionCatalogContainsEveryAuthoredTextureWithUniquePublishedIdentity()
    {
            IReadOnlyDictionary<string, string> catalog =
                TextureDeliveryCatalog.Read(TextureDeliveryCatalog.AssetPath).All;
        string root = Path.GetFullPath("Assets/Textures");
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file).StartsWith('.'))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            Assert.That(catalog.ContainsKey(relative), Is.True, relative);
        }

        Assert.That(catalog["Cells/Crystals/Blue/Blue.png"], Is.EqualTo("Cells/Blue.png"));
    }
}
