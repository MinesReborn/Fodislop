#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using Fodinae.Core.Interfaces;
using UnityEngine;

namespace Fodinae.Game.Managers
{
    public sealed class ItemRegistry : IItemCatalog
    {
        private const string TAG = "[ItemRegistry]";
        private readonly Dictionary<ItemType, Texture2D> _iconCache = new();
        private readonly HashSet<ItemType> _missingIconWarned = new();
        private readonly IRuntimeAssetPaths _runtimeAssetPaths;

        public ItemRegistry(IRuntimeAssetPaths runtimeAssetPaths)
        {
            _runtimeAssetPaths = runtimeAssetPaths;
        }

        public string GetName(ItemType type) => type.ToString();

        public string GetDescription(ItemType type) => string.Empty;

        public IEnumerable<ItemType> AllTypes => (ItemType[])System.Enum.GetValues(typeof(ItemType));

        /// <summary>
        /// Loads a local icon on first use and retains it for the application lifetime.
        /// </summary>
        public Texture2D? GetIcon(ItemType type)
        {
            if (_iconCache.TryGetValue(type, out var t))
            {
                return t;
            }

            var typeName = type.ToString();
            var camelName = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
            // Раньше здесь стоял Application.dataPath напрямую — в редакторе это
            // Assets/, а в плеере каталог данных, куда сборка каталог Textures
            // не кладёт. Иконки предметов молча пропадали именно в билде.
            string? path = _runtimeAssetPaths.FindBundledTextureFile($"Items/{camelName}.png") ??
                _runtimeAssetPaths.FindBundledTextureFile($"Items/{typeName.ToLowerInvariant()}.png");

            if (path == null)
            {
                if (_missingIconWarned.Add(type))
                {
                    Debug.Log($"{TAG} No local icon for item type '{type}' (searched {camelName}.png), will use server texture if available");
                }

                return null;
            }

            Texture2D tex;
            try
            {
                tex = RuntimeTextureFactory.DecodeEncodedImageToRgba32NoMip(
                    File.ReadAllBytes(path),
                    $"ItemIcon_{type}",
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    makeNoLongerReadable: true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{TAG} Local icon '{path}' for item type '{type}' is corrupt; " +
                    $"will use the server texture if available. {exception.Message}");
                return null;
            }

            _iconCache[type] = tex;
            return tex;
        }

    }
}
