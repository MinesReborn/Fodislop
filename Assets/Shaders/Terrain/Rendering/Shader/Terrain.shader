Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        // Runtime materials must inject both textures. Neutral shader values
        // deliberately make a missing injection visible instead of rendering
        // an implicit white/gray world.
        _PrismaticFlowMap ("X Crystal Phase Vectors", 2D) = "black" {}
        _FlowMap ("Shimmer Flow Map", 2D) = "black" {}
        _TerrainDecalAtlasArray ("Terrain Decal Atlas Array", 2DArray) = "" {}
        _ShimmerColor ("Shimmer Color", Color) = (0,0,0,0)
        _FlowScale ("Flow Scale", Vector) = (0,0,0,0)
        _ShimmerSpeedScale ("Shimmer Speed Scale", Float) = 0
        _BlinkingSpeedScale ("Blinking Speed Scale", Float) = 0
        _EmissionBrightnessStart ("Emission Brightness Start", Float) = 0.25
        _EmissionBrightnessEnd ("Emission Brightness End", Float) = 0.60
        _SurfaceEmissionStrength ("Surface Emission Strength", Float) = 1
        _TerrainDepthPixels ("Tunnel Depth (Pixels)", Range(0,16)) = 16
        _TerrainDepthDarkening ("Tunnel Wall Darkening", Range(0,1)) = 0.35
        _RoundableCornerRadius ("Roundable Corner Radius", Float) = 0.51
        [HideInInspector] _RimDistanceScale ("Rim Distance Scale", Float) = 0
        [HideInInspector] _RimFalloff ("Rim Falloff", Float) = 0
        [HideInInspector] _TerrainRimEnabled ("Terrain Rim Enabled", Float) = 0
        [HideInInspector] _RimQuantizationEnabled ("Rim Quantization Enabled", Float) = 0
        // Авторский вид поверхности: значения приезжают из TerrainConfigHolder
        // свойствами материала, дефолт здесь — те же числа.
        _TerrainDebugDeltaContrast ("Terrain Debug Delta Contrast", Float) = 128
        _FacetedGlintDirection ("Faceted Glint Direction", Vector) = (0.62,0.38,0,0)
        _FacetedGlintSweepStart ("Faceted Glint Sweep Start", Float) = -0.12
        _FacetedGlintSweepEnd ("Faceted Glint Sweep End", Float) = 1.12
        _FacetedGlintBandStart ("Faceted Glint Band Start", Float) = 0.035
        _FacetedGlintBandEnd ("Faceted Glint Band End", Float) = 0.13
        _FacetedGlintMaskStart ("Faceted Glint Mask Start", Float) = 0.2
        _FacetedGlintMaskEnd ("Faceted Glint Mask End", Float) = 0.75
        _FacetedGlintStrength ("Faceted Glint Strength", Float) = 0.45
        _FacetedGlintMix ("Faceted Glint Mix", Float) = 0.72
        _FacetedGlintRiseEnd ("Faceted Glint Rise End", Float) = 0.04
        _FacetedGlintFallStart ("Faceted Glint Fall Start", Float) = 0.28
        _FacetedGlintFallEnd ("Faceted Glint Fall End", Float) = 0.40
        _FacetedGlintSweepDuration ("Faceted Glint Sweep Duration", Float) = 0.40
        _ShimmerChromaFloor ("Shimmer Chroma Floor", Float) = 0.65
        _PrismaticPhaseSpeed ("Prismatic Phase Speed", Float) = 0.05
        _RainbowHueDivisor ("Rainbow Hue Divisor", Float) = 255
        _PrismaticTintA ("Prismatic Tint A", Color) = (0.2,1,0.2,1)
        _PrismaticTintB ("Prismatic Tint B", Color) = (0.2,0.2,1,1)
        _PrismaticTintC ("Prismatic Tint C", Color) = (1,1,1,1)
        _PrismaticTintD ("Prismatic Tint D", Color) = (0.1,1,1,1)
        _PrismaticTintE ("Prismatic Tint E", Color) = (1,0,0,1)
        _PremultiplyAlphaFloor ("Premultiply Alpha Floor", Float) = 0.15
        _AlphaCutoff ("Alpha Cutoff", Float) = 0.05
        [HideInInspector] _TerrainAtlas0 ("Terrain Atlas 0", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas1 ("Terrain Atlas 1", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas2 ("Terrain Atlas 2", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas3 ("Terrain Atlas 3", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas4 ("Terrain Atlas 4", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas5 ("Terrain Atlas 5", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas6 ("Terrain Atlas 6", 2D) = "black" {}
        [HideInInspector] _TerrainAtlas7 ("Terrain Atlas 7", 2D) = "black" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ KERN_WORLD_LIGHTING
            #include "Assets/Shaders/Terrain/Rendering/Passes/Forward/TerrainForwardPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "LightingMaterialField"
            Tags { "LightMode" = "KernLightingMaterialField" }

            // Альбедо видимого слоя: меш рисует все фоновые квады раньше
            // передних, поэтому передний план цвет фона перезаписывает, как
            // на экране. Занятость и сила свечения — максимум.
            Blend One Zero, One One
            BlendOp Add, Max
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainLightingFieldVert
            #pragma fragment MaterialFieldFrag
            #include "Assets/Shaders/Terrain/Rendering/Passes/MaterialField/TerrainMaterialFieldPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "LightingAmbientOcclusionField"
            Tags { "LightMode" = "KernLightingAmbientOcclusionField" }

            Blend One One
            BlendOp Max
            ColorMask R
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex TerrainLightingFieldVert
            #pragma fragment AmbientOcclusionFieldFrag
            #include "Assets/Shaders/Terrain/Rendering/Passes/AmbientOcclusionField/TerrainAmbientOcclusionFieldPass.hlsl"
            ENDHLSL
        }
    }
}
