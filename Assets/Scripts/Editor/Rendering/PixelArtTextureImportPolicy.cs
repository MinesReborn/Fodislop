#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

public sealed class PixelArtTextureImportPolicy : AssetPostprocessor
{
    private const string ProgrammatorRoot = "Assets/Resources/Programmator/";
    private const string SkillsRoot = "Assets/Resources/Skills/";

    public override int GetPostprocessOrder() => -1000;

    public override uint GetVersion() => 1;

    private void OnPreprocessTexture()
    {
        if (!IsPixelArtResource(assetPath))
        {
            return;
        }

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.spriteImportMode = SpriteImportMode.None;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        importer.mipmapEnabled = false;
        importer.streamingMipmaps = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 0;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
    }

    private static bool IsPixelArtResource(string path)
    {
        return path.StartsWith(ProgrammatorRoot, StringComparison.Ordinal) ||
            path.StartsWith(SkillsRoot, StringComparison.Ordinal);
    }
}
