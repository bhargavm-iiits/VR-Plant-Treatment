// Standing irrigation water in the Farm rows (FarmIrrigationBuilder / FarmIrrigationController). Shallow, muddy,
// alpha-blended, no depth writes, no emission. One merged quad per row per band:
//   UV0 = pool-local position (-1..1, -1..1), UV1 = (arrival m, depth m), UV2 = (half width, half length, shore m).
// The quad is built at its empty height, just below the soil, and rises by fill * depth, where
// fill = saturate((front - arrival) / fill span) * level (controller globals), so rows fill in the order the
// water reaches them and all drain together. Within a pool the water does not appear as one sheet: a procedural
// "ground height" (noise, lower along the edges) decides where the surface is above the soil, so water shows in low
// spots and along the edges first and covers the whole surface only near full. The tint darkens with local depth.
// Reflections: Fresnel-weighted GlossyEnvironmentReflection (reflection probe / sky) plus a tight sun highlight;
// gentle procedural ripples, and small ripple rings (drips landing) while water is flowing.
Shader "BTP/Irrigation Water"
{
    Properties
    {
        [MainColor] _BaseColor("Shallow Tint", Color) = (0.36, 0.29, 0.20, 0.22)
        _DeepColor("Deep Tint", Color) = (0.12, 0.09, 0.06, 0.8)
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
        _RippleScale("Ripple Scale", Float) = 5
        _RippleStrength("Ripple Strength", Range(0, 1)) = 0.06
        _RippleSpeed("Ripple Speed", Float) = 0.5
        _PatchScale("Low-spot Noise Scale", Float) = 2.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
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
                half4 _DeepColor;
                half _Smoothness;
                float _RippleScale;
                half _RippleStrength;
                float _RippleSpeed;
                float _PatchScale;
            CBUFFER_END

            // Set by FarmIrrigationController.
            float _BTP_IrrigationFront;
            float _BTP_IrrigationLevel;
            float _BTP_IrrigationFillSpan;
            float _BTP_IrrigationFlow;
            float _BTP_IrrigationPoolDelay;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 pool : TEXCOORD1;
                float3 extent : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 local : TEXCOORD1;    // xy: pool-local -1..1, z: fill, w: depth
                float3 extent : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WaterHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float WaterNoise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WaterHash(i), WaterHash(i + float2(1, 0)), f.x),
                            lerp(WaterHash(i + float2(0, 1)), WaterHash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float fill = saturate((_BTP_IrrigationFront - _BTP_IrrigationPoolDelay - input.pool.x) / max(_BTP_IrrigationFillSpan, 0.01)) * saturate(_BTP_IrrigationLevel);
                float3 positionOS = input.positionOS.xyz;
                positionOS.y += fill * input.pool.y;
                VertexPositionInputs position = GetVertexPositionInputs(positionOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.local = float4(input.uv, fill, input.pool.y);
                output.extent = input.extent;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float fill = input.local.z;
                clip(fill - 0.005);

                // Where is the water above the (procedural) soil? Low spots and the edges first.
                float2 xz = input.positionWS.xz;
                float edge = min((1.0 - abs(input.local.x)) * input.extent.x, (1.0 - abs(input.local.y)) * input.extent.y);
                float bumps = WaterNoise(xz * _PatchScale) * 0.65 + WaterNoise(xz * _PatchScale * 3.1 + 7.3) * 0.35;
                float soil = 1.08 * bumps * lerp(0.35, 1.0, smoothstep(0.0, 0.35, edge));
                float waterDepth = fill - soil;           // in units of the pool depth
                clip(waterDepth);
                half shore = smoothstep(0.0, max(input.extent.z, 0.001), edge);
                half depthMix = saturate(waterDepth * 3.0);

                // Gentle wind ripples.
                float2 p = xz * _RippleScale;
                float t = _Time.y * _RippleSpeed;
                float2 slope = float2(
                    cos(p.x + t * 2.1) * 0.6 + cos((p.x + p.y) * 0.7 - t * 1.7) * 0.4 + cos(p.x * 2.3 - p.y * 0.4 + t * 3.1) * 0.25,
                    cos(p.y * 1.1 - t * 1.9) * 0.6 + cos((p.y - p.x) * 0.8 + t * 1.3) * 0.4 + cos(p.y * 2.1 + p.x * 0.5 - t * 2.7) * 0.25) * _RippleStrength;

                // Drip rings: one possible drop per 0.45 m cell, about half the cells active, while water flows.
                float2 cell = floor(xz / 0.45);
                float2 local = xz / 0.45 - cell;
                float seed = WaterHash(cell);
                float2 centre = float2(0.2 + 0.6 * WaterHash(cell + 3.1), 0.2 + 0.6 * WaterHash(cell + 7.7));
                float age = frac(_Time.y * 0.6 + seed);
                float2 offset = local - centre;
                float dist = length(offset) * 0.45;
                float ringRadius = age * 0.16;
                float ring = exp(-pow((dist - ringRadius) / 0.008, 2.0)) * (1.0 - age) * step(seed, 0.5) * _BTP_IrrigationFlow;
                slope += (offset / max(length(offset), 1e-4)) * ring * 0.35;

                half3 normalWS = normalize(half3(-slope.x, 1.0, -slope.y));
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ndv = saturate(dot(normalWS, viewWS));
                half fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), 1.0 - _Smoothness, 1.0);

                Light light = GetMainLight();
                half3 halfDir = normalize(light.direction + viewWS);
                half spec = pow(saturate(dot(normalWS, halfDir)), 400.0) * 4.0;

                // Muddy body: thin water is a faint brown film, deeper water a darker brown.
                half4 body = lerp(_BaseColor, _DeepColor, depthMix);
                half3 lit = body.rgb * (light.color * 0.5 * saturate(light.direction.y) + SampleSH(half3(0, 1, 0)));
                // Inside the greenhouse the probe's open sky is too bright and blue for muddy water: damp it.
                reflection *= 0.5;
                half reflectWeight = saturate(fresnel * 0.65);
                half3 color = lerp(lit, reflection, reflectWeight) + light.color * spec;

                half alpha = saturate(body.a + fresnel * 0.3 + spec) * shore * smoothstep(0.0, 0.08, waterDepth) * smoothstep(0.0, 0.1, fill);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
