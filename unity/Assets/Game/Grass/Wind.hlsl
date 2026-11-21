#ifndef STABBY_WIND
#define STABBY_WIND

float4 _StabbyWind, _StabbyGust;

float WindHash(float2 p)
{
    uint2 q = (uint2)(int2)floor(p);
    uint h = q.x * 0x8da6b343u ^ q.y * 0xd8163841u;
    h ^= h >> 15; h *= 0x2c1b3c6du;
    h ^= h >> 12;
    return h * (1.0 / 4294967296.0);
}

float WindNoise(float2 p)
{
    float2 i = floor(p), f = p - i;
    f = f * f * (3 - 2 * f);
    float a = WindHash(i), b = WindHash(i + float2(1, 0)), c = WindHash(i + float2(0, 1)), d = WindHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float WindGust(float2 xz, float time)
{
    float2 p = (xz - _StabbyWind.xy * time * _StabbyGust.y) * _StabbyGust.x;
    return saturate(0.65 * WindNoise(p) + 0.35 * WindNoise(p * 2.3 + 17.1));
}

float2 WindPush(float2 xz, float time, float seed)
{
    float g = WindGust(xz, time);
    float sway = 0.25 * sin(6.2831853 * (_StabbyWind.w * time + seed));
    return _StabbyWind.xy * (_StabbyWind.z * (1 + sway) + _StabbyGust.z * g);
}

#endif
