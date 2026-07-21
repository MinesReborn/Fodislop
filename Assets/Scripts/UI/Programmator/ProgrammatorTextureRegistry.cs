using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Scripts.UI.Programmator
{
    public static class ProgrammatorTextureRegistry
    {
        private static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        public static Texture2D GetTexture(int id)
        {
            if (id == 0)
            {
                return null;
            }

            if (_cache.TryGetValue(id, out var tex))
            {
                return tex;
            }

            tex = Resources.Load<Texture2D>($"Programmator/{id}");
            if (tex != null)
            {
                _cache[id] = tex;
            }

            return tex;
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
