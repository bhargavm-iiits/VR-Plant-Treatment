#ifndef BTP_BERRY_IMPOSTOR_PASSES_INCLUDED
#define BTP_BERRY_IMPOSTOR_PASSES_INCLUDED

// BTP/Berry Impostor: every berry is one quad (2 triangles) that the vertex stage turns to face the viewer
// (the eye, or the light in the shadow pass) and stretches to the outline of the berry, a spheroid. The
// fragment stage clips the quad to that ellipse and rebuilds the berry's surface position and normal, so each
// berry is exactly round or oval at any distance, with smooth lighting, specular and received shadows.
//
// Mesh layout (BerryImpostors.cs): POSITION = berry centre, NORMAL = long axis (unit), TEXCOORD0 =
// (corner x, corner y, equatorial radius, polar half-length), corners at (+-1, +-1). Object space.
// Select the pass before including: BTP_BERRY_FORWARD, BTP_BERRY_SHADOW, BTP_BERRY_DEPTH or BTP_BERRY_DEPTH_NORMALS.

// The quad extends past the outline, so the anti-aliased (alpha to coverage) edge has room.
#define BERRY_QUAD_MARGIN 1.15

#if defined(BTP_BERRY_FORWARD)
    // Simple Lit Blinn-Phong with a constant specular colour: glossy berries.
    #define _SPECULAR_COLOR 1
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#elif defined(BTP_BERRY_SHADOW)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
    // Set by ShadowUtils.SetupShadowCasterConstantBuffer (see URP's ShadowCasterPass.hlsl).
    float3 _LightDirection;
    float3 _LightPosition;
#else
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half4 _SpecColor;
    half _Smoothness;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;   // berry centre
    float3 normalOS   : NORMAL;     // berry long axis
    float4 texcoord   : TEXCOORD0;  // corner xy, equatorial radius, polar half-length
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;   // position on the outline ellipse: the berry is the unit disc
#if defined(BTP_BERRY_FORWARD) || defined(BTP_BERRY_DEPTH_NORMALS)
    float3 centreWS   : TEXCOORD1;
    float3 axisU      : TEXCOORD2;   // outline semi-axes in world space (scaled by their length)
    float3 axisV      : TEXCOORD3;
    float3 axisW      : TEXCOORD4;   // towards the viewer, equatorial radius
#endif
#if defined(BTP_BERRY_FORWARD)
    half fogFactor    : TEXCOORD5;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct BerryFrame
{
    float3 centreWS;
    float3 u;  // outline semi-axis along the projected long axis
    float3 v;  // outline semi-axis across it (equatorial radius)
    float3 w;  // towards the viewer (equatorial radius)
};

// The berry's outline as seen along towardsViewer (unit): a spheroid with equatorial radius R and polar half-length
// L seen at angle theta to its axis shows an ellipse with semi-axes R and sqrt(L^2 sin^2 + R^2 cos^2).
BerryFrame BuildBerryFrame(Attributes input, float3 centreWS, float3 towardsViewer)
{
    float3x3 objectToWorld = (float3x3)GetObjectToWorldMatrix();
    float scale = length(float3(objectToWorld._m00, objectToWorld._m10, objectToWorld._m20));
    float radius = input.texcoord.z * scale;
    float halfLength = input.texcoord.w * scale;
    float3 axis = TransformObjectToWorldDir(input.normalOS);

    float c = dot(axis, towardsViewer);
    float3 projected = axis - towardsViewer * c;
    float projectedLength = length(projected);
    float3 u;
    if (projectedLength > 1e-3)
    {
        u = projected / projectedLength;
    }
    else
    {
        // Looking down the axis: the outline is a circle, any in-plane direction will do.
        float3 helper = abs(towardsViewer.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
        u = normalize(cross(helper, towardsViewer));
    }

    float3 v = cross(towardsViewer, u);
    float outlineLength = sqrt(halfLength * halfLength * saturate(1.0 - c * c) + radius * radius * c * c);

    BerryFrame frame;
    frame.centreWS = centreWS;
    frame.u = u * outlineLength;
    frame.v = v * radius;
    frame.w = towardsViewer * radius;
    return frame;
}

float3 CornerPositionWS(BerryFrame frame, float2 corner)
{
    return frame.centreWS + (frame.u * corner.x + frame.v * corner.y) * BERRY_QUAD_MARGIN;
}

// Unit disc coordinates of the fragment, its depth on the berry (0 at the outline, 1 facing the viewer).
float BerryDepth(float2 uv)
{
    return sqrt(saturate(1.0 - dot(uv, uv)));
}

// Gradient of the ellipsoid with semi-axes |u|, |v|, |w| at the fragment: the surface normal.
float3 BerryNormalWS(float2 uv, float z, float3 u, float3 v, float3 w)
{
    return normalize(u * (uv.x / dot(u, u)) + v * (uv.y / dot(v, v)) + w * (z / dot(w, w)));
}

void ClipToBerry(float2 uv)
{
    clip(1.0 - dot(uv, uv));
}

Varyings BerryVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 centreWS = TransformObjectToWorld(input.positionOS.xyz);
#if defined(BTP_BERRY_SHADOW)
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 towardsViewer = normalize(_LightPosition - centreWS);
    #else
        float3 towardsViewer = _LightDirection;
    #endif
#else
    // Perspective: towards this eye's position; orthographic: against the view direction.
    float3 towardsViewer = GetWorldSpaceNormalizeViewDir(centreWS);
#endif

    BerryFrame frame = BuildBerryFrame(input, centreWS, towardsViewer);
    float2 corner = input.texcoord.xy;
    float3 positionWS = CornerPositionWS(frame, corner);
    output.uv = corner * BERRY_QUAD_MARGIN;

#if defined(BTP_BERRY_SHADOW)
    // The quad faces the light, so the normal bias is zero and the depth bias does the work.
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, towardsViewer, towardsViewer));
    output.positionCS = ApplyShadowClamping(positionCS);
