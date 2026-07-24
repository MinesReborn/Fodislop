using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.Scripts.UI.Programmator
{
    public static class ProgrammatorTextureRegistry
    {
        private static readonly Dictionary<ProgAction, Texture2D> _cache = new Dictionary<ProgAction, Texture2D>();

        public static Texture2D GetTexture(ProgAction action)
        {
            if (_cache.TryGetValue(action, out var tex))
            {
                return tex;
            }

            tex = Resources.Load<Texture2D>($"Programmator/{(int)action}");
            if (tex != null)
            {
                _cache[action] = tex;
            }

            return tex;
        }

        public static void ClearCache()
        {
            _cache.Clear();
        }
    }
}
