#nullable enable

// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Effekseer;
using Effekseer.Internal;
using Fodinae;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using Fodinae.World.Terrain;
using UnityEngine;

namespace Fodinae.Effekseer
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
        private static readonly HashSet<EntityId> ActiveRuntimeEffectIds = new();

        public static int ActiveRuntimeEffectCount => ActiveRuntimeEffectIds.Count;

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
        /// <param name="assetLoader">The injected asset loader used for texture downloads.</param>
        /// <param name="textureTimeoutSeconds">
        /// Per-texture download timeout. Defaults to 10 seconds.
        /// </param>
        /// <returns>
        /// A loaded <see cref="EffekseerEffectAsset"/> with textures populated,
        /// registered in <see cref="EffekseerSystem"/> and ready to play.
        /// Invalid data or missing resources throw and never produce a partial effect.
        /// </returns>
        public static async UniTask<EffekseerEffectAsset> LoadEffectAsync(
            byte[] efkBytes,
            string effectName,
            IAssetLoader assetLoader,
            Func<string, string>? texturePathMapper = null,
            int textureTimeoutSeconds = 10)
        {
            if (efkBytes == null || efkBytes.Length < 4)
            {
                throw new ArgumentException(
                    "Effect data is empty or too short to be a valid .efk file.",
                    nameof(efkBytes));
            }

            if (!EffekseerSystem.IsValid)
            {
                throw new InvalidOperationException(
                    "EffekseerSystem must be initialized before loading a runtime effect.");
            }

            if (assetLoader == null)
            {
                throw new ArgumentNullException(nameof(assetLoader));
            }

            // ----- 1. Parse resource paths from the .efk binary -----
            var resourcePath = new EffekseerResourcePath();
            if (!EffekseerEffectAsset.ReadResourcePath(efkBytes, ref resourcePath))
            {
                throw new InvalidDataException(
                    $"Failed to parse .efk resource paths for effect '{effectName}'.");
            }

            // ----- 2. Create the asset container -----
            var asset = ScriptableObject.CreateInstance<EffekseerEffectAsset>();
            asset.efkBytes = efkBytes;
            asset.name = effectName;

            try
            {
                // ----- 3. Download and assign textures -----
                var textureResources = new List<EffekseerTextureResource>(resourcePath.TexturePathList.Count);
                foreach (var rawPath in resourcePath.TexturePathList)
                {
                    // Apply optional path remapping
                    var serverPath = texturePathMapper?.Invoke(rawPath) ?? rawPath;
                    if (serverPath == null)
                    {
                        Debug.LogWarning($"[RuntimeEffekseerLoader] Texture '{rawPath}' skipped by mapper");
                        continue;
                    }

                    var tex = await DownloadTextureAsync(assetLoader, serverPath, textureTimeoutSeconds);
                    textureResources.Add(new EffekseerTextureResource
                    {
                        path = rawPath,
                        texture = tex,
                    });
                }

                asset.textureResources = textureResources.ToArray();

                // ----- 4. (Optional) Sound, model, material, curve loading -----
                // Sounds could be loaded via WavUtility + AudioClip.Create
                // Models/Materials/Curves require their respective ScriptableObject types
                // For now, these are left empty — the native plugin will skip missing resources.

                // ----- 5. Register in native Effekseer -----
                // LoadEffect is intentionally called exactly once. Calling asset.LoadEffect()
                // after this repeats the native resource reload for the same asset.
                EffekseerSystem.Instance.LoadEffect(asset);
                ActiveRuntimeEffectIds.Add(asset.GetEntityId());
                return asset;
            }
            catch
            {
                // A failed/cancelled load can happen after some textures were already decoded.
                // Do not leave a half-created ScriptableObject or native effect behind.
                DestroyEffect(asset);
                throw;
            }
        }

        /// <summary>
        /// Releases a runtime-created effect and the standalone textures decoded
        /// for it. These objects are not assets from the Unity database, so
        /// unloading the native effect alone does not reclaim their memory.
        /// </summary>
        public static void DestroyEffect(EffekseerEffectAsset? asset)
        {
            if (asset == null)
            {
                return;
            }

            ActiveRuntimeEffectIds.Remove(asset.GetEntityId());

            var resources = asset.textureResources;
            if (resources != null)
            {
                for (int i = 0; i < resources.Length; i++)
                {
                    var texture = resources[i].texture;
                    if (texture != null)
                    {
                        UnityEngine.Object.Destroy(texture);
                    }
                }
            }

            UnityEngine.Object.Destroy(asset);
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
        public static EffekseerResourcePath? GetResourcePaths(byte[] efkBytes)
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
            IAssetLoader loader,
            string serverPath,
            int timeoutSeconds)
        {
            var bytes = await loader.GetAssetBytesAsync(
                serverPath,
                timeoutSeconds: timeoutSeconds);

            if (bytes == null || bytes.Length == 0)
            {
                throw new FileNotFoundException(
                    $"Effect texture '{serverPath}' was not returned by the asset loader.");
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
                    RuntimeTextureFactory.ApplySampling(
                        decoded.Atlas,
                        FilterMode.Point,
                        TextureWrapMode.Repeat);
                    return decoded.Atlas;
                }

                throw new InvalidDataException(
                    $"Animated effect texture '{serverPath}' contains no decodable frames.");
            }

            // Single-frame images are normalized to the same explicit runtime
            // format as terrain and UI textures.
            return RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
                bytes,
                $"EffekseerTex_{serverPath}",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Repeat,
                makeNoLongerReadable: true);
        }
    }
}