#else
    output.positionCS = TransformWorldToHClip(positionWS);
#endif

#if defined(BTP_BERRY_FORWARD) || defined(BTP_BERRY_DEPTH_NORMALS)
    output.centreWS = frame.centreWS;
    output.axisU = frame.u;
    output.axisV = frame.v;
    output.axisW = frame.w;
#endif
#if defined(BTP_BERRY_FORWARD)
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
#endif
    return output;
}

#if defined(BTP_BERRY_FORWARD)

half4 BerryForwardFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    // Anti-aliased outline: coverage from the distance to the edge in pixels (alpha to coverage with MSAA).
    float r = length(input.uv);
    float edge = max(fwidth(r), 1e-4);
    half coverage = (half)saturate((1.0 - r) / edge + 0.5);
    clip(coverage - 0.01);

    float z = BerryDepth(input.uv);
    float3 normalWS = BerryNormalWS(input.uv, z, input.axisU, input.axisV, input.axisW);

#if defined(_BERRY_UNLIT)
    // Billboard bakes render unlit: the albedo with a little roundness.
    return half4(_BaseColor.rgb * (0.78h + 0.22h * (half)z), coverage);
#else
    float3 positionWS = input.centreWS + input.axisU * input.uv.x + input.axisV * input.uv.y + input.axisW * z;

    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    #if defined(DEBUG_DISPLAY)
        inputData.positionCS = input.positionCS;
    #endif
    inputData.normalWS = (half3)normalWS;
    inputData.viewDirectionWS = (half3)GetWorldSpaceNormalizeViewDir(positionWS);
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        // On the berry's surface, not the quad: the sunlit side is never shadowed by the berry itself.
        inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif
    inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = half3(0, 0, 0);
    inputData.bakedGI = SampleSH(inputData.normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = _BaseColor.rgb;
    surfaceData.alpha = 1.0h;
    surfaceData.specular = _SpecColor.rgb;
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.occlusion = 1.0h;

    half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = coverage;
    return color;
#endif
}

#elif defined(BTP_BERRY_SHADOW)

half4 BerryShadowFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    ClipToBerry(input.uv);
    return 0;
}

#elif defined(BTP_BERRY_DEPTH)

half BerryDepthFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ClipToBerry(input.uv);
    return input.positionCS.z;
}

#elif defined(BTP_BERRY_DEPTH_NORMALS)

half4 BerryDepthNormalsFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ClipToBerry(input.uv);

    float3 normalWS = BerryNormalWS(input.uv, BerryDepth(input.uv), input.axisU, input.axisV, input.axisW);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
        float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
        return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
    #else
        return half4(normalWS, 0.0);
    #endif
}

#endif

#endif
