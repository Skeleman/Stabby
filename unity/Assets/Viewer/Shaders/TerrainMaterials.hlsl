#ifndef STABBY_TERRAIN_MATERIALS
#define STABBY_TERRAIN_MATERIALS

float4 _StabbySurface, _StabbyStrataWobble, _StabbyRockA, _StabbyRockB, _StabbyRockC, _StabbyCoatUV;
float4 _StabbyCoatA[4], _StabbyCoatB[4];
float _StabbyDetailOff;
TEXTURE2D(_StabbyCoat);
SAMPLER(sampler_StabbyCoat);

static float3 tmO;

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

float TmRockRelief(float2 qa, float level, float2 levelGrad, float hard, float fp, float layered, out float3 grad, out float bed)
{
    float4 rb = _StabbyRockB;
    float b = level * rb.x, ib = floor(b), s;
    bed = ib;
    float depth = layered * rb.y * lerp(1.6, 0.5, hard) * lerp(0.6, 1.4, TmHash(int2((int)ib, 7))) * TmFade(rb.x, fp);
    float hb = depth * TmGroove(b - ib, 0.16, s);
    grad = depth * s * rb.x * float3(levelGrad.x, 1, levelGrad.y);
    float jd = layered * rb.w * smoothstep(0.35, 0.85, hard) * TmFade(rb.z * 8, fp);
    if (jd <= 0) return hb;
    float2 d1 = float2(0.96, 0.28), d2 = float2(-0.28, 0.96);
    float j = dot(qa, d1) * rb.z + TmHash(int2((int)ib, 3)) * 4, ij = floor(j);
    float hj = jd * TmGroove(j - ij, 0.035, s);
    grad += jd * s * rb.z * float3(d1.x, 0, d1.y);
    float k = dot(qa, d2) * rb.z * 0.6 + TmHash(int2((int)ib, 5)) * 4, s2;
    float m = step(0.45, TmHash(int3((int)ib, (int)ij, 9)));
    float hk = jd * m * TmGroove(k - floor(k), 0.05, s2);
    grad += jd * m * s2 * rb.z * 0.6 * float3(d2.x, 0, d2.y);
    return hb + min(hj, hk);
}

