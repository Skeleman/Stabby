#ifndef STABBY_TERRAIN_MATERIALS
#define STABBY_TERRAIN_MATERIALS

float4 _StabbySurface, _StabbyStrataWobble, _StabbyRockA, _StabbyRockB, _StabbyRockC, _StabbyCoatUV;
float4 _StabbyCoatA[4], _StabbyCoatB[4];
float _StabbyDetailOff;
TEXTURE2D(_StabbyCoat);
SAMPLER(sampler_StabbyCoat);

struct TmOut { float3 albedo; float3 grad; };

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

float3 TmNoise2(float2 p)
{
    float2 fl = floor(p), f = p - fl;
    int2 i = (int2)fl;
    float2 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
    float a = TmHash(i), b = TmHash(i + int2(1, 0)), c = TmHash(i + int2(0, 1)), d = TmHash(i + int2(1, 1));
    float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
    return float3(2 * (a + k1 * u.x + k2 * u.y + k4 * u.x * u.y) - 1, 2 * du * float2(k1 + k4 * u.y, k2 + k4 * u.x));
}

float4 TmNoise3(float3 p)
{
    float3 fl = floor(p), f = p - fl;
    int3 i = (int3)fl;
    float3 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
    float a = TmHash(i), b = TmHash(i + int3(1, 0, 0)), c = TmHash(i + int3(0, 1, 0)), d = TmHash(i + int3(1, 1, 0));
    float e = TmHash(i + int3(0, 0, 1)), g = TmHash(i + int3(1, 0, 1)), h = TmHash(i + int3(0, 1, 1)), k = TmHash(i + int3(1, 1, 1));
    float k1 = b - a, k2 = c - a, k3 = e - a, k4 = a - b - c + d, k5 = a - c - e + h, k6 = a - b - e + g, k7 = -a + b + c - d + e - g - h + k;
    float v = a + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z;
    float3 gr = du * float3(k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z, k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x, k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y);
    return float4(2 * v - 1, 2 * gr);
}

float TmFade(float freq, float fp) { return saturate(1 / (freq * fp * _StabbySurface.x) - 1); }

float3 TmN2(float2 p, float w)
{
    float3 r = 0;
    [branch] if (w > 0) r = TmNoise2(p);
    return r;
}

float4 TmN3(float3 p, float w)
{
    float4 r = 0;
    [branch] if (w > 0) r = TmNoise3(p);
    return r;
}

float3 TmFbm2(float2 p, float freq, float amp, int octaves, float fp)
{
    float3 s = 0;
    for (int k = 0; k < octaves; k++)
    {
        float w = TmFade(freq, fp);
        if (w <= 0) break;
        float3 n = TmNoise2(p * freq + k * 19.31);
        s += amp * w * float3(n.x / freq, n.yz);
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
        float4 n = TmNoise3(p * freq + k * 19.31);
        s += amp * w * float4(n.x / freq, n.yzw);
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

float TmRockRelief(float3 q, float level, float2 levelGrad, float hard, float fp, out float3 grad, out float bed)
{
    float4 rb = _StabbyRockB;
    float b = level * rb.x, ib = floor(b), s;
    bed = ib;
    float depth = rb.y * lerp(1.6, 0.5, hard) * lerp(0.6, 1.4, TmHash(int2((int)ib, 7))) * TmFade(rb.x, fp);
    float hb = depth * TmGroove(b - ib, 0.3, s);
    grad = depth * s * rb.x * float3(levelGrad.x, 1, levelGrad.y);
    float jd = rb.w * smoothstep(0.35, 0.85, hard) * TmFade(rb.z * 8, fp);
    if (jd <= 0) return hb;
    float2 d1 = float2(0.96, 0.28), d2 = float2(-0.28, 0.96);
    float j = dot(q.xz, d1) * rb.z + TmHash(int2((int)ib, 3)) * 4, ij = floor(j);
    float hj = jd * TmGroove(j - ij, 0.05, s);
    grad += jd * s * rb.z * float3(d1.x, 0, d1.y);
    float k = dot(q.xz, d2) * rb.z * 0.6 + TmHash(int2((int)ib, 5)) * 4, s2;
    float m = step(0.45, TmHash(int3((int)ib, (int)ij, 9)));
    float hk = jd * m * TmGroove(k - floor(k), 0.07, s2);
    grad += jd * m * s2 * rb.z * 0.6 * float3(d2.x, 0, d2.y);
    return hb + min(hj, hk);
}

TmOut TmRock(float3 wp, float3 n, float3 v, float fp, float off, float2 dux, float2 duy)
{
    float4 ra = _StabbyRockA, sw = _StabbyStrataWobble;
    float period = max(_StabbyStrataPeriod, 1);
    float fw = TmFade(sw.y, fp);
    float3 wb = sw.x * fw * TmN2(wp.xz * sw.y + 17.3, fw) * float3(1, sw.y, sw.y);
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
            float hq = TmRockRelief(q, q.y + off + wb.x, wb.yz, hard, fp, g, bed);
            q = wp + v * (hq * pk / nv);
        }
    }
    float hr = TmRockRelief(q, q.y + off + wb.x, wb.yz, hard, fp, g, bed);
    float4 bump = TmFbm3(q, ra.w, ra.z, 3, fp);
    float gw = TmFade(ra.y, fp);
    float4 grain = TmN3(q * ra.y + 5.1, gw);
    TmOut o;
    o.grad = g + bump.yzw + 0.3 * ra.x * gw * grain.yzw;
    float3 c = st.rgb * (1 + ra.x * gw * grain.x) * (1 + 0.1 * TmFade(_StabbyRockB.x, fp) * (TmHash(int2((int)bed, 11)) * 2 - 1));
    c *= 1 - 0.45 * saturate(-hr / (_StabbyRockB.y * 1.6 + 1e-4));
    c *= 1 + 0.6 * bump.x * ra.w;
    float steep = saturate(1 - n.y * n.y);
    float t = dot(q.xz, normalize(float2(-n.z, n.x) + 1e-5)), fs = steep * TmFade(1.3, fp);
    c *= 1 - 0.14 * fs * saturate(TmN2(float2(t * 1.3, q.y * 0.08), fs).x);
    o.albedo = c;
    return o;
}

