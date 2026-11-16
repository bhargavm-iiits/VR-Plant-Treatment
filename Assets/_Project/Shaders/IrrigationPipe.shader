// Farm irrigation hardware (FarmIrrigationBuilder): matte, weathered plastic for PVC mains, black LDPE drip
// laterals, the poly tank and the fittings. Opaque, simple-lit (sun with shadows, SH ambient, low Blinn-Phong),
// with dust settling towards the ground and on undersides (_DustHeight above world y = 0) and a little
// procedural mottling. No flow effect: real pipes are opaque, so the flow shows through the gauge, valve,
// drips, wet soil and pools instead. All passes share one UnityPerMaterial CBUFFER (SRP Batcher).
Shader "BTP/Irrigation Pipe"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.62, 0.63, 0.62, 1)
        _DustColor("Dust Color", Color) = (0.48, 0.40, 0.31, 1)
        _DustHeight("Dust Height (m above ground)", Float) = 0.12
        _DustAmount("Dust Amount", Range(0, 1)) = 0.6
        _Smoothness("Smoothness", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _DustColor;
            float _DustHeight;
            half _DustAmount;
            half _Smoothness;
        CBUFFER_END

        float IrrigationHash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float IrrigationNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(IrrigationHash(i), IrrigationHash(i + float3(1, 0, 0)), f.x),
                             lerp(IrrigationHash(i + float3(0, 1, 0)), IrrigationHash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(IrrigationHash(i + float3(0, 0, 1)), IrrigationHash(i + float3(1, 0, 1)), f.x),
                             lerp(IrrigationHash(i + float3(0, 1, 1)), IrrigationHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * light.distanceAttenuation;
                half ndl = saturate(dot(normalWS, light.direction));
                half3 halfDir = normalize(light.direction + viewWS);
                half specPower = exp2(10.0 * _Smoothness + 1.0);
                half spec = pow(saturate(dot(normalWS, halfDir)), specPower) * _Smoothness;

                // Weathering: dust towards the ground and on the underside, mottled; dusty areas are matte.
                float mottle = IrrigationNoise(input.positionWS * 9.0) * 0.6 + IrrigationNoise(input.positionWS * 31.0) * 0.4;
                half ground = saturate(1.0 - input.positionWS.y / max(_DustHeight, 0.001));
                half under = saturate(-normalWS.y);
                half dust = saturate((ground * 0.8 + under * 0.5) * (0.55 + 0.9 * mottle) - 0.15) * _DustAmount;
                half3 albedo = lerp(_BaseColor.rgb * (0.92 + 0.16 * mottle), _DustColor.rgb, dust);
                spec *= 1.0 - dust;

                half3 color = albedo * (light.color * ndl * shadow + SampleSH(normalWS));
                color += light.color * spec * shadow * 0.5;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings NormalsVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
