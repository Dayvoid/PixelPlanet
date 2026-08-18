Shader "GeneSys/Planetoid Display"
{
    Properties
    {
        _Palette ("Material Palette", 2D) = "white" {}
        _Properties ("Material Properties", 2D) = "black" {}
        _VisualCoreRadius ("Visual Core Radius", Float) = 0.28
        _VisualCoreSquash ("Visual Core Squash", Float) = 0.45
        _OverlayMode ("Overlay Mode", Int) = 0
        _PressureDisplayScale ("Pressure Display Scale", Float) = 8.0
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
            Texture2D<float4> _WaterTex;
            Texture2D<float4> _HydrostaticTex;
            Texture2D<float4> _Palette;
            Texture2D<float4> _Properties;
            float _VisualCoreRadius;
            float _VisualCoreSquash;
            int _OverlayMode;
            float _PressureDisplayScale;

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

                uint width, height;
                _MaterialTex.GetDimensions(width, height);
                float displayedCore = _VisualCoreRadius * _VisualCoreSquash;
                float minInner = 1.25 / max(1.0, (float)height);
                float simulationRadius = displayRadius >= displayedCore
                    ? lerp(_VisualCoreRadius, 1.0, (displayRadius - displayedCore) / max(0.0001, 1.0 - displayedCore))
                    : lerp(minInner, _VisualCoreRadius, saturate(displayRadius / max(displayedCore, 1e-4)));

                float angle = atan2(p.y, p.x);
                if (angle < 0.0) angle += 6.28318530718;
                int2 cell = int2(
                    clamp((int)floor(angle / 6.28318530718 * width), 0, (int)width - 1),
                    clamp((int)floor(simulationRadius * height), 0, (int)height - 1));

                uint material = min(_MaterialTex.Load(int3(cell, 0)), 255u);
                float4 state = _StateTex.Load(int3(cell, 0));
                float2 flow = _FlowTex.Load(int3(cell, 0));
                float4 aux = _AuxTex.Load(int3(cell, 0));
                float4 water = _WaterTex.Load(int3(cell, 0));
                float hydro = _HydrostaticTex.Load(int3(cell, 0)).x;
                float4 baseColor = _Palette.Load(int3((int)material, 0, 0));
                float4 materialProperties = _Properties.Load(int3((int)material, 0, 0));
                float3 color = baseColor.rgb;
                float totalPressure = max(0.0, hydro) + max(0.0, state.y);
                float waterMass = saturate(water.x + water.y + water.z + water.w);

                if (_OverlayMode == 1) color = HeatColor(state.x);
                else if (_OverlayMode == 2)
                {
                    color = lerp(float3(0.02, 0.02, 0.08), float3(0.95, 0.35, 0.05), saturate(totalPressure / max(0.1, _PressureDisplayScale)));
                    color += float3(0.15, 0.05, 0.4) * saturate(state.y);
                }
                else if (_OverlayMode == 3) color = lerp(float3(0.1, 0.05, 0.01), float3(0.0, 0.55, 1.0), saturate(water.x));
                else if (_OverlayMode == 4) color = state.w >= 0.0 ? float3(saturate(abs(state.w)), 0.1, 0.05) : float3(0.05, 0.2, saturate(abs(state.w)));
                else if (_OverlayMode == 5) color = float3(saturate(flow.x * 0.5 + 0.5), saturate(flow.y * 0.5 + 0.5), saturate(length(flow)));
                else if (_OverlayMode == 6) color = lerp(float3(0.02, 0.02, 0.08), float3(0.9, 0.95, 1.0), saturate(water.w));
                else if (_OverlayMode == 7) color = lerp(float3(0.08, 0.02, 0.0), float3(0.0, 0.25, 1.0), saturate(water.y));
                else if (_OverlayMode == 8) color = lerp(float3(0.0, 0.05, 0.0), float3(0.2, 1.0, 0.1), saturate(aux.z));
                else if (_OverlayMode == 9) color = lerp(float3(0.0, 0.0, 0.0), float3(1.0, 0.4, 0.0), saturate(aux.w));
                else if (_OverlayMode == 10) color = float3(saturate(materialProperties.x), saturate(materialProperties.y), 0.1);
                else if (_OverlayMode == 11)
                {
                    color = lerp(float3(0.05, 0.04, 0.02), float3(0.0, 0.55, 1.0), waterMass);
                    if (material == 9u) color = lerp(color, float3(0.0, 0.35, 0.95), 0.45);
                    if (material == 10u) color = lerp(color, float3(0.7, 0.9, 1.0), 0.45);
                }
                else if (_OverlayMode == 12) color = lerp(float3(0.04, 0.05, 0.08), float3(0.7, 0.9, 1.0), saturate(water.z));
                else if (_OverlayMode == 13) color = lerp(float3(0.02, 0.01, 0.0), float3(1.0, 0.35, 0.05), saturate(aux.w * 0.7 + aux.z * 0.3));

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
