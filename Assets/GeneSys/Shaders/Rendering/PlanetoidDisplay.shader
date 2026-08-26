Shader "GeneSys/Planetoid Display"
{
    Properties
    {
        _Palette ("Material Palette", 2D) = "white" {}
        _Properties ("Material Properties", 2D) = "black" {}
        _Categories ("Material Categories", 2D) = "black" {}
        _VisualCoreRadius ("Visual Core Radius", Float) = 0.28
        _VisualCoreSquash ("Visual Core Squash", Float) = 0.45
        _OverlayMode ("Overlay Mode", Int) = 0
        _SolarAngle01 ("Solar Angle", Float) = 0
        _DayNightLightingStrength ("Day Night Lighting Strength", Float) = 0.85
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Planetoid"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            Texture2D<uint> _MaterialTex;
            Texture2D<float4> _StateTex;
            Texture2D<float2> _FlowTex;
            Texture2D<float4> _AuxTex;
            Texture2D<uint> _ShadeTex;
            Texture2D<float4> _EcologyTex;
                Texture2D<float4> _CombustionTex;
            Texture2D<float4> _StormTex;
            Texture2DArray<float4> _LifeGenomeTex;
            Texture2D<float> _LightTex;
            Texture2D<float4> _Palette;
            Texture2D<float4> _Properties;
            Texture2D<float4> _Categories;
            float _VisualCoreRadius;
            float _VisualCoreSquash;
            float _AtmosphereStartRadius;
            int _OverlayMode;
            float _SolarAngle01;
            float _DayNightLightingStrength;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float3 HeatColor(float value)
            {
                float t = saturate((value + 30.0) / 1100.0);
                return lerp(float3(0.05, 0.15, 0.7), float3(1.0, 0.12, 0.01), t);
            }

            bool IsMottledCategory(float category)
            {
                return category >= 2.0 && category <= 6.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float displayRadius = length(p);
                if (displayRadius > 1.0) return half4(0, 0, 0, 0);

                float displayedCore = _VisualCoreRadius * _VisualCoreSquash;
                float simulationRadius = displayRadius >= displayedCore
                    ? lerp(_VisualCoreRadius, 1.0, (displayRadius - displayedCore) / max(0.0001, 1.0 - displayedCore))
                    : displayRadius / max(0.05, _VisualCoreSquash);

                uint width, height;
                _MaterialTex.GetDimensions(width, height);
                float angle = atan2(p.y, p.x);
                if (angle < 0.0) angle += 6.28318530718;
                int2 cell = int2(
                    clamp((int)floor(angle / 6.28318530718 * width), 0, (int)width - 1),
                    clamp((int)floor(simulationRadius * height), 0, (int)height - 1));

                uint material = min(_MaterialTex.Load(int3(cell, 0)), 255u);
                float4 state = _StateTex.Load(int3(cell, 0));
                float2 flow = _FlowTex.Load(int3(cell, 0));
                float4 aux = _AuxTex.Load(int3(cell, 0));
                uint shade = min(_ShadeTex.Load(int3(cell, 0)), 2u);
                float4 ecology = _EcologyTex.Load(int3(cell, 0));
                float4 combustion = _CombustionTex.Load(int3(cell, 0));
                float4 storm = _StormTex.Load(int3(cell, 0));
                float4 life = _LifeGenomeTex.Load(int4(cell, 0, 0));
                uint4 genome = asuint(_LifeGenomeTex.Load(int4(cell, 1, 0)));
                float lightField = _LightTex.Load(int3(cell, 0));
                float category = _Categories.Load(int3((int)material, 0, 0)).x;
                if (!IsMottledCategory(category)) shade = 0u;
                float4 baseColor = _Palette.Load(int3((int)material, (int)shade, 0));
                float4 materialProperties = _Properties.Load(int3((int)material, 0, 0));
                float3 color = baseColor.rgb;
                bool atmosphereCarrier = material == 1u || material == 11u || simulationRadius >= _AtmosphereStartRadius;
                float cloud = atmosphereCarrier ? saturate(state.z * 2.5) : 0.0;

                if (_OverlayMode == 0)
                {
                    float myco = saturate(ecology.y);
                    if (material == 7u && myco > 0.001)
                        color = lerp(color, float3(0.10, 0.32, 0.11), myco);
                    else if (material == 8u && myco > 0.001)
                        color = lerp(color, float3(0.62, 0.68, 0.48), myco);

                    if (material == 128u)
                    {
                        uint stage = genome.w & 255u;
                        float3 stageColor = stage == 1u ? float3(0.18, 0.62, 0.24)
                            : stage == 2u ? float3(0.28, 0.42, 0.22)
                            : stage == 3u ? float3(0.45, 0.32, 0.14)
                            : float3(0.22, 0.48, 0.2);
                        float biomass = saturate(life.y);
                        float radius = biomass < 0.33 ? 0.22 : (biomass < 0.66 ? 0.35 : 0.48);
                        float2 cellUv = float2(
                            frac(angle / 6.28318530718 * width),
                            frac(simulationRadius * height));
                        float disc = saturate((radius - length(cellUv - 0.5)) * 8.0);
                        color = lerp(color * 0.45, stageColor, lerp(0.35, 0.95, disc));
                    }

                    if (cloud > 0.02)
                        color = lerp(color, float3(0.92, 0.95, 1.0), saturate(cloud * 0.9));

                    float soot = saturate(combustion.z);
                    if (soot > 0.02)
                        color *= 1.0 - soot * 0.55;
                    float flame = saturate(combustion.y);
                    if (flame > 0.02)
                        color = lerp(color, float3(1.0, 0.38, 0.05), saturate(flame * 0.95)) + float3(1.0, 0.45, 0.08) * flame * 0.65;

                    float bolt = saturate(storm.y);
                    float flash = saturate(storm.z);
                    if (flash > 0.02)
                        color += float3(0.42, 0.55, 0.95) * flash * 0.55;
                    if (bolt > 0.02)
                        color = lerp(color, float3(0.82, 0.92, 1.0), saturate(bolt)) + float3(0.55, 0.72, 1.0) * bolt * 0.8;

                    // Soft ambient + directional day/night, matching Weather AtmosphericForcing insolation.
                    float strength = saturate(_DayNightLightingStrength);
                    if (strength > 0.001)
                    {
                        float theta01 = angle / 6.28318530718;
                        float insolation = max(0.0, cos((theta01 - _SolarAngle01) * 6.28318530718));
                        float dayFactor = saturate(insolation * 0.75 + 0.25);
                        float lighting = lerp(1.0 - 0.82 * strength, 1.0 + 0.12 * strength, dayFactor);
                        color *= lighting;
                    }
                }
                else if (_OverlayMode == 1) color = HeatColor(state.x);
                else if (_OverlayMode == 2) color = lerp(float3(0.02, 0.02, 0.08), float3(1.0, 0.1, 0.8), saturate(state.y));
                else if (_OverlayMode == 3) color = lerp(float3(0.1, 0.05, 0.01), float3(0.0, 0.55, 1.0), saturate(state.z));
                else if (_OverlayMode == 4) color = state.w >= 0.0 ? float3(saturate(abs(state.w)), 0.1, 0.05) : float3(0.05, 0.2, saturate(abs(state.w)));
                else if (_OverlayMode == 5) color = float3(saturate(flow.x * 0.5 + 0.5), saturate(flow.y * 0.5 + 0.5), saturate(length(flow)));
                else if (_OverlayMode == 6) color = lerp(float3(0.02, 0.02, 0.08), float3(0.9, 0.95, 1.0), saturate(aux.x));
                else if (_OverlayMode == 7) color = lerp(float3(0.08, 0.02, 0.0), float3(0.0, 0.25, 1.0), saturate(aux.y));
                else if (_OverlayMode == 8) color = lerp(float3(0.0, 0.05, 0.0), float3(0.2, 1.0, 0.1), saturate(aux.z));
                else if (_OverlayMode == 9) color = lerp(float3(0.0, 0.0, 0.0), float3(1.0, 0.4, 0.0), saturate(aux.w));
                else if (_OverlayMode == 10) color = float3(saturate(materialProperties.x), saturate(materialProperties.y), 0.1);
                else if (_OverlayMode == 11)
                {
                    float surface = atmosphereCarrier ? 0.0 : saturate(state.z);
                    float ground = saturate(aux.y) * 0.65;
                    float vapor = saturate(aux.x) * 0.35;
                    float cloudSignal = cloud * 0.55;
                    float thermal = saturate(aux.w) * 0.25 + saturate(aux.z) * 0.15;
                    float waterSignal = saturate(surface + ground + vapor + cloudSignal + thermal);
                    color = lerp(float3(0.05, 0.04, 0.02), float3(0.0, 0.55, 1.0), waterSignal);
                    if (material == 9u) color = lerp(color, float3(0.0, 0.35, 0.95), 0.65);
                    if (cloud > 0.05) color = lerp(color, float3(0.85, 0.9, 1.0), saturate(cloud));
                    if (thermal > 0.15) color = lerp(color, float3(1.0, 0.35, 0.05), thermal);
                }
                else if (_OverlayMode == 12)
                {
                    // Vertical velocity: cyan updraft, amber downdraft.
                    float v = clamp(flow.y * 0.35, -1.0, 1.0);
                    color = v >= 0.0
                        ? lerp(float3(0.05, 0.05, 0.08), float3(0.2, 0.95, 1.0), v)
                        : lerp(float3(0.05, 0.05, 0.08), float3(1.0, 0.55, 0.05), -v);
                }
                else if (_OverlayMode == 13)
                {
                    float equilibrium = min(2.0, (1.0 - simulationRadius) * 2.0);
                    float anomaly = clamp((state.y - equilibrium) * 0.75, -1.0, 1.0);
                    color = anomaly >= 0.0
                        ? lerp(float3(0.05, 0.05, 0.1), float3(1.0, 0.15, 0.45), anomaly)
                        : lerp(float3(0.05, 0.05, 0.1), float3(0.15, 0.55, 1.0), -anomaly);
                }
                else if (_OverlayMode == 14)
                {
                    float altitudeCooling = saturate((simulationRadius - _AtmosphereStartRadius) / max(0.01, 1.0 - _AtmosphereStartRadius));
                    float thermal = saturate((state.x + 20.0) / 60.0);
                    float capacity = max(0.01, 0.55 * thermal * (1.0 - altitudeCooling * 0.65) * (1.0 + saturate(state.y) * 0.25));
                    float saturation = atmosphereCarrier ? saturate(aux.x / capacity) : 0.0;
                    color = lerp(float3(0.05, 0.08, 0.12), float3(0.95, 0.95, 1.0), saturation);
                    if (saturation > 0.85) color = lerp(color, float3(0.55, 0.85, 1.0), (saturation - 0.85) / 0.15);
                }
                else if (_OverlayMode == 15)
                {
                    float cloudOnly = atmosphereCarrier ? saturate(state.z * 3.0) : 0.0;
                    color = lerp(float3(0.02, 0.03, 0.06), float3(0.92, 0.95, 1.0), cloudOnly);
                }
                else if (_OverlayMode == 16)
                {
                    float spores = saturate(ecology.x);
                    float myco = saturate(ecology.y);
                    uint traits = (uint)round(ecology.z);
                    color = lerp(float3(0.04, 0.03, 0.02), float3(0.28, 0.72, 0.22), myco);
                    color = lerp(color, float3(0.86, 0.8, 0.28), spores * 0.55);
                    if ((traits & 16u) != 0u || (traits & 32u) != 0u)
                        color = lerp(color, float3(1.0, 0.35, 0.12), 0.35);
                    if ((traits & 1u) != 0u || (traits & 2u) != 0u)
                        color = lerp(color, float3(0.85, 0.72, 0.2), 0.28);
                    if ((traits & 4u) != 0u || (traits & 8u) != 0u)
                        color = lerp(color, float3(0.25, 0.85, 1.0), 0.28);
                }
                else if (_OverlayMode == 17)
                {
                    float flame = saturate(combustion.y);
                    float ignite = saturate(combustion.w);
                    float soot = saturate(combustion.z);
                    color = lerp(float3(0.04, 0.02, 0.02), float3(1.0, 0.28, 0.04), flame);
                    color = lerp(color, float3(1.0, 0.72, 0.12), ignite * 0.45);
                    color = lerp(color, float3(0.12, 0.1, 0.1), soot * 0.4);
                }
                else if (_OverlayMode == 18)
                {
                    color = lerp(float3(0.08, 0.02, 0.02), float3(0.25, 0.75, 1.0), saturate(combustion.x));
                }
                else if (_OverlayMode == 19)
                {
                    float q = clamp(storm.x * 0.55, -1.0, 1.0);
                    color = q >= 0.0
                        ? lerp(float3(0.05, 0.05, 0.1), float3(1.0, 0.22, 0.12), q)
                        : lerp(float3(0.05, 0.05, 0.1), float3(0.15, 0.45, 1.0), -q);
                    color = lerp(color, float3(0.9, 0.95, 1.0), saturate(storm.y));
                    color += float3(0.4, 0.52, 0.95) * saturate(storm.z) * 0.4;
                }
                else if (_OverlayMode == 20)
                {
                    float spores = saturate(life.x);
                    float biomass = saturate(life.y);
                    uint stage = genome.w & 255u;
                    color = lerp(float3(0.04, 0.05, 0.03), float3(0.18, 0.72, 0.28), biomass);
                    if (stage == 2u)
                        color = lerp(color, float3(0.32, 0.42, 0.2), 0.55);
                    if (stage == 3u)
                        color = lerp(color, float3(0.55, 0.35, 0.12), 0.7);
                    color = lerp(color, float3(0.82, 0.88, 0.35), spores * 0.55);
                    if (material == 128u)
                        color = lerp(color, float3(0.12, 0.85, 0.32), 0.25);
                }
                else if (_OverlayMode == 21)
                {
                    color = lerp(float3(0.02, 0.02, 0.06), float3(1.0, 0.92, 0.45), saturate(lightField));
                }
                else if (_OverlayMode == 22)
                {
                    float hue = frac(((genome.x ^ genome.y ^ genome.z) * 0.0000000023) + (genome.w & 255u) * 0.07);
                    color = float3(hue, saturate(life.y), 0.35 + 0.4 * saturate(life.z));
                    if (material == 128u)
                        color = lerp(float3(0.05, 0.05, 0.08), color, 0.9);
                    else
                        color = lerp(float3(0.04, 0.03, 0.04), color, saturate(life.x));
                }

                float radialGrid = frac(simulationRadius * height);
                float angularGrid = frac(angle / 6.28318530718 * width);
                float gridLine = step(radialGrid, 0.025) + step(angularGrid, 0.025);
                color *= 1.0 - saturate(gridLine) * 0.12;
                return half4(color, baseColor.a > 0.0 ? 1.0 : 0.0);
            }
            ENDHLSL
        }
    }
}
