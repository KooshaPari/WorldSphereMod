// WSM3D/SphereTerrain
//
// Replaces OpaqueVertexColor for HeightField terrain. Reads per-vertex Color as
// albedo, multiplies by per-instance _Color (MaterialPropertyBlock tint), then
// RE-PROJECTS the UV through sphere UV math so the terrain's per-tile UVs map
// onto a sphere instead of rendering as flat columns.
//
// Phi = atan2(worldZ, worldX) / (2*PI) + 0.5
// Theta = acos(worldY / radius) / PI
// This gives a wrap-around UV in [0,1] for both axes that aligns with WorldBox's
// procedural sphere shape.
//
// Falls back to per-vertex UVs if sphere math would NaN (vertex at exact origin).
// Supports a Texture2DArray for biome layers sampled by per-vertex biomeIndex.

Shader "WSM3D/SphereTerrain"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Fallback", 2D) = "white" {}
        _TerrainTexArray ("Biome TexArray", 2DArray) = "white" {}
        _TerrainLayers ("Layer Count", Float) = 1
        _SphereRadius ("Sphere Radius", Float) = 256
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" "IgnoreProjector"="True" }
        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual
        Blend Off

        Pass
        {
            Name "SphereTerrain"
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
                float2 uv1 : TEXCOORD1;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 sphereUV : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            UNITY_DECLARE_TEX2D_ARRAY(_TerrainTexArray);
            float _TerrainLayers;
            float _SphereRadius;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float r = length(wp) + 1e-6;
                float phi = atan2(wp.z, wp.x) / 6.28318530718 + 0.5;
                float theta = acos(clamp(wp.y / max(r, 1e-6), -1, 1)) / 3.14159265359;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.sphereUV = float3(phi, theta, r);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int layer = clamp((int)(i.color.a * 255.0), 0, max(0, (int)_TerrainLayers - 1));
                fixed4 biome = UNITY_SAMPLE_TEX2D_ARRAY(_TerrainTexArray, i.sphereUV.xy, layer);
                fixed4 fallback = tex2D(_MainTex, i.sphereUV.xy);
                fixed4 albedo = lerp(fallback, biome, step(0.5, i.color.a));
                return albedo * i.color * _Color;
            }

            ENDCG
        }
    }
    Fallback "WSM3D/OpaqueVertexColor"
}
