#ifndef STABBY_TERRAIN_MATERIALS
#define STABBY_TERRAIN_MATERIALS

#include "Assets/Viewer/Shaders/Look.hlsl"

float4 _StabbySurface, _StabbyStrataWobble, _StabbyRockA, _StabbyRockB, _StabbyRockC, _StabbyCoatUV;
float4 _StabbyCoatA[4], _StabbyCoatB[4], _StabbyCoatRough;
float _StabbyDetailOff;
TEXTURE2D(_StabbyCoat);
SAMPLER(sampler_StabbyCoat);
TEXTURE2D(_StabbyWaterLevel);
SAMPLER(sampler_StabbyWaterLevel);

static float3 tmO;
static float tmGrass;
static float tmRough;
static float tmCurv;
static float4 tmShore;

struct TmOut { float3 albedo; float3 grad; };

float2 TmOrigin(float3 cam) { return floor(cam.xz * (1.0 / 1024)) * 1024; }

uint TmHashU(uint3 p)
{
    uint h = p.x * 0x8da6b343u ^ p.y * 0xd8163841u ^ p.z * 0xcb1ab31fu;
    h ^= h >> 15; h *= 0x2c1b3c6du;
    h ^= h >> 12; h *= 0x297a2d39u;
    h ^= h >> 15;
    return h;
}

float TmHash(int3 p) { return TmHashU((uint3)p) * (1.0 / 4294967296.0); }
float TmHash(int2 p) { return TmHash(int3(p, 0)); }

float2 TmGrad(int2 p)
{
    uint h = TmHashU(uint3((uint2)p, 0));
    return float2(h & 0xffffu, h >> 16) * (2.0 / 65535) - 1;
}

float3 TmNoise2(float2 p, int2 s)
{
    float2 fl = floor(p), f = p - fl;
    int2 i = (int2)fl + s;
    float2 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
    float2 ga = TmGrad(i), gb = TmGrad(i + int2(1, 0)), gc = TmGrad(i + int2(0, 1)), gd = TmGrad(i + int2(1, 1));
    float a = dot(ga, f), b = dot(gb, f - float2(1, 0)), c = dot(gc, f - float2(0, 1)), d = dot(gd, f - 1);
    float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
    float2 g = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) + du * float2(k1 + k4 * u.y, k2 + k4 * u.x);
    return 2 * float3(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y, g);
}

float3 TmNoise2(float2 p) { return TmNoise2(p, int2(0, 0)); }

