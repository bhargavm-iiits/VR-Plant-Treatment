#ifndef BTP_FERTILISER_GRANULE_PASSES_INCLUDED
#define BTP_FERTILISER_GRANULE_PASSES_INCLUDED

// BTP/Fertiliser Granule passes (see FertiliserGranule.shader and GranuleRenderer.cs / GranuleMeshes.cs).
// Mesh: POSITION = unit direction; TEXCOORD0/1 = radius of variants 0-3 / 4-7; TEXCOORD2..7 = the 8 variants'
// normals, flattened (variant k at floats 3k..3k+2). Instance buffer: GranuleInstance below (64 bytes).

#if defined(BTP_GRANULE_FORWARD)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#else
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

struct GranuleInstance
{
    float4 positionRadius; // world centre, radius (m)
    float4 rotation;       // quaternion xyzw
    float4 colour;         // linear albedo, smoothness
    float4 data;           // variant, speckle, speckle seed, ground height (-1e4: none)
};

StructuredBuffer<GranuleInstance> _Granules;

TEXTURE2D(_DetailNormal);
SAMPLER(sampler_DetailNormal);
TEXTURE2D(_DetailRoughness);
SAMPLER(sampler_DetailRoughness);

CBUFFER_START(UnityPerMaterial)
    float _DetailScale;
    float _DetailStrength;
    float _SpeckleScale;
    float _ContactDarkening;
CBUFFER_END

// Procedural instancing: the instance data comes from _Granules, nothing to set up per instance.
void GranuleProceduralSetup() {}

struct Attributes
{
    float4 positionOS : POSITION;
    float4 radiusA    : TEXCOORD0;
    float4 radiusB    : TEXCOORD1;
    float4 normal0    : TEXCOORD2;
    float4 normal1    : TEXCOORD3;
    float4 normal2    : TEXCOORD4;
    float4 normal3    : TEXCOORD5;
    float4 normal4    : TEXCOORD6;
    float4 normal5    : TEXCOORD7;
#if UNITY_ANY_INSTANCING_ENABLED
    UNITY_VERTEX_INPUT_INSTANCE_ID
#else
    uint instanceID   : SV_InstanceID;
#endif
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS   : TEXCOORD0;
#if defined(BTP_GRANULE_FORWARD)
    float3 positionWS : TEXCOORD1;
    float3 objectPos  : TEXCOORD2;  // granule space, radius ~1
    float3 objectNormal : TEXCOORD3;
    nointerpolation float4 rotation : TEXCOORD4;
    nointerpolation float4 colour   : TEXCOORD5;
    nointerpolation float4 data     : TEXCOORD6; // speckle, seed, ground height, radius
    half fogFactor    : TEXCOORD7;
#endif
    UNITY_VERTEX_OUTPUT_STEREO
};

float3 QRotate(float4 q, float3 v)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

float Pick4(float4 v, uint i)
{
    return i == 0u ? v.x : (i == 1u ? v.y : (i == 2u ? v.z : v.w));
}

float PickFlat(Attributes a, uint i)
{
    uint c = i >> 2;
    float4 v = c == 0u ? a.normal0 : (c == 1u ? a.normal1 : (c == 2u ? a.normal2 : (c == 3u ? a.normal3 : (c == 4u ? a.normal4 : a.normal5))));
    return Pick4(v, i & 3u);
}

uint GranuleIndex(Attributes input)
{
#if UNITY_ANY_INSTANCING_ENABLED
    return unity_InstanceID;
#else
    return input.instanceID;
#endif
}

Varyings GranuleVertex(Attributes input)
{
    Varyings output = (Varyings)0;
#if UNITY_ANY_INSTANCING_ENABLED
    UNITY_SETUP_INSTANCE_ID(input);
#endif
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    GranuleInstance g = _Granules[GranuleIndex(input)];
    uint variant = min((uint)(g.data.x + 0.5), 7u);
    float radius = variant < 4u ? Pick4(input.radiusA, variant) : Pick4(input.radiusB, variant - 4u);
    uint b = variant * 3u;
    float3 normalOS = normalize(float3(PickFlat(input, b), PickFlat(input, b + 1u), PickFlat(input, b + 2u)));
    float3 positionOS = input.positionOS.xyz * radius;

    float3 positionWS = g.positionRadius.xyz + QRotate(g.rotation, positionOS) * g.positionRadius.w;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.normalWS = QRotate(g.rotation, normalOS);
#if defined(BTP_GRANULE_FORWARD)
    output.positionWS = positionWS;
    output.objectPos = positionOS;
    output.objectNormal = normalOS;
    output.rotation = g.rotation;
    output.colour = g.colour;
    output.data = float4(g.data.y, g.data.z, g.data.w, g.positionRadius.w);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
#endif
    return output;
}

