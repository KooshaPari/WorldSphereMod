// Phase 8 sky: 3-color gradient (zenith / horizon / ground) + sun disc.
// URP custom skybox pass. Hosek-Wilkie keyword reserved as future upgrade
// (`_HOSEK_WILKIE`), not implemented in this ship. Loaded at runtime via
// Resources.Load<Shader>("Shaders/ProceduralSky") by ProceduralSky.cs;
// shader output is also baked into a 128-res RenderTextureCube so Phase 4
// WaterGerstner's Fresnel stays in sync.

Shader "WorldSphereMod3D/ProceduralSky"
{
    Properties
    {
        _ZenithColor   ("Zenith",   Color)  = (0.18, 0.42, 0.78, 1)
        _HorizonColor  ("Horizon",  Color)  = (0.78, 0.86, 0.95, 1)
        _GroundColor   ("Ground",   Color)  = (0.12, 0.10, 0.08, 1)
        _SunDir        ("Sun Direction (world)", Vector) = (0, 1, 0, 0)
        _SunColor      ("Sun Color", Color) = (1, 0.96, 0.88, 1)
        _SunSize       ("Sun Size",        Float) = 0.04
        _SunBloom      ("Sun Bloom",       Float) = 0.12
        _HorizonPower  ("Horizon Power",   Float) = 4
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "Skybox"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _HOSEK_WILKIE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _GroundColor;
                float4 _SunDir;
                float4 _SunColor;
                float  _SunSize;
                float  _SunBloom;
                float  _HorizonPower;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 worldDir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.worldDir   = mul(unity_ObjectToWorld, v.positionOS).xyz;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.worldDir);
                float horizon = pow(1 - abs(dir.y), _HorizonPower);
                float3 color = lerp(
                    lerp(_GroundColor.rgb, _HorizonColor.rgb, saturate(dir.y + 0.05)),
                    _ZenithColor.rgb,
                    saturate(dir.y));
                float sunDot = dot(dir, _SunDir.xyz);
                float disc = smoothstep(cos(_SunSize), cos(_SunSize * 0.9), sunDot);
                color += _SunColor.rgb * disc * _SunBloom;
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
}
