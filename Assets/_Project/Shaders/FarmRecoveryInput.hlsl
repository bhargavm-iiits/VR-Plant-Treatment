#ifndef BTP_FARM_RECOVERY_INPUT_INCLUDED
#define BTP_FARM_RECOVERY_INPUT_INCLUDED

// Material inputs for BTP/Farm Recovery: URP Simple Lit inputs plus a healthy/diseased albedo pair.
// It stands in for LeafDiseaseInput.hlsl so the shared LeafDiseaseForwardPass.hlsl can be reused
// unchanged (that file includes LeafDiseaseInput.hlsl, which this guard skips).
#define BTP_LEAF_DISEASE_INPUT_INCLUDED
#define UNIVERSAL_SIMPLE_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
#include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/SurfaceType.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    half4 _BaseColor;
    half4 _SpecColor;
    half4 _EmissionColor;
    half _Cutoff;
    half _AlphaClip;       // 1 for alpha-clipped leaf cards (read by the editor tools; the keyword does the clipping)
    half _DiseaseAmount;   // 1 = fully diseased albedo, 0 = fully healthy albedo
    half _MaskScale;       // tiling of _RecoveryMask (1 for meshes, >1 for billboard atlases)
    // Leaf wind (_LEAF_WIND, LeafWind.hlsl). Declared in every variant so the SRP Batcher sees one layout.
    float _WindStrength;   // 0 = still, 1 = the Blender animation, 2 = twice the angles
    float _WindSpeed;      // playback rate of the loop
    float _WindLoopSeconds; // length of the baked loop (8 s for the cherry and the tomatoes)
    float4 _WindTex_TexelSize; // set by Unity; z = width = keys + 1, or 2 x keys + 1 with travel keys
    float _WindTravel;     // 1 = the texture also holds each part's travel (LeafWind.hlsl)
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

TEXTURE2D(_DiseasedMap);    SAMPLER(sampler_DiseasedMap);
// R: per-texel recovery threshold (percentile of the distance to a lesion seed). Texels with a low value
// stay diseased longest, so lesions shrink back towards their seeds as _DiseaseAmount falls.
TEXTURE2D(_RecoveryMask);   SAMPLER(sampler_RecoveryMask);
TEXTURE2D(_SpecGlossMap);   SAMPLER(sampler_SpecGlossMap);

half4 SampleSpecularSmoothness(float2 uv, half alpha, half4 specColor, TEXTURE2D_PARAM(specMap, sampler_specMap))
{
    half4 specularSmoothness = half4(0, 0, 0, 1);
#ifdef _SPECGLOSSMAP
    specularSmoothness = SAMPLE_TEXTURE2D(specMap, sampler_specMap, uv) * specColor;
#elif defined(_SPECULAR_COLOR)
    specularSmoothness = specColor;
#endif

#ifdef _GLOSSINESS_FROM_BASE_ALPHA
    specularSmoothness.a = alpha;
#endif

    return specularSmoothness;
}

// Share of the texel that shows the diseased albedo for a given disease amount.
half DiseasedShare(float2 uv)
{
    half threshold = SAMPLE_TEXTURE2D(_RecoveryMask, sampler_RecoveryMask, uv * _MaskScale).r;
    return saturate((_DiseaseAmount * 1.08h - threshold) / 0.08h);
}

// Same signature as the Leaf Disease input, so the shared forward pass calls it.
inline void InitializeLeafDiseaseSurfaceData(float2 uv, float3 positionOS, half stemMask, out SurfaceData outSurfaceData)
{
    outSurfaceData = (SurfaceData)0;

    half4 healthy = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = healthy.a * _BaseColor.a;
    outSurfaceData.alpha = AlphaDiscard(outSurfaceData.alpha, _Cutoff);

    half3 diseased = SAMPLE_TEXTURE2D(_DiseasedMap, sampler_DiseasedMap, uv).rgb;
    half3 albedo = lerp(healthy.rgb, diseased, DiseasedShare(uv)) * _BaseColor.rgb;
    outSurfaceData.albedo = AlphaModulate(albedo, outSurfaceData.alpha);

    half4 specularSmoothness = SampleSpecularSmoothness(uv, outSurfaceData.alpha, _SpecColor, TEXTURE2D_ARGS(_SpecGlossMap, sampler_SpecGlossMap));
    outSurfaceData.metallic = 0.0;
    outSurfaceData.specular = specularSmoothness.rgb;
    outSurfaceData.smoothness = specularSmoothness.a;
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap));
    outSurfaceData.occlusion = 1.0;
    outSurfaceData.emission = 0;
}

#endif
