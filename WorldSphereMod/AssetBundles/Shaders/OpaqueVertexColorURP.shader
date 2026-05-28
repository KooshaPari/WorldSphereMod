// WSM3D/OpaqueVertexColorURP
//
// URP variant of OpaqueVertexColor for projects with the Universal Render
// Pipeline. Identical output: per-vertex Color × _Color × _MainTex + emission,
// no lighting attenuation. SRP-batcher compatible.
//
// WorldBox itself doesn't ship URP but downstream consumers / future migrations
// might. Bake alongside the BRP variant so the AssetBundle is multi-pipeline.

Shader "WSM3D/OpaqueVertexColorURP"
{
    Properties
    {
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Main Tex", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0.15, 0.15, 0.15, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = IN.color.rgb * _BaseColor.rgb * tex.rgb;
                half3 final = saturate(albedo + _EmissionColor.rgb);
                return half4(final, 1);
            }
            ENDHLSL
        }
    }

    Fallback "WSM3D/OpaqueVertexColor"
}
