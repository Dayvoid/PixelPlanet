Shader "GeneSys/Atmosphere Glow"
{
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.35, 0.7, 1.0, 1)
        _InnerRadius ("Inner Radius", Float) = 0.92
        _OuterRadius ("Outer Radius", Float) = 1.18
        _Intensity ("Intensity", Float) = 0.7
        _Softness ("Softness", Float) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent-20"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "AtmosphereGlow"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                float _InnerRadius;
                float _OuterRadius;
                float _Intensity;
                float _Softness;
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

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float inner = max(0.01, _InnerRadius);
                float outer = max(inner + 0.001, _OuterRadius);
                if (r < inner || r > outer) return half4(0, 0, 0, 0);

                float t = saturate((r - inner) / (outer - inner));
                float falloff = pow(1.0 - t, max(0.2, _Softness));
                float edgeBoost = smoothstep(0.0, 0.2, t) * smoothstep(1.0, 0.55, t);
                float alpha = falloff * edgeBoost * max(0.0, _Intensity);
                return half4(_GlowColor.rgb, alpha * _GlowColor.a);
            }
            ENDHLSL
        }
    }
}
