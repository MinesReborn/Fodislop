#nullable enable

using System.Collections.Generic;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.UI.Programmator
{
    public interface IProgrammatorTextureCatalog
    {
        Texture2D? GetTexture(ProgAction action);
    }

    public sealed class ProgrammatorTextureRegistry : IProgrammatorTextureCatalog
    {
        private readonly Dictionary<ProgAction, Texture2D> _cache = new();

        public Texture2D? GetTexture(ProgAction action)
        {
            if (_cache.TryGetValue(action, out var tex))
            {
                return tex;
            }

            tex = Resources.Load<Texture2D>($"Programmator/{(int)action}");
            if (tex != null)
            {
                RuntimeTextureFactory.ApplySampling(
                    tex,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                _cache[action] = tex;
            }

            return tex;
        }
    }
}
