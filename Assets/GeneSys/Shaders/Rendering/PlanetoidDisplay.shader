Shader "GeneSys/Planetoid Display"
{
    Properties
    {
        _Palette ("Material Palette", 2D) = "white" {}
        _Properties ("Material Properties", 2D) = "black" {}
        _VisualCoreRadius ("Visual Core Radius", Float) = 0.28
        _VisualCoreSquash ("Visual Core Squash", Float) = 0.45
        _OverlayMode ("Overlay Mode", Int) = 0
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
            Texture2D<float4> _Palette;
            Texture2D<float4> _Properties;
            float _VisualCoreRadius;
            float _VisualCoreSquash;
            float _AtmosphereStartRadius;
            int _OverlayMode;

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
                float4 baseColor = _Palette.Load(int3((int)material, 0, 0));
                float4 materialProperties = _Properties.Load(int3((int)material, 0, 0));
                float3 color = baseColor.rgb;
                bool atmosphereCarrier = material == 1u || material == 11u || simulationRadius >= _AtmosphereStartRadius;
                float cloud = atmosphereCarrier ? saturate(state.z * 2.5) : 0.0;

                if (_OverlayMode == 0)
                {
                    if (cloud > 0.02)
                        color = lerp(color, float3(0.92, 0.95, 1.0), saturate(cloud * 0.9));
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
