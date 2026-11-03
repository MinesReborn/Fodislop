#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MinesServer.Data;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

using Kern.Game.Inventory;

namespace Kern.Tests.Game;

public sealed class ItemRegistryTests
{
    private sealed class StubAssetPaths : IRuntimeAssetPaths
    {
        public UniTask EnsureReadyAsync() => UniTask.CompletedTask;

        public IReadOnlyList<string> GetEmbeddedTextureFilenames() => Array.Empty<string>();

        public IReadOnlyList<string> GetDummyWorldTextureFilenames() => Array.Empty<string>();

        public byte[]? GetTextureBytes(string relativePath) => null;

        public UniTask<IReadOnlyList<string>> GetAudioBankFilenamesAsync() =>
            UniTask.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public UniTask<byte[]?> GetAudioBankBytesAsync(string relativePath) =>
            UniTask.FromResult<byte[]?>(null);
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
    public void GetName_ReturnsCatalogName_NotEnumName()
    {
        var registry = new ItemRegistry(new StubAssetPaths());

        string name = registry.GetName(ItemType.Cred);

        Assert.That(name, Is.Not.Empty);
        Assert.That(name, Is.Not.EqualTo(nameof(ItemType.Cred)));
    }

    [Test]
    public void GetDescription_ReturnsCatalogDescription()
    {
        var registry = new ItemRegistry(new StubAssetPaths());

        Assert.That(registry.GetDescription(ItemType.Cred), Is.Not.Empty);
    }

    [Test]
    public void AllTypes_ContainsCred()
    {
        var registry = new ItemRegistry(new StubAssetPaths());
        Assert.That(registry.AllTypes.Contains(ItemType.Cred), Is.True);
    }
}
