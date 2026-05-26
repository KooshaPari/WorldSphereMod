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
        _Color ("Water Tint", Color) = (0.15, 0.40, 0.55, 0.55)
        _DeepColor ("Deep Color", Color) = (0.05, 0.20, 0.35, 1)
        _Foam ("Foam Color", Color) = (1, 1, 1, 1)
        _WaveTime ("Wave Time", Float) = 0
        _WaveAmplitude ("Wave Amplitude", Range(0, 1)) = 0.15
        _WaveSteepness ("Wave Steepness", Range(0, 1)) = 0.4
        _WaveDirX ("Wave Dir X", Float) = 0.7
        _WaveDirZ ("Wave Dir Z", Float) = 0.7
        _WaveLength ("Wave Length", Range(1, 50)) = 8
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "IgnoreProjector"="True" }
        LOD 200
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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; float3 worldNormal : TEXCOORD1; float foam : TEXCOORD2; };

            fixed4 _Color, _DeepColor, _Foam;
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
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fresnel = pow(1 - saturate(dot(normalize(i.worldNormal), viewDir)), 3);
                fixed3 baseTint = lerp(_DeepColor.rgb, _Color.rgb, fresnel);
                fixed3 finalRgb = lerp(baseTint, _Foam.rgb, smoothstep(0.85, 0.99, i.foam));
                return fixed4(finalRgb, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