float4 TmNoise3(float3 p, int3 s)
{
    float3 fl = floor(p), f = p - fl;
    int3 i = (int3)fl + s;
    float3 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
    float a = TmHash(i), b = TmHash(i + int3(1, 0, 0)), c = TmHash(i + int3(0, 1, 0)), d = TmHash(i + int3(1, 1, 0));
    float e = TmHash(i + int3(0, 0, 1)), g = TmHash(i + int3(1, 0, 1)), h = TmHash(i + int3(0, 1, 1)), k = TmHash(i + int3(1, 1, 1));
    float k1 = b - a, k2 = c - a, k3 = e - a, k4 = a - b - c + d, k5 = a - c - e + h, k6 = a - b - e + g, k7 = -a + b + c - d + e - g - h + k;
    float v = a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
    float3 gr = du * float3(k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z, k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x, k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
    return float4(2 * v - 1, 2 * gr);
}

float TmQ(float f) { return round(f * 1024) * (1.0 / 1024); }

int2 TmShift(float q) { return (int2)((tmO.xz * (1.0 / 1024)) * (q * 1024)); }

float3 TmL2(float2 l, float f, float c)
{
    float q = TmQ(f);
    return TmNoise2(l * q + c, TmShift(q));
}

float4 TmL3(float3 l, float f, float c)
{
    float q = TmQ(f);
    int2 s = TmShift(q);
    return TmNoise3(l * q + c, int3(s.x, 0, s.y));
}

float TmFade(float freq, float fp) { return saturate(2.5 - 2.5 * freq * fp * _StabbySurface.x); }

float3 TmN2(float2 p, float w)
{
    float3 r = 0;
    [branch] if (w > 0) r = TmNoise2(p);
    return r;
}

float3 TmLN2(float2 l, float f, float c, float w)
{
    float3 r = 0;
    [branch] if (w > 0) r = TmL2(l, f, c);
    return r;
}

float4 TmLN3(float3 l, float f, float c, float w)
{
    float4 r = 0;
    [branch] if (w > 0) r = TmL3(l, f, c);
    return r;
}

float3 TmFbm2(float2 p, float freq, float amp, int octaves, float fp)
{
    float3 s = 0;
    for (int k = 0; k < octaves; k++)
    {
        float w = TmFade(freq, fp);
        if (w <= 0) break;
        float q = TmQ(freq);
        float3 n = TmNoise2(p * q + k * 19.31, TmShift(q));
        s += amp * w * float3(n.x / q, n.yz);
        freq *= 2.03;
        amp *= 0.6;
    }
    return s;
}

float4 TmFbm3(float3 p, float freq, float amp, int octaves, float fp)
{
    float4 s = 0;
    for (int k = 0; k < octaves; k++)
    {
        float w = TmFade(freq, fp);
        if (w <= 0) break;
        float q = TmQ(freq);
        int2 sh = TmShift(q);
        float4 n = TmNoise3(p * q + k * 19.31, int3(sh.x, 0, sh.y));
        s += amp * w * float4(n.x / q, n.yzw);
        freq *= 2.03;
        amp *= 0.6;
    }
    return s;
}

float TmGroove(float x, float width, out float slope)
{
    float c = min(x, 1 - x), t = saturate(c / width);
    slope = 6 * t * (1 - t) / width * (x < 0.5 ? 1 : -1);
    return t * t * (3 - 2 * t) - 1;
}

static const float2 tmJ1 = float2(0.96, 0.28), tmJ2 = float2(-0.28, 0.96);

float TmChip(float c, float w, float r, out float dp)
{
    float t = saturate((c + r) / w);
    dp = 6 * t * (1 - t) / w;
    return t * t * (3 - 2 * t) - 1;
}

float TmRockRelief(float2 qa, float level, float2 levelGrad, float hard, float fp, float layered, float beds, float2 jw, out float3 grad, out float bed, out float bedF)
{
    float4 rb = _StabbyRockB;
    float3 dal = float3(0.8, 0, 0.6), dlev = float3(levelGrad.x, 1, levelGrad.y);
    float al = dot(qa, dal.xz), b0 = level * rb.x;
    float3 tw = TmNoise2(float2(b0 * 0.37, al * (1.0 / 40)) + 5.3);
    float b = b0 + 0.6 * tw.x, ib = floor(b);
    float3 lg = dlev * rb.x * (1 + 0.222 * tw.y) + 0.015 * tw.z * dal;
    bed = ib;
    bedF = b - ib;
    float kb = floor(b + 0.5);
    int ik = (int)kb;
    float3 w1 = TmNoise2(float2(al * (1.0 / 5.5), kb * 7.13));
    float3 w2 = TmNoise2(float2(al * (1.0 / 1.4), kb * 3.71 + 40));
    float x = b - kb + 0.07 * w1.x + 0.025 * w2.x;
    float3 xg = lg + (0.0127 * w1.y + 0.0179 * w2.y) * dal;
    float3 vw = TmNoise2(float2(al * (1.0 / 9), kb * 5.9 + 80));
    float3 vd = TmNoise2(float2(al * (1.0 / 17), kb * 2.3 + 120));
    float hk0 = TmHash(int2(ik, 7));
    float pres = smoothstep(-0.4, 0.35, vd.x + 0.5 * (hk0 - 0.5));
    float depth = beds * layered * rb.y * lerp(1.6, 0.5, hard) * lerp(0.3, 1.4, hk0) * pres * TmFade(rb.x * 8, fp);
    float ws = 0.13 * lerp(0.45, 1.6, saturate(0.5 + 0.6 * vw.x)) * (x < 0 ? 1.5 : 0.8);
    float fr = TmFade(8, fp);
    float3 rn = TmNoise2(float2(al * 2.7, level * 3.3) + 9.1);
    float3 rn2 = fr * TmNoise2(float2(al * 7.9, level * 8.3) + 21.7);
    float r = ws * (0.4 * rn.x + 0.18 * rn2.x), dp;
    float3 rg = ws * (0.4 * (2.7 * rn.y * dal + 3.3 * rn.z * dlev) + 0.18 * (7.9 * rn2.y * dal + 8.3 * rn2.z * dlev));
    float gb = TmChip(abs(x), ws, r, dp);
    grad = depth * dp * ((x < 0 ? -1 : 1) * xg + rg);
    float hb = depth * gb;
    float jd = layered * rb.w * (0.25 + 0.75 * beds) * smoothstep(0.35, 0.85, hard) * TmFade(rb.z * 20, fp) * (1 + 0.5 * gb);
    if (jd <= 0) return hb;
    float2 d1 = tmJ1, d2 = tmJ2;
    int bi = (int)ib;
    float3 pw = TmNoise2(float2(b * 0.21, al * (1.0 / 30)) + 41.3);
    float3 pv = TmNoise2(float2(level * (1.0 / 1.1), al * (1.0 / 7)) + 55.1);
    float u1 = dot(qa, d1) * rb.z;
    float3 sw1 = TmNoise2(float2(u1 * 0.31, b * 0.11) + 87.7);
    float j = u1 + 0.45 * sw1.x + 0.35 * pw.x + 0.06 * pv.x + 0.02 * rn.x;
    float ij = floor(j + 0.5), e = j - ij;
    int jb = (int)ij;
    float3 jgv = rb.z * (1 + 0.14 * sw1.y) * float3(d1.x, 0, d1.y) + 0.0735 * pw.y * lg + 0.0545 * pv.z * dlev;
    float hp = TmHash(int2(jb, 23));
    float3 jx = TmNoise2(float2(b * 0.3, ij * 4.7 + 19));
    float run = smoothstep(0, 0.35, jx.x + 0.6 * (hp - 0.5)) * step(0.12, TmHash(int2(bi * 31 + jb, 37)));
    float a1 = jd * jw.x * run * lerp(0.45, 1.35, frac(hp * 7.31)) * saturate(0.6 + 0.7 * rn.x + 0.3 * vw.x);
    float wj = 0.035 * lerp(0.6, 1.5, saturate(0.5 + 0.7 * w1.x)), dj;
    float hj = a1 * TmChip(abs(e), wj, wj * (0.45 * rn.x + 0.2 * rn2.x), dj);
    float3 gj = a1 * dj * ((e < 0 ? -1 : 1) * jgv + wj * 0.45 * (2.7 * rn.y * dal + 3.3 * rn.z * dlev));
    float3 pw2 = TmNoise2(float2(b * 0.17, dot(qa, d1) * (1.0 / 25)) + 63.9);
    float u2 = dot(qa, d2) * rb.z * 0.6;
    float3 sw2k = TmNoise2(float2(u2 * 0.27, b * 0.13) + 23.3);
    float k = u2 + 0.45 * sw2k.x + 0.3 * pw2.x + 0.05 * w2.x;
    float ic = floor(k + 0.5), ek = k - ic, d2p;
    float3 kx = TmNoise2(float2(b * 0.45, ic * 3.1 + 71));
    float hq = TmHash(int2((int)ic, 43));
    float m = smoothstep(0.05, 0.4, kx.x + 0.5 * (hq - 0.5)) * lerp(0.4, 1.2, frac(hq * 5.13)) * saturate(0.5 + rn.x);
    float3 kgv = rb.z * 0.6 * (1 + 0.12 * sw2k.y) * float3(d2.x, 0, d2.y) + 0.051 * pw2.y * lg;
    float wk = 0.05 * lerp(0.6, 1.4, saturate(0.5 + 0.7 * vw.x));
    float hk = jd * jw.y * m * TmChip(abs(ek), wk, wk * 0.45 * rn2.x, d2p);
    float3 gk = jd * jw.y * m * d2p * ((ek < 0 ? -1 : 1) * kgv + wk * 0.45 * (7.9 * rn2.y * dal + 8.3 * rn2.z * dlev));
    float hm = min(hj, hk);
    grad += (hj < hk ? gj : gk) + 0.5 * hm / (1 + 0.5 * gb) * dp * (x < 0 ? -1 : 1) * xg;
    return hb + hm;
}

float TmCragPlane(float u, float y, float fp, float hard, float mass, out float2 g, out float cav, out float tone, out float streak)
{
    float3 mw = TmNoise2(float2(y * (1.0 / 40), u * (1.0 / 60)) + 3.7);
    u += 4 * mw.x;
    float amp = lerp(0.6, 1.2, hard) * lerp(0.85, 1.1, mass);
    float f1 = TmFade(1.0 / 18, fp), f2 = TmFade(1.0 / 6.5, fp), f3 = TmFade(1.0 / 1.7, fp);
    float3 b1 = TmNoise2(float2(u * (1.0 / 18), y * (1.0 / 110)) + 61.7);
    float3 b2 = TmNoise2(float2(u * (1.0 / 6.5), y * (1.0 / 38)) + 17.9);
    float3 fl = TmNoise2(float2(u * (1.0 / 1.7), y * (1.0 / 11)) + 33.1);
    float s1 = b1.x < 0 ? -1 : 1, s2 = b2.x < 0 ? -1 : 1;
    float h = amp * (f1 * 5.0 * (abs(b1.x) - 0.35) + f2 * 1.7 * (abs(b2.x) - 0.35) + f3 * 0.22 * fl.x);
    g = amp * (f1 * 5.0 * s1 * b1.yz * float2(1.0 / 18, 1.0 / 110) + f2 * 1.7 * s2 * b2.yz * float2(1.0 / 6.5, 1.0 / 38) + f3 * 0.22 * fl.yz * float2(1.0 / 1.7, 1.0 / 11));
    float gully = f1 * (1 - smoothstep(0.02, 0.3, abs(b1.x))) + 0.6 * f2 * (1 - smoothstep(0.02, 0.25, abs(b2.x)));
    float ff = TmFade(1.0 / 9, fp), fs = TmFade(1.0 / 2.6, fp);
    float3 fw = TmNoise2(float2(u * (1.0 / 3.3), y * (1.0 / 4.1)) + 27.1);
    float2 p1 = float2(u * (1.0 / 9) + 0.35 * b2.x + 0.12 * fw.x, y * (1.0 / 12) + 0.35 * fl.x + 0.1 * fw.y);
    float2 p2 = float2(u * (1.0 / 2.6) + 0.3 * fl.x + 0.15 * fw.y, y * (1.0 / 3.2) + 0.3 * b2.x + 0.15 * fw.x);
    float2 c1 = floor(p1), c2 = floor(p2), e1 = min(p1 - c1, 1 - p1 + c1), e2 = min(p2 - c2, 1 - p2 + c2);
    float k1 = smoothstep(0, 0.1 + 0.07 * fw.x, min(e1.x, e1.y)), k2 = smoothstep(0, 0.12 + 0.08 * fw.y, min(e2.x, e2.y));
    int2 i1 = (int2)c1, i2 = (int2)c2;
    float2 t1 = float2(TmHash(int3(i1, 41)), TmHash(int3(i1, 43))) - 0.5;
    float2 t2 = float2(TmHash(int3(i2, 47)), TmHash(int3(i2, 53))) - 0.5;
    g += ff * k1 * lerp(0.45, 0.8, mass) * t1 + fs * k2 * lerp(0.15, 0.4, mass) * t2;
    tone = ff * 0.1 * (TmHash(int3(i1, 59)) - 0.5) + fs * 0.05 * (TmHash(int3(i2, 61)) - 0.5);
    float xj = u * (1.0 / 4.6) + 0.3 * TmNoise2(float2(y * (1.0 / 6), u * (1.0 / 30)) + 91.3).x + 0.12 * TmNoise2(float2(y * (1.0 / 1.7), u * (1.0 / 9)) + 15.9).x + 0.06 * fl.x;
    float ij = floor(xj + 0.5), e = xj - ij;
    float3 jp = TmNoise2(float2(y * (1.0 / 8), ij * 4.7 + 3));
    float pj = smoothstep(-0.25, 0.3, jp.x + 0.5 * (TmHash(int2((int)ij, 67)) - 0.5));
    float fj = TmFade(2, fp), fjf = TmFade(0.4, fp);
    float3 jr = fj * TmNoise2(float2(u * 3.1, y * 2.6) + 77.7);
    float3 jv = TmNoise2(float2(y * (1.0 / 4), ij * 2.9 + 11));
    float wj = 0.024 * lerp(0.5, 1.7, saturate(0.5 + 0.6 * jv.x)), dj;
    float dpj = 0.4 * lerp(0.5, 1.2, hard) * pj * fj;
    float hj = dpj * TmChip(abs(e), wj, wj * 0.5 * jr.x, dj);
    g += dpj * dj * (float2((e < 0 ? -1 : 1) * (1.0 / 4.6), 0) + wj * 0.5 * jr.yz * float2(3.1, 2.6));
    h += hj;
    float3 je = TmNoise2(float2(u * 0.9, y * 0.7) + 49.1);
    float lw0 = lerp(0.12, 0.55, saturate(0.5 + 0.6 * jv.x)) * (1 + 0.45 * je.x), lw = lw0 + 1.5 * fp;
    float jl = fjf * pj * saturate(0.55 + 0.9 * je.x) * (lw0 / lw) * (1 - smoothstep(0, lw, abs(e) * 4.6 + 0.2 * lw0 * jr.x));
    cav = saturate(0.6 * gully + 0.7 * jl - 0.15 * h);
    float3 st1 = TmNoise2(float2(u * (1.0 / 0.9), y * (1.0 / 24)) + 13.3);
    float3 st2 = TmNoise2(float2(u * (1.0 / 3.8), y * (1.0 / 60)) + 29.9);
    float lf = frac(y * (1.0 / 6.5) + 0.6 * mw.x + 0.3 * b2.x);
    streak = smoothstep(0.05, 0.6, 0.5 * TmFade(1.0 / 0.9, fp) * st1.x + 0.6 * st2.x + 0.4 * gully) * lerp(0.3, 1, lf * lf);
    return h;
}

void TmCrag(float2 wa, float y, float3 n, float hard, float mass, float fp, out float3 grad, out float cav, out float tone, out float streak)
{
    float a1 = dot(n.xz, tmJ1), a2 = dot(n.xz, tmJ2);
    float w1 = smoothstep(0.25, 0.75, a1 * a1 / (a1 * a1 + a2 * a2 + 1e-5)), w2 = 1 - w1;
    grad = 0; cav = 0; tone = 0; streak = 0;
    float2 g; float cv, tn, sk;
    [branch] if (w1 > 0.01)
    {
        TmCragPlane(dot(wa, tmJ2), y, fp, hard, mass, g, cv, tn, sk);
        grad += w1 * (g.x * float3(tmJ2.x, 0, tmJ2.y) + float3(0, g.y, 0));
        cav += w1 * cv; tone += w1 * tn; streak += w1 * sk;
    }
    [branch] if (w2 > 0.01)
    {
        TmCragPlane(dot(wa, tmJ1) + 517.3, y, fp, hard, mass, g, cv, tn, sk);
        grad += w2 * (g.x * float3(tmJ1.x, 0, tmJ1.y) + float3(0, g.y, 0));
        cav += w2 * cv; tone += w2 * tn; streak += w2 * sk;
    }
}

float TmStrataWarp(float2 xz, float h)
{
    float dip = 7 * TmNoise2(xz * (1.0 / 420) + 3.1).x;
    float thick = 3.5 * TmNoise2(float2(h * (1.0 / 23), dot(xz, float2(0.8, 0.6)) * (1.0 / 900)) + 11.7).x;
    float broad = 5 * TmNoise2(float2(h * (1.0 / 61), dot(xz, float2(0.3, -0.95)) * (1.0 / 1300)) + 47.9).x;
    float fine = 0.6 * TmNoise2(float2(h * (1.0 / 9), dot(xz, float2(-0.6, 0.8)) * (1.0 / 500)) + 29.3).x;
    return dip + broad + thick + fine;
}

TmOut TmRock(float3 wp, float3 n, float3 v, float fp, float off, float2 dux, float2 duy, float stone)
{
    float4 ra = _StabbyRockA, sw = _StabbyStrataWobble;
    float period = max(_StabbyStrataPeriod, 1);
    float layered = 1 - stone;
    float sed = smoothstep(0.3, 0.7, abs(frac(off * _StabbyRockC.y) - 0.5) * 2), mass = 1 - sed;
    off += TmStrataWarp(wp.xz + tmO.xz, wp.y + off);
    float fw = TmFade(sw.y, fp) * layered;
    float3 wb = sw.x * fw * TmN2((wp.xz + tmO.xz) * sw.y + 17.3, fw) * float3(1, sw.y, sw.y);
    float level = wp.y + off + wb.x;
    float4 st = SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(level / period, 0.5), dux, duy);
    float hard = lerp(0.82, st.a, sed);
    float sf = smoothstep(-0.35, 0.45, TmNoise2(float2(dot(wp.xz + tmO.xz, float2(0.8, 0.6)) * (1.0 / 240), level * (1.0 / 38)) + 71.3).x);
    float bk = layered * lerp(0.08, 1, sed) * lerp(0.3, 1, sf);
    float3 g;
    float bed, bedF;
    float3 q = wp;
    float nl = length(n.xz);
    float2 ja = float2(dot(n.xz, tmJ1), dot(n.xz, tmJ2));
    float2 jc = abs(n.y * ja) / (max(nl, 1e-3) * sqrt(max(1 - ja * ja, 1e-4)));
    float2 jw = lerp(1, smoothstep(0.85, 0.6, jc), smoothstep(0.1, 0.3, nl)) * smoothstep(0.35, 0.7, nl);
    float pk = _StabbyRockC.x * saturate(2 - fp * _StabbySurface.y);
    if (pk > 0)
    {
        float nv = max(dot(v, n), 0.55);
        for (int k = 0; k < 3; k++)
        {
            float hq = TmRockRelief(q.xz + tmO.xz, q.y + off + wb.x, wb.yz, hard, fp, layered, bk, jw, g, bed, bedF);
            q = wp + v * (0.7 * hq * pk / nv);
        }
    }
    float hr = TmRockRelief(q.xz + tmO.xz, q.y + off + wb.x, wb.yz, hard, fp, layered, bk, jw, g, bed, bedF);
    float ck = smoothstep(0.3, 0.7, nl) * (layered + 0.7 * stone) * _StabbyRockC.z;
    float3 crg = 0;
    float cav = 0, ctone = 0, streak = 0;
    [branch] if (ck > 0) TmCrag(q.xz + tmO.xz, q.y, n, hard, mass, fp, crg, cav, ctone, streak);
    float crs = ck * lerp(0.8, 1.3, mass);
    float4 bump = TmFbm3(q, ra.w, ra.z, 3, fp);
    float gw = TmFade(ra.y, fp), fw2 = TmFade(ra.y * 2.9, fp);
    float4 grain = TmLN3(q, ra.y, 5.1, gw);
    float4 fine = TmLN3(q, ra.y * 2.9, 11.7, fw2);
    float fw3 = TmFade(ra.y * 7.3, fp);
    float4 micro = TmLN3(q, ra.y * 7.3, 53.3, fw3);
    TmOut o;
    o.grad = g + crs * crg + (0.4 - 0.1 * stone) * bump.yzw + 0.3 * ra.x * (gw * grain.yzw + 0.5 * fw2 * fine.yzw + 0.3 * fw3 * micro.yzw);
    float speck = smoothstep(-0.6, 0.6, fine.x) - 0.5;
    float3 lw = float3(0.2126, 0.7152, 0.0722);
    float3 sm = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(0.5, 0.5), 12).rgb;
    float3 sw2 = lerp(sm, dot(sm, lw) * float3(1.05, 1, 0.9), 0.4);
    float3 sc = lerp(st.rgb, sw2, (1 - (1 - 0.62 * smoothstep(0.12, 1.6, fp)) * (0.12 + 0.38 * bk / max(layered, 1e-3))) * layered);
    float sl = max(dot(sc, lw), 1e-3), warm = saturate((sc.r - sc.b) / sl * 4 + 0.35), pinkish = saturate((sc.r - sc.g) / sl * 4);
    float3 pal = lerp(float3(1.02, 1, 0.95), lerp(lerp(float3(1.16, 1.04, 0.8), float3(1.13, 1.05, 0.74), saturate((sc.g - sc.b) / sl * 3 - 0.5)), float3(1.1, 1.01, 0.86), pinkish), warm);
    float sl2 = lerp(dot(sw2, lw), sl, 0.06 + 0.32 * bk);
    sc = lerp(sc, sl2 * pal, 0.8 * layered);
    sc = lerp(sc, dot(sc, lw) * float3(1.02, 1, 0.97), 0.85 * stone);
    float3 c = sc * (1 + ra.x * gw * grain.x) * (1 + ra.x * 0.9 * fw2 * speck) * (1 + 0.06 * bk * TmFade(_StabbyRockB.x, fp) * (lerp(TmHash(int2((int)bed, 11)), TmHash(int2((int)bed + 1, 11)), smoothstep(0.7, 1, bedF)) * 2 - 1)) * (1 + crs * ctone);
    float fh = TmFade(0.12, fp);
    float4 drift = TmLN3(q, 0.12, 37.9, fh);
    float4 drift2 = TmLN3(q, 0.31, 41.3, fh);
    c *= 1 + fh * (0.1 * drift2.x + 0.05 * drift.x * float3(1, 0.15, -0.85) + 0.025 * drift2.x * float3(-0.3, 0.6, -0.3));
    float lam = (q.y + off + wb.x) * _StabbyRockB.x * 4 + 0.37 * bed + 0.3 * drift2.x + 0.08 * gw * grain.x, il = floor(lam), fl = lam - il;
    float flam = TmFade(_StabbyRockB.x * 4, fp) * (layered + 0.35 * stone);
    int lb = (int)bed * 7 + 13;
    float lt = lerp(TmHash(int2((int)il, lb)), TmHash(int2((int)il + 1, lb)), smoothstep(0.7, 1, fl)) * 2 - 1;
    float sa = dot(q.xz + tmO.xz, float2(0.8, 0.6)), kl = floor(lam + 0.5), el = lam - kl;
    float3 ln = TmNoise2(float2(sa * 0.53, kl * 2.9) + 31.7);
    float wl0 = 0.05 * (1 + 0.6 * ln.x), wl = wl0 + fp * _StabbyRockB.x * 4;
    float pt = step(0.62, TmHash(int2((int)kl, lb + 1))) * saturate(0.4 + 1.2 * ln.x) * (wl0 / wl) * (1 - smoothstep(0, wl, abs(el + 0.03 * ln.y)));
    c *= 1 + flam * lerp(0.35, 0.6, bk) * (0.1 * lt - 0.12 * pt);
    float fm = TmFade(ra.y * 5, fp);
    float4 mn = TmLN3(q * float3(1, 3.5, 1), ra.y * 5, 43.1, fm);
    float dm = fm * lerp(0.1, 1.8, smoothstep(-0.25, 0.35, 0.6 * drift.x + 0.4 * drift2.x)), gs = gw * grain.x * 0.5;
    float lgt = dm * (1 + gs) * smoothstep(0.35, 0.75, mn.x), drk = dm * (1 - gs) * smoothstep(0.35, 0.75, -mn.x);
    c = lerp(c, dot(c, lw) * 1.6, 0.2 * lgt) * (1 - 0.17 * drk);
    c *= (1 - 0.3 * saturate(-hr / (_StabbyRockB.y * 1.2 + 1e-4))) * (1 - 0.35 * crs * cav) * (1 - 0.4 * crs * streak * float3(1, 0.99, 0.97));
    c *= 1 + 0.3 * (1 - 0.5 * stone) * bump.x * ra.w;
    float up = saturate(n.y), dn = saturate((0.05 - n.y) * 2.5);
    float cl = dot(c, lw);
    c = lerp(c, cl * 1.15, 0.3 * up * up);
    c = lerp(c, cl, 0.6 * dn) * lerp(1, float3(0.94, 0.97, 1.03), dn);
    float steep = saturate(1 - n.y * n.y);
    float fs = steep * TmFade(1.3, fp);
    float sn = TmLN3(q * float3(1, 0.06, 1), 1.3, 23.7, fs).x;
    float3 sv = TmNoise2(float2(sa * 0.29, bed * 3.7) + 61.3);
    float fb = saturate(bedF + 0.05 * sv.x), wv = 0.06 + 0.1 * saturate(0.5 + 0.7 * sv.y) + fp * _StabbyRockB.x;
    float seep = fs * (0.3 + 0.7 * bk) * smoothstep(0.2, 0.9, fb) * smoothstep(1, 1 - wv, fb) * smoothstep(0.2, 0.5, sn + 0.25 * gw * grain.x);
    c *= (1 - 0.1 * fs * layered * saturate(sn)) * (1 - 0.5 * seep * float3(1, 1.03, 1.08));
    float lic = layered * TmFade(1.1, fp) * smoothstep(-0.2, 0.55, n.y) * smoothstep(0.05, 0.35, drift2.x + 0.35 * gw * grain.x + 0.25 * mn.x);
    c = lerp(c, dot(c, lw) * float3(0.96, 1.02, 0.86) * (1.12 + 0.12 * mn.x), 0.35 * lic);
    [branch] if (stone > 0)
    {
        float fwt = TmFade(0.9, fp), fcr = TmFade(3.6, fp);
        float4 weather = TmLN3(q, 0.9, 3.3, fwt);
        float4 gate = TmLN3(q, 0.22, 7.7, fcr);
        float4 crack = TmLN3(q, 0.45, 23.1, fcr);
        c *= 1 - stone * 0.12 * fs * saturate(TmLN3(q * float3(1, 0.057, 1), 2.1, 9.7, fs).x);
        float w0 = 0.035 * (0.5 + saturate(0.5 + 0.8 * weather.x)), w = w0 + 0.3 * fp;
        float ax = abs(crack.x + 0.012 * fwt * weather.y), tc = saturate(ax / w), cg = fcr * smoothstep(-0.1, 0.3, gate.x) * (w0 / w);
        float seam = (1 - tc * tc * (3 - 2 * tc)) * cg;
        float dl = cg * 6 * tc * (1 - tc) / w * (crack.x < 0 ? -1 : 1) * TmQ(0.45);
        o.grad += stone * 0.08 * dl * crack.yzw;
        float pit = smoothstep(0.1, 0.6, -bump.x * ra.w - 0.25 * fwt * weather.x);
        c *= 1 + stone * (0.12 * fwt * weather.x - 0.75 * seam - 0.12 * pit);
        float cs = dot(c, lw);
        c = lerp(c, cs * float3(1.04, 1, 0.93), 0.8 * stone);
        o.grad += stone * 0.06 * fwt * weather.yzw;
    }
    o.albedo = c;
    tmRough = LookRough() + _StabbyLook.w * (0.55 - hard + 0.4 * gw * grain.x);
    return o;
}

