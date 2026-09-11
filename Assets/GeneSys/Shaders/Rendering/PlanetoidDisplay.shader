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
            #include "../Simulation/Common/SolarInsolation.hlsl"

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
            Texture2DArray<float4> _FaunaTex;
            Texture2DArray<float4> _GrassTex;
            Texture2DArray<float4> _WaspTex;
            Texture2DArray<float4> _TreeTex;
            Texture2D<float2> _AcousticTex;
            Texture2D<float> _LightTex;
            Texture2DArray<float> _MobileMassTex;
            StructuredBuffer<float4> _ClimateState;
            int _ClimateBins;
            Texture2D<float4> _Palette;
            Texture2D<float4> _Properties;
            Texture2D<float4> _Categories;
            float _VisualCoreRadius;
            float _VisualCoreSquash;
            float _AtmosphereStartRadius;
            int _OverlayMode;
            float _SolarAngle01;
            float _DayNightLightingStrength;
            float _VaporCapacityScale;

            float DisplayVaporSaturation(float temperature)
            {
                float t = clamp(temperature, -40.0, 80.0);
                float es = exp(17.27 * t / max(1e-3, 237.7 + t));
                return max(1e-4, max(0.001, _VaporCapacityScale) * es);
            }

            float DisplayRelativeHumidity(float vapor, float temperature)
            {
                return saturate(max(0.0, vapor) / max(1e-5, DisplayVaporSaturation(temperature)));
            }

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

            float3 GrassHue(float gene)
            {
                float t = gene / 255.0;
                return lerp(float3(0.16, 0.48, 0.12), float3(0.38, 0.78, 0.22), t);
            }

            float3 FlowerHue(float gene, uint rare)
            {
                float t = gene / 255.0;
                float3 baseCol = lerp(float3(0.85, 0.28, 0.38), float3(0.92, 0.78, 0.25), t);
                if (rare == 2u) baseCol = lerp(baseCol, float3(0.35, 0.55, 0.95), 0.55);
                else if (rare == 32u) baseCol = lerp(baseCol, float3(0.95, 0.45, 0.12), 0.55);
                else if (rare == 8u) baseCol = lerp(baseCol, float3(0.75, 0.9, 0.35), 0.4);
                return baseCol;
            }

            float sdSegment(float2 p, float2 a, float2 b, float thick)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-5));
                return length(pa - ba * h) - thick;
            }

            float3 DrawGrass(float3 color, float2 cellUv, float2 flow, int2 cell, uint width)
            {
                [unroll]
                for (uint slot = 0u; slot < 3u; slot++)
                {
                    float4 life = _GrassTex.Load(int4(cell, (int)(slot * 4u), 0));
                    uint4 genome = asuint(_GrassTex.Load(int4(cell, (int)(slot * 4u + 1u), 0)));
                    float4 timing = _GrassTex.Load(int4(cell, (int)(slot * 4u + 2u), 0));
                    uint stage = genome.w & 255u;
                    if (stage == 0u) continue;
                    float2 offset = slot == 0u ? float2(-0.28, 0.10) : (slot == 2u ? float2(0.28, 0.10) : float2(0.0, 0.22));
                    float2 baseP = float2(0.5, 0.18) + offset * 0.35;
                    uint heightGene = (genome.y >> 8) & 255u;
                    uint colorGene = (genome.y >> 16) & 255u;
                    uint sizeGene = (genome.y >> 24) & 255u;
                    uint flowerGene = genome.z & 255u;
                    float height = 0.22 + (heightGene / 255.0) * 0.38 * saturate(life.x);
                    float bend = clamp(flow.x, -1.5, 1.5) * 0.12;
                    float2 tip = baseP + float2(bend, height);
                    float blade = sdSegment(cellUv, baseP, tip, 0.025 + saturate(life.x) * 0.02);
                    float3 bladeCol = GrassHue((float)colorGene);
                    if (stage == 1u) bladeCol = lerp(bladeCol, float3(0.32, 0.52, 0.18), 0.35);
                    color = lerp(color, bladeCol, saturate(1.0 - blade * 18.0) * 0.95);

                    uint mask = (uint)round(max(timing.w, 0.0)) & 7u;
                    uint rare = 0u;
                    uint rareRank = 0u;
                    [unroll]
                    for (uint bit = 0u; bit < 3u; bit++)
                    {
                        if ((mask & (1u << bit)) == 0u) continue;
                        float2 rootEnd = baseP + float2((bit == 0u ? -0.18 : (bit == 2u ? 0.18 : 0.0)), -0.22);
                        float root = sdSegment(cellUv, baseP, rootEnd, 0.012);
                        color = lerp(color, float3(0.28, 0.16, 0.08), saturate(1.0 - root * 22.0) * 0.85);
                        int tx = cell.x + (bit == 0u ? -1 : (bit == 2u ? 1 : 0));
                        tx = (tx % (int)width + (int)width) % (int)width;
                        int ty = cell.y - 1;
                        if (ty < 0) continue;
                        uint traits = (uint)round(_EcologyTex.Load(int3(tx, ty, 0)).z);
                        uint flags[3] = { 2u, 32u, 8u };
                        [unroll]
                        for (uint f = 0u; f < 3u; f++)
                        {
                            if ((traits & flags[f]) != 0u && (3u - f) > rareRank)
                            {
                                rare = flags[f];
                                rareRank = 3u - f;
                            }
                        }
                    }

                    if (((uint)round(max(timing.w, 0.0)) & 8u) != 0u)
                    {
                        float flowerR = 0.04 + (sizeGene / 255.0) * 0.06;
                        float disc = saturate((flowerR - length(cellUv - tip)) * 20.0);
                        color = lerp(color, FlowerHue((float)flowerGene, rare), disc);
                    }
                }
                return color;
            }

            // Banded body plus two wing strokes whose beat amplitude falls off with speed, so a
            // hovering wasp blurs its wings and a gliding one holds them out flat.
            float3 DrawWasp(float3 color, float2 cellUv, int2 cell, uint stage, uint cargoCount)
            {
                float2 center = float2(0.5, 0.5);
                float4 motion = _WaspTex.Load(int4(cell, 1, 0));
                float speed = saturate(length(motion.xy) * 0.35);

                float radius = stage == 2u ? 0.26 : 0.34;
                float disc = saturate((radius - length(cellUv - center)) * 9.0);
                float band = step(0.5, frac(cellUv.x * 3.0));
                float3 body = lerp(float3(0.10, 0.09, 0.06), float3(0.94, 0.76, 0.14), band);
                if (stage == 2u) body = lerp(body, float3(0.72, 0.66, 0.32), 0.35);

                float phase = _Time.y * lerp(46.0, 14.0, speed) + (float)(cell.x * 7 + cell.y * 13);
                float beat = sin(phase) * lerp(0.16, 0.05, speed);
                float wingAlpha = lerp(0.30, 0.6, speed);
                [unroll]
                for (int side = 0; side < 2; side++)
                {
                    float sx = side == 0 ? -1.0 : 1.0;
                    float2 root = center + float2(sx * 0.05, 0.03);
                    float2 tip = center + float2(sx * 0.34, 0.10 + beat * sx * sx);
                    float wing = sdSegment(cellUv, root, tip, 0.018);
                    color = lerp(color, float3(0.86, 0.9, 0.95), saturate(1.0 - wing * 20.0) * wingAlpha);
                }

                color = lerp(color * 0.4, body, lerp(0.4, 0.95, disc));
                if (cargoCount > 0u)
                {
                    float rim = saturate((0.10 - abs(length(cellUv - center) - radius)) * 14.0);
                    color = lerp(color, float3(0.98, 0.92, 0.35), rim * (0.3 + 0.2 * (float)cargoCount));
                }
                return color;
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
                float4 faunaVitals = _FaunaTex.Load(int4(cell, 0, 0));
                uint4 faunaGenome = asuint(_FaunaTex.Load(int4(cell, 2, 0)));
                float2 acoustic = _AcousticTex.Load(int3(cell, 0));
                float category = _Categories.Load(int3((int)material, 0, 0)).x;
                if (!IsMottledCategory(category)) shade = 0u;
                float4 baseColor = _Palette.Load(int3((int)material, (int)shade, 0));
                float4 materialProperties = _Properties.Load(int3((int)material, 0, 0));
                float3 color = baseColor.rgb;
                bool atmosphereCarrier = material == 1u || simulationRadius >= _AtmosphereStartRadius;
                float cloud = atmosphereCarrier ? saturate(state.z * 2.5) : 0.0;
                float relativeHumidity = atmosphereCarrier ? DisplayRelativeHumidity(aux.x, state.x) : 0.0;

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
                    else if (material == 129u || material == 130u)
                    {
                        uint stage = faunaGenome.w & 255u;
                        float3 stageColor = material == 130u || stage == 1u ? float3(0.82, 0.74, 0.48)
                            : stage == 2u ? float3(0.55, 0.38, 0.16)
                            : float3(0.42, 0.26, 0.10);
                        float2 cellUv = float2(
                            frac(angle / 6.28318530718 * width),
                            frac(simulationRadius * height));
                        float radius = material == 130u ? 0.22 : 0.38;
                        float disc = saturate((radius - length(cellUv - 0.5)) * 8.0);
                        color = lerp(color * 0.4, stageColor, lerp(0.4, 0.95, disc));
                    }
                    else if (material == 132u)
                    {
                        uint4 waspGenome = asuint(_WaspTex.Load(int4(cell, 2, 0)));
                        uint cargoCount = 0u;
                        [unroll]
                        for (int slot = 0; slot < 3; slot++)
                            if ((asuint(_WaspTex.Load(int4(cell, 4 + slot, 0))).w & 1u) != 0u) cargoCount++;
                        float2 cellUv = float2(
                            frac(angle / 6.28318530718 * width),
                            frac(simulationRadius * height));
                        color = DrawWasp(color, cellUv, cell, waspGenome.w & 255u, cargoCount);
                    }
                    else if (material == 133u)
                    {
                        float2 cellUv = float2(
                            frac(angle / 6.28318530718 * width),
                            frac(simulationRadius * height));
                        float disc = saturate((0.2 - length(cellUv - 0.5)) * 8.0);
                        color = lerp(color * 0.4, float3(0.90, 0.86, 0.60), lerp(0.4, 0.95, disc));
                    }

                    {
                        float2 grassUv = float2(
                            frac(angle / 6.28318530718 * width),
                            frac(simulationRadius * height));
                        color = DrawGrass(color, grassUv, flow, cell, width);
                    }

                    if (cloud > 0.02)
                        color = lerp(color, float3(0.92, 0.95, 1.0), saturate(cloud * 0.9));
                    if (atmosphereCarrier && relativeHumidity > 0.75)
                        color = lerp(color, float3(0.85, 0.90, 0.95), saturate((relativeHumidity - 0.75) * 2.2) * 0.45);
                    if (!atmosphereCarrier && state.z > 0.02 && state.z < 0.55 && material != 9u && material != 10u)
                        color = lerp(color, float3(0.70, 0.86, 0.96), saturate(state.z * 2.0) * 0.28);
                    float mobileSediment = max(0.0, _MobileMassTex.Load(int4(cell, 0, 0))) + max(0.0, _MobileMassTex.Load(int4(cell, 1, 0)));
                    if (mobileSediment > 0.05 && (material == 1u || material == 0u || material == 7u || material == 15u))
                        color = lerp(color, float3(0.62, 0.52, 0.28), saturate(mobileSediment) * 0.65);

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

                    // Linear solar falloff + stronger day/night, matching Weather AtmosphericForcing.
                    float strength = saturate(_DayNightLightingStrength);
                    if (strength > 0.001)
                    {
                        // Attenuate sunlight with depth so subterranean mantle/core depths are not sliced by surface day/night
                        float depthFade = saturate((simulationRadius - _VisualCoreRadius) / max(0.01, 1.0 - _VisualCoreRadius));
                        float effectiveStrength = strength * depthFade;
                        float theta01 = angle / 6.28318530718;
                        float insolation = SolarInsolation(theta01, _SolarAngle01);
                        float dayFactor = saturate(insolation * 0.94 + 0.03);
                        float lighting = lerp(1.0 - 0.92 * effectiveStrength, 1.0 + 0.22 * effectiveStrength, dayFactor);
                        color *= lighting;
                        color = lerp(lerp(color, color * float3(0.62, 0.70, 0.95), depthFade), color, dayFactor);
                    }
                }
                else if (_OverlayMode == 1) color = HeatColor(state.x);
                else if (_OverlayMode == 2) color = lerp(float3(0.02, 0.02, 0.08), float3(1.0, 0.1, 0.8), saturate(state.y));
                else if (_OverlayMode == 3) color = lerp(float3(0.05, 0.08, 0.12), float3(0.95, 0.95, 1.0), relativeHumidity);
                else if (_OverlayMode == 4) color = state.w >= 0.0 ? float3(saturate(abs(state.w)), 0.1, 0.05) : float3(0.05, 0.2, saturate(abs(state.w)));
                else if (_OverlayMode == 5) color = float3(saturate(flow.x * 0.5 + 0.5), saturate(flow.y * 0.5 + 0.5), saturate(length(flow)));
                else if (_OverlayMode == 6) color = lerp(float3(0.02, 0.02, 0.08), float3(0.9, 0.95, 1.0), saturate(aux.x));
                else if (_OverlayMode == 7) color = lerp(float3(0.08, 0.02, 0.0), float3(0.0, 0.25, 1.0), saturate(aux.y));
                else if (_OverlayMode == 8) color = lerp(float3(0.0, 0.05, 0.0), float3(0.2, 1.0, 0.1), saturate(aux.z));
                else if (_OverlayMode == 9) color = lerp(float3(0.0, 0.0, 0.0), float3(1.0, 0.4, 0.0), saturate(aux.w));
                else if (_OverlayMode == 10) color = float3(saturate(materialProperties.x), saturate(materialProperties.y), 0.1);
                else if (_OverlayMode == 11)
                {
                    color = float3(saturate(atmosphereCarrier ? 0.0 : state.z), saturate(aux.x), saturate(aux.y));
                    if (material == 9u) color = lerp(color, float3(0.15, 0.45, 1.0), 0.55);
                    if (cloud > 0.05) color = lerp(color, float3(0.85, 0.9, 1.0), saturate(cloud * 0.65));
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
                    color = lerp(float3(0.05, 0.08, 0.12), float3(0.95, 0.95, 1.0), relativeHumidity);
                    if (relativeHumidity > 0.85) color = lerp(color, float3(0.55, 0.85, 1.0), (relativeHumidity - 0.85) / 0.15);
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
                else if (_OverlayMode == 23)
                {
                    uint stage = faunaGenome.w & 255u;
                    color = lerp(float3(0.05, 0.04, 0.03), float3(0.55, 0.32, 0.1), saturate(faunaVitals.x));
                    if (material == 130u || stage == 1u)
                        color = lerp(color, float3(0.82, 0.74, 0.48), 0.7);
                    else if (stage == 2u)
                        color = lerp(color, float3(0.55, 0.38, 0.16), 0.45);
                    color = lerp(color, float3(0.2, 0.55, 0.9), saturate(faunaVitals.y) * 0.35);
                    if (material == 129u)
                        color = lerp(color, float3(0.45, 0.28, 0.12), 0.25);
                }
                else if (_OverlayMode == 24)
                {
                    float feed = clamp(acoustic.x, -1.0, 1.0);
                    float mate = clamp(acoustic.y, -1.0, 1.0);
                    color = float3(0.04, 0.04, 0.06);
                    color = lerp(color, float3(0.15, 0.85, 0.35), saturate(feed));
                    color = lerp(color, float3(0.95, 0.45, 0.15), saturate(mate));
                    if (feed < 0.0) color = lerp(color, float3(0.1, 0.2, 0.45), saturate(-feed));
                    if (mate < 0.0) color = lerp(color, float3(0.35, 0.1, 0.4), saturate(-mate));
                }
                else if (_OverlayMode == 25)
                {
                    color = float3(0.04, 0.05, 0.03);
                    float2 grassUv = float2(
                        frac(angle / 6.28318530718 * width),
                        frac(simulationRadius * height));
                    color = DrawGrass(color, grassUv, flow, cell, width);
                    float occupied = 0.0;
                    [unroll]
                    for (uint slot = 0u; slot < 3u; slot++)
                    {
                        uint4 g = asuint(_GrassTex.Load(int4(cell, (int)(slot * 4u + 1u), 0)));
                        if ((g.w & 255u) != 0u) occupied += 0.33;
                    }
                    color = lerp(float3(0.05, 0.06, 0.04), color, 0.35 + occupied);
                }
                else if (_OverlayMode == 26)
                {
                    float4 phys = _TreeTex.Load(int4(cell, 0, 0));
                    uint4 topology = asuint(_TreeTex.Load(int4(cell, 1, 0)));
                    uint4 treeGenome = asuint(_TreeTex.Load(int4(cell, 2, 0)));
                    uint stage = treeGenome.w & 255u;
                    uint role = (topology.z >> 8) & 255u;
                    color = float3(0.05, 0.04, 0.03);
                    if (topology.x != 0u)
                    {
                        if (role == 1u) color = float3(0.28, 0.16, 0.08);
                        else if (role == 2u) color = float3(0.32, 0.7, 0.22);
                        else if (role == 3u) color = float3(0.34, 0.2, 0.1);
                        else if (role == 4u) color = float3(0.42, 0.26, 0.12);
                        else if (role == 5u) color = float3(0.48, 0.32, 0.14);
                        else color = float3(0.22, 0.62, 0.18);
                        if (stage == 4u) color = lerp(color, float3(0.18, 0.12, 0.08), 0.65);
                        color = lerp(color * 0.35, color, saturate(phys.w));
                    }
                    else if (material == 134u || material == 135u)
                        color = float3(0.55, 0.12, 0.12);
                }
                else if (_OverlayMode == 27)
                {
                    float coarse = saturate(_MobileMassTex.Load(int4(cell, 0, 0)));
                    float fine = saturate(_MobileMassTex.Load(int4(cell, 1, 0)));
                    float structural = saturate(_MobileMassTex.Load(int4(cell, 5, 0)));
                    color = float3(coarse, fine, structural);
                }
                else if (_OverlayMode == 28)
                {
                    int bins = max(8, _ClimateBins);
                    int bin = (int)((uint)cell.x * (uint)bins / max(1u, width));
                    float4 mem = _ClimateState[bin * 3];
                    float4 land = _ClimateState[bin * 3 + 1];
                    float wet = saturate(mem.z);
                    float albedo = saturate(mem.w);
                    float tAnom = saturate((mem.x + 10.0) / 50.0);
                    if (atmosphereCarrier)
                        color = lerp(float3(0.12, 0.22, 0.72), float3(0.92, 0.28, 0.12), tAnom);
                    else
                        color = lerp(float3(0.42, 0.28, 0.12), float3(0.18, 0.42, 0.78), wet);
                    color = lerp(color, float3(0.92, 0.94, 0.98), albedo * 0.35);
                    color = lerp(color, color * float3(0.75, 1.05, 0.7), saturate(land.w) * 0.25);
                }

                float radialGrid = frac(simulationRadius * height);
                float angularGrid = frac(angle / 6.28318530718 * width);
                float gridLine = step(radialGrid, 0.025) + step(angularGrid, 0.025);
                float coreGridFade = saturate((simulationRadius - _VisualCoreRadius * 0.5) / max(0.01, _VisualCoreRadius * 0.5));
                color *= 1.0 - saturate(gridLine) * (0.12 * coreGridFade);
                return half4(color, baseColor.a > 0.0 ? 1.0 : 0.0);
            }
            ENDHLSL
        }
    }
}
