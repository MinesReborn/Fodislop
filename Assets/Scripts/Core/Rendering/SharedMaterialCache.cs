#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fodinae.Core
{
    public interface ISharedMaterialCache
    {
        Material GetForTexture(Texture2D texture);
    }

    public sealed class SharedMaterialCache : ISharedMaterialCache, IDisposable
    {
        private readonly Dictionary<Texture2D, Material> _materials = new();
        private Shader? _shader;

        private Shader Shader
        {
            get
            {
                if (_shader == null)
                {
                    _shader = Shader.Find(ProjectRuntimeContracts.ShaderNames.WorldEntity) ??
                        throw new InvalidOperationException(
                            "SharedMaterialCache requires the supported " +
                            $"'{ProjectRuntimeContracts.ShaderNames.WorldEntity}' shader.");
                }

                return _shader;
            }
        }

        public Material GetForTexture(Texture2D texture)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(nameof(texture));
            }

            if (_materials.TryGetValue(texture, out var mat))
            {
                return mat;
            }

            mat = new Material(Shader)
            {
                name = $"Shared Sprite Material ({texture.name})",
                hideFlags = HideFlags.DontSave,
                mainTexture = texture,
            };
            _materials[texture] = mat;
            return mat;
        }

        public void Dispose()
        {
            foreach (var mat in _materials.Values)
            {
                if (mat != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(mat);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(mat);
                    }
                }
            }

            _materials.Clear();
            _shader = null;
        }
    }
}