TmOut TmRock(float3 wp, float3 n, float3 v, float fp, float off, float2 dux, float2 duy, float stone)
{
    float4 ra = _StabbyRockA, sw = _StabbyStrataWobble;
    float period = max(_StabbyStrataPeriod, 1);
    float layered = 1 - stone;
    float fw = TmFade(sw.y, fp) * layered;
    float3 wb = sw.x * fw * TmN2((wp.xz + tmO.xz) * sw.y + 17.3, fw) * float3(1, sw.y, sw.y);
    float level = wp.y + off + wb.x;
    float4 st = SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(level / period, 0.5), dux, duy);
    float hard = st.a;
    float3 g;
    float bed;
    float3 q = wp;
    float pk = _StabbyRockC.x * saturate(2 - fp * _StabbySurface.y);
    if (pk > 0)
    {
        float nv = max(dot(v, n), 0.3);
        for (int k = 0; k < 3; k++)
        {
            float hq = TmRockRelief(q.xz + tmO.xz, q.y + off + wb.x, wb.yz, hard, fp, layered, g, bed);
            q = wp + v * (hq * pk / nv);
        }
    }
    float hr = TmRockRelief(q.xz + tmO.xz, q.y + off + wb.x, wb.yz, hard, fp, layered, g, bed);
    float4 bump = TmFbm3(q, ra.w, ra.z, 3, fp);
    float gw = TmFade(ra.y, fp), fw2 = TmFade(ra.y * 2.9, fp);
    float4 grain = TmLN3(q, ra.y, 5.1, gw);
    float4 fine = TmLN3(q, ra.y * 2.9, 11.7, fw2);
    TmOut o;
    o.grad = g + bump.yzw + 0.3 * ra.x * (gw * grain.yzw + 0.5 * fw2 * fine.yzw);
    float speck = smoothstep(-0.6, 0.6, fine.x) - 0.5;
    float3 c = st.rgb * (1 + ra.x * gw * grain.x) * (1 + ra.x * 0.9 * fw2 * speck) * (1 + 0.12 * TmFade(_StabbyRockB.x, fp) * (TmHash(int2((int)bed, 11)) * 2 - 1));
    c *= 1 - 0.5 * saturate(-hr / (_StabbyRockB.y * 1.2 + 1e-4));
    c *= 1 + 0.3 * bump.x * ra.w;
    float steep = saturate(1 - n.y * n.y);
    float t = dot(q.xz + tmO.xz, normalize(float2(-n.z, n.x) + 1e-5)), fs = steep * TmFade(1.3, fp);
    c *= 1 - 0.14 * fs * layered * saturate(TmN2(float2(t * 1.3, q.y * 0.08), fs).x);
    [branch] if (stone > 0)
    {
        float fwt = TmFade(0.9, fp), fcr = TmFade(3.6, fp);
        float4 weather = TmLN3(q, 0.9, 3.3, fwt);
        float4 gate = TmLN3(q, 0.22, 7.7, fcr);
        float4 crack = TmLN3(q, 0.45, 23.1, fcr);
        float w = 0.035, ax = abs(crack.x), tc = saturate(ax / w), cg = fcr * smoothstep(-0.1, 0.3, gate.x);
        float seam = (1 - tc * tc * (3 - 2 * tc)) * cg;
        float dl = cg * 6 * tc * (1 - tc) / w * (crack.x < 0 ? -1 : 1) * TmQ(0.45);
        o.grad += stone * 0.08 * dl * crack.yzw;
        float pit = smoothstep(0.1, 0.6, -bump.x * ra.w - 0.25 * fwt * weather.x);
        c *= 1 + stone * (0.22 * fwt * weather.x - 0.75 * seam - 0.3 * pit);
        o.grad += stone * 0.25 * fwt * weather.yzw;
    }
    o.albedo = c;
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
    float lum = dot(base, float3(0.2126, 0.7152, 0.0722));
    float3 green = lerp(lum.xxx, base, 0.6) * float3(1.05, 1, 0.78);
    float3 straw = lum * float3(1.45, 1.2, 0.55);
    float w = _StabbyStrataWobble.w;
    float hn = 0.65 * TmNoise2(pa * w * 2.7 + 5.3).x + 0.35 * TmNoise2(pa * w * 0.6 + 1.9).x;
    float3 c = lerp(green, straw, saturate(hn * 1.6) * B.w * 2.2);
    c *= lerp(1, float3(0.8, 0.93, 0.82), saturate(-hn * 1.6) * B.w * 3);
    float fpa = TmFade(w, fp);
    c *= 1 + _StabbyStrataWobble.z * fpa * TmN2(pa * w, fpa).x;
    c *= 1 - 0.3 * fc * (1 - smoothstep(-0.5, 0.3, clump.x));
    c *= 1 + 0.3 * B.w * ft * (smoothstep(-0.6, 0.6, tuft.x) - 0.5);
    float bl = smoothstep(-0.45, 0.45, blade.x);
    c *= 1 + B.w * 1.8 * fb * (bl - 0.5);
    c = lerp(c, straw * 1.15, fb * 0.35 * saturate(blade.x * 2 - 1.2) * saturate(B.w * 4));
    float2 dw = mul(float2x2(0.8, -0.6, 0.6, 0.8), pa);
    float dirt = smoothstep(0.35, 1.0, TmNoise2(dw * 0.035 + 31.3).x + 0.5 * TmNoise2(dw * 0.09 + 7.1).x) * 0.3;
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
    float3 soil = lerp(base, float3(0.16, 0.11, 0.07), 0.5);
    float3 c = soil * (1 + A.z * fg * grain.x) * (1 + 0.6 * clod.x * A.y);
    float3 stone = lerp(float3(0.32, 0.29, 0.25), float3(0.22, 0.2, 0.18), tint * 2 + 0.5);
    float pw = h1 >= h2 ? fpk * step(1e-6, h1) : fq * step(1e-6, h2);
    float t = h1 >= h2 ? tint : tint2;
    c = lerp(c, stone * (1 + B.w * t), pw);
    c *= 1 - 0.35 * max(ring * fpk, ring2 * fq) * (1 - pw);
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
    return o;
}

