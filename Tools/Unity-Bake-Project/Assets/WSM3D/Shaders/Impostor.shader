// WSM3D/Impostor
//
// 8-direction octahedral impostor billboard. Samples a strip atlas based on
// view-to-impostor angle. Used by LodSelector when entity falls below voxel
// screen-coverage threshold.

Shader "WSM3D/Impostor"
{
    Properties
    {
        _MainTex ("Atlas (8 frames horizontal)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _FrameCount ("Frame Count", Range(1, 16)) = 8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 100
        Cull Off
        ZWrite On
        AlphaToMask On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _FrameCount)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 wpos = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 viewDir = normalize(_WorldSpaceCameraPos - wpos);
                float angle = atan2(viewDir.z, viewDir.x);
                float frameCount = UNITY_ACCESS_INSTANCED_PROP(Props, _FrameCount);
                float f = floor((angle + UNITY_PI) / UNITY_TWO_PI * frameCount) / frameCount;
                float2 quadUV = v.uv;
                quadUV.x = (quadUV.x + f * frameCount) / frameCount;
                o.uv = TRANSFORM_TEX(quadUV, _MainTex);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                col *= tint;
                clip(col.a - 0.5);
                return col;
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
