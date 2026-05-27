// WSM3D/GerstnerWater
//
// Mesh water surface with vertex-displaced Gerstner waves + Fresnel-tinted blue
// coloration. This shader targets the built-in render pipeline and avoids URP
// includes so it can compile in the WorldBox bake/runtime path.
//
// Properties:
//   _Color           — base water tint (RGBA, alpha controls transparency)
//   _DeepColor       — deeper-water tint (mixed by view angle)
//   _WaveTime        — driven by WaterRender per frame; controls phase
//   _WaveAmplitude   — peak crest height
//   _WaveSteepness   — wave shape sharpness (0 = sine, 1 = peaked)
//   _WaveDirX/Z      — primary wave direction unit vector
//   _Foam            — foam color (added near crests)
//
// At runtime VoxelRender / WaterSurface resolves via Shader.Find("WSM3D/GerstnerWater").

Shader "WSM3D/GerstnerWater"
{
    Properties
    {
        _Color ("Shallow Color", Color) = (0.22, 0.65, 0.70, 0.75)
        _DeepColor ("Deep Color", Color) = (0.04, 0.12, 0.30, 0.95)
        _Foam ("Foam Color", Color) = (0.92, 0.95, 1.00, 1)
        _WaterDepth ("Water Depth", Float) = 0
        _MaxDepth ("Max Depth", Float) = 6
        _WaveTime ("Wave Time", Float) = 0
        _WaveAmplitude ("Wave Amplitude", Range(0, 1)) = 0.05
        _WaveSteepness ("Wave Steepness", Range(0, 1)) = 0.35
        _WaveDirX ("Wave Dir X", Float) = 0.7
        _WaveDirZ ("Wave Dir Z", Float) = 0.7
        _WaveLength ("Wave Length", Range(1, 50)) = 10
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Back
        ZWrite On

        Pass
        {
            Name "GerstnerWaterPass"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; float3 worldNormal : TEXCOORD1; float foam : TEXCOORD2; float depth : TEXCOORD3; };

            fixed4 _Color, _DeepColor, _Foam;
            float _WaterDepth, _MaxDepth;
            float _WaveTime, _WaveAmplitude, _WaveSteepness, _WaveDirX, _WaveDirZ, _WaveLength;

            float3 GerstnerWave(float3 p, out float crestFactor)
            {
                float2 dir = normalize(float2(_WaveDirX, _WaveDirZ));
                float k = UNITY_TWO_PI / max(_WaveLength, 0.001);
                float phase = dot(dir, p.xz) * k + _WaveTime;
                float c = cos(phase);
                float s = sin(phase);
                float steep = _WaveSteepness / max(k * _WaveAmplitude, 0.001);
                float3 displ;
                displ.x = steep * _WaveAmplitude * dir.x * c;
                displ.z = steep * _WaveAmplitude * dir.y * c;
                displ.y = _WaveAmplitude * s;
                crestFactor = saturate(s * 0.5 + 0.5);
                return displ;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float crest;
                float3 displ = GerstnerWave(worldPos, crest);
                worldPos += displ;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.worldPos = worldPos;
                o.worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);
                o.foam = crest;
                o.depth = v.color.r;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 N = normalize(i.worldNormal);
                float fresnel = pow(1 - saturate(dot(N, viewDir)), 3);

                float depthFrac = saturate(i.depth);
                fixed4 shallow = _Color;
                fixed4 deep = _DeepColor;
                fixed3 baseTint = lerp(shallow.rgb, deep.rgb, depthFrac);
                float baseAlpha = lerp(shallow.a, deep.a, depthFrac);

                fixed3 foamMixed = lerp(baseTint, _Foam.rgb, smoothstep(0.82, 0.98, i.foam));
                fixed3 finalRgb = lerp(foamMixed, foamMixed * 1.15 + 0.08, fresnel * 0.4);

                return fixed4(finalRgb, baseAlpha);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
