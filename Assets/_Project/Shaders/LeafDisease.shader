// URP Simple Lit with an illustrative leaf-disease overlay for the Tomato recovery demo.
// Only leaf pixels change: _DiseaseMask.r marks leaf texels and vertex colour R marks
// stems (0), so fruit, stems and soil keep their original colour. The comparison split
// shows _Compare* values on one side of an object-space plane (diseased vs current week).
Shader "BTP/Leaf Disease"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Clipping", Range(0.0, 1.0)) = 0.5
        _SpecColor("Specular Color", Color) = (0.2, 0.2, 0.2, 0.3)
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}

        [Header(Leaf Disease)]
        [NoScaleOffset] _DiseaseMask("Disease Mask (R leaf, G lesion field, B chlorosis field)", 2D) = "black" {}
        _Severity("Lesion Severity", Range(0, 1)) = 0
        _Chlorosis("Chlorosis", Range(0, 1)) = 0
        _CompareSeverity("Comparison Lesion Severity", Range(0, 1)) = 0.85
        _CompareChlorosis("Comparison Chlorosis", Range(0, 1)) = 0.7
        [Toggle] _SplitEnabled("Show Comparison Split", Float) = 0
        _SplitPlane("Split Plane (object space normal xyz, offset w)", Vector) = (1, 0, 0, 0)
        [Toggle] _UseVertexStemMask("Exclude Stems (vertex colour R)", Float) = 1
        _ChlorosisColor("Chlorosis Tint", Color) = (1.0, 0.86, 0.32, 1)
        _LesionColor("Lesion Color", Color) = (0.23, 0.15, 0.07, 1)
        _LesionRingColor("Lesion Ring Color", Color) = (0.12, 0.07, 0.03, 1)
        _HaloColor("Lesion Halo Tint", Color) = (1.0, 0.92, 0.35, 1)

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

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LeafDiseasePassVertex
            #pragma fragment LeafDiseasePassFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _SPECGLOSSMAP _SPECULAR_COLOR

            // Only what these scenes use: one real-time sun with hard shadows, fog, instancing
            // (single-pass stereo). No additional lights, lightmaps, SSAO, cookies or light
            // layers, which keeps the variant count, and build time, small.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

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
            #pragma multi_compile_instancing

            #include "LeafDiseaseInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
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
            #pragma multi_compile_instancing

            #include "LeafDiseaseInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
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
            #pragma multi_compile_instancing

            #include "LeafDiseaseInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
