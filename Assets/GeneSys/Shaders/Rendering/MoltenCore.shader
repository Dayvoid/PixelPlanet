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

            // One soft wax body. Vertical motion is a sine (rise, then sink). A slower
            // cosine drifts it sideways. Radius breathes. Kernels sum so overlaps bridge.
            void AccumulateLavaBlob(float2 p, float time, float speed, float index, inout float field, inout float2 carry)
            {
                float phase = index * 1.37;
                float rate = 0.85 + 0.22 * frac(index * 0.37);
                // 2*pi/10: a full rise-and-fall is about ten seconds at speed 1 and rate 1.
                float omega = 0.628 * speed * rate;
                float yPhase = time * omega + phase;
                float xPhase = time * omega * 0.37 + phase * 1.7;
                float2 center = float2(cos(xPhase) * 0.42, sin(yPhase) * 0.62);
                float radius = 0.22 + 0.05 * sin(time * 0.4 * speed + phase);
                float2 delta = p - center;
                float kernel = exp(-dot(delta, delta) / max(radius * radius, 1e-3));
                kernel *= 1.0 - smoothstep(0.68, 0.92, length(center));
                field += kernel;
                float2 velocity = float2(-sin(xPhase) * 0.42 * omega * 0.37, cos(yPhase) * 0.62 * omega);
                carry += velocity * kernel;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                if (r > 1.0) return half4(0, 0, 0, 0);

                float phi = atan2(p.y, p.x);
                float speed = _CirculationSpeed;
                float time = _Time.y;

                float blobField = 0.0;
                float2 blobCarry = 0.0;
                [unroll]
                for (int i = 0; i < 7; i++)
                {
                    AccumulateLavaBlob(p, time, speed, (float)i, blobField, blobCarry);
                }

                // Resting radial warp (the time-zero convection field) plus a bounded
                // standing wave. Blob velocity pushes that texture locally so it travels
                // with the wax instead of shearing around the disc.
                float2 warpOffset = float2(
                    sin(r * 5.2 + phi * 2.0),
                    cos(r * 4.4 - phi * 2.0)
                ) * 0.18;
                float2 standing = float2(
                    sin(p.y * 2.4 + time * 0.35 * speed) - sin(p.y * 2.4),
                    sin(p.x * 2.1 - time * 0.28 * speed) - sin(p.x * 2.1)
                ) * 0.07;
                float2 sampleP = p + standing - blobCarry * 0.35;

                float2 coord1 = sampleP * 3.4 + warpOffset;
                float2 coord2 = sampleP * 6.8 - warpOffset * 1.5;

                float n1 = ConvectionFbm(coord1);
                float n2 = ConvectionFbm(coord2 + n1 * 0.65);

                float blobHeat = saturate(blobField * 0.85);
                float blobPeak = pow(saturate((blobField - 0.72) / 0.85), 1.4);

                // Viscous semi-solid slag rafts. Hot blobs and the white center stay clear of crust.
                float slagPattern = ConvectionFbm(sampleP * 8.5 + float2(n1, n2) * 0.8);
                float slag = smoothstep(0.56, 0.72, slagPattern + (r - 0.25) * 0.35);

                // Convective heartbeat / thermal breathing pulse
                float pulse = 0.94 + 0.06 * sin(_Time.y * 1.8 + r * 4.0);

                // Blinding white-hot central core zone
                float coreSolid = 1.0 - smoothstep(0.0, 0.42, r);

                // Multi-tier incandescent molten metal palette. Blob peaks and noise
                // carry the bright accents; gaps stay deeper and pick up more slag.
                float3 molten = lerp(_DeepColor.rgb, _MagmaColor.rgb, saturate(n2 * 1.35 + (1.0 - r) * 0.4 + blobHeat * 0.45));
                float accent = saturate(coreSolid * 0.9 + blobPeak * 0.7 + saturate(n2 - 0.55) * 0.25);
                molten = lerp(molten, _CoreColor.rgb, accent);

                molten = lerp(molten, _SlagColor.rgb, slag * (1.0 - coreSolid * 0.85) * (1.0 - blobHeat * 0.8) * 0.8);

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
