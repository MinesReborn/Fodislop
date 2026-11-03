#nullable enable

using System;
using System.Collections.Generic;
using Kern;
using Kern.Core.Interfaces;
using Kern.World.Terrain;
using Kern.World.Textures;
using UnityEngine;

namespace Kern.World
{
    /// <summary>Owns generated flow maps and asynchronous terrain decal atlases.</summary>
    internal sealed class WorldTextureAuxiliaryAssets : IDisposable
    {
        private readonly TerrainDecalAtlasLoader _decalLoader = new();

        private Texture2D? _prismaticFlowMapTexture;
        private Texture2D? _flowMapTexture;

        public Texture2D? PrismaticFlowMapTexture => _prismaticFlowMapTexture;
        public Texture2D? FlowMapTexture => _flowMapTexture;
        public Texture2DArray? TerrainDecalAtlasArray => _decalLoader.TextureArray;

        public void Initialize()
        {
            _prismaticFlowMapTexture = WorldTextureGenerator.CreatePrismaticFlowMap();
            RegenerateFlowMap();
        }

        public void LoadDecals(
            IReadOnlyDictionary<string, byte[]> worldAssetBytes,
            IAsyncOperationSupervisor operations,
            Action notifyTextureLoaded,
            bool keepCurrentUntilReady = false) =>
            _decalLoader.Load(
                worldAssetBytes,
                TerrainDecalCatalog.GetTextureNames(),
                operations,
                notifyTextureLoaded,
                keepCurrentUntilReady);

        public void ClearDecals() => _decalLoader.Clear();

        public void RegenerateFlowMap()
        {
            DestroyTexture(_flowMapTexture);
            _flowMapTexture = WorldTextureGenerator.CreateFlowMap();
        }

        public void Dispose()
        {
            DestroyTexture(_flowMapTexture);
            _flowMapTexture = null;

            DestroyTexture(_prismaticFlowMapTexture);
            _prismaticFlowMapTexture = null;

            _decalLoader.Dispose();
        }

        private static void DestroyTexture(Texture2D? texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