TmOut TmRock(float3 wp, float3 n, float3 v, float fp, float off, float2 dux, float2 duy)
{
    return TmRock(wp, n, v, fp, off, dux, duy, 0);
}

float3 TmBlade(float2 p, float2 d, float f, float3 w, out float2 grad)
{
    float2 du = round(d * f * 410) * (1.0 / 1024), dv = round(float2(-d.y, d.x) * f * 1024) * (1.0 / 1024);
    float2 ot = tmO.xz * (1.0 / 1024);
    float3 n = TmNoise2(float2(dot(p, du), dot(p, dv)) + w.x * float2(0.3, 0.6), int2(dot(ot, du * 1024), dot(ot, dv * 1024)));
    grad = n.y * (du + 0.3 * w.yz) + n.z * (dv + 0.6 * w.yz);
    return n;
}

float TmChroma(float3 c)
{
    float hi = max(c.r, max(c.g, c.b));
    return (hi - min(c.r, min(c.g, c.b))) / max(hi, 1e-3);
}

float3 TmGrassGreen(float3 base)
{
    float lum = dot(base, float3(0.2126, 0.7152, 0.0722));
    float3 g = lerp(lum.xxx, base, 0.58) * float3(0.97, 0.93, 0.78);
    float dry = smoothstep(0.6, 1.0, base.r / max(base.g, 1e-3));
    return lerp(g, dot(g, float3(0.2126, 0.7152, 0.0722)) * float3(1.08, 0.98, 0.66), 0.75 * dry);
}

