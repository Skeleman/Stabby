#ifndef STABBY_CLOUDS_INCLUDED
#define STABBY_CLOUDS_INCLUDED

float4 _StabbyCloudShape, _StabbyWeather, _StabbyCloudDrift, _StabbyCloudLight, _StabbyCloudLightDir;
float _StabbyCloudsOff;

float CloudHash(int2 q, uint mask)
{
    uint2 u = (uint2)q & mask;
    uint h = u.x * 0x8da6b343u ^ u.y * 0xd8163841u;
    h ^= h >> 13;
    h *= 0x5bd1e995u;
    h ^= h >> 15;
    return (h & 0xffffu) / 65535.0;
}

float CloudNoise(float2 p, uint mask)
{
    float2 i = floor(p), f = p - i;
    f = f * f * f * (f * (f * 6 - 15) + 10);
    int2 c = (int2)i;
    float a = CloudHash(c, mask), b = CloudHash(c + int2(1, 0), mask);
    float d = CloudHash(c + int2(0, 1), mask), e = CloudHash(c + int2(1, 1), mask);
    return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
}

float CloudBase(float2 p, int octaves)
{
    float s = 0, w = 0.5, n = 0;
    uint mask = 255u;
    for (int k = 0; k < octaves; k++)
    {
        s += w * CloudNoise(p, mask);
        n += w;
        p = p * 2 + float2(17.3, 41.9);
        mask = mask * 2 + 1;
        w *= 0.5;
    }
    return s / n;
}

float CloudRamp(float x)
{
    return x <= 0 ? 0 : x < 1 ? 0.5 * x * x : x - 0.5;
}

float CloudTop(float2 xz, float2 churn, int octaves, bool detail)
{
    float2 p = xz * _StabbyCloudShape.z;
    float2 warp = float2(CloudNoise(p * 0.5 + _StabbyCloudDrift.xy * 0.5 + 91.7, 127u), CloudNoise(p * 0.5 + _StabbyCloudDrift.xy * 0.5 + 13.1, 127u)) - 0.5;
    float n = CloudBase(p + _StabbyCloudDrift.xy + warp * 0.6, octaves);
    float body = saturate((n - lerp(0.68, 0.32, _StabbyWeather.x) + _StabbyWeather.y * 0.06) * 4.5);
    if (detail) body -= (CloudBase(p * 8 + churn * 8, 2) - 0.5) * 0.45 * (1 - body * 0.6);
    return body * (0.55 + 0.45 * body);
}

float CloudShape(float2 xz, float h, float2 churn, int octaves, bool detail)
{
    return saturate((CloudTop(xz, churn, octaves, detail) - h) * 5) * saturate(h * 6 + 0.25);
}

// Integrates saturate((top - h) * 5) along a segment where both top and h vary linearly, so adjacent steps meet without terraces at grazing angles.
float CloudSpan(float topA, float topB, float hA, float hB)
{
    float a = topA - hA;
    float b = (topB - hB) - a;
    float cap = abs(b) > 1e-4 ? (CloudRamp((a + b) * 5) - CloudRamp(a * 5)) / (5 * b) : saturate((a + b * 0.5) * 5);
    return cap * saturate((hA + hB) * 3 + 0.25);
}

float4 StabbyClouds(float3 d, float2 px, float3 horizon, float3 zenith, float3 glow, float toward)
{
    if (_StabbyCloudsOff > 0 || d.y < 0.012 || _StabbyWeather.x <= 0) return float4(0, 0, 0, 1);
    float wet = _StabbyWeather.y;
    float thick = _StabbyCloudShape.y;
    float bottom = max(_StabbyCloudShape.x - _WorldSpaceCameraPos.y, 100);
    float t0 = bottom / d.y;
    float span = min(thick / d.y, thick * 5);
    const int steps = 4;
    float dt = span / steps;
    float3 l = normalize(_StabbyCloudLightDir.xyz);
    float mu = dot(d, l);
    float phase = 0.55 + 0.9 * pow(saturate(mu), 6) + 0.25 * saturate(mu);
    float sigma = _StabbyCloudLight.w * (1 + wet);
    float3 under = horizon * 0.55 + glow * (1.2 + 2.6 * toward * toward);
    float3 over = zenith * 0.75 + horizon * 0.25;
    float3 sun = _StabbyCloudLight.rgb;
    float2 cam = _WorldSpaceCameraPos.xz;
    float trans = 1;
    float3 light = 0;
    float3 org = float3(cam.x, 0, cam.y);
    float topA = CloudTop(org.xz + d.xz * t0, _StabbyCloudDrift.zw, 4, true);
    for (int k = 0; k < steps; k++)
    {
        float t = t0 + span * (k + 0.5) / steps;
        float3 q = org + d * t;
        float topB = CloudTop(org.xz + d.xz * (t + dt * 0.5), _StabbyCloudDrift.zw, 4, true);
        float hA = saturate(k * dt * d.y / thick);
        float hB = saturate((k + 1) * dt * d.y / thick);
        float h = (hA + hB) * 0.5;
        float dens = CloudSpan(topA, topB, hA, hB);
        topA = topB;
        if (dens <= 0.001) continue;
        float od = dens * sigma * dt / thick * 3;
        float lh = h + l.y * 0.45;
        float2 lq = q.xz + l.xz / max(abs(l.y), 0.15) * thick * 0.45;
        float shade = CloudShape(lq, saturate(lh), _StabbyCloudDrift.zw, 3, false) * (lh > 1 ? 0 : 1);
        float lod = (shade * 1.4 + dens * 0.6) * sigma;
        float direct = exp(-lod * 1.6) * (1 - exp(-od * 2.2) * 0.35);
        float3 amb = lerp(under, over, h) * (0.65 + 0.35 * (1 - dens)) * (1 - wet * 0.55 * (1 - h));
        float a = 1 - exp(-od);
        light += trans * a * (sun * direct * phase + amb);
        trans *= 1 - a;
        if (trans < 0.02) break;
    }
    float fade = saturate(1.15 - t0 / 45000);
    float haze = exp(-t0 / 22000);
    light = lerp(horizon * (1 - trans), light, haze) * fade;
    trans = lerp(1, trans, fade);
    return float4(light, trans);
}

#endif
