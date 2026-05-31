// WSM3D/FoliageWind
//
// Crossed-quad foliage shader for Phase 3. Consumes the wind globals that
// WindSwayDriver uploads every LateUpdate (_WindTime, _WindDir, _WindSpeed),
// sways only the top of each quad (uv2.x ~ height-along-quad in 0..1), and
// scales sway by vertex.color.a so the mesher can opt rocks out (alpha=0) and
// trees in (alpha>0). Vertex.color.rgb is the sprite-sampled per-vertex tint
// (multiplied by per-instance _Color from the MaterialPropertyBlock), and
// _MainTex provides the cutout alpha for crossed-quad foliage cards.
//
// Build step: included in the wsm3d-shaders AssetBundle bake list
// (Tools/Unity-Bake-Project/Assets/Editor/BakeShaders.cs).
// Runtime resolution: WorldSphereMod/Code/Foliage/FoliageMaterial.cs
// (Core.Sphere.LoadedShaders["FoliageWind"] → Shader.Find →
// Resources.Load<Shader>("Shaders/FoliageWind")).

Shader "WSM3D/FoliageWind"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _SwayScale ("Global Sway Scale", Float) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest+1"
            "IgnoreProjector" = "True"
            "DisableBatching" = "False"
        }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "FoliageWind"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
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
            float _Cutoff;
            float _SwayScale;

            // Globals uploaded by WindSwayDriver.LateUpdate.
            float _WindTime;
            float4 _WindDir;
            float _WindSpeed;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // uv2.x = height-along-quad in 0..1; base verts stay anchored,
                // tops sway maximum. Square the falloff so the bottom third
                // barely budges.
                float heightT = saturate(v.uv2.x);
                float falloff = heightT * heightT;

                // Vertex.color.a = per-vertex sway amplitude written by the
                // CrossedQuadMesher (rocks=0 → no sway, trees>0 → sway).
                float amp = v.color.a;

                // Phase noise so adjacent quads don't sway in lockstep. World-
                // space xz seeds the phase; cheap sin avoids needing a noise
                // texture lookup in the vertex stage.
                float3 wpos = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                float phase = wpos.x * 0.37 + wpos.z * 0.21;
                float t = _WindTime * _WindSpeed + phase;

                float sway = sin(t) * 0.7 + sin(t * 2.3 + 1.7) * 0.3;
                float2 dir = _WindDir.xy;
                // Guard against unset globals (zero vector) — keep meshes static
                // until WindSwayDriver has fired at least once.
                float dirLen = max(length(dir), 1e-4);
                dir /= dirLen;

                float3 offset = float3(dir.x, 0, dir.y) * (sway * falloff * amp * _SwayScale);
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                worldPos.xyz += offset;
                o.pos = mul(UNITY_MATRIX_VP, worldPos);

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);

                // Crossed-quad cutout: discard fully transparent texels and
                // anything below the cutoff so the silhouette stays sharp
                // without enabling true alpha blending.
                fixed alpha = tex.a * tint.a;
                clip(alpha - _Cutoff);

                fixed3 albedo = i.color.rgb * tint.rgb * tex.rgb;

                // Cheap diffuse + ambient term so foliage isn't flat-lit.
                float3 n = normalize(i.worldNormal);
                float NdotL = max(0.0, dot(n, _WorldSpaceLightPos0.xyz));
                fixed3 lit = albedo * (NdotL * 0.55 + 0.45);

                return fixed4(lit, alpha);
            }
            ENDCG
        }
    }

    Fallback "Transparent/Cutout/VertexLit"
}