TmOut TmGrass(float3 wp, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz, pa = p + tmO.xz;
    float3 bump = TmFbm2(p, A.y, A.x, 3, fp);
    float fc = TmFade(B.y, fp), ft = TmFade(B.y * 2.6, fp);
    float3 clump = TmLN2(p, B.y, 3.7, fc);
    float3 tuft = TmLN2(p, B.y * 2.6, 8.3, ft);
    float fb = TmFade(A.w, fp);
    float3 blade = 0;
    float2 bg = 0;
    [branch] if (fb > 0)
    {
        blade = TmBlade(p, _StabbySurface.zw, A.w, float3(clump.x, clump.yz * TmQ(B.y)), bg);
        bg /= A.w;
    }
    float3 g2 = float3(bump.y + B.x * (fc * clump.y + 0.5 * ft * tuft.y), 0, bump.z + B.x * (fc * clump.z + 0.5 * ft * tuft.z));
    g2 += A.z * fb * float3(bg.x, 0, bg.y);
    float3 green = TmGrassGreen(base);
    float hv = fc * clump.x + 0.5 * TmNoise2(pa * _StabbyStrataWobble.w * 1.3 + 23.7).x;
    green *= 1 + 0.06 * hv * float3(1, 0.15, -1);
    float w = _StabbyStrataWobble.w;
    float hn = 0.65 * TmNoise2(pa * w * 2.7 + 5.3).x + 0.35 * TmNoise2(pa * w * 0.6 + 1.9).x;
    float3 c = green * (1 + B.w * 2.5 * hn);
    float fpa = TmFade(w, fp);
    c *= 1 + _StabbyStrataWobble.z * fpa * TmN2(pa * w, fpa).x;
    c *= 1 - 0.3 * fc * (1 - smoothstep(-0.5, 0.3, clump.x));
    c *= 1 + 0.3 * B.w * ft * (smoothstep(-0.6, 0.6, tuft.x) - 0.5);
    float bl = smoothstep(-0.45, 0.45, blade.x);
    c *= 1 + B.w * 1.8 * fb * (bl - 0.5);
    c = lerp(c, green * 1.2, fb * 0.35 * saturate(blade.x * 2 - 1.2) * saturate(B.w * 4));
    float2 dw = mul(float2x2(0.8, -0.6, 0.6, 0.8), pa);
    float dirt = smoothstep(0.35, 1.0, TmNoise2(dw * 0.035 + 31.3).x + 0.5 * TmNoise2(dw * 0.09 + 7.1).x) * 0.12;
    c = lerp(c, float3(0.085, 0.062, 0.04), dirt);
    TmOut o;
    o.grad = g2 * (1 - 0.6 * dirt);
    o.albedo = c;
    return o;
}

