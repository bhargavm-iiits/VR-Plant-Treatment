// Round, smooth fruit for VR at 2 triangles each: one camera-facing quad per berry, clipped to the berry's
// spheroid outline, with the surface position and normal rebuilt per pixel (see BerryImpostorPasses.hlsl).
// Simple Lit lighting (Blinn-Phong, constant specular), main-light shadows cast and received, fog,
// single-pass instanced stereo and the SRP Batcher. _BERRY_UNLIT is for the billboard bakes only.
Shader "BTP/Berry Impostor"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.66, 0.8, 0.34, 1)
        _SpecColor("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.6
        [Toggle(_BERRY_UNLIT)] _BerryUnlit("Unlit (billboard bake)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            AlphaToMask On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BerryVertex
            #pragma fragment BerryForwardFragment

            #pragma shader_feature_local_fragment _BERRY_UNLIT

            // One real-time sun with shadows, fog and instancing (single-pass stereo), as in BTP/Farm Recovery.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #define BTP_BERRY_FORWARD
            #include "BerryImpostorPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BerryVertex
            #pragma fragment BerryShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #define BTP_BERRY_SHADOW
            #include "BerryImpostorPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BerryVertex
            #pragma fragment BerryDepthFragment
            #pragma multi_compile_instancing

            #define BTP_BERRY_DEPTH
            #include "BerryImpostorPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex BerryVertex
            #pragma fragment BerryDepthNormalsFragment
            #pragma multi_compile_instancing

            #define BTP_BERRY_DEPTH_NORMALS
            #include "BerryImpostorPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
