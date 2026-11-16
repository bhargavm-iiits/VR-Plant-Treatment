// The disease texture is the baseline; the green endpoint is an illustrative visualization.
Shader "BTP/Leaf Recovery"
{
    Properties
    {
        [MainTexture] _BaseMap("Supplied disease texture", 2D) = "white" {}
        _HealthyMap("Reconstructed healthy foliage", 2D) = "white" {}
        _RecoveryMap("Damage and local recovery delays", 2D) = "black" {}
        [Normal] _BumpMap("Leaf veins and surface normal", 2D) = "bump" {}
        _RoughnessMap("Surface roughness", 2D) = "gray" {}
        _BumpScale("Vein relief", Range(0,2)) = 0.65
        _Transmission("Thin leaf light transmission", Range(0,1)) = 0.12
        _Recovery("Recovery", Range(0,1)) = 0
        _BaselineRecovery("Baseline recovery (whole leaf at Week 6)", Range(0,1)) = 0
        _RecoveryFrom("Recovery at transition start", Range(0,1)) = 0
        _BaselineFrom("Baseline recovery at transition start", Range(0,1)) = 0
        _TransitionParams("Transition (progress, glow, 1/sweep extent, unused)", Vector) = (1,0,1,0)
        _SplitEnabled("Compare baseline / current", Float) = 1
        _SplitPlane("Object-space split plane", Vector) = (1,0,0,0)
        // Manual treatment mode (Treatment lab). All default to 0, which leaves the leaf exactly as above.
        _SpotFade("Treatment: lesions dry, lighten and shrink (cores remain)", Range(0,1)) = 0
        _YellowFade("Treatment: yellowing fades", Range(0,1)) = 0
        _GreenGain("Treatment: green tissue returns", Range(0,1)) = 0
        _VeinGain("Treatment: veins become prominent", Range(0,1)) = 0
        _Worsen("Treatment: worsening after a poor treatment", Range(0,1)) = 0
        _TreatGlow("Treatment: glow along veins while recovering", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_HealthyMap); SAMPLER(sampler_HealthyMap);
            TEXTURE2D(_RecoveryMap); SAMPLER(sampler_RecoveryMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _BumpScale;
                half _Transmission;
                half _Recovery;
                half _BaselineRecovery;
                half _RecoveryFrom;
                half _BaselineFrom;
                float4 _TransitionParams;
                half _SplitEnabled;
                float4 _SplitPlane;
                half _SpotFade;
                half _YellowFade;
                half _GreenGain;
                half _VeinGain;
                half _Worsen;
                half _TreatGlow;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                half fog : TEXCOORD4;
                float4 tangentWS : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 diseased = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 healthy = SAMPLE_TEXTURE2D(_HealthyMap, sampler_HealthyMap, input.uv).rgb;
                half3 recoveryField = SAMPLE_TEXTURE2D(_RecoveryMap, sampler_RecoveryMap, input.uv).rgb;
                float planeDistance = dot(input.positionOS, _SplitPlane.xyz) - _SplitPlane.w;
                half isBaseline = (_SplitEnabled > 0.5h && planeDistance < 0) ? 1 : 0;
                half amountTo = isBaseline > 0.5h ? _BaselineRecovery : _Recovery;
                half amountFrom = isBaseline > 0.5h ? _BaselineFrom : _RecoveryFrom;
                // Healing front: during a week change each texel moves from the old to the new amount
                // in turn, sweeping out from the divider (close to the midrib) to the leaf edge, broken up
                // by the per-patch delay (G). At progress 0 and 1 every texel is exactly at an endpoint,
                // so settled weeks look identical to a uniform amount.
                const half frontWidth = 0.3h;
                half sweepOrder = saturate(saturate(abs(planeDistance) * _TransitionParams.z) * 0.78h + recoveryField.g * 0.22h);
                half front = saturate(((half)_TransitionParams.x * (1.0h + frontWidth) - sweepOrder) / frontWidth);
                front = front * front * (3.0h - 2.0h * front);
                half amount = lerp(amountFrom, amountTo, front);
                // Soft glow on the moving band only, strongest on lesions; zero once settled (glow = 0).
                half glowBand = front * (1.0h - front) * 4.0h * saturate(abs(amountTo - amountFrom) * 8.0h);
                half3 healingGlow = half3(0.30h, 0.95h, 0.62h) * (glowBand * (half)_TransitionParams.y * (0.35h + 0.65h * recoveryField.r) * 0.45h);
                // Spots fade locally, rather than recolouring the whole leaf at once.
                // Heavily affected regions start a little later and finish last; the endpoints stay exact.
                // The delay (G) only staggers the start, so each week's change stays visible: using it
                // directly as the start threshold kept most lesions unchanged until Weeks 4-5.
                half healStart = recoveryField.g * 0.15h;
                half healEnd = min(0.70h + recoveryField.g * 0.25h, 1.0h);
                half localHealing = saturate((amount - healStart) / (healEnd - healStart));
                half pigmentRecovery = saturate(amount);
                half blend = lerp(pigmentRecovery, localHealing, recoveryField.r);
                half3 albedo = lerp(diseased, healthy, blend);
                half4 bump = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                // Manual treatment on the same mesh and texture set. Every term is scaled by its amount, so all
                // amounts at 0 reproduce the leaf exactly. Lesion cores (high damage) only dry and lighten: the
                // spot fade never removes them, matching real tissue that does not regenerate.
                half damage = recoveryField.r;
                half3 original = albedo;
                half luma = dot(original, half3(0.299h, 0.587h, 0.114h));
                // Necrotic (dark brown) lesion tissue and chlorotic (yellow) tissue, judged from the leaf's own colour.
                half necrotic = saturate((0.42h - luma) * 5.0h) * saturate(damage * 4.0h);
                half yellowMask = saturate(((original.r + original.g) * 0.5h - original.b) * 3.0h) * saturate(1.0h - abs(original.g - original.r) * 4.0h)
                    * saturate((luma - 0.3h) * 4.0h);
                half veinMask = saturate(length(UnpackNormalScale(bump, 1.0h).xy) * 2.5h - 0.15h);
                // Spot edges (lower damage) recover first, so spots shrink; cores only dry and lighten and never vanish.
                half edgeReach = _SpotFade * 0.55h;
                half shrink = (1.0h - smoothstep(edgeReach - 0.12h, edgeReach + 0.04h, damage)) * necrotic * saturate(_SpotFade * 20.0h);
                albedo = lerp(albedo, healthy, shrink * 0.7h);
                half3 dried = lerp(albedo, half3(0.55h, 0.47h, 0.33h) * (0.55h + luma), 0.6h);
                albedo = lerp(albedo, dried, necrotic * _SpotFade * 0.6h);
                // Yellowing fades back towards green.
                albedo = lerp(albedo, healthy, yellowMask * (1.0h - necrotic) * _YellowFade * 0.9h);
                // Remaining green tissue regains colour and its veins become prominent.
                half3 greener = lerp(albedo, healthy, 0.65h) * half3(0.96h, 1.06h, 0.94h);
                albedo = lerp(albedo, greener, (1.0h - necrotic) * _GreenGain);
                albedo = lerp(albedo, albedo * half3(1.08h, 1.18h, 1.02h) + half3(0.0h, 0.025h, 0.0h), veinMask * (1.0h - necrotic) * _VeinGain * 0.6h);
                // Worsening: lesions darken and spread a little; the rest of the leaf dulls and yellows.
                half halo = saturate(damage * 3.0h);
                albedo = lerp(albedo, albedo * half3(0.62h, 0.5h, 0.35h), halo * _Worsen * 0.6h);
                albedo = lerp(albedo, half3(0.75h, 0.68h, 0.25h) * (0.5h + luma), (1.0h - halo) * _Worsen * 0.25h);
                half lesion = necrotic;
                // Lesion relief fades too, so healed areas do not keep the dark crater shading.
                half relief = _BumpScale * lerp(1.0h, 0.25h, blend * recoveryField.r) * (1.0h + _VeinGain * 0.8h * (1.0h - lesion));
                half3 normalTS = UnpackNormalScale(bump, relief);
                half3 geometricNormal = normalize(input.normalWS);
                half3 tangent = normalize(input.tangentWS.xyz);
                half3 bitangent = cross(geometricNormal, tangent) * input.tangentWS.w;
                half3 normal = normalize(mul(normalTS, half3x3(tangent, bitangent, geometricNormal))) * IS_FRONT_VFACE(face, 1, -1);
                InputData data = (InputData)0;
                data.positionWS = input.positionWS;
                data.normalWS = normal;
                data.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                data.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                data.bakedGI = SampleSH(normal);
                data.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                data.shadowMask = half4(1,1,1,1);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.occlusion = 1;
                surface.normalTS = normalTS;
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, input.uv).r;
                surface.smoothness = lerp(0.50h, 0.18h, roughness);
                half4 lit = UniversalFragmentPBR(data, surface);
                Light light = GetMainLight(data.shadowCoord);
                half transmitted = pow(saturate(dot(-normal, light.direction)), 1.5h) * _Transmission;
                lit.rgb += albedo * light.color * transmitted * light.distanceAttenuation;
                lit.rgb += healingGlow;
                lit.rgb += half3(0.30h, 0.95h, 0.62h) * (_TreatGlow * (0.25h + 0.75h * veinMask) * (1.0h - lesion) * 0.22h);
                return half4(MixFog(lit.rgb, input.fog), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Simple Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Simple Lit/DepthOnly"
    }
}
