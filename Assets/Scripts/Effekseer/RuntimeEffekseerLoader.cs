// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Effekseer;
using Effekseer.Internal;
using Fodinae.Scripts;
using Fodinae.Scripts.World;
using UnityEngine;

namespace Fodinae.Scripts.Effekseer
{
    /// <summary>
    /// Utility for loading Effekseer effects from raw .efk bytes at runtime,
    /// downloading textures from the server asset pipeline before native loading.
    ///
    /// Usage:
    /// <code>
    /// var asset = await RuntimeEffekseerLoader.LoadEffectAsync(
    ///     efkBytes, "myEffect",
    ///     texturePathMapper: path => "VFX/" + path);
    /// EffekseerSystem.PlayEffect(asset, position);
    /// </code>
    /// </summary>
    public static class RuntimeEffekseerLoader
    {
        /// <summary>
        /// Load an Effekseer effect from raw .efk bytes, downloading all referenced
        /// textures from the server asset pipeline and populating the asset before
        /// native registration. The effect is immediately ready for <see cref="EffekseerSystem.PlayEffect"/>.
        /// </summary>
        /// <param name="efkBytes">Raw .efk file data (SKFE format).</param>
        /// <param name="effectName">Name for the effect asset (used for logging and native registration).</param>
        /// <param name="texturePathMapper">
        /// Optional function to remap texture paths found in the .efk before requesting them
        /// from the server. Example: <c>path => "VFX/" + path</c>.
        /// Return null from the mapper to skip a texture.
        /// </param>
        /// <param name="clientAssetLoader">
        /// The asset loader instance to use for texture downloads.
        /// Defaults to <see cref="ClientAssetLoader.Instance"/>.
        /// </param>
        /// <param name="textureTimeoutSeconds">
        /// Per-texture download timeout. Defaults to 10 seconds.
        /// </param>
        /// <returns>
        /// A loaded <see cref="EffekseerEffectAsset"/> with textures populated,
        /// registered in <see cref="EffekseerSystem"/> and ready to play.
        /// Returns null if the .efk data is invalid or loading fails.
        /// </returns>
        public static async UniTask<EffekseerEffectAsset> LoadEffectAsync(
            byte[] efkBytes,
            string effectName,
            Func<string, string> texturePathMapper = null,
            ClientAssetLoader clientAssetLoader = null,
            int textureTimeoutSeconds = 10)
        {
            if (efkBytes == null || efkBytes.Length < 4)
            {
                Debug.LogError("[RuntimeEffekseerLoader] Invalid or empty .efk data");
                return null;
            }

            if (!EffekseerSystem.IsValid)
            {
                Debug.LogError("[RuntimeEffekseerLoader] EffekseerSystem is not initialized");
                return null;
            }

            var loader = clientAssetLoader != null ? clientAssetLoader : ClientAssetLoader.Instance;
            if (loader == null)
            {
                Debug.LogError("[RuntimeEffekseerLoader] No ClientAssetLoader available");
                return null;
            }

            // ----- 1. Parse resource paths from the .efk binary -----
            var resourcePath = new EffekseerResourcePath();
            if (!EffekseerEffectAsset.ReadResourcePath(efkBytes, ref resourcePath))
            {
                Debug.LogError($"[RuntimeEffekseerLoader] Failed to parse .efk resource paths for '{effectName}'");
                return null;
            }

            // ----- 2. Create the asset container -----
            var asset = ScriptableObject.CreateInstance<EffekseerEffectAsset>();
            asset.efkBytes = efkBytes;
            asset.name = effectName;

            // ----- 3. Download and assign textures -----
            var textureResources = new List<EffekseerTextureResource>(resourcePath.TexturePathList.Count);
            foreach (var rawPath in resourcePath.TexturePathList)
            {
                // Apply optional path remapping
                var serverPath = texturePathMapper != null ? texturePathMapper(rawPath) : rawPath;
                if (serverPath == null)
                {
                    Debug.LogWarning($"[RuntimeEffekseerLoader] Texture '{rawPath}' skipped by mapper");
                    continue;
                }

                var tex = await DownloadTextureAsync(loader, serverPath, textureTimeoutSeconds);
                if (tex != null)
                {
                    textureResources.Add(new EffekseerTextureResource
                    {
                        path = rawPath,
                        texture = tex,
                    });

                    Debug.Log($"[RuntimeEffekseerLoader] Loaded texture '{rawPath}' → '{serverPath}' for effect '{effectName}'");
                }
                else
                {
                    Debug.LogWarning($"[RuntimeEffekseerLoader] Failed to download texture '{serverPath}' (from '{rawPath}') for effect '{effectName}'");
                }
            }

            asset.textureResources = textureResources.ToArray();

            // ----- 4. (Optional) Sound, model, material, curve loading -----
            // Sounds could be loaded via WavUtility + AudioClip.Create
            // Models/Materials/Curves require their respective ScriptableObject types
            // For now, these are left empty — the native plugin will skip missing resources.

            // ----- 5. Register in native Effekseer -----
            // This triggers the texture loader callbacks which will find our populated resources
            // via the effectAssetInLoading → GetTextureFromPath → FindTexture chain.
            EffekseerSystem.Instance.LoadEffect(asset);
            asset.LoadEffect();

            Debug.Log($"[RuntimeEffekseerLoader] Effect '{effectName}' loaded with {textureResources.Count} texture(s)");
            return asset;
        }

