#ifndef STABBY_LOCAL_LIGHTS
#define STABBY_LOCAL_LIGHTS

float3 LocalLights(float3 wp, float3 n, float4 positionCS)
{
    float3 sum = 0;
#if USE_CLUSTER_LIGHT_LOOP || defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = wp;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light a = GetAdditionalLight(lightIndex, wp, half4(1, 1, 1, 1));
        sum += a.color * (a.distanceAttenuation * a.shadowAttenuation * saturate(dot(n, a.direction)));
    LIGHT_LOOP_END
#endif
    return sum;
}

#endif
