#ifndef STABBY_LOCAL_LIGHTS
#define STABBY_LOCAL_LIGHTS

float3 LocalLightsWrap(float3 wp, float3 n, float4 positionCS, float wrap)
{
    float3 sum = 0;
#if USE_CLUSTER_LIGHT_LOOP || defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = wp;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light a = GetAdditionalLight(lightIndex, wp, half4(1, 1, 1, 1));
        sum += a.color * (a.distanceAttenuation * a.shadowAttenuation * saturate((dot(n, a.direction) + wrap) / (1 + wrap)));
    LIGHT_LOOP_END
#endif
    return sum;
}

float3 LocalLights(float3 wp, float3 n, float4 positionCS) { return LocalLightsWrap(wp, n, positionCS, 0); }

struct LocalGlint { float3 diffuse; float3 spec; float3 color; float3 dir; };

LocalGlint LocalLightsGlint(float3 wp, float3 n, float3 v, float e, float4 positionCS)
{
    LocalGlint g = (LocalGlint)0;
#if USE_CLUSTER_LIGHT_LOOP || defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = wp;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint pixelLightCount = GetAdditionalLightsCount();
    float3 acc = 0;
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light a = GetAdditionalLight(lightIndex, wp, half4(1, 1, 1, 1));
        float3 col = a.color * (a.distanceAttenuation * a.shadowAttenuation);
        float3 hv = a.direction + v;
        float3 h = hv * rsqrt(max(dot(hv, hv), 1e-6));
        g.diffuse += col * saturate(dot(n, a.direction));
        g.spec += col * pow(saturate(dot(n, h)), e);
        g.color += col;
        acc += a.direction * dot(col, float3(0.3, 0.59, 0.11));
    LIGHT_LOOP_END
    g.dir = acc * rsqrt(max(dot(acc, acc), 1e-12));
#endif
    return g;
}
#endif
