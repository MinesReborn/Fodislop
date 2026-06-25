using System.IO;
using UnityEditor;
using UnityEngine;

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
        var outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "exported"));
        Directory.CreateDirectory(outputDir);

        var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        var exportedCount = 0;
        foreach (var obj in sprites)
        {
            if (obj is Sprite sprite)
            {
                if (sprite.texture == null)
                {
                    Debug.LogWarning($"Sprite '{sprite.name}' has no source texture, skipping");
                    continue;
                }

                var tex = new Texture2D((int)sprite.rect.width, (int)sprite.rect.height);

                if (sprite.texture.isReadable)
                {
                    var pixels = sprite.texture.GetPixels(
                        (int)sprite.rect.x, (int)sprite.rect.y,
                        (int)sprite.rect.width, (int)sprite.rect.height
                    );
                    tex.SetPixels(pixels);
                }
                else
                {
                    // Fallback: copy through RenderTexture for non-readable textures
                    var rt = RenderTexture.GetTemporary(
                        sprite.texture.width, sprite.texture.height, 0,
                        RenderTextureFormat.Default, RenderTextureReadWrite.sRGB
                    );
                    Graphics.Blit(sprite.texture, rt);

                    var prevRT = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(
                        new Rect(sprite.rect.x, sprite.rect.y, sprite.rect.width, sprite.rect.height),
                        0, 0
                    );
                    RenderTexture.active = prevRT;
                    RenderTexture.ReleaseTemporary(rt);
                }

                tex.Apply();

                var png = tex.EncodeToPNG();
                var path = $"{outputDir}/{sprite.name}.png";
                File.WriteAllBytes(path, png);
                DestroyImmediate(tex);
                exportedCount++;
            }
        }

        Debug.Log($"Exported {exportedCount} sprite(s) to {outputDir}");
    }
}
