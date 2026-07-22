using System.Collections.Generic;
using System.IO;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.Game.Managers
{
    public static class ItemRegistry
    {
        private const string TAG = "[ItemRegistry]";
        private static readonly Dictionary<ItemType, Texture2D> _iconCache = new();

        public static string GetName(ItemType type) => type.ToString();

        public static string GetDescription(ItemType type) => string.Empty;

        public static IEnumerable<ItemType> AllTypes => (ItemType[])System.Enum.GetValues(typeof(ItemType));

        public static Texture2D GetIcon(ItemType type)
        {
            if (_iconCache.TryGetValue(type, out var t))
            {
                return t;
            }

            var typeName = type.ToString();
            var camelName = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
            var path = Path.Combine(Application.dataPath, "Textures", "Items", camelName + ".png");
            if (!File.Exists(path))
            {
                path = Path.Combine(Application.dataPath, "Textures", "Items", typeName.ToLowerInvariant() + ".png");
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"{TAG} Icon not found for {type} (searched {camelName}.png, {typeName.ToLowerInvariant()}.png)");
                return null;
            }

            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(path));
            _iconCache[type] = tex;
            return tex;
        }
    }
}
