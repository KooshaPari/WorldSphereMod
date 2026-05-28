Shader "Hidden/ScreenSpaceAO"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _CameraDepthTexture("Camera Depth", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "SSAO"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_TexelSize;
            int _SampleCount;
            float _Radius;
            float _Bias;
            float _Intensity;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            // Pre-baked kernel (matches ScreenSpaceAO.BuildKernelStatic seed 1337).
            float2 GetKernelOffset(int idx)
            {
                if (idx == 0) return float2(-0.606145, -0.795354);
                if (idx == 1) return float2(-0.394218, 0.919017);
                if (idx == 2) return float2(0.596959, 0.802272);
                if (idx == 3) return float2(0.831585, -0.555397);
                if (idx == 4) return float2(-0.481523, -0.876433);
                if (idx == 5) return float2(-0.675777, -0.737106);
                if (idx == 6) return float2(0.493279, -0.869871);
                if (idx == 7) return float2(0.578975, -0.815345);
                if (idx == 8) return float2(0.660758, 0.750599);
                if (idx == 9) return float2(0.600963, -0.799277);
                if (idx == 10) return float2(-0.096412, 0.995341);
                if (idx == 11) return float2(0.755236, -0.655452);
                if (idx == 12) return float2(0.987003, -0.160701);
                if (idx == 13) return float2(-0.150155, -0.988663);
                if (idx == 14) return float2(0.703728, 0.71047);
                return float2(0.907898, 0.41919);
            }

            float GetKernelScale(int idx)
            {
                return (idx + 1) / 16.0;
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0)
                    o.uv = 1.0 - o.uv;
                #endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, i.uv);
                float centerDepthRaw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv).r;
                float centerDepth = Linear01Depth(centerDepthRaw);
                if (centerDepth >= 0.999f)
                {
                    return color;
                }

                float occlusion = 0;
                float radius = max(_Radius * 0.5, 0.0001);
                float2 texel = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
                int sampleCount = min(max(_SampleCount, 1), 16);

                [loop]
                for (int s = 0; s < 16; s++)
                {
                    if (s >= sampleCount) break;

                    float scale = lerp(0.05, 1.0, GetKernelScale(s));
                    float2 offset = GetKernelOffset(s) * texel * radius * scale;
                    float2 sampleUV = i.uv + offset;
                    if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                    {
                        continue;
                    }

                    float sampleDepthRaw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampleUV).r;
                    float sampleDepth = Linear01Depth(sampleDepthRaw);
                    float depthDiff = sampleDepth - centerDepth;
                    float rangeWeight = smoothstep(0.0, _Radius, max(0.0, -depthDiff));
                    float occluder = step(_Bias, -depthDiff);
                    occlusion += occluder * rangeWeight;
                }

                float ao = 1.0 - saturate(occlusion / max(1, sampleCount));
                float3 aoColor = color.rgb * lerp(1.0, ao, _Intensity);
                return fixed4(aoColor, color.a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
