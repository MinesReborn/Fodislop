Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        _WorldMapTex ("World Map", 2D) = "white" {}
        _CellConfigTex ("Cell Config", 2D) = "white" {}
        _Atlases ("Atlases", 2DArray) = "white" {}
        _FlowMapRect ("Flow Map Rect", Vector) = (0,0,0,0)
        _ShimmerColor ("Shimmer Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #define PASS_UNIVERSAL_FORWARD
            #include "TerrainPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }
            Cull Off
            ZWrite On
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #define PASS_UNIVERSAL_2D
            #include "TerrainPass.hlsl"
            ENDHLSL
        }
    }
}
