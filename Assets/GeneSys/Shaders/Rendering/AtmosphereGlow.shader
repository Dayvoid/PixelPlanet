Shader "GeneSys/Atmosphere Glow"
{
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.35, 0.7, 1.0, 1)
        _InnerRadius ("Inner Radius", Float) = 0.92
        _OuterRadius ("Outer Radius", Float) = 1.18
        _Intensity ("Intensity", Float) = 0.7
        _Softness ("Softness", Float) = 1.4
        _PixelScale ("Pixel Scale", Float) = 18
        _RayCount ("Ray Count", Float) = 7
        _Speed ("Speed", Float) = 1.0
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
                float _PixelScale;
                float _RayCount;
                float _Speed;
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

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

                // Match solar corona: quantized hash dither + slow radial rays.
                float angle = atan2(p.y, p.x);
                float rays = max(1.0, _RayCount);
                float speed = max(0.001, _Speed);
                float ray = 0.55 + 0.45 * abs(sin(angle * rays + _Time.y * (0.4 * speed)));
                float pixelScale = max(4.0, _PixelScale);
                float2 pixelCell = floor(float2(angle * rays * 2.8, r * pixelScale) + _Time.y * (0.25 * speed));
                float pixelNoise = lerp(0.72, 1.0, Hash21(pixelCell));

                float alpha = falloff * edgeBoost * ray * pixelNoise * max(0.0, _Intensity);
                float3 color = _GlowColor.rgb * lerp(0.88, 1.08, pixelNoise);
                return half4(color, alpha * _GlowColor.a);
            }
            ENDHLSL
        }
    }
}
