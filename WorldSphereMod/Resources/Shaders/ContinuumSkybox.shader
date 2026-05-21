// Fresh skybox shader for WSM3D's HDR cube bake path.
// It writes a sun-driven gradient directly into a cubemap so water and
// voxel reflections can sample the baked result without a separate probe.

Shader "WorldSphereMod3D/ContinuumSkybox"
{
    Properties
    {
        _ZenithColor   ("Zenith", Color) = (0.20, 0.42, 0.82, 1)
        _HorizonColor  ("Horizon", Color) = (0.78, 0.86, 0.96, 1)
        _GroundColor   ("Ground", Color) = (0.10, 0.09, 0.10, 1)
        _SunDir        ("Sun Direction (world)", Vector) = (0, 1, 0, 0)
        _SunColor      ("Sun Color", Color) = (1, 0.95, 0.88, 1)
        _Exposure      ("Exposure", Float) = 1.25
        _SunSize       ("Sun Size", Float) = 0.04
        _SunBloom      ("Sun Bloom", Float) = 0.22
        _SunGlow       ("Sun Glow", Float) = 0.9
        _HorizonPower  ("Horizon Power", Float) = 3.5
        _HorizonGlow   ("Horizon Glow", Float) = 0.25
        _AerialFactor  ("Aerial Factor", Float) = 0.2
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float4 _SunDir;
                float4 _SunColor;
                float  _Exposure;
                float  _SunSize;
                float  _SunBloom;
                float  _SunGlow;
                float  _HorizonPower;
                float  _HorizonGlow;
                float  _AerialFactor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.dirWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            float3 BuildSky(float3 dir, float3 sunDir)
            {
                float up = saturate(dir.y * 0.5f + 0.5f);
                float horizonBand = pow(saturate(1.0f - abs(dir.y)), _HorizonPower);
                float sunAltitude = saturate(sunDir.y * 0.5f + 0.5f);
                float dusk = saturate(1.0f - sunAltitude);

                float3 horizon = lerp(_HorizonColor.rgb, _SunColor.rgb, dusk * 0.65f);
                float3 zenith = lerp(_ZenithColor.rgb, _SunColor.rgb * 0.35f, dusk * 0.20f);
                float3 ground = lerp(_GroundColor.rgb, horizon, saturate(dir.y + 0.12f));

                float3 sky = lerp(ground, horizon, saturate(up + horizonBand * _AerialFactor));
                sky = lerp(sky, zenith, up * (0.35f + 0.65f * horizonBand));

                float sunDot = dot(dir, sunDir);
                float sunDisc = smoothstep(cos(_SunSize), cos(_SunSize * 0.35f), sunDot);
                float sunHalo = pow(saturate(sunDot), 64.0f) * _SunGlow;
                float horizonGlow = horizonBand * _HorizonGlow * dusk;

                sky += _SunColor.rgb * (sunDisc * _SunBloom + sunHalo);
                sky += horizonGlow * _SunColor.rgb;
                return max(sky, 0.0f);
            }

            float4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dirWS);
                float3 sunDir = normalize(_SunDir.xyz);
                float3 color = BuildSky(dir, sunDir) * _Exposure;
                return float4(color, 1.0f);
            }
            ENDHLSL
        }
    }
}
