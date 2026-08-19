Shader "GeneSys/Space Particle"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _TwinkleStrength ("Twinkle Strength", Float) = 0.65
        _Softness ("Softness", Float) = 1.5
        _NoiseScale ("Noise Scale", Float) = 3.0
        _Mode ("Mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Background+10"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "SpaceParticle"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_particles
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _TwinkleStrength;
                float _Softness;
                float _NoiseScale;
                float _Mode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _BaseColor;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.custom1 = input.custom1;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);
                float soft = max(0.05, _Softness);
                float mask = saturate(1.0 - pow(saturate(radius), soft));
                float texAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                mask *= max(texAlpha, 0.001);

                float3 color = input.color.rgb;
                float alpha = input.color.a * mask;

                if (_Mode < 0.5)
                {
                    float phase = input.custom1.x;
                    float speed = max(0.05, input.custom1.y);
                    float twinkle = 0.5 + 0.5 * sin((_Time.y * speed) + phase);
                    float amount = saturate(_TwinkleStrength);
                    color = lerp(color, float3(1.0, 1.0, 1.0), twinkle * amount * 0.85);
                    alpha *= lerp(1.0 - amount * 0.35, 1.0 + amount * 0.65, twinkle);
                }
                else
                {
                    float2 noiseUv = input.uv * _NoiseScale + float2(_Time.y * 0.015, -_Time.y * 0.01);
                    float n = Noise(noiseUv);
                    float mottling = lerp(0.55, 1.0, n);
                    color *= mottling;
                    alpha *= lerp(0.65, 1.0, n);
                }

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