TmOut TmGrass(float3 wp, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz;
    float3 bump = TmFbm2(p, A.y, A.x, 2, fp);
    float fc = TmFade(B.y, fp);
    float3 clump = TmN2(p * B.y + 3.7, fc);
    float2 d = normalize(clump.yz + float2(1e-3, 0)), e = float2(-d.y, d.x);
    float fb = TmFade(A.w, fp);
    float3 blade = TmN2(float2(dot(p, d) * A.w * 0.25, dot(p, e) * A.w), fb);
    float3 g2 = float3(bump.y + B.x * fc * clump.y, 0, bump.z + B.x * fc * clump.z);
    float2 bg = blade.y * 0.25 * d + blade.z * e;
    g2 += A.z * fb * float3(bg.x, 0, bg.y);
    float hn = TmNoise2(p * _StabbyStrataWobble.w * 2.7 + 5.3).x;
    float3 c = lerp(base, base * float3(1.4, 1.15, 0.5), saturate(hn * 0.5 + 0.5) * B.w * 1.6);
    float fpa = TmFade(_StabbyStrataWobble.w, fp);
    c *= 1 + _StabbyStrataWobble.z * fpa * TmN2(p * _StabbyStrataWobble.w, fpa).x;
    c *= 1 + B.w * fb * blade.x * 0.8;
    c *= 1 - 0.25 * fc * saturate(-clump.x);
    float2 dw = mul(float2x2(0.8, -0.6, 0.6, 0.8), p);
    float dirt = smoothstep(0.35, 1.0, TmNoise2(dw * 0.035 + 31.3).x + 0.5 * TmNoise2(dw * 0.09 + 7.1).x) * 0.3;
    c = lerp(c, float3(0.085, 0.062, 0.04), dirt);
    TmOut o;
    o.grad = g2 * (1 - 0.6 * dirt);
    o.albedo = c;
    return o;
}

