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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_ST;
            float4 _Samples[16];
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

            v2f vert(appdata_t v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
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

                [loop]
                for (int s = 0; s < 16; s++)
                {
                    if (s >= _SampleCount) break;

                    float scale = lerp(0.05, 1.0, _Samples[s].w);
                    float2 offset = _Samples[s].xy * texel * radius * scale;
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

                float ao = 1.0 - saturate(occlusion / max(1, _SampleCount));
                float3 aoColor = color.rgb * lerp(1.0, ao, _Intensity);
                return fixed4(aoColor, color.a);
            }
            ENDCG
        }
    }
}