float TmPebbles(float2 p, float freq, float amp, int salt, out float2 grad, out float ring, out float tint)
{
    float q = TmQ(freq);
    float2 s = p * q, cl = floor(s), f = s - cl;
    int2 ci = (int2)cl + TmShift(q);
    float2 ctr = 0.25 + 0.5 * float2(TmHash(int3(ci, salt)), TmHash(int3(ci, salt + 1)));
    float r = 0.18 + 0.17 * TmHash(int3(ci, salt + 2));
    float2 dv = f - ctr;
    float d2 = dot(dv, dv) / (r * r), sq = sqrt(max(1 - d2, 0.02));
    float inside = step(d2, 1);
    grad = -amp * inside * dv / (r * sq);
    ring = saturate(1 - abs(sqrt(d2) - 1) * 6);
    tint = inside * (TmHash(int3(ci, salt + 3)) - 0.5);
    return inside * amp * r * sq / q;
}

float4 TmFleck(float2 p, float2x2 m, float f, float fp, int salt, float rMin, float rMax, float share)
{
    float w = TmFade(4 * f, fp);
    if (w <= 0) return 0;
    float2 q = mul(m, p) * f, fl = floor(q);
    int2 c = (int2)fl + (int2)(mul(m, tmO.xz) * f);
    float4 hs = float4(TmHash(int3(c, salt)), TmHash(int3(c, salt + 1)), TmHash(int3(c, salt + 2)), TmHash(int3(c, salt + 3)));
    if (hs.w > share) return 0;
    float2 u = normalize(TmGrad(c + int2(salt, 0)) + 1e-4), e = q - fl - (0.3 + 0.4 * hs.xy);
    float r = lerp(rMin, rMax, hs.z * hs.z), st = 1 + hs.w / share;
    float2 a = float2(dot(e, u), dot(e, float2(-u.y, u.x)) * st);
    float2 e2 = e - u * r * 0.6;
    float d = min(length(a), 1.3 * length(e2));
    float in1 = saturate((r - d) / max(fp * f, 0.04) + 0.5);
    float2 gq = -e * in1 / r;
    return w * float4(in1 * (hs.x * 2 - 1), mul(gq, m), in1);
}

float3 TmSnowAlbedo(float hollow)
{
    float3 s = float3(0.84, 0.88, 0.93);
    return lerp(s, s * float3(0.84, 0.91, 1.0), saturate(hollow));
}

float TmSnowLie(float2 p, float hollow, float amount, float fp)
{
    float fm = TmFade(0.16, fp);
    float s = TmLN2(p, 0.045, 81.3, 1).x + 0.5 * fm * TmLN2(p, 0.16, 83.9, fm).x + 0.8 * hollow;
    float t = 1 - 2.2 * amount, e = 0.14 + 0.15 * (1 - fm);
    return amount > 0 ? smoothstep(t - e, t + e, s) : 0;
}

float3 TmLichen(float2 p, float3 c, float fp, float share)
{
    float fl = TmFade(14, fp);
    [branch] if (fl <= 0) return c;
    float clump = saturate(1.6 * TmLN2(p, 0.37, 63.1, 1).x);
    float2 q = p + 0.16 * float2(TmLN2(p, 1.4, 61.7, 1).x, TmLN2(p, 1.4, 65.3, 1).x) + 0.035 * float2(TmLN2(p, 11, 67.9, fl).x, TmLN2(p, 11, 69.1, fl).x);
    float sh = share * clump;
    float4 a = TmFleck(q, float2x2(1, 0.3, -0.3, 1), 4.1, fp, 71, 0.04, 0.34, sh * 0.6);
    float4 b = TmFleck(q, float2x2(1, -0.5, 0.5, 1), 9.7, fp, 77, 0.05, 0.32, sh * 1.4);
    float4 d = TmFleck(q, float2x2(1, 0.8, -0.8, 1), 21, fp, 83, 0.08, 0.38, sh * 2);
    float4 m = a.w >= b.w ? a : b;
    m = m.w >= d.w ? m : d;
    float tex = 0.6 + 0.4 * TmLN2(p, 23, 73.3, fl).x;
    float op = 0.4 * m.w * fl * tex * (0.55 + 0.45 * abs(m.x) / max(m.w, 1e-3));
    return lerp(c, lerp(c, float3(0.36, 0.37, 0.3), 0.5), op);
}

