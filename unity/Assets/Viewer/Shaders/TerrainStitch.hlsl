#ifndef STABBY_TERRAIN_STITCH
#define STABBY_TERRAIN_STITCH

CBUFFER_START(UnityPerMaterial)
float4 _StitchMask;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _StitchMask)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#define _StitchMask UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _StitchMask)
#endif

float StitchT(float3 positionOS, float4 stitch)
{
    float4 sm = _StitchMask;
    uint bits = (uint)stitch.w, mask = (uint)sm.x;
    float t = 0;
    if (sm.z > 0) t = saturate((distance(TransformObjectToWorld(positionOS), _WorldSpaceCameraPos) - sm.y) / (sm.z - sm.y));
    if ((bits & (mask >> 8)) != 0) t = 0;
    if ((bits & mask & 255u) != 0) t = 1;
    return t;
}

#endif
