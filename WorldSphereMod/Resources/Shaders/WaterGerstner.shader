// Phase 4 water: Gerstner waves + depth tint + Fresnel + foam.
// Built against URP. Ships as source — Phase 5b's AssetBundleBuilder
// project will compile this into the worldsphere bundle for runtime
// load. Until then, Resources.Load<Shader> picks it up if a Resources/
// folder is present in the mod's data dir; else WaterSurface falls back
// to the Sprites/Default placeholder chain.

Shader "WorldSphereMod3D/WaterGerstner"
{
    Properties
    {
        _ShallowColor ("Shallow",   Color) = (0.26, 0.78, 0.75, 1)
        _DeepColor    ("Deep",      Color) = (0.05, 0.12, 0.35, 1)
        _MaxDepth     ("Max Depth", Float) = 8
        _FresnelPower ("Fresnel Power", Float) = 4
        _FoamRange    ("Foam Range",    Float) = 0.5
        _WaterDepth   ("Per-tile depth (m)", Float) = 0
        _SkyCubemap   ("Sky Cubemap", CUBE) = "" {}

        _WaveDir0  ("Wave Dir 0",       Vector) = (1, 0, 0, 0)
        _WaveDir1  ("Wave Dir 1",       Vector) = (0.7, 0.7, 0, 0)
        _WaveDir2  ("Wave Dir 2",       Vector) = (-0.5, 0.866, 0, 0)
        _WaveAmp   ("Wave Amplitude xyz", Vector) = (0.12, 0.075, 0.05, 0)
        _WaveFreq  ("Wave Frequency xyz", Vector) = (0.45, 1.1, 2.0, 0)
        _WaveSpeed ("Wave Speed xyz",     Vector) = (1.0, 1.6, 2.4, 0)
        _WaveTime  ("Time",               Float)  = 0
        _CylinderRadius ("Cylinder radius (cyl. shape)", Float) = 12
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _FLAT_WORLD

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _MaxDepth;
                float  _FresnelPower;
                float  _FoamRange;
                float  _WaterDepth;
                float4 _WaveDir0;
                float4 _WaveDir1;
                float4 _WaveDir2;
                float4 _WaveAmp;
                float4 _WaveFreq;
                float4 _WaveSpeed;
                float  _WaveTime;
                float  _CylinderRadius;
            CBUFFER_END

            samplerCUBE _SkyCubemap;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 worldPos : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 screenPos : TEXCOORD2; };

            float2 WavePhaseInputXZ(float3 worldPos)
            {
                #ifdef _FLAT_WORLD
                    return worldPos.xz;
                #else
                    // Cylindrical: x → angle * radius along seam; y → linear vertical
                    float theta = atan2(worldPos.x, worldPos.z) * _CylinderRadius;
                    return float2(theta, worldPos.y);
                #endif
            }

            float GerstnerHeight(float3 worldPos)
            {
                float2 p = WavePhaseInputXZ(worldPos);
                float h = 0;
                float phase;
                phase = dot(_WaveDir0.xy, p) * _WaveFreq.x - _WaveTime * _WaveSpeed.x;
                h += _WaveAmp.x * sin(phase);
                phase = dot(_WaveDir1.xy, p) * _WaveFreq.y - _WaveTime * _WaveSpeed.y;
                h += _WaveAmp.y * sin(phase);
                phase = dot(_WaveDir2.xy, p) * _WaveFreq.z - _WaveTime * _WaveSpeed.z;
                h += _WaveAmp.z * sin(phase);
                return h;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                wp.y += GerstnerHeight(wp);
                o.worldPos = wp;
                o.positionCS = TransformWorldToHClip(wp);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float depthFrac = saturate(_WaterDepth / max(_MaxDepth, 0.0001));
                float3 tint = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFrac);

                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 N = normalize(i.normalWS);
                float fresnel = pow(1 - saturate(dot(N, V)), _FresnelPower);
                float3 R = reflect(-V, N);
                float3 sky = texCUBE(_SkyCubemap, R).rgb;

                // Screen-space foam from depth prepass.
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = SampleSceneDepth(uv);
                float sceneLinear = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float waterLinear = LinearEyeDepth(i.positionCS.z / i.positionCS.w, _ZBufferParams);
                float foam = saturate(1 - (sceneLinear - waterLinear) / max(_FoamRange, 0.0001));

                float3 color = lerp(tint + foam, sky, fresnel);
                return float4(color, 0.9);
            }
            ENDHLSL
        }
    }
}
