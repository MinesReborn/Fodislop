#nullable enable

using System.Linq;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using Kern.Core.Interfaces;
using Kern.Game.Managers;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.Game;

public sealed class ItemRegistryTests
{
    private sealed class StubAssetPaths : IRuntimeAssetPaths
    {
        public string BundledTexturesRoot => string.Empty;
        public string PersistentTexturesRoot => string.Empty;

        public UniTask EnsureReadyAsync() => UniTask.CompletedTask;

        public string? FindBundledTextureFile(string relativePath) => null;

        public string? FindTextureFile(string relativePath) => null;
    }

    [Test]
    public void GetIcon_MissingTexture_ReturnsNull()
    {
        var registry = new ItemRegistry(new StubAssetPaths());

        Texture2D? first = registry.GetIcon(ItemType.Cred);
        Texture2D? second = registry.GetIcon(ItemType.Cred);

        Assert.That(first, Is.Null);
        Assert.That(second, Is.Null);
    }

    [Test]
    public void GetName_ReturnsEnumName()
    {
        var registry = new ItemRegistry(new StubAssetPaths());
        Assert.That(registry.GetName(ItemType.Cred), Is.EqualTo("Cred"));
    }

    [Test]
    public void GetDescription_ReturnsEmpty()
    {
        var registry = new ItemRegistry(new StubAssetPaths());
        Assert.That(registry.GetDescription(ItemType.Cred), Is.Empty);
    }

    [Test]
    public void AllTypes_ContainsCred()
    {
        var registry = new ItemRegistry(new StubAssetPaths());
        Assert.That(registry.AllTypes.Contains(ItemType.Cred), Is.True);
    }
}
