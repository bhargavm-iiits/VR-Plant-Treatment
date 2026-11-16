// URP Simple Lit for Farm plants and their billboards, with a healthy/diseased albedo pair for the Farm
// six-week timeline. _DiseaseAmount = 1 shows the diseased albedo (Week 1), 0 the healthy one (Week 6).
// In between, _RecoveryMask (a per-texel threshold) decides which texels have recovered, so lesions
// shrink back to their seeds instead of crossfading. Both albedos differ only on leaf pixels, so fruit,
// stems and soil never change. Illustrative only. The forward pass is shared with BTP/Leaf Disease.
// Optional _LEAF_WIND (vertex stage, all passes): per-leaf baked wind from _WindTex, see LeafWind.hlsl.
Shader "BTP/Farm Recovery"
{
    Properties
    {
        [MainTexture] _BaseMap("Healthy Albedo", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _DiseasedMap("Diseased Albedo", 2D) = "white" {}
        [NoScaleOffset] _RecoveryMask("Recovery Threshold (R)", 2D) = "black" {}
        _MaskScale("Recovery Mask Tiling", Float) = 1
        _DiseaseAmount("Disease Amount", Range(0, 1)) = 1
        _Cutoff("Alpha Clipping", Range(0.0, 1.0)) = 0.5
        [ToggleUI] _AlphaClip("Alpha Clip", Float) = 0
        [ToggleUI] _AlphaToMask("Alpha To Coverage", Float) = 0
        _SpecColor("Specular Color", Color) = (0.2, 0.2, 0.2, 0.3)
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}

        // Opt-in per-leaf wind (cherry tree, tomato plants): baked rotation keys per leaf, indexed by UV1. See LeafWind.hlsl.
        [Toggle(_LEAF_WIND)] _LeafWind("Leaf Wind", Float) = 0
        [NoScaleOffset] _WindTex("Wind Keys", 2D) = "black" {}
        _WindStrength("Wind Strength", Range(0, 2)) = 1
        _WindSpeed("Wind Speed", Range(0, 3)) = 1
        _WindLoopSeconds("Wind Loop Seconds", Float) = 8
        [ToggleUI] _WindTravel("Wind Travel Keys", Float) = 0

        [HideInInspector] _Cull("__cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LeafDiseasePassVertex
            #pragma fragment LeafDiseasePassFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_vertex _LEAF_WIND

            // One real-time sun with shadows, fog and instancing (single-pass stereo), as in BTP/Leaf Disease.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "FarmRecoveryInput.hlsl"
            #include "LeafDiseaseForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _LEAF_WIND
            #pragma multi_compile_instancing

            #include "FarmRecoveryInput.hlsl"
            #define BTP_PASS_SHADOW_CASTER
            #include "FarmRecoveryWindPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _LEAF_WIND
            #pragma multi_compile_instancing

            #include "FarmRecoveryInput.hlsl"
            #define BTP_PASS_DEPTH_ONLY
            #include "FarmRecoveryWindPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_vertex _LEAF_WIND
            #pragma multi_compile_instancing

            #include "FarmRecoveryInput.hlsl"
            #define BTP_PASS_DEPTH_NORMALS
            #include "FarmRecoveryWindPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
