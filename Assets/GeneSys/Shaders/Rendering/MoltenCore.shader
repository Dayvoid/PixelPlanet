Shader "GeneSys/Molten Core"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1.0, 0.96, 0.82, 1)
        _MagmaColor ("Magma Color", Color) = (1.0, 0.52, 0.06, 1)
        _DeepColor ("Deep Molten Color", Color) = (0.82, 0.14, 0.02, 1)
        _SlagColor ("Slag Color", Color) = (0.18, 0.07, 0.03, 1)
        _Intensity ("Intensity", Float) = 1.0
        _CirculationSpeed ("Circulation Speed", Float) = 1.0
        _HeatGlow ("Heat Glow", Float) = 1.0
        _CoreRadius ("Core Radius", Float) = 0.85
        _EdgeSoftness ("Edge Softness", Float) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "MoltenCore"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _MagmaColor;
                float4 _DeepColor;
                float4 _SlagColor;
                float _Intensity;
                float _CirculationSpeed;
                float _HeatGlow;
                float _CoreRadius;
                float _EdgeSoftness;
                float _SimHeatGlow;
                float _SimCirculation;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // High quality procedural noise functions for fluid convective turbulence
            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float ConvectionFbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float2 shift = float2(100.0, 100.0);
                float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * ValueNoise(p);
                    p = mul(rot, p) * 2.05 + shift;
                    amplitude *= 0.5;
                }
                return value;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                if (r > 1.0) return half4(0, 0, 0, 0);

                float phi = atan2(p.y, p.x);
                float speed = _CirculationSpeed;

                // Differential fluid rotation: inner bands rotate faster than outer mantle edge
                float differentialOmega = (0.75 + 0.55 / max(0.15, r + 0.18)) * speed;
                float swirlAngle = phi + _Time.y * differentialOmega;
                float2 swirlP = float2(cos(swirlAngle), sin(swirlAngle)) * r;

                // Domain warped multi-scale convection fields
                float2 warpOffset = float2(
                    sin(r * 5.2 - _Time.y * 0.7 * speed + phi * 2.0),
                    cos(r * 4.4 + _Time.y * 0.6 * speed - phi * 2.0)
                ) * 0.18;

                float2 coord1 = swirlP * 3.4 + warpOffset + float2(_Time.y * 0.12 * speed, 0.0);
                float2 coord2 = swirlP * 6.8 - warpOffset * 1.5 - float2(0.0, _Time.y * 0.18 * speed);

                float n1 = ConvectionFbm(coord1);
                float n2 = ConvectionFbm(coord2 + n1 * 0.65);

                // Thermal plumes & incandescent magnetic filaments
                float filamentAngle = swirlAngle * 3.0 + n1 * 6.28;
                float filament = pow(saturate(sin(filamentAngle) * 0.5 + 0.5), 3.5);
                float microFilament = pow(saturate(cos(swirlAngle * 5.0 - n2 * 8.0) * 0.5 + 0.5), 4.0);

                // Viscous semi-solid slag rafts and cooling crust flakes
                float slagPattern = ConvectionFbm(swirlP * 8.5 + float2(n1, n2) * 0.8);
                float slag = smoothstep(0.56, 0.72, slagPattern + (r - 0.25) * 0.35);

                // Convective heartbeat / thermal breathing pulse
                float pulse = 0.94 + 0.06 * sin(_Time.y * 1.8 + r * 4.0);

                // Blinding white-hot central core zone
                float coreSolid = 1.0 - smoothstep(0.0, 0.42, r);

                // Multi-tier incandescent molten metal palette
                float3 molten = lerp(_DeepColor.rgb, _MagmaColor.rgb, saturate(n2 * 1.35 + (1.0 - r) * 0.4));
                molten = lerp(molten, _CoreColor.rgb, saturate(coreSolid * 0.9 + filament * 0.5 + microFilament * 0.3));

                // Apply slag rafts (darker cooling flakes floating on top)
                molten = lerp(molten, _SlagColor.rgb, slag * (1.0 - coreSolid * 0.85) * 0.8);

                // Thermal glow scaling and incandescence boost
                float simHeat = max(0.25, _SimHeatGlow);
                float simFlow = max(0.25, _SimCirculation);
                float3 finalColor = molten * (_HeatGlow * pulse * _Intensity * simHeat);
                finalColor = lerp(finalColor, finalColor * float3(1.15, 0.85, 0.55), saturate(simFlow * 0.35));

                // Edge falloff: 100% solid in the center to obliterate underlying radial singularity,
                // smooth thermal transition into the planetary mantle at the perimeter.
                float coreRadius = saturate(_CoreRadius);
                float innerEdge = max(0.2, coreRadius * 0.65);
                float alpha = 1.0;
                if (r > innerEdge)
                {
                    float t = saturate((r - innerEdge) / max(0.001, 1.0 - innerEdge));
                    alpha = pow(1.0 - t, max(0.2, _EdgeSoftness));
                }
                alpha = saturate(alpha * _Intensity);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
