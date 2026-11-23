#ifndef STABBY_SHADOW_TEXEL
#define STABBY_SHADOW_TEXEL

float4 _StabbyShadowSplit;
float4 _StabbyShadowTexelM;

static const float ShadowTexelHysteresis = 0.15;

float ShadowTexelM(float d)
{
    return d <= _StabbyShadowSplit.x ? _StabbyShadowTexelM.x : d <= _StabbyShadowSplit.y ? _StabbyShadowTexelM.y : d <= _StabbyShadowSplit.z ? _StabbyShadowTexelM.z : _StabbyShadowTexelM.w;
}

float ShadowTexelFade(float sizeM, float d)
{
    float b = ShadowTexelM(d);
    return b <= 0 ? 1 : smoothstep(b * (1 - ShadowTexelHysteresis), b, sizeM);
}

#endif
