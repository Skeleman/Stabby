#ifndef STABBY_CLOUDS_INCLUDED
#define STABBY_CLOUDS_INCLUDED

float4 _StabbyCloudShape, _StabbyWeather, _StabbyCloudDrift, _StabbyCloudLight, _StabbyCloudLightDir, _StabbyCloudMapAt;
float _StabbyCloudsOff;
TEXTURE2D(_StabbyCloudMap);
SAMPLER(sampler_StabbyCloudMap);

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

float CloudBody(float2 xz, float2 churn, int octaves, float detail, out float raw)
{
    float2 p = xz * _StabbyCloudShape.z;
    float2 warp = float2(CloudNoise(p * 0.5 + _StabbyCloudDrift.xy * 0.5 + 91.7, 127u), CloudNoise(p * 0.5 + _StabbyCloudDrift.xy * 0.5 + 13.1, 127u)) - 0.5;
    float n = CloudBase(p + _StabbyCloudDrift.xy + warp * 0.6, octaves);
    raw = (n - lerp(0.68, 0.32, _StabbyWeather.x) + _StabbyWeather.y * 0.06) * 4.5;
    float body = saturate(raw);
    if (detail > 0) body -= (CloudBase(p * 8 + churn * 8, 2) - 0.5) * 0.45 * (1 - body * 0.6) * detail;
    raw += body - saturate(raw);
    return body;
}

float CloudCap(float body, float h0, float h1)
{
    float top = body * (0.55 + 0.45 * body);
    float h = (h0 + h1) * 0.5;
    float w = h1 - h0;
    float cap = w > 1e-4 ? (CloudRamp((top - h0) * 5) - CloudRamp((top - h1) * 5)) / (5 * w) : saturate((top - h) * 5);
    return cap * saturate(h * 6 + 0.25);
}

float CloudShape(float2 xz, float h0, float h1, float2 churn, int octaves, float detail, out float raw)
{
    return CloudCap(CloudBody(xz, churn, octaves, detail, raw), h0, h1);
}

// The map squeezes the plane into a disc so a texel grows with distance like a horizon pixel does.
float2 CloudMapUV(float2 xz)
{
    float2 o = xz - _StabbyCloudMapAt.xy;
    return o / (length(o) + _StabbyCloudMapAt.z) * 0.5 + 0.5;
}

float2 CloudMapXZ(float2 uv)
{
    float2 m = uv * 2 - 1;
    return _StabbyCloudMapAt.xy + m * _StabbyCloudMapAt.z / max(1 - length(m), 1e-3);
}

float3 CloudMapRead(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_StabbyCloudMap, sampler_StabbyCloudMap, CloudMapUV(xz), 0).xyz;
}

struct CloudLit
{
    float3 l, under, over, sun;
    float phase, sigma, wet;
};

float4 CloudMarch(float3 d, float bottom, float thick, float t0, float span, int steps, bool map, CloudLit c)
{
    float dt = span / steps;
    float2 cam = _WorldSpaceCameraPos.xz;
    float trans = 1;
    float3 light = 0;
    float rawPrev = 1, densPrev = 0, aPrev = 1;
    for (int k = 0; k < steps; k++)
    {
        float t = t0 + span * (k + 0.5) / steps;
        float2 q = cam + d.xz * t;
        float h = saturate((t * d.y - bottom) / thick);
        float hs = dt * d.y / thick * 0.5;
        float raw, dens;
        if (map)
        {
            float3 m = CloudMapRead(q);
            raw = m.y;
            dens = CloudCap(m.x, saturate(h - hs), saturate(h + hs));
        }
        else dens = CloudShape(q, saturate(h - hs), saturate(h + hs), _StabbyCloudDrift.zw, 4, 1, raw);
        float entry = rawPrev >= 0 ? 0 : saturate(-rawPrev / max(raw - rawPrev, 1e-4));
        float f = max(entry, aPrev);
        rawPrev = raw;
        float densStep = densPrev;
        densPrev = dens;
        if (dens <= 0.001) { aPrev = 0; continue; }
        float od = dens * c.sigma * dt / thick * 3;
        float a = 1 - exp(-od);
        aPrev = a;
        // Shade at the interpolated cloud entry between this step's point and the last one, so adjacent steps meet in colour.
        float back = (1 - f) * dt;
        float hS = saturate(h - back * d.y / thick);
        float2 qS = q - d.xz * back;
        float densS = lerp(densStep, dens, f);
        float odS = densS * c.sigma * dt / thick * 3;
        float lh = hS + c.l.y * 0.45;
        float2 lq = qS + c.l.xz / max(abs(c.l.y), 0.15) * thick * 0.45;
        float shadeRaw;
        float shade = (map ? CloudCap(CloudMapRead(lq).z, saturate(lh), saturate(lh)) : CloudShape(lq, saturate(lh), saturate(lh), _StabbyCloudDrift.zw, 3, 0, shadeRaw)) * (lh > 1 ? 0 : 1);
        float lod = (shade * 1.4 + densS * 0.6) * c.sigma;
        float direct = exp(-lod * 1.6) * (1 - exp(-odS * 2.2) * 0.35);
        float3 amb = lerp(c.under, c.over, hS) * (0.65 + 0.35 * (1 - densS)) * (1 - c.wet * 0.55 * (1 - hS));
        light += trans * a * (c.sun * direct * c.phase + amb);
        trans *= 1 - a;
        if (trans < 0.02) break;
    }
    return float4(light, trans);
}

float4 StabbyClouds(float3 d, float2 px, float3 horizon, float3 zenith, float3 glow, float toward)
{
    if (_StabbyCloudsOff > 0 || d.y < 0.012 || _StabbyWeather.x <= 0) return float4(0, 0, 0, 1);
    CloudLit c;
    c.wet = _StabbyWeather.y;
    float thick = _StabbyCloudShape.y;
    float bottom = max(_StabbyCloudShape.x - _WorldSpaceCameraPos.y, 100);
    float t0 = bottom / d.y;
    float span = min(thick / d.y, thick * 5);
    c.l = normalize(_StabbyCloudLightDir.xyz);
    float mu = dot(d, c.l);
    c.phase = 0.55 + 0.9 * pow(saturate(mu), 6) + 0.25 * saturate(mu);
    c.sigma = _StabbyCloudLight.w * (1 + c.wet);
    c.under = horizon * 0.55 + glow * (1.2 + 2.6 * toward * toward);
    c.over = zenith * 0.75 + horizon * 0.25;
    c.sun = _StabbyCloudLight.rgb;
    // Low rays cross the field sideways, so four procedural steps draw stacked copies of the outline; there the march reads the prefiltered map in many cheap steps.
    float low = _StabbyCloudMapAt.w > 0 ? saturate((0.3 - d.y) / 0.1) : 0;
    float4 r;
    if (low >= 1) r = CloudMarch(d, bottom, thick, t0, span, 32, true, c);
    else if (low <= 0) r = CloudMarch(d, bottom, thick, t0, span, 4, false, c);
    else r = lerp(CloudMarch(d, bottom, thick, t0, span, 4, false, c), CloudMarch(d, bottom, thick, t0, span, 32, true, c), low);
    float3 light = r.rgb;
    float trans = r.a;
    float fade = saturate(1.15 - t0 / 45000);
    float haze = exp(-t0 / 22000);
    light = lerp(horizon * (1 - trans), light, haze) * fade;
    trans = lerp(1, trans, fade);
    return float4(light, trans);
}

#endif
