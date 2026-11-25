#ifndef STABBY_CAVE_SKY
#define STABBY_CAVE_SKY

TEXTURE3D(_StabbyCaveDark);
SAMPLER(sampler_StabbyCaveDark);
float4 _StabbyCaveDarkMin, _StabbyCaveDarkInv;
float _StabbyCaveDarkOff;

float CaveSky(float3 wp)
{
    float3 uvw = (wp - _StabbyCaveDarkMin.xyz) * _StabbyCaveDarkInv.xyz;
    [branch] if (_StabbyCaveDarkMin.w <= 0 || any(uvw < 0) || any(uvw > 1)) return 1;
    return 1 - SAMPLE_TEXTURE3D_LOD(_StabbyCaveDark, sampler_StabbyCaveDark, uvw, 0).r * (1 - _StabbyCaveDarkOff);
}

#endif