TmOut TmSoil(float3 wp, float3 n, float3 v, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz;
    float fpk = TmFade(B.y, fp);
    float2 gp, gq;
    float ring, tint, ring2, tint2;
    float pk = B.z * fpk * saturate(2 - fp * _StabbySurface.y);
    if (pk > 0)
    {
        float nv = max(dot(v, n), 0.3), top = B.x * 0.35 / B.y;
        float2 q = p;
        for (int k = 0; k < 2; k++)
        {
            float hq = max(TmPebbles(q, B.y, B.x, 21, gp, ring, tint), TmPebbles(q, B.y * 2.3, B.x, 41, gq, ring2, tint2)) - top;
            q = p + v.xz * (hq * pk / nv);
        }
        p = q;
    }
    float h1 = 0, h2 = 0, fq = TmFade(B.y * 2.3, fp);
    gp = 0; gq = 0; ring = 0; ring2 = 0; tint = 0; tint2 = 0;
    [branch] if (fpk > 0) h1 = TmPebbles(p, B.y, B.x * fpk, 21, gp, ring, tint);
    [branch] if (fq > 0) h2 = TmPebbles(p, B.y * 2.3, B.x * fq, 41, gq, ring2, tint2);
    float3 clod = TmFbm2(p, A.y, A.x, 3, fp);
    float fg = TmFade(A.w, fp);
    float3 grain = TmLN2(p, A.w, 9.1, fg);
    float2 g = clod.yz + (h1 >= h2 ? gp : gq) + 0.4 * A.z * fg * grain.yz;
    float cold = 1 - smoothstep(0.22, 0.42, TmChroma(base));
    float3 soil = lerp(base, float3(0.16, 0.11, 0.07), 0.5);
    soil = lerp(soil, dot(soil, float3(0.2126, 0.7152, 0.0722)) * float3(1.02, 1.0, 0.9), 0.5 * cold);
    float3 c = soil * (1 + A.z * fg * grain.x) * (1 + (0.6 - 0.4 * cold) * clod.x * A.y);
    float3 stone = lerp(float3(0.32, 0.29, 0.25), float3(0.22, 0.2, 0.18), tint * 2 + 0.5);
    float pw = h1 >= h2 ? fpk * step(1e-6, h1) : fq * step(1e-6, h2);
    float t = h1 >= h2 ? tint : tint2;
    c = lerp(c, stone * (1 + B.w * t), pw);
    c *= 1 - 0.35 * max(ring * fpk, ring2 * fq) * (1 - pw);
    [branch] if (cold > 0.01)
    {
        c = lerp(c, TmLichen(p, c, fp, 0.3), cold);
        float lie = TmSnowLie(p, saturate(-clod.x * A.y * 4), cold * 0.5 * smoothstep(620, 950, wp.y), fp);
        c = lerp(c, TmSnowAlbedo(saturate(-clod.x * A.y * 4)), lie);
        g *= 1 - 0.8 * lie;
    }
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
    return o;
}

float TmSwash(float2 p, float hw, float fp, inout float2 g)
{
    float rel = tmShore.w * smoothstep(-0.35, 0.05, hw) * smoothstep(1.45, 0.65, hw), r = 0;
    [branch] if (rel > 0.01)
    {
        float d = hw / tmShore.z, dp = max(d, 0);
        float fw = TmFade(0.45, fp), fv = TmFade(1.4, fp);
        float3 w0 = TmLN2(p, 0.06, 71.3, 1), w1 = TmLN2(p, 0.45, 73.9, fw), w2 = TmLN2(p, 1.4, 75.1, fv);
        float lam0 = 0.42, k = 0.075, lam = lam0 + k * dp;
        float dw = d + 0.7 * lam * w0.x + 0.28 * fw * w1.x + 0.07 * fv * w2.x;
        float2 gd = tmShore.xy + 0.7 * lam * 0.06 * w0.yz + 0.28 * 0.45 * fw * w1.yz + 0.07 * 1.4 * fv * w2.yz;
        float s = dw < 0 ? dw / lam0 : log(1 + k * dw / lam0) / k;
        float li = floor(s), x = (frac(s) - 0.5) * lam;
        float hi = TmHash(int3((int)li, 0x5a1, 3));
        float br = TmLN2(p, 0.16 + 0.1 * hi, 77.7 + 9.13 * hi, 1).x;
        float gap = smoothstep(-0.3, 0.1, br + 0.45 * hi - 0.15);
        float crisp = smoothstep(16, 1, dp);
        float wid = max(lam * (0.045 + 0.05 * hi) * (0.75 + 0.45 * fv * w2.x) * (1 + 1.4 * (1 - crisp)), 0.02);
        float wx = x > 0 ? 0.55 * wid : wid, e = exp(-x * x / (wx * wx));
        float fl = TmFade(0.3 / wid, fp);
        float amp = rel * gap * (0.3 + 0.7 * crisp) * (0.6 + 0.8 * hi);
        r = amp * lerp(min(1, 1.3 * wid / lam), e, fl);
        g += amp * fl * 0.018 * (-2 * x / (wx * wx)) * e * gd;
    }
    return r;
}

TmOut TmSand(float3 wp, float3 n, float3 v, float3 base, float fp, float shore, float hw, float4 A, float4 B)
{
    float2 p = wp.xz, wind = _StabbySurface.zw;
    float f = B.y * 0.3, k0 = frac(dot(tmO.xz, wind) * f);
    float calm = saturate((n.y - 0.85) / 0.1) * TmFade(f * 4, fp);
    float3 warp = TmN2((p + tmO.xz) * f * 0.1 + 13.1, calm);
    float inland = 1 - shore, warm = inland * smoothstep(0.36, 0.5, (base.r - base.b) / max(base.r, 1e-3));
    float ripple = B.x * calm * 1.2 * inland * smoothstep(0.9, 1.9, hw) * cos(6.2831853 * (dot(p, wind) * f + k0 + 0.5 * warp.x));
    float3 bump = TmFbm2(p, A.y, A.x, 3, fp);
    float fg = TmFade(A.w, fp);
    float3 grain = TmLN2(p, A.w, 2.9, fg);
    float3 patch = TmLN2(p, 0.15, 17.3, 1);
    float2 g = bump.yz + ripple * (wind + 0.05 * f * warp.yz) + 0.3 * A.z * fg * grain.yz;
    float fm = TmFade(7, fp);
    float3 mid = TmLN2(p, 7, 5.7, fm);
    float3 sand = lerp(base, ToLinear(_StabbySand.rgb), 0.5 + 0.5 * shore) * lerp(1, float3(1.03, 0.95, 0.78), warm);
    float3 c = sand * (1 + A.z * fg * grain.x) * (1 + B.w * patch.x) * (1 + 0.5 * B.w * fm * mid.x * float3(1, 1, 0.8));
    float sw = TmSwash(p, hw, fp, g);
    c *= 1 - sw * float3(0.3, 0.33, 0.38);
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
    return o;
}

TmOut TmSnow(float3 wp, float3 n, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz, pa = p + tmO.xz, wind = _StabbySurface.zw, side = float2(-wind.y, wind.x);
    float3 drift = TmFbm2(p, A.y, A.x, 3, fp);
    float fs = TmFade(B.y, fp);
    float3 sz = TmN2(float2(dot(pa, wind) * B.y * 0.3, dot(pa, side) * B.y) + 7.7, fs);
    float ridge = 1 - abs(sz.x), sgn = sz.x >= 0 ? -1 : 1;
    float2 sg = sgn * (sz.y * 0.3 * wind + sz.z * side);
    float fg = TmFade(A.w, fp);
    float3 grain = TmLN2(p, A.w, 4.4, fg);
    float hd = drift.x * A.y;
    float hollow = saturate(-hd * 3) + 0.4 * fs * (1 - ridge);
    float fb = TmFade(0.7, fp), ff = TmFade(2.6, fp), fx = TmFade(7, fp);
    float bm = TmLN2(p, 0.7, 85.1, fb).x;
    float fn = TmN2(float2(dot(pa, wind) * 0.9, dot(pa, side) * 2.6) + 3.1, ff).x;
    float fx3 = TmLN2(p, 7, 87.3, fx).x;
    float conv = clamp(-10 * tmCurv, -0.7, 0.7) + 1.4 * hd + 0.45 * fs * (ridge - 0.5) + 1.2 * (1 - n.y) - 0.3 * dot(n.xz, wind) - 0.6 * dot(drift.yz, wind);
    float sv = 0.22 * TmLN2(p, 0.045, 81.3, 1).x - conv + 0.2 * fb * bm + 0.13 * ff * fn + 0.07 * fx * fx3;
    float e = 0.02 + 0.35 * saturate(1.5 * fp);
    float lie = smoothstep(-e, e, sv + 0.42);
    float3 snow = TmSnowAlbedo(saturate(hollow) * (0.5 + B.w * 4)) * (1 + A.z * fg * grain.x * 0.5);
    float ft = TmFade(1.7, fp), fh = TmFade(0.33, fp), f1 = TmFade(0.9, fp), f2 = TmFade(3.2, fp), f3 = TmFade(9, fp);
    float3 tuft = TmLN2(p, 1.7, 44.1, ft);
    float hv = TmLN2(p, 0.33, 46.7, fh).x, m1 = TmLN2(p, 0.9, 51.3, f1).x, m2 = TmLN2(p, 3.2, 53.9, f2).x, m3 = TmLN2(p, 9, 57.1, f3).x;
    float3 heath = lerp(float3(0.15, 0.15, 0.072), float3(0.178, 0.13, 0.08), saturate(0.5 + 0.9 * fh * hv + 0.5 * f1 * m1));
    heath = lerp(heath, float3(0.2, 0.115, 0.068), saturate(1.6 * f2 * (m2 - 0.2)) * saturate(0.5 + f1 * m1));
    heath = lerp(heath, float3(0.155, 0.17, 0.07), 0.6 * saturate(2 * f3 * (m3 - 0.3)) * saturate(0.5 - f1 * m1));
    heath *= (1 + B.w * 3 * ft * tuft.x) * (1 + 0.16 * f3 * m3) * (1 + A.z * fg * grain.x * 0.3);
    heath = TmLichen(p, heath, fp, 0.22);
    float2 gh = 0.5 * drift.yz + 0.4 * B.x * ft * tuft.yz + 0.3 * A.z * fg * grain.yz;
    float2 gs = drift.yz + B.x * fs * sg + 0.3 * A.z * fg * grain.yz;
    float2 g = lerp(gh, gs, lie);
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = lerp(heath, snow, lie);
    return o;
}