float TmPebbles(float2 p, float freq, float amp, int salt, out float2 grad, out float ring, out float tint)
{
    float2 s = p * freq, cl = floor(s), f = s - cl;
    int2 ci = (int2)cl;
    float2 ctr = 0.25 + 0.5 * float2(TmHash(int3(ci, salt)), TmHash(int3(ci, salt + 1)));
    float r = 0.18 + 0.17 * TmHash(int3(ci, salt + 2));
    float2 dv = f - ctr;
    float d2 = dot(dv, dv) / (r * r), q = sqrt(max(1 - d2, 0.02));
    float inside = step(d2, 1);
    grad = -amp * inside * dv / (r * q);
    ring = saturate(1 - abs(sqrt(d2) - 1) * 6);
    tint = inside * (TmHash(int3(ci, salt + 3)) - 0.5);
    return inside * amp * r * q / freq;
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
    float3 grain = TmN2(p * A.w + 9.1, fg);
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

TmOut TmSand(float3 wp, float3 n, float3 v, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz, wind = _StabbySurface.zw, side = float2(-wind.y, wind.x);
    float calm = saturate((n.y - 0.85) / 0.1) * TmFade(B.y, fp);
    float3 warp = TmN2(p * B.y * 0.12 + 13.1, calm);
    float pk = B.z * calm * saturate(2 - fp * _StabbySurface.y);
    if (pk > 0)
    {
        float nv = max(dot(v, n), 0.3);
        float2 q = p;
        for (int k = 0; k < 2; k++)
        {
            float s0 = dot(q, wind) * B.y + 1.5 * warp.x + 0.3 * dot(q, side) * B.y * 0.05, f0 = frac(s0);
            float hq = B.x / B.y * (6.75 * f0 * f0 * (1 - f0) - 1);
            q = p + v.xz * (hq * pk / nv);
        }
        p = q;
    }
    float s = dot(p, wind) * B.y + 1.5 * warp.x + 0.3 * dot(p, side) * B.y * 0.05, fr = frac(s);
    float prof = 6.75 * fr * fr * (1 - fr), dprof = 6.75 * fr * (2 - 3 * fr);
    float2 ds = wind + 1.5 * warp.yz * 0.12 + 0.015 * side;
    float3 bump = TmFbm2(p, A.y, A.x, 2, fp);
    float fg = TmFade(A.w, fp);
    float3 grain = TmN2(p * A.w + 2.9, fg);
    float2 g = bump.yz + B.x * calm * dprof * ds + 0.3 * A.z * fg * grain.yz;
    float3 sand = lerp(base, ToLinear(_StabbySand.rgb), 0.5);
    float3 c = sand * (1 + A.z * fg * grain.x) * (1 - B.w * calm * (1 - prof));
    c *= 1 - 0.4 * saturate(1 - wp.y / 1.2) * step(0, wp.y);
    TmOut o;
    o.grad = float3(g.x, 0, g.y);
    o.albedo = c;
    return o;
}

TmOut TmSnow(float3 wp, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz, wind = _StabbySurface.zw, side = float2(-wind.y, wind.x);
    float3 drift = TmFbm2(p, A.y, A.x, 3, fp);
    float fs = TmFade(B.y, fp);
    float3 sz = TmN2(float2(dot(p, wind) * B.y * 0.3, dot(p, side) * B.y) + 7.7, fs);
    float ridge = 1 - abs(sz.x), sgn = sz.x >= 0 ? -1 : 1;
    float2 sg = sgn * (sz.y * 0.3 * wind + sz.z * side);
    float fg = TmFade(A.w, fp);
    float3 grain = TmN2(p * A.w + 4.4, fg);
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

TmOut TmEvaluate(float3 wp, float3 n, float3 coatColor, float off, float rockMix)
{
    float fp = max(length(fwidth(wp)), 1e-4);
    float face = abs(normalize(cross(ddx(wp), ddy(wp)) + float3(0, 1e-9, 0)).y);
    float period = max(_StabbyStrataPeriod, 1);
    float u0 = (wp.y + off) / period;
    float2 dux = float2(ddx(u0), 0), duy = float2(ddy(u0), 0);
    float3 v = normalize(_WorldSpaceCameraPos - wp);
    float fj = TmFade(0.27, fp);
    float3 jn = TmN2(wp.xz * 0.27 + 41.7, fj);
    float4 cw = SAMPLE_TEXTURE2D_LOD(_StabbyCoat, sampler_StabbyCoat, wp.xz * _StabbyCoatUV.x + _StabbyCoatUV.y, 0);
    float shore = saturate(1 - wp.y / 6);
    cw = lerp(cw, float4(0, 0, 1, 0), shore);
    cw = saturate(cw * (1 + 0.8 * fj * float4(jn.x, -jn.x, jn.x * 0.5, -jn.x)));
    cw /= max(dot(cw, 1), 1e-3);
    float m = saturate((rockMix - 0.5) * 1.6 + 0.5 + 0.3 * fj * jn.x);
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
    if (m > 0.01) TmAdd(o, TmRock(wp, n, v, fp, off, dux, duy), m);
    float c = 1 - m;
    if (c > 0.01)
    {
        TmOut k;
        k.albedo = 0;
        k.grad = 0;
        if (cw.x > 0.02) TmAdd(k, TmGrass(wp, coatColor, fp, _StabbyCoatA[0], _StabbyCoatB[0]), cw.x);
        if (cw.y > 0.02) TmAdd(k, TmSoil(wp, n, v, coatColor, fp, _StabbyCoatA[1], _StabbyCoatB[1]), cw.y);
        if (cw.z > 0.02) TmAdd(k, TmSand(wp, n, v, coatColor, fp, _StabbyCoatA[2], _StabbyCoatB[2]), cw.z);
        if (cw.w > 0.02) TmAdd(k, TmSnow(wp, coatColor, fp, _StabbyCoatA[3], _StabbyCoatB[3]), cw.w);
        float kept = (cw.x > 0.02 ? cw.x : 0) + (cw.y > 0.02 ? cw.y : 0) + (cw.z > 0.02 ? cw.z : 0) + (cw.w > 0.02 ? cw.w : 0);
        k.albedo += coatColor * (1 - kept);
        float up = saturate((min(n.y, face) - 0.35) / 0.35);
        o.albedo += c * lerp(coatColor, k.albedo, up);
        o.grad += c * up * k.grad;
    }
    float3 g = o.grad - n * dot(o.grad, n);
    o.grad = normalize(n - g);
    return o;
}

#endif