#if defined(BTP_GRANULE_FORWARD)

float GranuleHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float GranuleNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(lerp(GranuleHash(i), GranuleHash(i + float3(1, 0, 0)), f.x), lerp(GranuleHash(i + float3(0, 1, 0)), GranuleHash(i + float3(1, 1, 0)), f.x), f.y);
    float b = lerp(lerp(GranuleHash(i + float3(0, 0, 1)), GranuleHash(i + float3(1, 0, 1)), f.x), lerp(GranuleHash(i + float3(0, 1, 1)), GranuleHash(i + float3(1, 1, 1)), f.x), f.y);
    return lerp(a, b, f.z);
}

half4 GranuleForwardFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float speckle = input.data.x;
    float seed = input.data.y;
    float3 n = normalize(input.objectNormal);
    float3 p = input.objectPos;

    // Micro surface (triplanar in granule space, whiteout blend): the coating's pits and grain.
    float3 blend = pow(abs(n), 4.0);
    blend /= max(dot(blend, 1.0), 1e-4);
    float3 uvw = p * _DetailScale + seed * 0.173;
    half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uvw.zy), _DetailStrength);
    half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uvw.xz), _DetailStrength);
    half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uvw.xy), _DetailStrength);
    tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
    float3 detailOS = normalize(tx.zyx * blend.x + ty.xzy * blend.y + tz.xyz * blend.z);
    float3 normalWS = normalize(QRotate(input.rotation, detailOS));
    half rough = SAMPLE_TEXTURE2D(_DetailRoughness, sampler_DetailRoughness, uvw.zy).r * blend.x
               + SAMPLE_TEXTURE2D(_DetailRoughness, sampler_DetailRoughness, uvw.xz).r * blend.y
               + SAMPLE_TEXTURE2D(_DetailRoughness, sampler_DetailRoughness, uvw.xy).r * blend.z;

    // Speckled, mottled granule body: broad tone patches, dark and light flecks.
    float3 sp = p * _SpeckleScale + seed;
    float mottle = GranuleNoise(sp);
    float darkFleck = smoothstep(0.74, 0.84, GranuleNoise(sp * 3.1 + 7.3));
    float lightFleck = smoothstep(0.78, 0.9, GranuleNoise(sp * 2.3 - 3.1));
    half3 albedo = input.colour.rgb * lerp(1.0, 0.8 + 0.4 * mottle, speckle);
    albedo = lerp(albedo, albedo * 0.5, darkFleck * speckle);
    albedo = lerp(albedo, saturate(albedo * 1.25 + 0.06), lightFleck * speckle * 0.7);

    // Contact darkening: the side resting on the soil / the heap below is in its own shadow.
    half occlusion = 1.0;
    float ground = input.data.z;
    if (ground > -1000.0)
    {
        float h = (input.positionWS.y - ground) / max(input.data.w * 2.0, 1e-4);
        occlusion = lerp(1.0 - _ContactDarkening, 1.0, smoothstep(0.0, 0.75, h));
    }

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = (half3)normalWS;
    inputData.viewDirectionWS = (half3)GetWorldSpaceNormalizeViewDir(input.positionWS);
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = half3(0, 0, 0);
    inputData.bakedGI = SampleSH(inputData.normalWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedo;
    surfaceData.alpha = 1.0h;
    surfaceData.metallic = 0.0h;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.smoothness = saturate(input.colour.a * lerp(0.75, 1.15, 1.0 - rough));
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.occlusion = occlusion;

    half4 colour = UniversalFragmentPBR(inputData, surfaceData);
    colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
    colour.a = 1.0h;
    return colour;
}

#elif defined(BTP_GRANULE_DEPTH)

half GranuleDepthFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return input.positionCS.z;
}

#elif defined(BTP_GRANULE_DEPTH_NORMALS)

half4 GranuleDepthNormalsFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float3 normalWS = normalize(input.normalWS);
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
