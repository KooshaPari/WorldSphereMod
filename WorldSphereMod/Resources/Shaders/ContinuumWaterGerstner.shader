// Fresh water shader for WSM3D. Gerstner displacement is paired with
// procedural normal noise, Fresnel reflection and shoreline foam.

Shader "WorldSphereMod3D/ContinuumWaterGerstner"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.24, 0.76, 0.74, 1)
        _DeepColor    ("Deep", Color) = (0.04, 0.10, 0.30, 1)
        _FoamColor    ("Foam", Color) = (0.95, 0.98, 1.00, 1)
        _SunColor     ("Sun Color", Color) = (1, 0.95, 0.88, 1)
        _SunDir       ("Sun Direction", Vector) = (0, 1, 0, 0)
        _SkyCubemap   ("Sky Cubemap", CUBE) = "" {}

        _MaxDepth     ("Max Depth", Float) = 8
        _WaterDepth   ("Water Depth", Float) = 0
        _FresnelPower ("Fresnel Power", Float) = 4
        _FoamRange    ("Foam Range", Float) = 0.45
        _FoamBoost    ("Foam Boost", Float) = 1.35
        _FoamTint     ("Foam Tint", Float) = 0.55
        _NoiseScale   ("Normal Noise Scale", Float) = 0.14
        _NoiseStrength ("Normal Noise Strength", Float) = 0.18
        _WaveTime     ("Time", Float) = 0
        _CylinderRadius ("Cylinder radius (cyl. shape)", Float) = 12

        _WaveDir0  ("Wave Dir 0", Vector) = (1, 0, 0, 0)
        _WaveDir1  ("Wave Dir 1", Vector) = (0.7, 0.7, 0, 0)
        _WaveDir2  ("Wave Dir 2", Vector) = (-0.5, 0.866, 0, 0)
        _WaveAmp   ("Wave Amplitude xyz", Vector) = (0.08, 0.05, 0.03, 0)
        _WaveFreq  ("Wave Frequency xyz", Vector) = (0.5, 1.2, 2.1, 0)
        _WaveSpeed ("Wave Speed xyz", Vector) = (1.0, 1.6, 2.4, 0)
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _FLAT_WORLD

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float4 _SunColor;
                float4 _SunDir;
                float  _MaxDepth;
                float  _WaterDepth;
                float  _FresnelPower;
                float  _FoamRange;
                float  _FoamBoost;
                float  _FoamTint;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _WaveTime;
                float  _CylinderRadius;
                float4 _WaveDir0;
                float4 _WaveDir1;
                float4 _WaveDir2;
                float4 _WaveAmp;
                float4 _WaveFreq;
                float4 _WaveSpeed;
            CBUFFER_END

            samplerCUBE _SkyCubemap;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float2 noiseUV : TEXCOORD3;
            };

            float2 WavePhaseInput(float3 worldPos)
            {
                #ifdef _FLAT_WORLD
                    return worldPos.xz;
                #else
                    float theta = atan2(worldPos.x, worldPos.z) * _CylinderRadius;
                    return float2(theta, worldPos.y);
                #endif
            }

            float Hash12(float2 p)
            {
                p = frac(p * float2(123.34f, 456.21f));
                p += dot(p, p + 34.45f);
                return frac(p.x * p.y);
            }

            float2 Noise2(float2 uv)
            {
                return float2(Hash12(uv), Hash12(uv + 19.19f)) * 2.0f - 1.0f;
            }

            struct WaveState
            {
                float3 pos;
                float3 tangent;
                float3 binormal;
            };

            WaveState ApplyWave(WaveState state, float2 dir, float amp, float freq, float speed, float2 phasePos)
            {
                float phase = dot(dir, phasePos) * freq - _WaveTime * speed;
                float sine = sin(phase);
                float cosine = cos(phase);
                float steep = amp * freq;

                state.pos += float3(dir.x * amp * cosine, amp * sine, dir.y * amp * cosine);
                state.tangent += float3(1.0f - dir.x * dir.x * steep * sine, dir.x * amp * cosine, -dir.x * dir.y * steep * sine);
                state.binormal += float3(-dir.x * dir.y * steep * sine, dir.y * amp * cosine, 1.0f - dir.y * dir.y * steep * sine);
                return state;
            }

            WaveState BuildWaveState(float3 worldPos)
            {
                float2 phasePos = WavePhaseInput(worldPos);
                WaveState state;
                state.pos = worldPos;
                state.tangent = float3(1, 0, 0);
                state.binormal = float3(0, 0, 1);
                state = ApplyWave(state, normalize(_WaveDir0.xy), _WaveAmp.x, _WaveFreq.x, _WaveSpeed.x, phasePos);
                state = ApplyWave(state, normalize(_WaveDir1.xy), _WaveAmp.y, _WaveFreq.y, _WaveSpeed.y, phasePos);
                state = ApplyWave(state, normalize(_WaveDir2.xy), _WaveAmp.z, _WaveFreq.z, _WaveSpeed.z, phasePos);
                return state;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                WaveState wave = BuildWaveState(wp);
                o.worldPos = wave.pos;
                o.normalWS = normalize(cross(wave.binormal, wave.tangent));
                o.positionCS = TransformWorldToHClip(wave.pos);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.noiseUV = WavePhaseInput(wave.pos) * _NoiseScale;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 N = normalize(i.normalWS);

                float2 noise = Noise2(i.noiseUV + _Time.y * 0.12f);
                N = normalize(N + float3(noise * _NoiseStrength, 0.0f));

                float3 shallow = _ShallowColor.rgb;
                float3 deep = _DeepColor.rgb;
                float depthMask = saturate(_WaterDepth / max(_MaxDepth, 0.0001f));
                float3 baseColor = lerp(shallow, deep, depthMask);

                float2 uv = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = SampleSceneDepth(uv);
                float sceneLinear = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float surfaceLinear = LinearEyeDepth(i.positionCS.z / i.positionCS.w, _ZBufferParams);
                float foamDepth = saturate(1.0f - (sceneLinear - surfaceLinear) / max(_FoamRange, 0.0001f));
                float crest = saturate((1.0f - N.y) * _FoamBoost);
                float foam = saturate(foamDepth + crest * 0.35f);

                float fresnel = pow(1.0f - saturate(dot(N, V)), _FresnelPower);
                float3 reflection = texCUBE(_SkyCubemap, reflect(-V, N)).rgb;
                float3 sunRef = _SunColor.rgb * pow(saturate(dot(reflect(-V, N), normalize(_SunDir.xyz))), 32.0f) * 0.12f;

                float3 color = lerp(baseColor + foam * _FoamColor.rgb * _FoamTint, reflection + sunRef, fresnel);
                color = lerp(color, _FoamColor.rgb, foam * 0.65f);

                return float4(color, 0.92f);
            }
            ENDHLSL
        }
    }
}
