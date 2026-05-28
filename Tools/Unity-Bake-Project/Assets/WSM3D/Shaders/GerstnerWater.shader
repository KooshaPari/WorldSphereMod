// WSM3D/GerstnerWater
//
// Mesh water surface with 3-direction Gerstner vertex displacement, cubemap
// sky reflection (Fresnel-weighted), and depth-gradient shore foam. Targets
// the built-in render pipeline so it compiles in the WorldBox bake/runtime
// path without URP includes.
//
// Vertex colors (baked by WaterSurface.RebuildMesh):
//   R = depthFrac (0 shallow .. 1 deep)
//   G = shoreFrac (1 at shore, falls off into open water — drives foam)
//
// At runtime VoxelRender / WaterSurface resolves via Shader.Find("WSM3D/GerstnerWater").

Shader "WSM3D/GerstnerWater"
{
    Properties
    {
        _Color ("Shallow Color", Color) = (0.22, 0.65, 0.70, 0.75)
        _DeepColor ("Deep Color", Color) = (0.04, 0.12, 0.30, 0.95)
        _Foam ("Foam Color", Color) = (0.92, 0.95, 1.00, 1)
        _WaterDepth ("Water Depth", Float) = 0
        _MaxDepth ("Max Depth", Float) = 6
        _WaveTime ("Wave Time", Float) = 0
        _WaveAmplitude ("Wave Amplitude", Range(0, 1)) = 0.05
        _WaveSteepness ("Wave Steepness", Range(0, 1)) = 0.35
        _WaveDirX ("Wave Dir X", Float) = 0.7
        _WaveDirZ ("Wave Dir Z", Float) = 0.7
        _WaveDir2X ("Wave Dir2 X", Float) = -0.5
        _WaveDir2Z ("Wave Dir2 Z", Float) = 0.86
        _WaveDir3X ("Wave Dir3 X", Float) = 0.3
        _WaveDir3Z ("Wave Dir3 Z", Float) = -0.95
        _WaveLength ("Wave Length", Range(1, 50)) = 10
        _SkyCubemap ("Sky Cubemap", Cube) = "" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.6
        _ShoreFoamWidth ("Shore Foam Width", Range(0.001, 0.5)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Back
        ZWrite Off

        Pass
        {
            Name "GerstnerWaterPass"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 worldPos : TEXCOORD0; float3 worldNormal : TEXCOORD1; float depth : TEXCOORD2; float shore : TEXCOORD3; };

            fixed4 _Color, _DeepColor, _Foam;
            float _WaterDepth, _MaxDepth;
            float _WaveTime, _WaveAmplitude, _WaveSteepness, _WaveLength;
            float _WaveDirX, _WaveDirZ;
            float _WaveDir2X, _WaveDir2Z;
            float _WaveDir3X, _WaveDir3Z;
            float _ReflectionStrength, _ShoreFoamWidth;
            samplerCUBE _SkyCubemap;

            // Single Gerstner contribution. Returns displacement and accumulates
            // a partial tangent/bitangent estimate via cosine of phase so callers
            // can rebuild a normal after summing all waves.
            float3 GerstnerWaveContribution(float3 p, float2 dir, float ampScale, float phaseOffset)
            {
                float2 ndir = normalize(dir);
                float k = UNITY_TWO_PI / max(_WaveLength, 0.001);
                float phase = dot(ndir, p.xz) * k + _WaveTime + phaseOffset;
                float c = cos(phase);
                float s = sin(phase);
                float amp = _WaveAmplitude * ampScale;
                float steep = _WaveSteepness / max(k * max(amp, 0.001), 0.001);
                float3 displ;
                displ.x = steep * amp * ndir.x * c;
                displ.z = steep * amp * ndir.y * c;
                displ.y = amp * s;
                return displ;
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Sum 3 Gerstner waves with perturbed directions and decreasing
                // amplitude — the secondary/tertiary waves are smaller chop on
                // top of the primary swell.
                float3 d1 = GerstnerWaveContribution(worldPos, float2(_WaveDirX,  _WaveDirZ),  1.00, 0.0);
                float3 d2 = GerstnerWaveContribution(worldPos, float2(_WaveDir2X, _WaveDir2Z), 0.55, 1.7);
                float3 d3 = GerstnerWaveContribution(worldPos, float2(_WaveDir3X, _WaveDir3Z), 0.30, 3.4);
                float3 displ = d1 + d2 + d3;

                worldPos += displ;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.worldPos = worldPos;

                // Tilt the mesh normal with the summed horizontal displacement so
                // Fresnel + cubemap reflection track the wave surface, not just
                // the flat sphere normal. Renormalize after combining.
                float3 baseN = mul((float3x3)unity_ObjectToWorld, v.normal);
                float3 perturbed = baseN + float3(-displ.x, 0, -displ.z) * 2.0;
                o.worldNormal = normalize(perturbed);

                o.depth = v.color.r;
                o.shore = v.color.g;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 N = normalize(i.worldNormal);
                float fresnel = pow(1 - saturate(dot(N, viewDir)), 3);

                float depthFrac = saturate(i.depth);
                fixed4 shallow = _Color;
                fixed4 deep = _DeepColor;
                fixed3 baseTint = lerp(shallow.rgb, deep.rgb, depthFrac);
                float baseAlpha = lerp(shallow.a, deep.a, depthFrac);

                // Depth-gradient shore foam. vertex.color.G is high (~1) at the
                // shoreline (a corner touched by at least one non-water neighbor)
                // and falls off into open water. _ShoreFoamWidth controls the
                // edge softness. Wave crests no longer drive foam — the swell
                // contributes only via the perturbed normal.
                float foamMask = smoothstep(1.0 - _ShoreFoamWidth, 1.0, saturate(i.shore));
                fixed3 foamMixed = lerp(baseTint, _Foam.rgb, foamMask);

                // Cubemap sky reflection sampled along the reflected view vector
                // and blended in proportional to Fresnel. _ReflectionStrength
                // lets WaterSurface dial reflection per-frame (e.g. dim at
                // night). Without a real cubemap bound the sample returns black,
                // so the lerp gracefully degrades to baseTint.
                float3 reflectDir = reflect(-viewDir, N);
                fixed3 skyColor = texCUBE(_SkyCubemap, reflectDir).rgb;
                fixed3 reflective = lerp(foamMixed, skyColor, fresnel * _ReflectionStrength);

                // Final highlight: keep the existing subtle specular bump on top
                // of the reflected color so very-glancing pixels still gain a
                // touch of brightness when no cubemap is configured.
                fixed3 finalRgb = lerp(reflective, reflective * 1.15 + 0.08, fresnel * 0.2);

                return fixed4(finalRgb, baseAlpha);
            }
            ENDCG
        }
    }

    Fallback "Diffuse"
}
