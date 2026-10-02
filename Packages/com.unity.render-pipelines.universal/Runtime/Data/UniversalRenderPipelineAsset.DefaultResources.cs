using System;
using UnityEditor;

namespace UnityEngine.Rendering.Universal
{
    internal enum DefaultMaterialType
    {
        Default,
        Particle,
        Terrain,
        Sprite,
        SpriteMask,
        Decal,
    }

    public partial class UniversalRenderPipelineAsset
    {
        #region Materials

        Material GetMaterial(DefaultMaterialType materialType)
        {
#if UNITY_EDITOR
            Material material = null;

            if (scriptableRendererData != null)
                material = scriptableRendererData.GetDefaultMaterial(materialType);

            if (material == null)
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineEditorMaterials>(out var defaultMaterials))
                {
                    return materialType switch
                    {
                      DefaultMaterialType.Default => defaultMaterials.defaultMaterial,
                      DefaultMaterialType.Particle => defaultMaterials.defaultParticleUnlitMaterial,
                      DefaultMaterialType.Terrain => defaultMaterials.defaultTerrainLitMaterial,
                      DefaultMaterialType.Decal => defaultMaterials.defaultDecalMaterial,
                      DefaultMaterialType.Sprite => defaultMaterials.defaultSpriteMaterial,
                      _ => null
                    };
                }
            }

            return material;
#else
            return null;
#endif
        }

        /// <returns>Returns the default Material.</returns>
        public override Material defaultMaterial => GetMaterial(DefaultMaterialType.Default);

        /// <returns>Returns the default particle Material.</returns>
        public override Material defaultParticleMaterial => GetMaterial(DefaultMaterialType.Particle);

        /// <returns>Returns the default line Material.</returns>
        public override Material defaultLineMaterial => GetMaterial(DefaultMaterialType.Particle);

        /// <returns>Returns the default terrain Material.</returns>
        public override Material defaultTerrainMaterial => GetMaterial(DefaultMaterialType.Terrain);

        /// <returns>Returns the material containing the default lit and unlit shader passes for sprites in the 2D renderer.</returns>
        public override Material default2DMaterial => GetMaterial(DefaultMaterialType.Sprite);

        /// <returns>Returns the material containing the default shader pass for sprite mask in the 2D renderer.</returns>
        public override Material default2DMaskMaterial => GetMaterial(DefaultMaterialType.SpriteMask);

        /// <returns>Returns the Material containing the Unity decal shader.</returns>
        public Material decalMaterial => GetMaterial(DefaultMaterialType.Decal);

        #endregion

        #region Shaders

#if UNITY_EDITOR
        private UniversalRenderPipelineEditorShaders defaultShaders =>
            GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineEditorShaders>();
#endif

        Shader m_DefaultShader;

        /// <returns>Returns the default shader for the specified renderer.</returns>
        public override Shader defaultShader
        {
            get
            {
#if UNITY_EDITOR
                // TODO: When importing project, AssetPreviewUpdater:CreatePreviewForAsset will be called multiple time
                // which in turns calls this property to get the default shader.
                // The property should never return null as, when null, it loads the data using AssetDatabase.LoadAssetAtPath.
                // However it seems there's an issue that LoadAssetAtPath will not load the asset in some cases. so adding the null check
                // here to fix template tests.
                if (scriptableRendererData != null)
                {
                    Shader defaultShader = scriptableRendererData.GetDefaultShader();
                    if (defaultShader != null)
                        return defaultShader;
                }

                if (m_DefaultShader == null)
                {
                    string path = AssetDatabase.GUIDToAssetPath(ShaderUtils.GetShaderGUID(ShaderPathID.Lit));
                    m_DefaultShader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                }
#endif

                if (m_DefaultShader == null)
                    m_DefaultShader = Shader.Find(ShaderUtils.GetShaderPath(ShaderPathID.Lit));

                return m_DefaultShader;
            }
        }

        #region Terrain

        public override Shader terrainDetailLitShader
        {
            get
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                        out var shadersResources))
                {
                    return shadersResources.terrainDetailLitShader;
                }

                return null;
            }
        }

        public override Shader terrainDetailGrassShader
        {
            get
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                        out var shadersResources))
                {
                    return shadersResources.terrainDetailGrassShader;
                }

                return null;
            }
        }

        public override Shader terrainDetailGrassBillboardShader
        {
            get
            {
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRenderPipelineRuntimeTerrainShaders>(
                        out var shadersResources))
                {
                    return shadersResources.terrainDetailGrassBillboardShader;
                }

                return null;
            }
        }

        #endregion

#if UNITY_EDITOR

        #region Autodesk

        /// <returns>Returns the Autodesk Interactive shader that this asset uses.</returns>
        public override Shader autodeskInteractiveShader => defaultShaders?.autodeskInteractiveShader;

        /// <returns>Returns the Autodesk Interactive transparent shader that this asset uses.</returns>
        public override Shader autodeskInteractiveTransparentShader => defaultShaders?.autodeskInteractiveTransparentShader;

        /// <returns>Returns the Autodesk Interactive mask shader that this asset uses</returns>
        public override Shader autodeskInteractiveMaskedShader => defaultShaders?.autodeskInteractiveMaskedShader;

        #endregion

        #region SpeedTree

        /// <returns>Returns the default SpeedTree7 shader that this asset uses.</returns>
        public override Shader defaultSpeedTree7Shader => defaultShaders?.defaultSpeedTree7Shader;

        /// <returns>Returns the default SpeedTree8 shader that this asset uses.</returns>
        public override Shader defaultSpeedTree8Shader => defaultShaders?.defaultSpeedTree8Shader;

        /// <returns>Returns the default SpeedTree9 shader that this asset uses.</returns>
        public override Shader defaultSpeedTree9Shader => defaultShaders?.defaultSpeedTree9Shader;

        #endregion

#endif

        #endregion
    }
}
