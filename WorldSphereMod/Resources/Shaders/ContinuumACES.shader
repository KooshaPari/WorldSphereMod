// ACES tonemap pass for WSM3D. This is a compact full-screen shader that
// mirrors the same filmic fit URP exposes through its Tonemapping override.
// It is kept as a standalone asset so a future renderer-feature pass can bind
// it directly without changing the curve.

Shader "Hidden/WorldSphereMod3D/ContinuumACES"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Exposure ("Exposure", Float) = 1
        _LutTex ("LUT", 2D) = "white" {}
        _LutParams ("LUT Params", Vector) = (256, 16, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "ACES"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_LutTex);
            SAMPLER(sampler_LutTex);

            CBUFFER_START(UnityPerMaterial)
                float  _Exposure;
                float4 _LutParams;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float3 ApplyACES(float3 x)
            {
                const float a = 2.51f;
                const float b = 0.03f;
                const float c = 2.43f;
                const float d = 0.59f;
                const float e = 0.14f;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            float3 ApplyLutStrip(float3 color)
            {
                if (_LutParams.x <= 1.0f || _LutParams.y <= 1.0f)
                    return color;

                float3 scaled = saturate(color);
                float sliceCount = _LutParams.y;
                float sliceIndex = scaled.b * (sliceCount - 1.0f);
                float sliceFloor = floor(sliceIndex);
                float sliceFrac = sliceIndex - sliceFloor;
                float sliceWidth = _LutParams.x / sliceCount;

                float2 baseUV = float2((sliceFloor * sliceWidth + scaled.r * (sliceWidth - 1.0f) + 0.5f) / _LutParams.x, scaled.g);
                float2 nextUV = float2((min(sliceFloor + 1.0f, sliceCount - 1.0f) * sliceWidth + scaled.r * (sliceWidth - 1.0f) + 0.5f) / _LutParams.x, scaled.g);

                float3 a = SAMPLE_TEXTURE2D(_LutTex, sampler_LutTex, baseUV).rgb;
                float3 b = SAMPLE_TEXTURE2D(_LutTex, sampler_LutTex, nextUV).rgb;
                return lerp(a, b, sliceFrac);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Exposure;
                color = ApplyLutStrip(color);
                color = ApplyACES(color);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
