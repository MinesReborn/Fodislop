Shader "Kern/UI/MenuLineUnlit"
{
    // Unlit colour for the menu rig's orbit line and station point.
    //
    // Exists because Sprites/Default blends straight alpha (SrcAlpha,
    // OneMinusSrcAlpha) and the menu camera renders into a PREMULTIPLIED target
    // - the atmosphere shell needs that to both add in-scattered light and
    // attenuate the crust in one pass. Straight-alpha geometry written into a
    // premultiplied buffer applies the source alpha twice: once to the colour by
    // the blend, and again to the alpha channel, so the ring ended up stored at
    // alpha^2. The resolve blit then divided its colour by that too-small alpha
    // and handed the UI a line that was simultaneously too faint and too bright.
    //
    // The explicit render queue is the other half of the fix: this and the
    // atmosphere shell share a bounding-box centre (both are centred on the
    // planet), so distance sorting between them has no tie-breaker. The rig
    // pushes this material's queue above the shell's so the order is pinned
    // rather than left to whatever the sort happens to produce.
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Cull Off
        ZWrite Off

        // Depth-tested against the opaque crust, so the far half of the orbit
        // passes behind the planet instead of drawing over it.
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // LineRenderer bakes its width/colour gradient into vertex
                // colour; ignoring it would drop the taper the component is
                // configured with.
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 c = _Color * input.color;
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
