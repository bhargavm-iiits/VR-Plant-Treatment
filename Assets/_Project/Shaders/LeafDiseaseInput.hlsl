#ifndef BTP_LEAF_DISEASE_INPUT_INCLUDED
#define BTP_LEAF_DISEASE_INPUT_INCLUDED

// Stand-in for URP's SimpleLitInput.hlsl: the same material inputs, plus the leaf disease
// parameters. Defining URP's include guard keeps its version out of the URP pass files.
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
    // Disease state (driven per week by TomatoRecoveryDemo).
    half _Severity;          // lesion severity, 0..1
    half _Chlorosis;         // leaf yellowing, 0..1
    half _CompareSeverity;   // values for the comparison half of the plant
    half _CompareChlorosis;
    half _SplitEnabled;      // 1 = object-space half-plane shows the comparison values
    float4 _SplitPlane;      // object-space plane: xyz normal, w offset
    half _UseVertexStemMask; // 1 = vertex colour R (0 = stem) excludes stems from the disease
    half4 _ChlorosisColor;
    half4 _LesionColor;
    half4 _LesionRingColor;
    half4 _HaloColor;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// R: leaf mask, G: lesion threshold field, B: chlorosis threshold field (baked by LeafMaskBaker).
TEXTURE2D(_DiseaseMask);        SAMPLER(sampler_DiseaseMask);
TEXTURE2D(_SpecGlossMap);       SAMPLER(sampler_SpecGlossMap);

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

// Applies chlorosis (yellowing), lesion halos and concentric "target" lesions to leaf pixels.
// leafWeight is 0 for fruit, stems and soil, which keep their original colour.
half3 ApplyLeafDisease(half3 albedo, half4 mask, half leafWeight, half severity, half chlorosis)
{
    half leaf = mask.r * leafWeight;

    half luminance = dot(albedo, half3(0.299h, 0.587h, 0.114h));
    half yellowing = saturate((chlorosis - mask.b) * 6.0h) * leaf;
    albedo = lerp(albedo, luminance * 1.35h * _ChlorosisColor.rgb, yellowing * 0.85h);

    half inside = severity - mask.g;                         // > 0 inside a lesion
    half lesion = saturate(inside * 40.0h) * leaf;
    half halo = saturate((inside + 0.05h) * 40.0h) * leaf * (1.0h - lesion);
    half ring = step(0.5h, frac(max(inside, 0.0h) * 14.0h));
    half3 lesionColor = lerp(_LesionColor.rgb, _LesionRingColor.rgb, ring);

    albedo = lerp(albedo, luminance * 1.2h * _HaloColor.rgb, halo * 0.75h);
    albedo = lerp(albedo, lesionColor, lesion);
    return albedo;
}

inline void InitializeLeafDiseaseSurfaceData(float2 uv, float3 positionOS, half stemMask, out SurfaceData outSurfaceData)
{
    outSurfaceData = (SurfaceData)0;

    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = albedoAlpha.a * _BaseColor.a;
    outSurfaceData.alpha = AlphaDiscard(outSurfaceData.alpha, _Cutoff);

    half3 albedo = albedoAlpha.rgb * _BaseColor.rgb;

    half compareSide = (_SplitEnabled > 0.5h && dot(positionOS, _SplitPlane.xyz) < _SplitPlane.w) ? 1.0h : 0.0h;
    half severity = lerp(_Severity, _CompareSeverity, compareSide);
    half chlorosis = lerp(_Chlorosis, _CompareChlorosis, compareSide);
    half leafWeight = lerp(1.0h, stemMask, _UseVertexStemMask);
    half4 mask = SAMPLE_TEXTURE2D(_DiseaseMask, sampler_DiseaseMask, uv);
    albedo = ApplyLeafDisease(albedo, mask, leafWeight, severity, chlorosis);

    outSurfaceData.albedo = AlphaModulate(albedo, outSurfaceData.alpha);

    half4 specularSmoothness = SampleSpecularSmoothness(uv, outSurfaceData.alpha, _SpecColor, TEXTURE2D_ARGS(_SpecGlossMap, sampler_SpecGlossMap));
    outSurfaceData.metallic = 0.0;
    outSurfaceData.specular = specularSmoothness.rgb;
    outSurfaceData.smoothness = specularSmoothness.a;
    outSurfaceData.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap));
    outSurfaceData.occlusion = 1.0;
    outSurfaceData.emission = SampleEmission(uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap));
}

#endif