void TmAdd(inout TmOut acc, TmOut m, float w)
{
    acc.albedo += m.albedo * w;
    acc.grad += m.grad * w;
}

float3 TmMicro(float2 p, float fp)
{
    float f1 = TmFade(0.333, fp), f2 = TmFade(1.43, fp), f3 = TmFade(4.76, fp);
    float3 s = f1 * TmLN2(p, 0.333, 41.7, f1);
    float3 c = f2 * TmLN2(p, 1.43, 5.3, f2);
    float3 g = f3 * TmLN2(p, 4.76, 17.3, f3);
    float2 d = 0.07 * s.yz + 0.05 * c.yz + 0.04 * g.yz;
    return float3(0.4 * s.x + 0.6 * c.x + 0.4 * g.x, d);
}

TmOut TmEarth(float3 wp, float3 n, float3 base, float fp)
{
    float2 p = wp.xz, pa = p + tmO.xz;
    float sn = length(n.xz);
    float2 d = n.xz / max(sn, 1e-3);
    float fr = TmFade(2.5, fp) * saturate(sn * 3);
    float rut = 0;
    float2 rg = 0;
    [branch] if (fr > 0)
    {
        float ws = 0;
        [unroll] for (int k = 0; k < 4; k++)
        {
            float2 e = k == 0 ? float2(1, 0) : k == 1 ? float2(0.7071, 0.7071) : k == 2 ? float2(0, 1) : float2(-0.7071, 0.7071);
            float2 s = float2(-e.y, e.x);
            float a = dot(d, e), wk = a * a;
            wk *= wk;
            wk *= wk;
            float3 r = TmNoise2(float2(dot(pa, e) * 0.35, dot(pa, s) * 2.5) + 11.3 * k);
            rut += wk * r.x;
            rg += wk * (r.y * 0.35 * e + r.z * 2.5 * s);
            ws += wk;
        }
        rut *= fr / ws;
        rg *= fr / ws;
    }
    float fc = TmFade(1.1, fp), fg = TmFade(7, fp), ff = TmFade(19, fp);
    float3 clod = TmLN2(p, 1.1, 8.7, fc);
    float3 grain = TmLN2(p, 7, 14.9, fg);
    float3 fine = TmLN2(p, 19, 3.3, ff);
    float4 pb1 = TmFleck(p, float2x2(1, 0.3, -0.3, 1), 9, fp, 71, 0.1, 0.26, 0.4);
    float4 pb2 = TmFleck(p, float2x2(1, -0.6, 0.6, 1), 23, fp, 79, 0.12, 0.3, 0.3);
    float3 earth = lerp(base, float3(0.15, 0.125, 0.1), 0.7);
    float3 c = earth * (1 + 0.12 * fc * clod.x + 0.1 * fg * grain.x + 0.06 * ff * fine.x) * (1 + 0.14 * rut);
    float4 pb = pb1.w >= pb2.w ? pb1 : pb2;
    c = lerp(c, float3(0.26, 0.245, 0.225) * (1 + 0.3 * pb.x), 0.8 * pb.w);
    float2 g = 0.03 * rg + 0.05 * fc * clod.yz + 0.06 * fg * grain.yz + 0.1 * pb.yz;
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
    return o;
}

void TmLitter(inout TmOut k, float3 wp, float3 base, float fp, float w)
{
    float fo = w * smoothstep(0.3, 0.2, dot(base, float3(0.2126, 0.7152, 0.0722)));
    [branch] if (fo > 0.01)
    {
        float2 p = wp.xz, g = 0;
        float nd = smoothstep(1.2, 1.4, base.b / max(base.r, 1e-3));
        float fl = TmFade(0.7, fp);
        float3 lp = TmLN2(p, 0.7, 27.1, fl);
        float lit = fo * (0.16 + 0.3 * fl * smoothstep(-0.2, 0.7, lp.x)), t = 0.45;
        float fn = TmFade(14, fp) * nd;
        [branch] if (fn > 0)
        {
            float2 g1, g2;
            float3 b1 = TmBlade(p, float2(0.8, 0.6), 14, 0, g1);
            float3 b2 = TmBlade(p, float2(-0.6, 0.8), 19, 0, g2);
            t += fn * 0.35 * (max(b1.x, b2.x) - 0.2);
            g += fn * 0.04 * (b1.x > b2.x ? g1 / 14 : g2 / 19);
        }
        float fv = 1 - nd;
        [branch] if (fv > 0.01)
        {
            float4 a = TmFleck(p, float2x2(1, 0.4, -0.4, 1), 6, fp, 53, 0.2, 0.42, 0.7);
            float4 b = TmFleck(p, float2x2(1, -0.6, 0.6, 1), 9, fp, 59, 0.16, 0.34, 0.55);
            float4 s = a.w >= b.w ? a : b;
            lit = lerp(lit, fo * 0.9, fv * s.w * (0.5 + 0.5 * fl * smoothstep(-0.5, 0.5, lp.x)));
            t = lerp(t, 0.5 + 0.45 * s.x / max(s.w, 1e-3), fv * s.w);
            g += fv * 0.08 * s.yz;
        }
        float3 dk = lerp(float3(0.085, 0.052, 0.026), float3(0.075, 0.042, 0.022), nd);
        float3 lt = lerp(float3(0.19, 0.12, 0.05), float3(0.15, 0.088, 0.045), nd);
        k.albedo = lerp(k.albedo, lerp(dk, lt, saturate(t)), lit);
        k.grad += fo * float3(g.x, 0, g.y);
    }
}

void TmStoneCover(inout TmOut o, float3 wp, float3 n, float4 cw, float fp, float stone, float m)
{
    float fm = TmFade(0.6, fp), fl = TmFade(5.2, fp);
    float4 patch = TmLN3(wp, 0.6, 51.3, fm);
    float4 streak = TmLN3(wp * float3(1, 0.12, 1), 5.2, 61.7, fl);
    float top = smoothstep(0.45, 0.9, n.y), side = 1 - top, wet = cw.x + 0.4 * cw.y, dry = cw.z + 0.5 * cw.y;
    float moss = stone * wet * top * smoothstep(-0.15, 0.35, fm * patch.x + 0.2 * fl * streak.x);
    float lichen = stone * saturate(wet + 0.3 * cw.z) * smoothstep(-0.2, 0.5, n.y) * fm * smoothstep(0.1, 0.45, patch.x + 0.3 * fl * streak.x) * (1 - moss);
    float stain = stone * saturate(0.4 + 0.4 * dry + 0.3 * wet) * side * fl * smoothstep(0.15, 0.6, streak.x) * smoothstep(-0.4, 0.2, -patch.x) * (1 - moss);
    float dust = stone * dry * top * fm * smoothstep(0, 0.5, patch.x) * (1 - moss);
    float frost = stone * cw.w * smoothstep(0.55, 0.85, n.y);
    float3 mossC = lerp(float3(0.045, 0.07, 0.02), float3(0.09, 0.11, 0.03), saturate(0.5 + fm * patch.y));
    float lum = dot(o.albedo, float3(0.2126, 0.7152, 0.0722));
    o.albedo = lerp(o.albedo, m * mossC, moss);
    o.albedo = lerp(o.albedo, lum * float3(1.06, 1.06, 0.9) * 1.2, 0.4 * lichen);
    o.albedo *= 1 - 0.22 * stain;
    o.albedo = lerp(o.albedo, m * ToLinear(_StabbySand.rgb), 0.3 * dust);
    o.albedo = lerp(o.albedo, m * float3(0.75, 0.79, 0.84), frost);
    o.grad *= 1 - 0.6 * moss - frost;
    tmRough = lerp(tmRough, _StabbyCoatRough.w, frost) + _StabbyLook.w * (moss + 0.5 * lichen);
}

