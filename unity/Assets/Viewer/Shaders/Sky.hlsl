#ifndef STABBY_SKY_INCLUDED
#define STABBY_SKY_INCLUDED

float4 _StabbyZenith, _StabbyHorizon, _StabbyGlow, _StabbySunDir, _StabbySunTint, _StabbyMoonDir, _StabbyMoonTint;
float _StabbyStars;

float StabbySkyToward(float3 d)
{
    float2 s = _StabbySunDir.xz;
    float sl = length(s), dl = length(d.xz);
    return sl > 1e-4 && dl > 1e-4 ? dot(d.xz / dl, s / sl) * 0.5 + 0.5 : 0.5;
}

float3 StabbySkyBand(float3 d, out float toward)
{
    toward = StabbySkyToward(d);
    float warm = saturate(max(_StabbyGlow.r, max(_StabbyGlow.g, _StabbyGlow.b)) * 6);
    return lerp(_StabbyHorizon.rgb, _StabbyZenith.rgb, 0.35 * warm * (1 - toward));
}

float3 StabbySkyHorizon(float3 d)
{
    float t;
    float3 c = StabbySkyBand(d, t);
    return c + _StabbyGlow.rgb * (t * t * t);
}

float3 StabbySkyColor(float3 d)
{
    float t;
    float3 h = StabbySkyBand(d, t);
    float e = max(d.y, 0);
    float3 c = lerp(h, _StabbyZenith.rgb, 1 - exp(-e * 4.5));
    return c + _StabbyGlow.rgb * (t * t * t) * exp(-e * (5 + 4 * (1 - t)));
}

float3 StabbyFogColor(float3 positionWS)
{
    return _StabbyHorizon.a > 0 ? StabbySkyHorizon(normalize(positionWS - _WorldSpaceCameraPos)) : unity_FogColor.rgb;
}

half3 StabbyMixFog(half3 c, float3 positionWS, float fogFactor)
{
    return MixFogColor(c, StabbyFogColor(positionWS), fogFactor);
}

#endif
