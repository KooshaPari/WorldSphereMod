// WSM3D/OpaqueVertexColor
//
// Minimal opaque shader that reads per-vertex Color as albedo + multiplies by
// _Color (MaterialPropertyBlock per-instance tint) + emission from _EmissionColor.
// Adds a basic directional light term so voxel actors get diffuse shading.
//
// Closes the "Standard-shader voxels render black because scene lighting doesn't
// reach them" issue with a lightweight diffuse + ambient term.
//
// Build step (once per WSM3D release that wants this on the visibility floor):
//   1. Open WorldSphereMod-AssetBundles Unity 2022.3 project
//   2. Add this file under Assets/Shaders/
//   3. Set as included in the 'worldsphere' AssetBundle (platform: Standalone)
//   4. Build AssetBundle to WorldSphereMod/AssetBundles/{win,linux,osx}/worldsphere
//
// At runtime VoxelRender.TryCompileInlineVoxelShader() resolves via
// Shader.Find("WSM3D/OpaqueVertexColor") + uses this in preference to Standard.

Shader "WSM3D/OpaqueVertexColor"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Main Tex (sampled white if unset)", 2D) = "white" {}
        _EmissionColor ("Emission", Color) = (0.15,0.15,0.15,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "DisableBatching"="False" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Name "OpaqueVertexColor"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _EmissionColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed4 emiss = UNITY_ACCESS_INSTANCED_PROP(Props, _EmissionColor);
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed3 albedo = i.color.rgb * tint.rgb * tex.rgb;
                float NdotL = max(0.0, dot(normalize(i.worldNormal), _WorldSpaceLightPos0.xyz));
                fixed3 final = saturate(albedo * (NdotL * 0.6 + 0.4) + emiss.rgb);
                return fixed4(final, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
