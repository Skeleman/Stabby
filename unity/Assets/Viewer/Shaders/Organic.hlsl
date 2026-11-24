#ifndef STABBY_ORGANIC
#define STABBY_ORGANIC

float4 _StabbySurface, _StabbyDetail;

static float3 orgO;

float2 OrgOrigin(float3 cam) { return floor(cam.xz * (1.0 / 1024)) * 1024; }

uint OrgHashU(uint3 p)
{
    uint h = p.x * 0x8da6b343u ^ p.y * 0xd8163841u ^ p.z * 0xcb1ab31fu;
    h ^= h >> 15; h *= 0x2c1b3c6du;
    h ^= h >> 12; h *= 0x297a2d39u;
    h ^= h >> 15;
    return h;
}

float OrgHash(int3 p) { return OrgHashU((uint3)p) * (1.0 / 4294967296.0); }
float OrgHash(int2 p) { return OrgHash(int3(p, 0)); }

float2 OrgGrad(int2 p)
{
    uint h = OrgHashU(uint3((uint2)p, 0));
    return float2(h & 0xffffu, h >> 16) * (2.0 / 65535) - 1;
}

float3 OrgNoise2(float2 p, int2 s)
{
    float2 fl = floor(p), f = p - fl;
    int2 i = (int2)fl + s;
    float2 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
    float2 ga = OrgGrad(i), gb = OrgGrad(i + int2(1, 0)), gc = OrgGrad(i + int2(0, 1)), gd = OrgGrad(i + int2(1, 1));
    float a = dot(ga, f), b = dot(gb, f - float2(1, 0)), c = dot(gc, f - float2(0, 1)), d = dot(gd, f - 1);
    float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
    float2 g = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) + du * float2(k1 + k4 * u.y, k2 + k4 * u.x);
    return 2 * float3(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y, g);
}

float3 OrgNoise2(float2 p) { return OrgNoise2(p, int2(0, 0)); }

