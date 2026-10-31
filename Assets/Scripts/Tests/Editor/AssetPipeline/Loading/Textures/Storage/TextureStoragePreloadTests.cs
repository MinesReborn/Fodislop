#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Kern.AssetPipeline;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;

using Kern.AssetPipeline.Loading;

namespace Kern.Tests.AssetPipeline;

[TestFixture]
public class TextureStoragePreloadTests
{
    private string _tempDir = null!;
    private GameObject _holder = null!;
    private TextureStorageManager _storageManager = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Kern_TexTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _holder = new GameObject("TextureStorageManager_Test");
        _storageManager = _holder.AddComponent<TextureStorageManager>();

        var assetPaths = new StubRuntimeAssetPaths(_tempDir);
        FieldInfo? pathsField = typeof(TextureStorageManager).GetField(
            "_runtimeAssetPaths",
            BindingFlags.NonPublic | BindingFlags.Instance);
        pathsField?.SetValue(_storageManager, assetPaths);
    }

    [TearDown]
    public void TearDown()
    {
        if (_holder != null)
        {
            UnityEngine.Object.DestroyImmediate(_holder);
        }

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup in tests
            }
        }
    }

    [Test]
    public async Task PreloadTexturesAsync_CachesRawBytesInRAM_AndResolvesAliases()
    {
        // Create dummy PNG in temp dir
        string cellDir = Path.Combine(_tempDir, "Cells");
        Directory.CreateDirectory(cellDir);
        string pngPath = Path.Combine(cellDir, "1.png");

        var testTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        testTex.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
        testTex.Apply();
        byte[] expectedBytes = testTex.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(testTex);

        await File.WriteAllBytesAsync(pngPath, expectedBytes);

        // Preload via relative path
        await _storageManager.PreloadTexturesAsync(new[] { "Cells/1.png" }, CancellationToken.None);

        // Verify HasTexture is true for both with-extension and extensionless
        Assert.That(_storageManager.HasTexture("Cells/1.png"), Is.True);
        Assert.That(_storageManager.HasTexture("Cells/1"), Is.True);

        // Delete the file on disk to prove subsequent calls serve strictly from RAM!
        File.Delete(pngPath);
        Assert.That(File.Exists(pngPath), Is.False);

        // Must still return true from RAM cache!
        Assert.That(_storageManager.HasTexture("Cells/1.png"), Is.True);
        Assert.That(_storageManager.HasTexture("Cells/1"), Is.True);

        // Must return cached bytes from RAM without throwing FileNotFoundException!
        byte[]? retrievedBytes = await _storageManager.GetTextureData("Cells/1.png");
        Assert.That(retrievedBytes, Is.Not.Null);
        Assert.That(retrievedBytes, Is.EqualTo(expectedBytes));

        // Extensionless lookup must also return cached bytes from RAM!
        byte[]? retrievedWithoutExt = await _storageManager.GetTextureData("Cells/1");
        Assert.That(retrievedWithoutExt, Is.Not.Null);
        Assert.That(retrievedWithoutExt, Is.EqualTo(expectedBytes));
    }

    [Test]
    public async Task ClearCache_ClearsRawBytesAndTextures()
    {
        string cellDir = Path.Combine(_tempDir, "Cells");
        Directory.CreateDirectory(cellDir);
        string pngPath = Path.Combine(cellDir, "2.png");

        var testTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        byte[] bytes = testTex.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(testTex);
        await File.WriteAllBytesAsync(pngPath, bytes);

        await _storageManager.PreloadTexturesAsync(new[] { "Cells/2.png" });
        Assert.That(_storageManager.HasTexture("Cells/2.png"), Is.True);

        _storageManager.ClearCache();

        File.Delete(pngPath);
        Assert.That(_storageManager.HasTexture("Cells/2.png"), Is.False);
    }

    private sealed class StubRuntimeAssetPaths : IRuntimeAssetPaths
    {
        private readonly string _rootDir;

        public StubRuntimeAssetPaths(string rootDir) => _rootDir = rootDir;

        public UniTask EnsureReadyAsync() => UniTask.CompletedTask;

        public IReadOnlyList<string> GetTextureFilenames() => Directory
            .EnumerateFiles(_rootDir, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_rootDir, path).Replace('\\', '/'))
            .ToArray();

        public byte[]? GetTextureBytes(string relativePath)
        {
            string candidate = Path.Combine(_rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return File.ReadAllBytes(candidate);
            }

            if (string.IsNullOrEmpty(Path.GetExtension(candidate)))
            {
                string withPng = candidate + ".png";
                if (File.Exists(withPng))
                {
                    return File.ReadAllBytes(withPng);
                }
            }

            return null;
        }

        public UniTask<IReadOnlyList<string>> GetAudioBankFilenamesAsync() =>
            UniTask.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public UniTask<byte[]?> GetAudioBankBytesAsync(string relativePath) =>
            UniTask.FromResult<byte[]?>(null);
    }
}
