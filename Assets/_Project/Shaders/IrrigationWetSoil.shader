// Wet soil spreading from each drip emitter (FarmIrrigationBuilder / FarmIrrigationController). One alpha-blended
// quad per row, lying just above the bed / ridge / strip surface; the shader draws the union of the circular wet
// patches of the row's evenly spaced emitters, so neighbouring patches merge along the bed. Per vertex:
//   UV0 = (x from the emitter line, z from the first emitter) in metres,
//   UV1 = (network distance of the first emitter, emitter spacing), UV2 = (max radius, emitter count, z stretch).
// Emitter k starts wetting once the water front (_BTP_IrrigationWetFront) has passed it, and its patch grows over
// _WetSpan metres of front travel. Turning off, _BTP_IrrigationWetness falls slowly: the patches shrink and lighten.
// Darker, slightly glossy soil; no emission.
Shader "BTP/Irrigation Wet Soil"
{
    Properties
    {
        [MainColor] _BaseColor("Wet Soil", Color) = (0.07, 0.05, 0.035, 0.62)
        _Smoothness("Smoothness", Range(0, 1)) = 0.6
        _WetSpan("Growth (m of front travel)", Float) = 40
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -2, -2
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                float _WetSpan;
            CBUFFER_END

            // Set by FarmIrrigationController.
            float _BTP_IrrigationWetFront;
            float _BTP_IrrigationWetness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 emitters : TEXCOORD1;
                float3 shape : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 emitters : TEXCOORD2;
                float3 shape : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WetHash(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float WetNoise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WetHash(i), WetHash(i + float2(1, 0)), f.x),
                            lerp(WetHash(i + float2(0, 1)), WetHash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = input.uv;
                output.emitters = input.emitters;
                output.shape = input.shape;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half wetness = saturate(_BTP_IrrigationWetness);
                clip(wetness - 0.001);

                float spacing = max(input.emitters.y, 0.01);
                float count = max(input.shape.y, 1.0);
                float radius = input.shape.x;
                float stretch = max(input.shape.z, 0.01);
                float2 xz = input.positionWS.xz;
                // Ragged, soaked-in edge.
                float rag = (WetNoise(xz * 7.0) - 0.5) * 0.35 + (WetNoise(xz * 23.0) - 0.5) * 0.15;

                float nearest = clamp(round(input.uv.y / spacing), 0.0, count - 1.0);
                half mask = 0.0;
                [unroll]
                for (int j = -1; j <= 1; j++)
                {
                    float k = clamp(nearest + j, 0.0, count - 1.0);
                    float grow = saturate((_BTP_IrrigationWetFront - (input.emitters.x + k * spacing)) / max(_WetSpan, 0.01));
                    float r = radius * sqrt(grow) * lerp(0.35, 1.0, wetness);
                    float2 d = float2(input.uv.x, (input.uv.y - k * spacing) / stretch);
                    float dist = length(d) / max(r, 1e-4) + rag;
                    mask = max(mask, (1.0 - smoothstep(0.55, 1.0, dist)) * step(1e-3, grow));
                }

                clip(mask - 0.002);
                half3 normalWS = half3(0, 1, 0);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light light = GetMainLight();
                half3 halfDir = normalize(light.direction + viewWS);
                // Wet soil: a soft sheen (broken up by the soil grain), not a mirror.
                half grain = WetNoise(xz * 41.0);
                half spec = pow(saturate(dot(normalWS, halfDir)), 60.0) * _Smoothness * 0.35 * grain;
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), 0.45, 1.0);

                half3 color = _BaseColor.rgb * (light.color * saturate(light.direction.y) + SampleSH(normalWS));
                color = lerp(color, sky, fresnel * 0.25 * _Smoothness) + light.color * spec;
                half alpha = _BaseColor.a * mask * wetness;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