float4 OrgNoise3(float3 p, int3 s)
{
    float3 fl = floor(p), f = p - fl;
    int3 i = (int3)fl + s;
    float3 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
    float a = OrgHash(i), b = OrgHash(i + int3(1, 0, 0)), c = OrgHash(i + int3(0, 1, 0)), d = OrgHash(i + int3(1, 1, 0));
    float e = OrgHash(i + int3(0, 0, 1)), g = OrgHash(i + int3(1, 0, 1)), h = OrgHash(i + int3(0, 1, 1)), k = OrgHash(i + int3(1, 1, 1));
    float k1 = b - a, k2 = c - a, k3 = e - a, k4 = a - b - c + d, k5 = a - c - e + h, k6 = a - b - e + g, k7 = -a + b + c - d + e - g - h + k;
    float v = a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
    float3 gr = du * float3(k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z, k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x, k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
    return float4(2 * v - 1, 2 * gr);
}

float4 OrgNoise3(float3 p) { return OrgNoise3(p, int3(0, 0, 0)); }

float OrgQ(float f) { return round(f * 1024) * (1.0 / 1024); }

int2 OrgShift(float q) { return (int2)((orgO.xz * (1.0 / 1024)) * (q * 1024)); }

int OrgDiv(int n, int d)
{
    int i = (int)floor((float)n / d);
    i -= i * d > n ? 1 : 0;
    i += (i + 1) * d <= n ? 1 : 0;
    return i;
}

static const int2x2 OrgRot2[4] = { int2x2(3, -4, 4, 3), int2x2(5, 12, -12, 5), int2x2(8, -15, 15, 8), int2x2(7, 24, -24, 7) };
static const int OrgRotD2[4] = { 5, 13, 17, 25 };
static const int3x3 OrgRot3[4] = { int3x3(0, 20, 15, -20, 9, -12, -15, -12, 16), int3x3(3, -2, 6, 6, 3, -2, -2, 6, 3), int3x3(11, -2, 10, 10, 5, -10, -2, 14, 5), int3x3(0, -20, -15, 20, 9, -12, 15, -12, 16) };
static const int OrgRotD3[4] = { 25, 7, 15, 25 };

float3 OrgL2(float2 l, float f, float2 c)
{
    float q = OrgQ(f);
    return OrgNoise2(l * q + c, OrgShift(q));
}

float4 OrgL3(float3 l, float f, float3 c)
{
    float q = OrgQ(f);
    int2 s = OrgShift(q);
    return OrgNoise3(l * q + c, int3(s.x, 0, s.y));
}

float3 OrgRL2(float2 l, float f, float2 c, int k)
{
    if (k == 0) return OrgL2(l, f, c);
    float q = OrgQ(f);
    int2x2 m = OrgRot2[(k - 1) & 3];
    int d = OrgRotD2[(k - 1) & 3];
    int2 n = mul(m, OrgShift(q));
    int2 i = int2(OrgDiv(n.x, d), OrgDiv(n.y, d));
    float2x2 r = (float2x2)m / d;
    float3 v = OrgNoise2(mul(r, l * q) + (float2)(n - i * d) / d + c, i);
    return float3(v.x, mul(v.yz, r));
}

float4 OrgRL3(float3 l, float f, float3 c, int k)
{
    if (k == 0) return OrgL3(l, f, c);
    float q = OrgQ(f);
    int2 s = OrgShift(q);
    int3x3 m = OrgRot3[(k - 1) & 3];
    int d = OrgRotD3[(k - 1) & 3];
    int3 n = mul(m, int3(s.x, 0, s.y));
    int3 i = int3(OrgDiv(n.x, d), OrgDiv(n.y, d), OrgDiv(n.z, d));
    float3x3 r = (float3x3)m / d;
    float4 v = OrgNoise3(mul(r, l * q) + (float3)(n - i * d) / d + c, i);
    return float4(v.x, mul(v.yzw, r));
}

float3 OrgRP2(float2 p, float f, float2 c, int k)
{
    if (k == 0) return OrgNoise2(p * f + c);
    float2x2 r = (float2x2)OrgRot2[(k - 1) & 3] / OrgRotD2[(k - 1) & 3];
    float3 v = OrgNoise2(mul(r, p * f) + c);
    return float3(v.x, mul(v.yz, r));
}

float4 OrgRP3(float3 p, float f, float3 c, int k)
{
    if (k == 0) return OrgNoise3(p * f + c);
    float3x3 r = (float3x3)OrgRot3[(k - 1) & 3] / OrgRotD3[(k - 1) & 3];
    float4 v = OrgNoise3(mul(r, p * f) + c);
    return float4(v.x, mul(v.yzw, r));
}

float OrgFade(float freq, float fp) { return saturate(2.5 - 2.5 * freq * fp * _StabbySurface.x); }

float3 OrgN2(float2 p, float w)
{
    float3 r = 0;
    [branch] if (w > 0) r = OrgNoise2(p);
    return r;
}

float3 OrgLN2(float2 l, float f, float c, float w)
{
    float3 r = 0;
    [branch] if (w > 0) r = OrgL2(l, f, c);
    return r;
}

float4 OrgLN3(float3 l, float f, float c, float w)
{
    float4 r = 0;
    [branch] if (w > 0) r = OrgL3(l, f, c);
    return r;
}

float OrgF(int k)
{
    float b = _StabbyDetail.x > 0 ? _StabbyDetail.x : 2.857;
    float a = _StabbyDetail.y > 0 ? _StabbyDetail.y : 2.7;
    return b * pow(a, k);
}

int OrgOctaves() { return _StabbyDetail.z > 0 ? (int)_StabbyDetail.z : 5; }

float OrgGain() { return _StabbyDetail.w > 0 ? _StabbyDetail.w : 0.55; }

float OrgRungW(int k, float fp) { return k < OrgOctaves() ? OrgFade(OrgF(k), fp) : 0; }

float4 OrgRLN3(float3 l, int k, float3 c, float w)
{
    float4 r = 0;
    [branch] if (w > 0) r = OrgRL3(l, OrgF(k), c, k);
    return r;
}

float3 OrgRung2(float2 l, int k, float c, float fp)
{
    float w = OrgRungW(k, fp);
    float3 r = 0;
    [branch] if (w > 0) r = w * OrgRL2(l, OrgF(k), c, k);
    return r;
}

float4 OrgRung3(float3 l, int k, float c, float fp)
{
    float w = OrgRungW(k, fp);
    float4 r = 0;
    [branch] if (w > 0) r = w * OrgRL3(l, OrgF(k), c, k);
    return r;
}

float3 OrgDetail2(float2 l, int k0, int count, float c, float fp)
{
    float3 s = 0;
    float a = 1, g = OrgGain();
    for (int k = k0; k < k0 + count; k++)
    {
        float w = OrgRungW(k, fp);
        if (w <= 0) break;
        s += a * w * OrgRL2(l, OrgF(k), c + k * 19.31, k);
        a *= g;
    }
    return s;
}

float4 OrgDetail3(float3 l, int k0, int count, float c, float fp)
{
    float4 s = 0;
    float a = 1, g = OrgGain();
    for (int k = k0; k < k0 + count; k++)
    {
        float w = OrgRungW(k, fp);
        if (w <= 0) break;
        s += a * w * OrgRL3(l, OrgF(k), c + k * 19.31, k);
        a *= g;
    }
    return s;
}

float3 OrgFbm2(float2 p, float freq, float amp, int octaves, float fp)
{
    float3 s = 0;
    for (int k = 0; k < octaves; k++)
    {
        float w = OrgFade(freq, fp);
        if (w <= 0) break;
        float q = OrgQ(freq);
        float3 n = OrgRL2(p, freq, k * 19.31, k);
        s += amp * w * float3(n.x / q, n.yz);
        freq *= 2.03;
        amp *= 0.6;
    }
    return s;
}

float4 OrgFbm3(float3 p, float freq, float amp, int octaves, float fp)
{
    float4 s = 0;
    for (int k = 0; k < octaves; k++)
    {
        float w = OrgFade(freq, fp);
        if (w <= 0) break;
        float q = OrgQ(freq);
        float4 n = OrgRL3(p, freq, k * 19.31, k);
        s += amp * w * float4(n.x / q, n.yzw);
        freq *= 2.03;
        amp *= 0.6;
    }
    return s;
}

float4 OrgDampedFbm3(float3 p, float freq, float amp, int octaves, float fp)
{
    float4 s = 0;
    float3 d = 0;
    for (int k = 0; k < octaves; k++)
    {
        float w = OrgFade(freq, fp);
        if (w <= 0) break;
        float q = OrgQ(freq);
        float4 n = OrgRL3(p, freq, k * 23.17, k);
        d += n.yzw;
        float damp = 1 / (1 + dot(d, d));
        s += amp * w * damp * float4(n.x / q, n.yzw);
        freq *= 2.03;
        amp *= 0.6;
    }
    return s;
}

float2 OrgWarp2(float2 l, float f, float amount, float c)
{
    return l + amount * float2(OrgL2(l, f, c).x, OrgL2(l, f, c + 17.1).x);
}

float3 OrgWarp3(float3 l, float f, float amount, float c)
{
    return l + amount * float3(OrgL3(l, f, c).x, OrgL3(l, f, c + 17.1).x, OrgL3(l, f, c + 31.7).x);
}

float OrgWarp1(float x, float3 l, float f, float amount, float c) { return x + amount * OrgL3(l, f, c).x; }

float4 OrgRidged3(float3 l, float f, float c)
{
    float4 n = OrgL3(l, f, c);
    return float4(1 - abs(n.x), -sign(n.x) * n.yzw);
}

float3 OrgCells2(float2 l, float f, float c, float jitter)
{
    float q = OrgQ(f);
    float2 p = l * q + c;
    float2 fl = floor(p), fr = p - fl;
    int2 i = (int2)fl + OrgShift(q);
    float d1 = 8, d2 = 8, id = 0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            int2 cell = i + int2(x, y);
            float2 o = float2(x, y) + 0.5 + jitter * (float2(OrgHash(int3(cell, 3)), OrgHash(int3(cell, 5))) - 0.5) - fr;
            float d = dot(o, o);
            if (d < d1) { d2 = d1; d1 = d; id = OrgHash(int3(cell, 7)); }
            else if (d < d2) d2 = d;
        }
    d1 = sqrt(d1);
    d2 = sqrt(d2);
    return float3(d1, d2 - d1, id);
}

float OrgBand(float x, float sharp, out float id)
{
    id = floor(x);
    float t = x - id;
    return smoothstep(0, sharp, t) * smoothstep(0, sharp, 1 - t);
}

float OrgMacro(float3 l, float c) { return 0.5 + 0.5 * OrgL3(l, 1.0 / 40, c).x; }

float4 OrgGrain3(float3 l, float c, float fp) { return OrgDetail3(l, 3, 2, c, fp); }

float OrgPits2(float2 l, float c, float fp, float size)
{
    float w = OrgRungW(2, fp);
    float r = 0;
    [branch] if (w > 0) r = w * (1 - smoothstep(0, size, OrgCells2(l, OrgF(2), c, 0.9).x));
    return r;
}

#define ORG_FD_GRAD3(F, p, e, g) { float orgH0 = F(p); g = float3(F(p + float3(e, 0, 0)) - orgH0, F(p + float3(0, e, 0)) - orgH0, F(p + float3(0, 0, e)) - orgH0) / (e); }

#endif