        /// <summary>
        /// Parse just the texture paths from an .efk without loading any assets.
        /// Useful for pre-flight inspection (e.g. to batch-download all textures for multiple effects).
        /// </summary>
        /// <param name="efkBytes">Raw .efk file data.</param>
        /// <returns>List of texture paths referenced in the .efk, or empty list if parsing fails.</returns>
        public static IReadOnlyList<string> GetTexturePaths(byte[] efkBytes)
        {
            var resourcePath = new EffekseerResourcePath();
            if (EffekseerEffectAsset.ReadResourcePath(efkBytes, ref resourcePath))
            {
                return resourcePath.TexturePathList.AsReadOnly();
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Parse all resource paths from an .efk without loading any assets.
        /// </summary>
        /// <param name="efkBytes">Raw .efk file data.</param>
        /// <returns>Parsed resource paths, or null if parsing fails.</returns>
        public static EffekseerResourcePath GetResourcePaths(byte[] efkBytes)
        {
            var resourcePath = new EffekseerResourcePath();
            if (EffekseerEffectAsset.ReadResourcePath(efkBytes, ref resourcePath))
            {
                return resourcePath;
            }

            return null;
        }

        /// <summary>
        /// Download a single texture from the server and decode it into a Texture2D.
        /// </summary>
        private static async UniTask<Texture2D> DownloadTextureAsync(
            ClientAssetLoader loader,
            string serverPath,
            int timeoutSeconds)
        {
            var bytes = await loader.GetAssetBytesAsync(
                serverPath,
                timeoutSeconds: timeoutSeconds);

            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            // Detect & decode animated container (GIF/WebP) or plain PNG
            var type = AnimationContainerDecoder.DetectType(bytes);
            if (type == AnimationContainerDecoder.ContainerType.GIF ||
                type == AnimationContainerDecoder.ContainerType.WebP)
            {
                var decoded = type == AnimationContainerDecoder.ContainerType.GIF
                    ? AnimationContainerDecoder.DecodeGif(bytes)
                    : AnimationContainerDecoder.DecodeWebP(bytes);

                if (decoded.Atlas != null)
                {
                    decoded.Atlas.name = $"EffekseerTex_{serverPath}";
                    decoded.Atlas.filterMode = FilterMode.Point;
                    return decoded.Atlas;
                }

                return null;
            }

            // PNG or other single-frame format
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
            {
                tex.name = $"EffekseerTex_{serverPath}";
                tex.filterMode = FilterMode.Point;
                return tex;
            }

            UnityEngine.Object.Destroy(tex);
            return null;
        }
    }
}
