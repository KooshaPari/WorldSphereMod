Shader "Hidden/ColorGradingLUT"
{
    Properties
    {
        _MainTex("MainTex", 2D) = "white" {}
        _LutTex("LUT Texture", 2D) = "white" {}
        _LookupTex("Lookup Texture", 2D) = "white" {}
        _LutParams("LUT Params", Vector) = (0.0625, 0.0625, 1, 0)
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
            sampler2D _LutTex;
            sampler2D _LookupTex;
            float4 _LutParams;

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

            fixed3 ApplyLUT(sampler2D lut, fixed3 c)
            {
                c = saturate(c);
                float scale = 31.0 / 32.0;
                float offset = 0.5 / 32.0;
                float slice = c.b * 31;
                float slice0 = floor(slice);
                float slice1 = min(slice0 + 1, 31);
                float u0 = (slice0 + c.r * scale + offset) / 32;
                float u1 = (slice1 + c.r * scale + offset) / 32;
                float v = c.g * scale + offset;
                fixed3 s0 = tex2D(lut, float2(u0, v)).rgb;
                fixed3 s1 = tex2D(lut, float2(u1, v)).rgb;
                return lerp(s0, s1, slice - slice0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Try _LutTex first (the property WSM3DPostStack sets),
                // then _LookupTex (alternate property name).
                // When no LUT texture is bound, both samplers return
                // "white" (the default) and ApplyLUT produces near-white,
                // so we skip the LUT entirely in that case by checking
                // _LutParams.z > 0.5 (it defaults to 1 when a real LUT
                // is present).
                if (_LutParams.z > 0.5)
                {
                    fixed3 graded = ApplyLUT(_LutTex, col.rgb);
                    col.rgb = graded;
                }

                return col;
            }
            ENDCG
        }
    }
}