TmOut TmEvaluate(float3 wp, float3 n, float3 coatColor, float off, float rockMix, float stone)
{
    float3 grassColor = coatColor;
    float2 o2 = TmOrigin(_WorldSpaceCameraPos);
    tmO = float3(o2.x, 0, o2.y);
    float3 dx = ddx(wp), dy = ddy(wp);
    float la = length(dx), lb = length(dy);
    float fp = max(max(sqrt(la * lb), 0.4 * max(la, lb)), 1e-4);
    float face = abs(normalize(cross(dx, dy) + float3(0, 1e-9, 0)).y);
    float period = max(_StabbyStrataPeriod, 1);
    float u0 = (wp.y + off) / period;
    float2 dux = float2(ddx(u0), 0), duy = float2(ddy(u0), 0);
    float3 v = normalize(_WorldSpaceCameraPos - tmO - wp);
    float2 wa = wp.xz + tmO.xz;
    float fj = TmFade(0.27, fp);
    float3 jn = TmN2(wa * 0.27 + 41.7, fj);
    float4 cw = SAMPLE_TEXTURE2D_LOD(_StabbyCoat, sampler_StabbyCoat, wa * _StabbyCoatUV.x + _StabbyCoatUV.y, 0);
    float hw = wp.y - SAMPLE_TEXTURE2D_LOD(_StabbyWaterLevel, sampler_StabbyWaterLevel, wa * _StabbyCoatUV.x + _StabbyCoatUV.y, 0).r;
    float sh = wp.y + 0.45 * TmLN2(wp.xz, 0.035, 19.3, step(wp.y, 3)).x;
    float shore = smoothstep(2.1, 0.9, sh);
    coatColor = lerp(coatColor, ToLinear(_StabbySand.rgb), smoothstep(2.8, 1.2, sh));
    cw = lerp(cw, float4(0, 0, 1, 0), shore);
    cw = saturate(cw * (1 + 0.8 * fj * (1 - shore) * float4(jn.x, -jn.x, jn.x * 0.3, -jn.x)));
    cw /= max(dot(cw, 1), 1e-3);
    cw *= cw * cw;
    cw /= max(dot(cw, 1), 1e-6);
    float fk = TmFade(1.3, fp);
    float3 jk = TmN2(wa * 1.3 + 13.9, fk);
    float br = 0.3 * fj * jn.x + 0.35 * fk * jk.x;
    float sl = min(n.y, face);
    float m = saturate(max((rockMix - 0.5) * 1.6 + 0.5 + br, 1 - smoothstep(0.5, 0.72, sl + 0.4 * br)));
    TmOut o;
    tmRough = LookRough();
    [branch] if (_StabbyDetailOff > 0)
    {
        float3 st = SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(u0, 0.5), dux, duy).rgb;
        o.albedo = lerp(coatColor, st, m);
        o.grad = n;
        return o;
    }
    o.albedo = 0;
    o.grad = 0;
    tmGrass = 0;
    if (m > 0.01) TmAdd(o, TmRock(wp, n, v, fp, off, dux, duy, stone), m);
    [branch] if (stone > 0.01) TmStoneCover(o, wp, n, cw, fp, stone, m);
    float c = 1 - m;
    float3 mr = 0;
    float scr = saturate(6 * m * (1 - m)) * smoothstep(0.95, 0.6, sl);
    float up = saturate((sl - 0.3) / 0.25), ew = 1 - saturate(cw.z + cw.w);
    TmOut e;
    e.albedo = coatColor;
    e.grad = 0;
    [branch] if (ew > 0.01 && ((up < 0.99 && c > 0.01) || scr > 0.01)) e = TmEarth(wp, n, coatColor, fp);
    float3 bank = lerp(coatColor, e.albedo, ew);
    tmRough = m * tmRough + c * (dot(cw, _StabbyCoatRough) + 0.5 * _StabbyLook.w * br);
    if (c > 0.01)
    {
        TmOut k;
        k.albedo = 0;
        k.grad = 0;
        if (cw.x > 0.02) TmAdd(k, TmGrass(wp, grassColor, fp, _StabbyCoatA[0], _StabbyCoatB[0]), cw.x);
        if (cw.y > 0.02) TmAdd(k, TmSoil(wp, n, v, coatColor, fp, _StabbyCoatA[1], _StabbyCoatB[1]), cw.y);
        if (cw.z > 0.02) TmAdd(k, TmSand(wp, n, v, coatColor, fp, shore, hw, _StabbyCoatA[2], _StabbyCoatB[2]), cw.z);
        if (cw.w > 0.02) TmAdd(k, TmSnow(wp, n, coatColor, fp, _StabbyCoatA[3], _StabbyCoatB[3]), cw.w);
        float kept = (cw.x > 0.02 ? cw.x : 0) + (cw.y > 0.02 ? cw.y : 0) + (cw.z > 0.02 ? cw.z : 0) + (cw.w > 0.02 ? cw.w : 0);
        k.albedo += lerp(coatColor, grassColor, cw.x) * (1 - kept);
        TmLitter(k, wp, grassColor, fp, cw.x);
        mr = TmMicro(wp.xz, fp) * dot(cw, float4(1, 1, 0.3, 0.5));
        k.albedo *= 1 + 0.1 * mr.x;
        k.grad += float3(mr.y, 0, mr.z);
        float bare = saturate(cw.y + cw.z) * (1 - cw.x);
        [branch] if (bare > 0.01)
        {
            float4 g1 = TmFleck(wp.xz, float2x2(1, 0, 0, 1), 41, fp, 23, 0.12, 0.32, 1);
            float4 g2 = TmFleck(wp.xz, float2x2(1, 0.5, -0.5, 1), 13, fp, 31, 0.15, 0.38, 0.55);
            float4 g3 = TmFleck(wp.xz, float2x2(1, -0.75, 0.75, 1), 19, fp, 37, 0.12, 0.25, 0.12);
            k.albedo *= 1 + bare * (0.06 * g1.x + 0.05 * g2.x - 0.12 * g3.w);
            k.grad += bare * 0.12 * float3(g2.y, 0, g2.z);
        }
        tmGrass = c * up * cw.x;
        o.albedo += c * lerp(bank * (1 + 0.1 * mr.x), k.albedo, up);
        o.grad += c * (up * k.grad + (1 - up) * ew * e.grad);
    }
    [branch] if (scr > 0.01)
    {
        float3 sm = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(0.5, 0.5), 12).rgb;
        float3 sc = lerp(bank, dot(sm, float3(0.2126, 0.7152, 0.0722)) * float3(1.04, 1, 0.92), 0.55 - 0.25 * ew);
        o.albedo = lerp(o.albedo, sc * (1 + 0.3 * mr.x * (1 - ew)), 0.55 * scr);
        o.grad += 0.55 * scr * ew * e.grad;
    }
    float wet = 0;
    [branch] if (hw < 2.4)
    {
        float fw = TmFade(0.8, fp);
        float3 tide = TmLN2(wp.xz, 0.11, 7.7, 1);
        float3 dry = TmLN2(wp.xz, 0.8, 29.1, fw);
        float t = saturate(hw / (1.75 + 0.35 * tide.x));
        float edge = smoothstep(0.2, 1, t);
        wet = (1 - smoothstep(0.12, 1, t)) * saturate(1 + edge * (0.5 * fw * dry.x + 0.35 * tide.x));
        o.albedo *= 1 - 0.38 * wet;
        tmRough *= 1 - 0.55 * wet;
    }
    float3 g = (o.grad - n * dot(o.grad, n)) * (1 - 0.5 * wet);
    o.grad = normalize(n - g);
    tmRough = saturate(tmRough);
    return o;
}

#endif
