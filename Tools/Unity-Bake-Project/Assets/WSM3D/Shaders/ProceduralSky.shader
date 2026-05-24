// WSM3D/ProceduralSky
//
// Atmospheric procedural skybox + HDR sun disc + horizon haze. Driven by
// SunDriver.CurrentAngle for time-of-day color interp.
//
// Properties:
//   _SunDirection — world-space unit vector to the sun
//   _SunIntensity — sun disc brightness (HDR)
//   _SkyTopColor  — zenith color
//   _SkyHorizon   — horizon haze color
//   _SkyGround    — below-horizon ground color

Shader "WSM3D/ProceduralSky"
{
    Properties
    {
        _SunDirection ("Sun Dir", Vector) = (0, 1, 0, 0)
        _SunSize ("Sun Disc Size", Range(0, 0.1)) = 0.02
        _SunIntensity ("Sun Intensity HDR", Range(0, 32)) = 8
        _SunColor ("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SkyTopColor ("Zenith", Color) = (0.10, 0.30, 0.65, 1)
        _SkyHorizon ("Horizon", Color) = (0.55, 0.65, 0.80, 1)
        _SkyGround ("Below Horizon", Color) = (0.05, 0.05, 0.10, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            float3 _SunDirection;
            float _SunSize, _SunIntensity;
            fixed4 _SunColor, _SkyTopColor, _SkyHorizon, _SkyGround;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                fixed3 sky = lerp(_SkyHorizon.rgb, _SkyTopColor.rgb, pow(h, 0.5));
                if (d.y < 0)
                {
                    sky = lerp(_SkyHorizon.rgb, _SkyGround.rgb, saturate(-d.y * 2));
                }
                float3 sun = normalize(_SunDirection);
                float sunDot = saturate(dot(d, sun));
                float sunMask = smoothstep(1 - _SunSize, 1 - _SunSize * 0.2, sunDot);
                sky += _SunColor.rgb * sunMask * _SunIntensity;
                float halo = pow(sunDot, 16) * 0.4;
                sky += _SunColor.rgb * halo;
                return fixed4(sky, 1);
            }
            ENDCG
        }
    }
}
