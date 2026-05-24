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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_ST;
            float4 _Samples[12];
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

                float3 irradiance = 0;
                float samples = 0;
                float2 texel = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y);
                float radius = max(_Radius, 0.0001);

                [loop]
                for (int s = 0; s < 12; s++)
                {
                    if (s >= _SampleCount) break;
                    float2 sampleUV = i.uv + _Samples[s].xy * texel * radius;
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
}
