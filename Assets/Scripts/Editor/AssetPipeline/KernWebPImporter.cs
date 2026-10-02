#nullable enable

using System;
using System.IO;
using Kern.World;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Kern.Editor
{
    [ScriptedImporter(2, "webp")]
    public sealed class KernWebPImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] bytes = File.ReadAllBytes(ctx.assetPath);
            Texture2D? texture = null;

            try
            {
                if (AnimationContainerDecoder.DetectType(bytes) == AnimationContainerDecoder.ContainerType.WebP)
                {
                    AnimationContainerDecoder.DecodedAnimation decoded = WebPAnimationDecoder.Decode(bytes);
                    texture = decoded.Atlas;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KernWebPImporter] Failed to decode WebP animation for {ctx.assetPath}: {ex.Message}");
            }

            if (texture == null)
            {
                texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
                if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
                {
                    texture.SetPixel(0, 0, Color.clear);
                    texture.Apply();
                }
            }

            texture.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("main", texture);
            ctx.SetMainObject(texture);
        }
    }
}
