#nullable enable

using System.IO;
using System.Linq;
using UnityEditor.Build;

namespace Kern.Editor;

public sealed class BuildTextureStager : BuildPlayerProcessor
{
    private const string StagingRoot = "Library/KernBuild/StreamingAssets";

    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        string source = Path.GetFullPath(Path.Combine("Assets", "Textures"));
        if (!Directory.Exists(source))
        {
            throw new BuildFailedException(
                $"Required runtime texture directory does not exist: {source}");
        }

        string stagingRoot = Path.GetFullPath(StagingRoot);
        string stagedTextures = Path.Combine(stagingRoot, "Textures");
        if (Directory.Exists(stagedTextures))
        {
            Directory.Delete(stagedTextures, recursive: true);
        }

        CopyRuntimeFiles(source, stagedTextures);
        string[] relativeFiles = Directory
            .EnumerateFiles(stagedTextures, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stagedTextures, path).Replace('\\', '/'))
            .OrderBy(path => path, System.StringComparer.Ordinal)
            .ToArray();
        string manifestPath = Path.Combine(stagingRoot, "Textures.manifest");
        File.WriteAllLines(manifestPath, relativeFiles);

        buildPlayerContext.AddAdditionalPathToStreamingAssets(stagedTextures, "Textures");
        buildPlayerContext.AddAdditionalPathToStreamingAssets(manifestPath, "Textures.manifest");
    }

    private static void CopyRuntimeFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string sourceFile in Directory.EnumerateFiles(source))
        {
            if (sourceFile.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(
                sourceFile,
                Path.Combine(destination, Path.GetFileName(sourceFile)),
                overwrite: true);
        }

        foreach (string sourceDirectory in Directory.EnumerateDirectories(source))
        {
            CopyRuntimeFiles(
                sourceDirectory,
                Path.Combine(destination, Path.GetFileName(sourceDirectory)));
        }
    }
}
