using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Scripts.Core
{
    public static class SharedMaterialCache
    {
        private static readonly Dictionary<Texture2D, Material> _materials = new();
        private static Shader _shader;

        private static Shader Shader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = Shader.Find("Sprites/Default");
                }

                return _shader;
            }
        }

        public static Material GetForTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }

            if (_materials.TryGetValue(texture, out var mat))
            {
                return mat;
            }

            mat = new Material(Shader);
            mat.mainTexture = texture;
            _materials[texture] = mat;
            return mat;
        }

        public static void Clear()
        {
            foreach (var mat in _materials.Values)
            {
                if (mat != null)
                {
                    Object.Destroy(mat);
                }
            }

            _materials.Clear();
        }
    }
}
