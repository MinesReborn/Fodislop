using UnityEngine;
using UnityEditor;
using System.IO;

public class ExportSprites : EditorWindow
{
    [MenuItem("Tools/Export Sprites to PNG")]
    private static void ExportSelectedSpriteToPNG()
    {
        var texture = Selection.activeObject as Texture2D;
        if (texture == null)
        {
            Debug.LogError("Select a texture first");
            return;
        }

        var assetPath = AssetDatabase.GetAssetPath(texture);
        var outputDir = Path.GetDirectoryName(assetPath) + "/exported";
        Directory.CreateDirectory(outputDir);

        var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        foreach (var obj in sprites)
        {
            if (obj is Sprite sprite)
            {
                var tex = new Texture2D((int)sprite.rect.width, (int)sprite.rect.height);
                var pixels = sprite.texture.GetPixels(
                    (int)sprite.rect.x, (int)sprite.rect.y,
                    (int)sprite.rect.width, (int)sprite.rect.height
                );
                tex.SetPixels(pixels);
                tex.Apply();

                var png = tex.EncodeToPNG();
                var path = $"{outputDir}/{sprite.name}.png";
                File.WriteAllBytes(path, png);
                DestroyImmediate(tex);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Exported to {outputDir}");
    }
}
