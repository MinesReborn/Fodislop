using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fodinae.Assets.Scripts.World;
using MinesServer.Data;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Manages multiple materials for different texture atlases
    /// </summary>
    public class MultiAtlasMaterialManager : MonoBehaviour
    {
        [Header("Material Configuration")]
        [Tooltip("Base material template for atlas rendering")]
        [SerializeField] private Material _baseMaterialTemplate; [Tooltip("Shader property name for the main texture")]
        [SerializeField] private string _texturePropertyName = "_BaseMap";

        [Tooltip("Shader property name for the atlas texture")]
        [SerializeField] private string _atlasTexturePropertyName = "_AtlasTexture";

        private readonly Dictionary<int, Material> _atlasMaterials = new();
        private readonly Dictionary<Texture2D, int> _textureToAtlasIndex = new();
        private int _nextAtlasIndex = 0;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_baseMaterialTemplate == null)
            {
                // Create a default material if none provided
                _baseMaterialTemplate = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

                // Ensure it supports transparency
                _baseMaterialTemplate.SetFloat("_Surface", 1.0f);
                _baseMaterialTemplate.SetFloat("_Blend", 0.0f);
                _baseMaterialTemplate.SetOverrideTag("RenderType", "Transparent");
                _baseMaterialTemplate.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _baseMaterialTemplate.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _baseMaterialTemplate.SetFloat("_ZWrite", 0.0f);
                _baseMaterialTemplate.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _baseMaterialTemplate.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            // Create initial material
            CreateAtlasMaterial(0, null);
        }

        /// <summary>
        /// Get or create a material for the specified atlas texture
        /// </summary>
        /// <param name="atlasTexture">The atlas texture</param>
        /// <returns>Material index for the atlas</returns>
        public int GetAtlasMaterialIndex(Texture2D atlasTexture)
        {
            if (atlasTexture == null) return 0;

            // Check if we already have a material for this texture
            if (_textureToAtlasIndex.TryGetValue(atlasTexture, out int index))
            {
                return index;
            }

            // Create new material for this atlas
            index = _nextAtlasIndex++;
            CreateAtlasMaterial(index, atlasTexture);

            _textureToAtlasIndex[atlasTexture] = index;
            return index;
        }

        /// <summary>
        /// Create a material for the specified atlas texture
        /// </summary>
        /// <param name="index">Material index</param>
        /// <param name="atlasTexture">The atlas texture</param>
        private void CreateAtlasMaterial(int index, Texture2D atlasTexture)
        {
            var material = new Material(_baseMaterialTemplate);
            material.name = $"AtlasMaterial_{index}";

            if (atlasTexture != null)
            {
                material.SetTexture(_atlasTexturePropertyName, atlasTexture);
            }

            _atlasMaterials[index] = material;
        }

        /// <summary>
        /// Get all materials for rendering
        /// </summary>
        /// <returns>Array of materials</returns>
        public Material[] GetAllMaterials()
        {
            return _atlasMaterials.Values.ToArray();
        }

        /// <summary>
        /// Get material by index
        /// </summary>
        /// <param name="index">Material index</param>
        /// <returns>Material or null if not found</returns>
        public Material GetMaterial(int index)
        {
            return _atlasMaterials.TryGetValue(index, out var material) ? material : null;
        }

        /// <summary>
        /// Update all materials with current atlas textures
        /// </summary>
        public void UpdateAtlasTextures()
        {
            var atlases = TextureAtlasManager.Instance?.GetAllAtlases();
            if (atlases == null) return;

            // Clear existing texture mappings
            _textureToAtlasIndex.Clear();

            // Update materials with current atlas textures
            for (int i = 0; i < atlases.Count; i++)
            {
                var atlas = atlases[i];
                var texture = atlas.GetAtlasTexture().GetAwaiter().GetResult();

                if (texture != null)
                {
                    int index = GetAtlasMaterialIndex(texture);
                    var material = GetMaterial(index);
                    if (material != null)
                    {
                        material.SetTexture(_atlasTexturePropertyName, texture);
                    }
                }
            }
        }

        /// <summary>
        /// Clear all materials and reset
        /// </summary>
        public void Clear()
        {
            foreach (var material in _atlasMaterials.Values)
            {
                Destroy(material);
            }
            _atlasMaterials.Clear();
            _textureToAtlasIndex.Clear();
            _nextAtlasIndex = 0;

            // Recreate default material
            CreateAtlasMaterial(0, null);
        }
    }

    /// <summary>
    /// Singleton manager for texture atlas system
    /// </summary>
    public class TextureAtlasManager : MonoBehaviour
    {
        private static TextureAtlasManager _instance;
        public static TextureAtlasManager Instance => _instance;

        private WorldTextureManager _worldTextureManager;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _worldTextureManager = FindObjectOfType<WorldTextureManager>();
        }

        /// <summary>
        /// Get all active texture atlases
        /// </summary>
        /// <returns>List of texture atlases</returns>
        public List<TextureAtlas> GetAllAtlases()
        {
            return _worldTextureManager?.GetAllAtlases() ?? new List<TextureAtlas>();
        }

        /// <summary>
        /// Get texture coordinate for a cell
        /// </summary>
        /// <param name="cellType">Cell type</param>
        /// <param name="x">World X position</param>
        /// <param name="y">World Y position</param>
        /// <returns>Atlas coordinate</returns>
        public async System.Threading.Tasks.ValueTask<AtlasCoordinate> GetCellTextureCoordinate(CellType cellType, int x, int y)
        {
            return await _worldTextureManager.GetCellTextureCoordinate(cellType, x, y);
        }
    }
}