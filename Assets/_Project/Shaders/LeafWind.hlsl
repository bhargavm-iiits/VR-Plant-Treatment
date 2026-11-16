#ifndef BTP_LEAF_WIND_INCLUDED
#define BTP_LEAF_WIND_INCLUDED

// Per-leaf wind replayed in the vertex shader (BTP/Farm Recovery, keyword _LEAF_WIND). The cherry tree's
// 1,225 leaves and the tomato plants' 17-31 leaves were animated as separate objects in Blender; joined into
// one mesh they would be static, and an animated transform per leaf is far too many for Quest. Instead each
// leaf vertex carries its leaf index in UV1.x (index + 1; 0 = wood, fruit, stems: untouched) and _WindTex
// holds the baked motion, width = keys + 1:
//   row    = leaf index
//   column k (0..keys-1) = rotation key k as a unit quaternion (xyzw) in mesh object space, relative to the
//                          exported pose (key 0 = identity); keys evenly spaced over a seamless loop of
//                          _WindLoopSeconds (cherry 48 keys, tomatoes 16, both 8 s)
//   last column          = the leaf's rotation pivot (its base, where it meets the petiole) in object space
// With _WindTravel (the field and tropical crops, whose stems sway too) the row is keys rotations, then keys
// displacements of the pivot (object space, relative to the exported pose), then the pivot: width = 2 x keys + 1.
// Each part then moves exactly as it did in Blender, so a leaf stays on its swaying stem.
// Written by Assets/_Project/Scripts/Editor/LeafWindBaker.cs. The key count comes from the texture width
// (_WindTex_TexelSize.z, set by Unity for every material texture), so one shader variant serves any count.
// The leaf rotates rigidly about its base, so it stays attached to its static petiole. Vertex texture fetch
// with LOAD (no sampler, no filtering): supported on GLES 3.0, Vulkan and D3D11.

TEXTURE2D(_WindTex);

float4 WindKey(int column, int leaf)
{
    return LOAD_TEXTURE2D_LOD(_WindTex, int2(column, leaf), 0);
}

float3 WindRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Cheap hash of the object's world position: neighbouring trees sway out of step.
float WindPhase()
{
    float3 p = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
    return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453) * _WindLoopSeconds;
}

void ApplyLeafWind(inout float3 positionOS, inout float3 normalOS, inout float4 tangentOS, float2 windUV)
{
    int leaf = (int)round(windUV.x) - 1;
    if (leaf < 0)
        return;

    int width = (int)round(_WindTex_TexelSize.z);
    int keys = _WindTravel > 0.5 ? (width - 1) / 2 : width - 1;
    float t = frac((_Time.y * _WindSpeed + WindPhase()) / _WindLoopSeconds) * keys;
    float k0 = min(floor(t), keys - 1);   // frac can round up to 1.0 in float
    int k1 = (int)k0 + 1;
    k1 = k1 >= keys ? 0 : k1;   // wrap the last key -> 0 (cheaper than an integer modulus on mobile GPUs)
    float4 q0 = WindKey((int)k0, leaf);
    float4 q1 = WindKey(k1, leaf);
    // Same hemisphere before blending (the baker already enforces this; it keeps the wrap to key 0 safe).
    q1 = dot(q0, q1) < 0.0 ? -q1 : q1;
    float4 q = normalize(lerp(q0, q1, t - k0));
    // Strength scales the rotation towards identity (nlerp; exact enough for leaf-sized angles).
    q = lerp(float4(0.0, 0.0, 0.0, 1.0), q, _WindStrength);
    q *= rsqrt(max(dot(q, q), 1e-8));

    float3 pivot = WindKey(width - 1, leaf).xyz;
    positionOS = pivot + WindRotate(q, positionOS - pivot);
    if (_WindTravel > 0.5)
        positionOS += lerp(WindKey(keys + (int)k0, leaf).xyz, WindKey(keys + k1, leaf).xyz, t - k0) * _WindStrength;
    normalOS = WindRotate(q, normalOS);
    tangentOS.xyz = WindRotate(q, tangentOS.xyz);
}

#endif
