#nullable enable

using System;
using UnityEditor;
using UnityEngine;

namespace Kern.Editor;

public sealed class PixelArtTextureImportPolicy : AssetPostprocessor
{
    private const string ProgrammatorRoot = "Assets/Resources/Programmator/";
    private const string SkillsRoot = "Assets/Resources/UI/HUD/Player/Skills/Icons/";
    private const string SurfaceRoot = "Assets/Textures/Surface/";
    private const string CrystalDigitsRoot = "Assets/Resources/Effects/Crystals/Digits/";

    // Для ветки Assets/Textures политика нужна единообразием редактора, а не
    // видом в игре: BuildTextureStager копирует эти файлы как есть в
    // StreamingAssets/Textures, а TextureStorageManager.DecodeTexture
    // разбирает байты сам и принудительно ставит Point/Clamp. Настройки
    // импорта отсюда на экран не доезжают вовсе — но и в редакторе пусть
    // выглядят так же, как остальной пиксель-арт.
    private const string TexturesRoot = "Assets/Textures/";

    public override int GetPostprocessOrder() => -1000;

    private void OnPreprocessTexture()
    {
        if (!IsPixelArtResource(assetPath))
        {
            return;
        }

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
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
        // Surface cutouts and 3D albedo use their authored mip/filter/wrap settings.
        if (path.StartsWith(SurfaceRoot, StringComparison.Ordinal))
        {
            return false;
        }

        return path.StartsWith(ProgrammatorRoot, StringComparison.Ordinal) ||
            path.StartsWith(SkillsRoot, StringComparison.Ordinal) ||
            path.StartsWith(CrystalDigitsRoot, StringComparison.Ordinal) ||
            path.StartsWith(TexturesRoot, StringComparison.Ordinal);
    }
}