TmOut TmSand(float3 wp, float3 n, float3 v, float3 base, float fp, float shore, float4 A, float4 B)
{
    float2 p = wp.xz, wind = _StabbySurface.zw, side = float2(-wind.y, wind.x);
    float2 pa = p + tmO.xz;
    float k0 = dot(tmO.xz, wind) * B.y + 0.3 * dot(tmO.xz, side) * B.y * 0.05;
    k0 -= floor(k0);
    float calm = saturate((n.y - 0.85) / 0.1) * TmFade(B.y * 3, fp), sharp = TmFade(B.y * 8, fp);
    float3 warp = TmN2(pa * B.y * 0.12 + 13.1, calm);
    float pk = B.z * calm * saturate(2 - fp * _StabbySurface.y);
    if (pk > 0)
    {
        float nv = max(dot(v, n), 0.3);
        float2 q = p;
        for (int k = 0; k < 2; k++)
        {
            float s0 = dot(q, wind) * B.y + k0 + 1.5 * warp.x + 0.3 * dot(q, side) * B.y * 0.05, f0 = frac(s0);
            float hq = B.x / B.y * (6.75 * f0 * f0 * (1 - f0) - 1);
            q = p + v.xz * (hq * pk / nv);
        }
        p = q;
    }
    float s = dot(p, wind) * B.y + k0 + 1.5 * warp.x + 0.3 * dot(p, side) * B.y * 0.05, fr = frac(s);
    float fm = fr * (1 - fr);
    float prof = lerp(16 * fm * fm, 6.75 * fr * fm, sharp), dprof = lerp(32 * fm * (1 - 2 * fr), 6.75 * fr * (2 - 3 * fr), sharp);
    float2 ds = wind + 1.5 * warp.yz * 0.12 + 0.015 * side;
    float3 bump = TmFbm2(p, A.y, A.x, 2, fp);
    float fg = TmFade(A.w, fp);
    float3 grain = TmLN2(p, A.w, 2.9, fg);
    float2 g = bump.yz + B.x * calm * dprof * ds + 0.3 * A.z * fg * grain.yz;
    float3 sand = lerp(base, ToLinear(_StabbySand.rgb), 0.5 + 0.5 * shore);
    float3 c = sand * (1 + A.z * fg * grain.x) * (1 - B.w * calm * (1 - prof));
    c *= 1 - 0.4 * saturate(1 - wp.y / 1.2) * step(0, wp.y);
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
    return o;
}

