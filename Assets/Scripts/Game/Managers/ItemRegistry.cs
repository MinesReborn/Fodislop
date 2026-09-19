#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using Kern.Core.Interfaces;
using UnityEngine;

namespace Kern.Game.Managers;

public sealed class ItemRegistry(IRuntimeAssetPaths runtimeAssetPaths) : IItemCatalog
{
    private const string TAG = "[ItemRegistry]";
    private readonly Dictionary<ItemType, Texture2D?> _iconCache = new();
    private readonly HashSet<ItemType> _missingIconWarned = new();

    public string GetName(ItemType type) => type.ToString();

    public string GetDescription(ItemType type) => string.Empty;

    public IEnumerable<ItemType> AllTypes => (ItemType[])System.Enum.GetValues(typeof(ItemType));

    public Texture2D? GetIcon(ItemType type)
    {
        if (_iconCache.TryGetValue(type, out var t))
        {
            return t;
        }

        var typeName = type.ToString();
        var camelName = string.Create(typeName.Length, typeName, static (span, state) =>
        {
            state.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
        string? path = runtimeAssetPaths.FindBundledTextureFile($"Items/{camelName}.png") ??
            runtimeAssetPaths.FindBundledTextureFile($"Items/{typeName.ToLowerInvariant()}.png");

        if (path == null)
        {
            _iconCache[type] = null;
            if (_missingIconWarned.Add(type))
            {
                Debug.Log($"{TAG} No local icon for item type '{type}' (searched {camelName}.png), will use server texture if available");
            }

            return null;
        }

        Texture2D tex;
        try
        {
            tex = RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
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
