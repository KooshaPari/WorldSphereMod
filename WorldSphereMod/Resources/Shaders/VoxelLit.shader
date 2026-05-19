// Phase 5 voxel material skeleton.
// URP source shader for the future AssetBundle bake step. This file is not
// compiled by dotnet; it is kept as coherent Unity shader source for the bake.

Shader "WorldSphereMod3D/VoxelLit"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0

        // MeshInstanceBatcher supplies this as a per-instance Color array.
        // [InstancedOption] documents the intended Shader Graph-style slot.
        [InstancedOption] _InstanceColor ("Instance Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
            "RenderPipeline"="UniversalPipeline"
        }

        LOD 200
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            // Keep an explicit INSTANCING_ON variant for DrawMeshInstanced
            // batches that pass _InstanceColor through MaterialPropertyBlock.
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Smoothness;
                float _Metallic;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(WorldSphereVoxelProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
            UNITY_INSTANCING_BUFFER_END(WorldSphereVoxelProps)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 GetVoxelInstanceColor()
            {
                #if defined(INSTANCING_ON)
                    return UNITY_ACCESS_INSTANCED_PROP(WorldSphereVoxelProps, _InstanceColor);
                #else
                    return float4(1, 1, 1, 1);
                #endif
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normals.normalWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float4 instanceColor = GetVoxelInstanceColor();
                float4 tint = _BaseColor * instanceColor;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float nDotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = tint.rgb * mainLight.color * (nDotL * mainLight.shadowAttenuation);

                // Spherical harmonics ambient keeps unlit faces readable and
                // matches URP's lightweight forward path without requiring PBR.
                float3 ambient = SampleSH(normalWS) * tint.rgb;

                // Metallic and smoothness are surfaced for material parity with
                // future Shader Graph output; this skeleton keeps the model
                // Lambertian and only uses them for coarse energy shaping.
                float roughness = 1.0 - saturate(_Smoothness);
                float diffuseWeight = saturate(1.0 - _Metallic * 0.75 + roughness * 0.05);
                float3 color = (diffuse + ambient) * diffuseWeight;

                return half4(color, tint.a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