TmOut TmSnow(float3 wp, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz, pa = p + tmO.xz, wind = _StabbySurface.zw, side = float2(-wind.y, wind.x);
    float3 drift = TmFbm2(p, A.y, A.x, 3, fp);
    float fs = TmFade(B.y, fp);
    float3 sz = TmN2(float2(dot(pa, wind) * B.y * 0.3, dot(pa, side) * B.y) + 7.7, fs);
    float ridge = 1 - abs(sz.x), sgn = sz.x >= 0 ? -1 : 1;
    float2 sg = sgn * (sz.y * 0.3 * wind + sz.z * side);
    float fg = TmFade(A.w, fp);
    float3 grain = TmLN2(p, A.w, 4.4, fg);
    float2 g = drift.yz + B.x * fs * sg + 0.3 * A.z * fg * grain.yz;
    float3 snow = lerp(base, float3(0.82, 0.86, 0.9), 0.75);
    float hollow = saturate(-drift.x * A.y * 3) + 0.4 * fs * (1 - ridge);
    float3 c = lerp(snow, snow * float3(0.8, 0.88, 1.02), saturate(hollow) * (0.5 + B.w * 4));
    c *= 1 + A.z * fg * grain.x * 0.5;
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
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
    float shore = smoothstep(5.5, 2.5, wp.y);
    coatColor = lerp(coatColor, ToLinear(_StabbySand.rgb), smoothstep(6, 2, wp.y) * step(0, wp.y));
    cw = lerp(cw, float4(0, 0, 1, 0), shore);
    cw = saturate(cw * (1 + 0.8 * fj * (1 - shore) * float4(jn.x, -jn.x, jn.x * 0.5, -jn.x)));
    cw /= max(dot(cw, 1), 1e-3);
    float fk = TmFade(1.3, fp);
    float3 jk = TmN2(wa * 1.3 + 13.9, fk);
    float br = 0.3 * fj * jn.x + 0.35 * fk * jk.x;
    float sl = min(n.y, face);
    float m = saturate(max((rockMix - 0.5) * 1.6 + 0.5 + br, 1 - smoothstep(0.5, 0.72, sl + 0.4 * br)));
    TmOut o;
    [branch] if (_StabbyDetailOff > 0)
    {
        float3 st = SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(u0, 0.5), dux, duy).rgb;
        o.albedo = lerp(coatColor, st, m);
        o.grad = n;
        return o;
    }
    o.albedo = 0;
    o.grad = 0;
    if (m > 0.01) TmAdd(o, TmRock(wp, n, v, fp, off, dux, duy, stone), m);
    [branch] if (stone > 0.01)
    {
        float fm = TmFade(0.6, fp), fl = TmFade(5.2, fp);
        float4 patch = TmLN3(wp, 0.6, 51.3, fm);
        float4 spot = TmLN3(wp, 5.2, 61.7, fl);
        float top = smoothstep(0.45, 0.9, n.y), wet = cw.x + 0.4 * cw.y;
        float moss = stone * wet * top * smoothstep(-0.15, 0.35, fm * patch.x + 0.25 * fl * spot.x);
        float lichen = stone * saturate(wet + 0.3 * cw.z) * smoothstep(-0.2, 0.5, n.y) * fl * smoothstep(0.45, 0.65, spot.x) * smoothstep(-0.2, 0.3, patch.x) * (1 - moss);
        float frost = stone * cw.w * smoothstep(0.55, 0.85, n.y);
        float3 mossC = lerp(float3(0.045, 0.07, 0.02), float3(0.09, 0.11, 0.03), saturate(0.5 + fl * spot.x));
        o.albedo = lerp(o.albedo, m * mossC, moss);
        o.albedo = lerp(o.albedo, m * float3(0.3, 0.31, 0.22), 0.55 * lichen);
        o.albedo = lerp(o.albedo, m * float3(0.75, 0.79, 0.84), frost);
        o.grad *= 1 - 0.6 * moss - frost;
    }
    float c = 1 - m;
    if (c > 0.01)
    {
        TmOut k;
        k.albedo = 0;
        k.grad = 0;
        if (cw.x > 0.02) TmAdd(k, TmGrass(wp, grassColor, fp, _StabbyCoatA[0], _StabbyCoatB[0]), cw.x);
        if (cw.y > 0.02) TmAdd(k, TmSoil(wp, n, v, coatColor, fp, _StabbyCoatA[1], _StabbyCoatB[1]), cw.y);
        if (cw.z > 0.02) TmAdd(k, TmSand(wp, n, v, coatColor, fp, shore, _StabbyCoatA[2], _StabbyCoatB[2]), cw.z);
        if (cw.w > 0.02) TmAdd(k, TmSnow(wp, coatColor, fp, _StabbyCoatA[3], _StabbyCoatB[3]), cw.w);
        float kept = (cw.x > 0.02 ? cw.x : 0) + (cw.y > 0.02 ? cw.y : 0) + (cw.z > 0.02 ? cw.z : 0) + (cw.w > 0.02 ? cw.w : 0);
        k.albedo += coatColor * (1 - kept);
        float3 mr = TmMicro(wp.xz, fp) * dot(cw, float4(1, 1, 0.3, 0.5));
        k.albedo *= 1 + 0.1 * mr.x;
        k.grad += float3(mr.y, 0, mr.z);
        float up = saturate((sl - 0.3) / 0.25);
        o.albedo += c * lerp(coatColor, k.albedo, up);
        o.grad += c * up * k.grad;
    }
    float3 g = o.grad - n * dot(o.grad, n);
    o.grad = normalize(n - g);
    return o;
}

#endif
