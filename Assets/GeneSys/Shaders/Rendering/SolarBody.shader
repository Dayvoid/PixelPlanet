Shader "GeneSys/Solar Body"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (1.0, 0.92, 0.55, 1)
        _CoronaColor ("Corona Color", Color) = (1.0, 0.55, 0.12, 1)
        _CoreRadius ("Core Radius", Float) = 0.28
        _CoronaRadius ("Corona Radius", Float) = 1.0
        _CoreIntensity ("Core Intensity", Float) = 1.0
        _CoronaIntensity ("Corona Intensity", Float) = 0.85
        _PulseSpeed ("Pulse Speed", Float) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent-10"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "SolarBody"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _CoronaColor;
                float _CoreRadius;
                float _CoronaRadius;
                float _CoreIntensity;
                float _CoronaIntensity;
                float _PulseSpeed;
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
                float coronaRadius = max(0.05, _CoronaRadius);
                if (r > coronaRadius) return half4(0, 0, 0, 0);

                float coreRadius = saturate(_CoreRadius) * coronaRadius;
                float pulse = 0.85 + 0.15 * sin(_Time.y * _PulseSpeed);
                float angle = atan2(p.y, p.x);
                float ray = 0.55 + 0.45 * abs(sin(angle * 7.0 + _Time.y * 0.4));
                float noise = lerp(0.75, 1.0, Hash21(floor(p * 18.0 + _Time.y * 0.25)));

                float core = 1.0 - smoothstep(coreRadius * 0.55, coreRadius, r);
                float halo = 1.0 - smoothstep(coreRadius, coronaRadius, r);
                halo *= ray * noise;

                float3 color = _CoreColor.rgb * core * _CoreIntensity * pulse;
                color += _CoronaColor.rgb * halo * _CoronaIntensity;
                float alpha = saturate(core * _CoreIntensity + halo * _CoronaIntensity);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
