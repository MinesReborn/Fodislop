#nullable enable

using Kern.AssetPipeline.Loading;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.Build;

namespace Kern.Editor;

public sealed class BuildTextureStager : BuildPlayerProcessor
{
    private const string StagingRoot = "Library/KernBuild/StreamingAssets";

    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        string source = Path.GetFullPath(Path.Combine("Assets/Textures"));
        if (!Directory.Exists(source))
        {
            throw new BuildFailedException(
                $"Required runtime texture directory does not exist: {source}");
        }

        string stagingRoot = Path.GetFullPath(StagingRoot);
        TextureDeliveryCatalogData textureCatalog = TextureDeliveryCatalog.Read(TextureDeliveryCatalog.AssetPath);
        TextureStagingWriter.ValidateCatalogCoverage(source, textureCatalog.All);

        StageTextureGroup(
            source,
            stagingRoot,
            "EmbeddedTextures",
            "EmbeddedTextures.manifest",
            textureCatalog.Embedded,
            buildPlayerContext);
        StageTextureGroup(
            source,
            stagingRoot,
            "DummyWorldTextures",
            "DummyWorldTextures.manifest",
            textureCatalog.DummyWorld,
            buildPlayerContext);

        string audioRoot = Path.GetFullPath(Path.Combine("Assets/StreamingAssets", "Audio"));
        if (Directory.Exists(audioRoot))
        {
            string[] audioBanks = Directory.EnumerateFiles(audioRoot, "*.bank", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(audioRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            string audioManifestPath = Path.Combine(stagingRoot, "Audio.manifest");
            File.WriteAllLines(audioManifestPath, audioBanks);
            buildPlayerContext.AddAdditionalPathToStreamingAssets(
                audioManifestPath,
                "Audio/Audio.manifest");
        }

        string worldMaps = Path.GetFullPath(Path.Combine("Assets/StreamingAssets/WorldMaps"));
        if (Directory.Exists(worldMaps))
        {
            string[] archiveEntries = Directory.EnumerateFiles(worldMaps, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Select(path => $"{Path.GetFileName(path)}|{ComputeSha256(path)}")
                .ToArray();
            string mapManifest = Path.Combine(stagingRoot, "WorldMaps.manifest");
            File.WriteAllLines(mapManifest, archiveEntries);
            buildPlayerContext.AddAdditionalPathToStreamingAssets(mapManifest, "WorldMaps/WorldMaps.manifest");
        }
    }

    private static void StageTextureGroup(
        string source,
        string stagingRoot,
        string folderName,
        string manifestName,
        IReadOnlyDictionary<string, string> catalog,
        BuildPlayerContext buildPlayerContext)
    {
        string stagedRoot = Path.Combine(stagingRoot, folderName);
        if (Directory.Exists(stagedRoot))
        {
            Directory.Delete(stagedRoot, recursive: true);
        }

        TextureStagingWriter.CopyCatalogFiles(source, stagedRoot, catalog);
        string[] relativeFiles = Directory
            .EnumerateFiles(stagedRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stagedRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string manifestPath = Path.Combine(stagingRoot, manifestName);
        File.WriteAllLines(manifestPath, relativeFiles);

        buildPlayerContext.AddAdditionalPathToStreamingAssets(stagedRoot, folderName);
        buildPlayerContext.AddAdditionalPathToStreamingAssets(manifestPath, manifestName);
    }

    private static string ComputeSha256(string path)
    {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }

}
