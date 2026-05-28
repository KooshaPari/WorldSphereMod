// WSM3D/StratumVoxelPBR
//
// Phase 1 Built-In pseudo-PBR voxel shader for Stratum atlas sampling.
// Samples per-voxel UV0 into atlas maps; keeps a vertex-color-only fallback
// via OpaqueVertexColor when _BaseMap is unset.
//
// Bake: include in wsm3d-shaders AssetBundle alongside OpaqueVertexColor.shader.

Shader "WSM3D/StratumVoxelPBR"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _HeightMap ("Height", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Name "StratumVoxelPBR"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _BaseMap;
            float4 _BaseMap_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed4 albedo = tex2D(_BaseMap, i.uv) * i.color * tint;
                return fixed4(albedo.rgb, 1);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
