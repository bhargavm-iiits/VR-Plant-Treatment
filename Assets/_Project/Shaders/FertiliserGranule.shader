// Fertiliser granules for the guided treatment (GranuleRenderer): every granule on screen comes from one
// Graphics.RenderMeshPrimitives call per LOD with a structured buffer of instances (position, radius, rotation,
// colour + smoothness, shape variant, speckle, ground height). The mesh (GranuleMeshes) carries 8 granule shapes in
// its texture coordinates; the vertex stage picks the instance's shape, sizes, rotates and places it. The fragment
// stage adds a speckled, slightly waxy coating (procedural mottling + a CC0 micro-surface normal/roughness map from
// ambientCG, triplanar in granule space) and darkens the side that touches the soil or the heap below.
// URP Lit (PBR) lighting: main light with shadows, additional lights (forward and Forward+), SH, fog, single-pass
// instanced / multiview stereo. Needs structured buffers in the vertex stage (Vulkan, D3D11+, Metal, GLES 3.1+).
Shader "BTP/Fertiliser Granule"
{
    Properties
    {
        [NoScaleOffset] _DetailNormal("Micro surface normal (GL)", 2D) = "bump" {}
        [NoScaleOffset] _DetailRoughness("Micro surface roughness", 2D) = "grey" {}
        _DetailScale("Micro surface tiling (per granule radius)", Float) = 0.45
        _DetailStrength("Micro surface normal strength", Range(0, 2)) = 0.55
        _SpeckleScale("Speckle scale", Float) = 4.0
        _ContactDarkening("Contact darkening", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #pragma target 4.5
        #pragma exclude_renderers gles
        #pragma multi_compile_instancing
        #pragma instancing_options procedural:GranuleProceduralSetup
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GranuleVertex
            #pragma fragment GranuleForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define BTP_GRANULE_FORWARD
            #include "FertiliserGranulePasses.hlsl"
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
            #pragma vertex GranuleVertex
            #pragma fragment GranuleDepthFragment
            #define BTP_GRANULE_DEPTH
            #include "FertiliserGranulePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex GranuleVertex
            #pragma fragment GranuleDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #define BTP_GRANULE_DEPTH_NORMALS
            #include "FertiliserGranulePasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
