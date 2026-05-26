Shader "Hidden/ScreenSpaceGI"
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
            Name "SSGI"
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

            // Pre-baked kernel (matches ScreenSpaceGI.BuildKernelStatic seed 4242).
            // Avoids uniform float4[] arrays that corrupt bundle shader assets.
            float2 GetGISample(int idx)
            {
                if (idx == 0) return float2(0.999997, -0.002536);
                if (idx == 1) return float2(0.613283, 0.789863);
                if (idx == 2) return float2(-0.565519, 0.824736);
                if (idx == 3) return float2(0.581849, 0.813297);
                if (idx == 4) return float2(0.998442, 0.055801);
                if (idx == 5) return float2(0.011593, -0.999933);
                if (idx == 6) return float2(0.941269, -0.337657);
                if (idx == 7) return float2(-0.685405, -0.728162);
                if (idx == 8) return float2(-0.712302, -0.701873);
                if (idx == 9) return float2(0.028889, 0.999583);
                if (idx == 10) return float2(-0.572159, 0.820143);
                return float2(0.668152, 0.744025);
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

                float3 irradiance = 0;
                float samples = 0;
                float2 texel = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
                float radius = max(_Radius, 0.0001);
                int sampleCount = min(max(_SampleCount, 1), 12);

                [loop]
                for (int s = 0; s < 12; s++)
                {
                    if (s >= sampleCount) break;
                    float2 sampleUV = i.uv + GetGISample(s) * texel * radius;
                    if (sampleUV.x < 0.0 || sampleUV.x > 1.0 || sampleUV.y < 0.0 || sampleUV.y > 1.0)
                    {
                        continue;
                    }
                    float sampleDepthRaw = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampleUV).r;
                    float sampleDepth = Linear01Depth(sampleDepthRaw);
                    float depthWeight = 1.0 - saturate(abs(sampleDepth - centerDepth) * 5.0);
                    irradiance += tex2D(_MainTex, sampleUV).rgb * max(0.0, depthWeight);
                    samples += max(0.0, depthWeight);
                }

                if (samples < 0.5)
                {
                    return color;
                }
                irradiance = irradiance / samples;
                float3 outColor = lerp(color.rgb, irradiance, _Intensity);
                return fixed4(outColor, color.a);
            }
            ENDCG
        }
    }
    Fallback "Unlit/Color"
}
