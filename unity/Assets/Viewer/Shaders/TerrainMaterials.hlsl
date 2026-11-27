#ifndef STABBY_TERRAIN_MATERIALS
#define STABBY_TERRAIN_MATERIALS

#include "Assets/Viewer/Shaders/Look.hlsl"
#include "Assets/Viewer/Shaders/Organic.hlsl"

float4 _StabbyStrataWobble, _StabbyRockA, _StabbyRockB, _StabbyRockC, _StabbyCoatUV;
float4 _StabbyCoatA[4], _StabbyCoatB[4], _StabbyCoatRough;
float _StabbyDetailOff;
TEXTURE2D(_StabbyCoat);
SAMPLER(sampler_StabbyCoat);
TEXTURE2D(_StabbyWaterLevel);
SAMPLER(sampler_StabbyWaterLevel);
TEXTURE2D(_StabbyForest);
SAMPLER(sampler_StabbyForest);

static float tmGrass;
static float tmRough;
static float tmCurv;
static float tmRoof;
static float4 tmShore;

struct TmOut { float3 albedo; float3 grad; };

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
    float3 tw = OrgNoise2(float2(b0 * 0.37, al * (1.0 / 40)) + 5.3);
    float3 tv = OrgNoise2(float2(b0 * 0.11, al * (1.0 / 90)) + 31.7);
    float b = b0 + 0.6 * tw.x + tv.x, ib = floor(b);
    float3 lg = dlev * rb.x * (1 + 0.222 * tw.y + 0.11 * tv.y) + (0.015 * tw.z + 0.0111 * tv.z) * dal;
    bed = ib;
    bedF = b - ib;
    float kb = floor(b + 0.5);
    int ik = (int)kb;
    float3 w1 = OrgNoise2(float2(al * (1.0 / 5.5), kb * 7.13));
    float3 w2 = OrgNoise2(float2(al * (1.0 / 1.4), kb * 3.71 + 40));
    float x = b - kb + 0.12 * w1.x + 0.025 * w2.x;
    float3 xg = lg + (0.0218 * w1.y + 0.0179 * w2.y) * dal;
    float3 vw = OrgNoise2(float2(al * (1.0 / 9), kb * 5.9 + 80));
    float3 vd = OrgNoise2(float2(al * (1.0 / 17), kb * 2.3 + 120));
    float hk0 = OrgHash(int2(ik, 7));
    float3 vb = OrgNoise2(float2(al * (1.0 / 6.5), kb * 4.1 + 160));
    float pres = smoothstep(0.05, 0.55, vd.x + 0.45 * vb.x + 0.5 * (hk0 - 0.5));
    float depth = beds * layered * rb.y * lerp(1.6, 0.5, hard) * lerp(0.3, 1.4, hk0) * pres * OrgFade(rb.x * 8, fp);
    float ws = 0.13 * lerp(0.45, 1.6, saturate(0.5 + 0.6 * vw.x)) * (x < 0 ? 1.5 : 0.8);
    float fr = OrgRungW(1, fp);
    float3 rn = OrgNoise2(float2(al * 2.7, level * 3.3) + 9.1);
    float3 rn2 = fr * OrgNoise2(float2(al * 7.9, level * 8.3) + 21.7);
    float r = ws * (0.4 * rn.x + 0.18 * rn2.x), dp;
    float3 rg = ws * (0.4 * (2.7 * rn.y * dal + 3.3 * rn.z * dlev) + 0.18 * (7.9 * rn2.y * dal + 8.3 * rn2.z * dlev));
    float gb = TmChip(abs(x), ws, r, dp);
    grad = depth * dp * ((x < 0 ? -1 : 1) * xg + rg);
    float hb = depth * gb;
    float jd = layered * rb.w * (0.25 + 0.75 * beds) * smoothstep(0.35, 0.85, hard) * OrgFade(rb.z * 20, fp) * (1 + 0.5 * gb);
    if (jd <= 0) return hb;
    float2 d1 = tmJ1, d2 = tmJ2;
    int bi = (int)ib;
    float3 pw = OrgNoise2(float2(b * 0.21, al * (1.0 / 30)) + 41.3);
    float3 pv = OrgNoise2(float2(level * (1.0 / 1.1), al * (1.0 / 7)) + 55.1);
    float u1 = dot(qa, d1) * rb.z;
    float3 sw1 = OrgNoise2(float2(u1 * 0.31, b * 0.11) + 87.7);
    float j = u1 + 0.45 * sw1.x + 0.35 * pw.x + 0.06 * pv.x + 0.02 * rn.x;
    float ij = floor(j + 0.5), e = j - ij;
    int jb = (int)ij;
    float3 jgv = rb.z * (1 + 0.14 * sw1.y) * float3(d1.x, 0, d1.y) + 0.0735 * pw.y * lg + 0.0545 * pv.z * dlev;
    float hp = OrgHash(int2(jb, 23));
    float3 jx = OrgNoise2(float2(b * 0.3, ij * 4.7 + 19));
    float run = smoothstep(0, 0.35, jx.x + 0.6 * (hp - 0.5)) * step(0.12, OrgHash(int2(bi * 31 + jb, 37)));
    float a1 = jd * jw.x * run * lerp(0.45, 1.35, frac(hp * 7.31)) * saturate(0.6 + 0.7 * rn.x + 0.3 * vw.x);
    float wj = 0.035 * lerp(0.6, 1.5, saturate(0.5 + 0.7 * w1.x)), dj;
    float hj = a1 * TmChip(abs(e), wj, wj * (0.45 * rn.x + 0.2 * rn2.x), dj);
    float3 gj = a1 * dj * ((e < 0 ? -1 : 1) * jgv + wj * 0.45 * (2.7 * rn.y * dal + 3.3 * rn.z * dlev));
    float3 pw2 = OrgNoise2(float2(b * 0.17, dot(qa, d1) * (1.0 / 25)) + 63.9);
    float u2 = dot(qa, d2) * rb.z * 0.6;
    float3 sw2k = OrgNoise2(float2(u2 * 0.27, b * 0.13) + 23.3);
    float k = u2 + 0.45 * sw2k.x + 0.3 * pw2.x + 0.05 * w2.x;
    float ic = floor(k + 0.5), ek = k - ic, d2p;
    float3 kx = OrgNoise2(float2(b * 0.45, ic * 3.1 + 71));
    float hq = OrgHash(int2((int)ic, 43));
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
    float3 mw = OrgNoise2(float2(y * (1.0 / 40), u * (1.0 / 60)) + 3.7);
    u += 4 * mw.x;
    float amp = lerp(0.6, 1.2, hard) * lerp(0.85, 1.1, mass);
    float f1 = OrgFade(1.0 / 18, fp), f2 = OrgFade(1.0 / 6.5, fp), f3 = OrgFade(1.0 / 1.7, fp);
    float3 b1 = OrgNoise2(float2(u * (1.0 / 18), y * (1.0 / 110)) + 61.7);
    float3 b2 = OrgNoise2(float2(u * (1.0 / 6.5), y * (1.0 / 38)) + 17.9);
    float3 fl = OrgNoise2(float2(u * (1.0 / 1.7), y * (1.0 / 11)) + 33.1);
    float a1 = sqrt(b1.x * b1.x + 0.0064), a2 = sqrt(b2.x * b2.x + 0.0036);
    float s1 = b1.x / a1, s2 = b2.x / a2;
    float h = amp * (f1 * 5.0 * (a1 - 0.35) + f2 * 1.7 * (a2 - 0.35) + f3 * 0.22 * fl.x);
    g = amp * (f1 * 5.0 * s1 * b1.yz * float2(1.0 / 18, 1.0 / 110) + f2 * 1.7 * s2 * b2.yz * float2(1.0 / 6.5, 1.0 / 38) + f3 * 0.22 * fl.yz * float2(1.0 / 1.7, 1.0 / 11));
    float gully = f1 * (1 - smoothstep(0.02, 0.3, abs(b1.x))) + 0.6 * f2 * (1 - smoothstep(0.02, 0.25, abs(b2.x)));
    float ff = OrgFade(1.0 / 9, fp), fs = OrgFade(1.0 / 2.6, fp);
    float3 fw = OrgNoise2(float2(u * (1.0 / 3.3), y * (1.0 / 4.1)) + 27.1);
    float2 p1 = float2(u * (1.0 / 9) + 0.35 * b2.x + 0.3 * fw.x, y * (1.0 / 12) + 0.35 * fl.x + 0.3 * fw.y);
    float2 p2 = float2(u * (1.0 / 2.6) + 0.3 * fl.x + 0.15 * fw.y, y * (1.0 / 3.2) + 0.3 * b2.x + 0.15 * fw.x);
    float2 c1 = floor(p1), c2 = floor(p2), e1 = min(p1 - c1, 1 - p1 + c1), e2 = min(p2 - c2, 1 - p2 + c2);
    float k1 = smoothstep(0, 0.3 + 0.07 * fw.x, min(e1.x, e1.y)), k2 = smoothstep(0, 0.25 + 0.08 * fw.y, min(e2.x, e2.y));
    int2 i1 = (int2)c1, i2 = (int2)c2;
    float2 t1 = float2(OrgHash(int3(i1, 41)), OrgHash(int3(i1, 43))) - 0.5;
    float2 t2 = float2(OrgHash(int3(i2, 47)), OrgHash(int3(i2, 53))) - 0.5;
    g += ff * k1 * lerp(0.45, 0.8, mass) * t1 + fs * k2 * lerp(0.15, 0.4, mass) * t2;
    tone = ff * k1 * 0.1 * (OrgHash(int3(i1, 59)) - 0.5) + fs * k2 * 0.05 * (OrgHash(int3(i2, 61)) - 0.5);
    float xj = u * (1.0 / 4.6) + 0.3 * OrgNoise2(float2(y * (1.0 / 6), u * (1.0 / 30)) + 91.3).x + 0.12 * OrgNoise2(float2(y * (1.0 / 1.7), u * (1.0 / 9)) + 15.9).x + 0.06 * fl.x;
    float ij = floor(xj + 0.5);
    float hj0 = OrgHash(int2((int)ij, 71)), hk = OrgHash(int2((int)ij, 73));
    float e = xj - ij - 0.32 * (hj0 - 0.5);
    float3 jp = OrgNoise2(float2(y * lerp(1.0 / 9, 1.0 / 3, hk), ij * 4.7 + 3));
    float3 jm = OrgNoise2(float2(u * (1.0 / 21) + 0.6 * b2.x, y * (1.0 / 15) + 0.5 * fw.x) + 57.3);
    float conc = saturate(0.35 + gully + clamp(6 * tmCurv, -0.4, 0.4));
    float run = smoothstep(0.1, 0.45, jp.x + 0.3 * jm.x);
    float pj = step(0.6, OrgHash(int2((int)ij, 67))) * run * smoothstep(-0.1, 0.3, jm.x) * conc;
    float fj = OrgRungW(0, fp), fjf = OrgFade(0.4, fp);
    float3 jr = fj * OrgNoise2(float2(u * 3.1, y * 2.6) + 77.7);
    float3 jv = OrgNoise2(float2(y * (1.0 / 4), ij * 2.9 + 11));
    float wj = 0.024 * lerp(0.5, 1.7, saturate(0.5 + 0.6 * jv.x)) * lerp(0.6, 1.4, hk) * lerp(0.4, 1, run), dj;
    float dpj = 0.4 * lerp(0.5, 1.2, hard) * pj * fj;
    float hj = dpj * TmChip(abs(e), wj, wj * 0.5 * jr.x, dj);
    g += dpj * dj * (float2((e < 0 ? -1 : 1) * (1.0 / 4.6), 0) + wj * 0.5 * jr.yz * float2(3.1, 2.6));
    h += hj;
    float3 je = OrgNoise2(float2(u * 0.9, y * 0.7) + 49.1);
    float lw0 = lerp(0.12, 0.55, saturate(0.5 + 0.6 * jv.x)) * (1 + 0.45 * je.x), lw = lw0 + 1.5 * fp;
    float jl = fjf * pj * saturate(0.55 + 0.9 * je.x) * (lw0 / lw) * (1 - smoothstep(0, lw, abs(e) * 4.6 + 0.2 * lw0 * jr.x));
    cav = saturate(0.6 * gully + 0.7 * jl - 0.15 * h);
    float3 st1 = OrgNoise2(float2(u * (1.0 / 0.9), y * (1.0 / 24)) + 13.3);
    float3 st2 = OrgNoise2(float2(u * (1.0 / 3.8), y * (1.0 / 60)) + 29.9);
    float lf = frac(y * (1.0 / 6.5) + 0.6 * mw.x + 0.3 * b2.x);
    streak = smoothstep(0.05, 0.6, 0.5 * OrgFade(1.0 / 0.9, fp) * st1.x + 0.6 * st2.x + 0.4 * gully) * lerp(0.3, 1, lf * lf);
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
    float dip = 7 * OrgNoise2(xz * (1.0 / 420) + 3.1).x;
    float thick = 3.5 * OrgNoise2(float2(h * (1.0 / 23), dot(xz, float2(0.8, 0.6)) * (1.0 / 900)) + 11.7).x;
    float broad = 5 * OrgNoise2(float2(h * (1.0 / 61), dot(xz, float2(0.3, -0.95)) * (1.0 / 1300)) + 47.9).x;
    float fine = 0.6 * OrgNoise2(float2(h * (1.0 / 9), dot(xz, float2(-0.6, 0.8)) * (1.0 / 500)) + 29.3).x;
    return dip + broad + thick + fine;
}

TmOut TmRock(float3 wp, float3 n, float3 v, float fp, float off, float2 dux, float2 duy, float stone)
{
    float4 ra = _StabbyRockA, sw = _StabbyStrataWobble;
    float period = max(_StabbyStrataPeriod, 1);
    float layered = 1 - stone;
    float sed = smoothstep(0.3, 0.7, abs(frac(off * _StabbyRockC.y) - 0.5) * 2), mass = 1 - sed;
    off += TmStrataWarp(wp.xz + orgO.xz, wp.y + off);
    float fw = OrgFade(sw.y, fp) * layered;
    float3 wb = sw.x * fw * OrgN2((wp.xz + orgO.xz) * sw.y + 17.3, fw) * float3(1, sw.y, sw.y);
    float level = wp.y + off + wb.x;
    float4 st = SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(level / period, 0.5), dux, duy);
    float hard = lerp(0.82, st.a, sed);
    float sf = smoothstep(-0.35, 0.45, OrgNoise2(float2(dot(wp.xz + orgO.xz, float2(0.8, 0.6)) * (1.0 / 240), level * (1.0 / 38)) + 71.3).x);
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
            float hq = TmRockRelief(q.xz + orgO.xz, q.y + off + wb.x, wb.yz, hard, fp, layered, bk, jw, g, bed, bedF);
            q = wp + v * (0.7 * hq * pk / nv);
        }
    }
    float hr = TmRockRelief(q.xz + orgO.xz, q.y + off + wb.x, wb.yz, hard, fp, layered, bk, jw, g, bed, bedF);
    float ck = smoothstep(0.6, 0.9, nl) * (layered + 0.7 * stone) * _StabbyRockC.z * (1 - smoothstep(0.15, 0.6, tmRoof));
    float3 crg = 0;
    float cav = 0, ctone = 0, streak = 0;
    [branch] if (ck > 0) TmCrag(q.xz + orgO.xz, q.y, n, hard, mass, fp, crg, cav, ctone, streak);
    float crs = ck * lerp(0.8, 1.3, mass);
    float vg = smoothstep(0.4226, 0.2588, n.y);
    float4 bump = OrgFbm3(q, ra.w, ra.z, 3, fp);
    float gw = OrgRungW(0, fp), fw2 = OrgRungW(1, fp);
    float4 grain = OrgRLN3(q, 0, 5.1, gw);
    float4 fine = OrgRLN3(q, 1, 11.7, fw2);
    float fw3 = OrgRungW(2, fp);
    float4 micro = OrgRLN3(q, 2, 53.3, fw3);
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
    float3 c = sc * (1 + ra.x * gw * grain.x) * (1 + ra.x * 0.9 * fw2 * speck) * (1 + 0.06 * bk * OrgFade(_StabbyRockB.x, fp) * (lerp(OrgHash(int2((int)bed, 11)), OrgHash(int2((int)bed + 1, 11)), smoothstep(0.7, 1, bedF)) * 2 - 1)) * (1 + crs * ctone);
    float fh = OrgFade(0.12, fp);
    float4 drift = OrgLN3(q, 0.12, 37.9, fh);
    float4 drift2 = OrgLN3(q, 0.31, 41.3, fh);
    c *= 1 + fh * (0.1 * drift2.x + 0.05 * drift.x * float3(1, 0.15, -0.85) + 0.025 * drift2.x * float3(-0.3, 0.6, -0.3));
    float lam = (q.y + off + wb.x) * _StabbyRockB.x * 4 + 0.37 * bed + 0.3 * drift2.x + 0.08 * gw * grain.x, il = floor(lam), fl = lam - il;
    float flam = OrgFade(_StabbyRockB.x * 4, fp) * (layered + 0.35 * stone);
    int lb = (int)bed * 7 + 13;
    float lt = lerp(OrgHash(int2((int)il, lb)), OrgHash(int2((int)il + 1, lb)), smoothstep(0.7, 1, fl)) * 2 - 1;
    float sa = dot(q.xz + orgO.xz, float2(0.8, 0.6)), kl = floor(lam + 0.5), el = lam - kl;
    float3 ln = OrgNoise2(float2(sa * 0.53, kl * 2.9) + 31.7);
    float wl0 = 0.05 * (1 + 0.6 * ln.x), wl = wl0 + fp * _StabbyRockB.x * 4;
    float pt = step(0.8, OrgHash(int2((int)kl, lb + 1))) * smoothstep(0.1, 0.45, OrgNoise2(float2(sa * 0.06, kl * 1.7) + 83.1).x) * saturate(0.4 + 1.2 * ln.x) * (wl0 / wl) * (1 - smoothstep(0, wl, abs(el + 0.03 * ln.y)));
    c *= 1 + flam * lerp(0.35, 0.6, bk) * (0.1 * lt - 0.12 * pt);
    float fm = OrgRungW(1, fp);
    float4 mn = OrgLN3(q * float3(1, 3.5, 1), OrgF(1), 43.1, fm);
    float dm = fm * lerp(0.1, 1.8, smoothstep(-0.25, 0.35, 0.6 * drift.x + 0.4 * drift2.x)), gs = gw * grain.x * 0.5;
    float lgt = dm * (1 + gs) * smoothstep(0.35, 0.75, mn.x), drk = dm * (1 - gs) * smoothstep(0.35, 0.75, -mn.x);
    c = lerp(c, dot(c, lw) * 1.6, 0.2 * lgt) * (1 - 0.17 * drk);
    c *= (1 - 0.3 * saturate(-hr / (_StabbyRockB.y * 1.2 + 1e-4))) * (1 - 0.35 * crs * cav) * (1 - 0.4 * crs * vg * streak * float3(1, 0.99, 0.97));
    c *= 1 + 0.3 * (1 - 0.5 * stone) * bump.x * ra.w;
    float up = saturate(n.y), dn = saturate((0.05 - n.y) * 2.5);
    float cl = dot(c, lw);
    c = lerp(c, cl * 1.15, 0.3 * up * up);
    c = lerp(c, cl, 0.6 * dn) * lerp(1, float3(0.94, 0.97, 1.03), dn);
    float steep = saturate(1 - n.y * n.y);
    float fs = steep * vg * OrgFade(1.3, fp);
    float sn = OrgLN3(q * float3(1, 0.06, 1), 1.3, 23.7, fs).x;
    float3 sv = OrgNoise2(float2(sa * 0.29, bed * 3.7) + 61.3);
    float fb = saturate(bedF + 0.05 * sv.x), wv = 0.06 + 0.1 * saturate(0.5 + 0.7 * sv.y) + fp * _StabbyRockB.x;
    float3 sr = OrgNoise2(float2(sa * lerp(0.4, 1.6, OrgHash(int2((int)bed, 29))), bed * 5.3) + 67.1);
    float scl = smoothstep(0, 0.4, OrgNoise2(float2(sa * 0.045, q.y * 0.03) + 73.9).x);
    float seep = fs * (0.3 + 0.7 * bk) * smoothstep(0.2, 0.9, fb) * smoothstep(1, 1 - wv, fb) * smoothstep(0.25, 0.6, sr.x + 0.25 * gw * grain.x) * scl;
    c *= (1 - 0.1 * fs * layered * saturate(sn)) * (1 - 0.5 * seep * float3(1, 1.03, 1.08));
    float lic = layered * OrgFade(1.1, fp) * smoothstep(-0.2, 0.55, n.y) * smoothstep(0.05, 0.35, drift2.x + 0.35 * gw * grain.x + 0.25 * mn.x);
    c = lerp(c, dot(c, lw) * float3(0.96, 1.02, 0.86) * (1.12 + 0.12 * mn.x), 0.35 * lic);
    [branch] if (stone > 0)
    {
        float fwt = OrgFade(0.9, fp), fcr = OrgRungW(0, fp);
        float4 weather = OrgLN3(q, 0.9, 3.3, fwt);
        float4 gate = OrgLN3(q, 0.22, 7.7, fcr);
        float4 crack = OrgLN3(q, 0.45, 23.1, fcr);
        c *= 1 - stone * 0.12 * fs * saturate(OrgLN3(q * float3(1, 0.057, 1), OrgF(0), 9.7, fs).x);
        float w0 = 0.035 * (0.5 + saturate(0.5 + 0.8 * weather.x)), w = w0 + 0.3 * fp;
        float ax = abs(crack.x + 0.012 * fwt * weather.y), tc = saturate(ax / w), cg = fcr * smoothstep(-0.1, 0.3, gate.x) * (w0 / w);
        float seam = (1 - tc * tc * (3 - 2 * tc)) * cg;
        float dl = cg * 6 * tc * (1 - tc) / w * (crack.x < 0 ? -1 : 1) * OrgQ(0.45);
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
    float2 ot = orgO.xz * (1.0 / 1024);
    float3 n = OrgNoise2(float2(dot(p, du), dot(p, dv)) + w.x * float2(0.3, 0.6), int2(dot(ot, du * 1024), dot(ot, dv * 1024)));
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
    float2 p = wp.xz, pa = p + orgO.xz;
    float3 bump = OrgFbm2(p, A.y, A.x, 3, fp);
    float fc = OrgFade(B.y, fp), ft = OrgFade(B.y * 2.6, fp);
    float3 clump = OrgLN2(p, B.y, 3.7, fc);
    float3 tuft = OrgLN2(p, B.y * 2.6, 8.3, ft);
    float fb = OrgRungW(2, fp);
    float3 blade = 0;
    float2 bg = 0;
    [branch] if (fb > 0)
    {
        blade = TmBlade(p, _StabbySurface.zw, OrgF(2), float3(clump.x, clump.yz * OrgQ(B.y)), bg);
        bg /= OrgF(2);
    }
    float3 g2 = float3(bump.y + B.x * (fc * clump.y + 0.5 * ft * tuft.y), 0, bump.z + B.x * (fc * clump.z + 0.5 * ft * tuft.z));
    g2 += A.z * fb * float3(bg.x, 0, bg.y);
    float3 green = TmGrassGreen(base);
    float hv = fc * clump.x + 0.12 * OrgNoise2(pa * _StabbyStrataWobble.w * 1.3 + 23.7).x;
    green *= 1 + 0.06 * hv * float3(1, 0.15, -1);
    float w = _StabbyStrataWobble.w;
    float hn = 0.65 * OrgNoise2(pa * w * 2.7 + 5.3).x + 0.35 * OrgNoise2(pa * w * 0.6 + 1.9).x;
    float3 c = green * (1 + B.w * 0.5 * hn);
    float fpa = OrgFade(w, fp);
    c *= 1 + 0.2 * _StabbyStrataWobble.z * fpa * OrgN2(pa * w, fpa).x;
    c *= 1 - 0.3 * fc * (1 - smoothstep(-0.5, 0.3, clump.x));
    c *= 1 + 0.3 * B.w * ft * (smoothstep(-0.6, 0.6, tuft.x) - 0.5);
    float bl = smoothstep(-0.45, 0.45, blade.x);
    c *= 1 + B.w * 1.8 * fb * (bl - 0.5);
    c = lerp(c, green * 1.2, fb * 0.35 * saturate(blade.x * 2 - 1.2) * saturate(B.w * 4));
    float2 dw = mul(float2x2(0.8, -0.6, 0.6, 0.8), pa);
    float dirt = smoothstep(0.35, 1.0, OrgNoise2(dw * 0.035 + 31.3).x + 0.5 * OrgNoise2(dw * 0.09 + 7.1).x) * 0.025;
    c = lerp(c, float3(0.085, 0.062, 0.04), dirt);
    TmOut o;
    o.grad = g2 * (1 - 0.6 * dirt);
    o.albedo = c;
    return o;
}

float TmPebbles(float2 p, float freq, float amp, int salt, out float2 grad, out float ring, out float tint)
{
    float q = OrgQ(freq);
    float2 s = p * q, cl = floor(s), f = s - cl;
    int2 ci = (int2)cl + OrgShift(q);
    float2 ctr = 0.25 + 0.5 * float2(OrgHash(int3(ci, salt)), OrgHash(int3(ci, salt + 1)));
    float r = 0.18 + 0.17 * OrgHash(int3(ci, salt + 2));
    float2 dv = f - ctr;
    float d2 = dot(dv, dv) / (r * r), sq = sqrt(max(1 - d2, 0.02));
    float inside = step(d2, 1);
    grad = -amp * inside * dv / (r * sq);
    ring = saturate(1 - abs(sqrt(d2) - 1) * 6);
    tint = inside * (OrgHash(int3(ci, salt + 3)) - 0.5);
    return inside * amp * r * sq / q;
}

float4 TmFleck(float2 p, float2x2 m, float f, float fp, int salt, float rMin, float rMax, float share)
{
    float w = OrgFade(4 * f, fp);
    if (w <= 0) return 0;
    float2 q = mul(m, p) * f, fl = floor(q);
    int2 c = (int2)fl + (int2)(mul(m, orgO.xz) * f);
    float4 hs = float4(OrgHash(int3(c, salt)), OrgHash(int3(c, salt + 1)), OrgHash(int3(c, salt + 2)), OrgHash(int3(c, salt + 3)));
    if (hs.w > share) return 0;
    float2 u = normalize(OrgGrad(c + int2(salt, 0)) + 1e-4), e = q - fl - (0.3 + 0.4 * hs.xy);
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
    float fm = OrgFade(0.16, fp);
    float s = OrgLN2(p, 0.045, 81.3, 1).x + 0.5 * fm * OrgLN2(p, 0.16, 83.9, fm).x + 0.8 * hollow;
    float t = 1 - 2.2 * amount, e = 0.14 + 0.15 * (1 - fm);
    return amount > 0 ? smoothstep(t - e, t + e, s) : 0;
}

float3 TmLichen(float2 p, float3 c, float fp, float share)
{
    float fl = OrgRungW(2, fp);
    [branch] if (fl <= 0) return c;
    float clump = saturate(1.6 * OrgLN2(p, 0.37, 63.1, 1).x);
    float2 q = p + 0.16 * float2(OrgLN2(p, 1.4, 61.7, 1).x, OrgLN2(p, 1.4, 65.3, 1).x) + 0.035 * float2(OrgLN2(p, OrgF(1), 67.9, fl).x, OrgLN2(p, OrgF(1), 69.1, fl).x);
    float sh = share * clump;
    float4 a = TmFleck(q, float2x2(1, 0.3, -0.3, 1), 4.1, fp, 71, 0.04, 0.34, sh * 0.6);
    float4 b = TmFleck(q, float2x2(1, -0.5, 0.5, 1), 9.7, fp, 77, 0.05, 0.32, sh * 1.4);
    float4 d = TmFleck(q, float2x2(1, 0.8, -0.8, 1), 21, fp, 83, 0.08, 0.38, sh * 2);
    float4 m = a.w >= b.w ? a : b;
    m = m.w >= d.w ? m : d;
    float tex = 0.6 + 0.4 * OrgLN2(p, OrgF(2), 73.3, fl).x;
    float op = 0.4 * m.w * fl * tex * (0.55 + 0.45 * abs(m.x) / max(m.w, 1e-3));
    return lerp(c, lerp(c, float3(0.36, 0.37, 0.3), 0.5), op);
}

TmOut TmSoil(float3 wp, float3 n, float3 v, float3 base, float fp, float4 A, float4 B)
{
    float2 p = wp.xz;
    float fpk = OrgFade(B.y, fp);
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
    float h1 = 0, h2 = 0, fq = OrgFade(B.y * 2.3, fp);
    gp = 0; gq = 0; ring = 0; ring2 = 0; tint = 0; tint2 = 0;
    [branch] if (fpk > 0) h1 = TmPebbles(p, B.y, B.x * fpk, 21, gp, ring, tint);
    [branch] if (fq > 0) h2 = TmPebbles(p, B.y * 2.3, B.x * fq, 41, gq, ring2, tint2);
    float3 clod = OrgFbm2(p, A.y, A.x, 3, fp);
    float fg = OrgRungW(2, fp);
    float3 grain = OrgLN2(p, OrgF(2), 9.1, fg);
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
        float fw = OrgFade(0.45, fp), fv = OrgFade(1.4, fp);
        float3 w0 = OrgLN2(p, 0.06, 71.3, 1), w1 = OrgLN2(p, 0.45, 73.9, fw), w2 = OrgLN2(p, 1.4, 75.1, fv);
        float lam0 = 0.42, k = 0.075, lam = lam0 + k * dp;
        float dw = d + 0.7 * lam * w0.x + 0.28 * fw * w1.x + 0.07 * fv * w2.x;
        float2 gd = tmShore.xy + 0.7 * lam * 0.06 * w0.yz + 0.28 * 0.45 * fw * w1.yz + 0.07 * 1.4 * fv * w2.yz;
        float s = dw < 0 ? dw / lam0 : log(1 + k * dw / lam0) / k;
        float li = floor(s), x = (frac(s) - 0.5) * lam;
        float hi = OrgHash(int3((int)li, 0x5a1, 3));
        float br = OrgLN2(p, 0.16 + 0.1 * hi, 77.7 + 9.13 * hi, 1).x;
        float gap = smoothstep(-0.3, 0.1, br + 0.45 * hi - 0.15);
        float crisp = smoothstep(16, 1, dp);
        float wid = max(lam * (0.045 + 0.05 * hi) * (0.75 + 0.45 * fv * w2.x) * (1 + 1.4 * (1 - crisp)), 0.02);
        float wx = x > 0 ? 0.55 * wid : wid, e = exp(-x * x / (wx * wx));
        float fl = OrgFade(0.3 / wid, fp);
        float amp = rel * gap * (0.3 + 0.7 * crisp) * (0.6 + 0.8 * hi);
        r = amp * lerp(min(1, 1.3 * wid / lam), e, fl);
        g += amp * fl * 0.018 * (-2 * x / (wx * wx)) * e * gd;
    }
    return r;
}

TmOut TmSand(float3 wp, float3 n, float3 v, float3 base, float fp, float shore, float hw, float4 A, float4 B)
{
    float2 p = wp.xz, wind = _StabbySurface.zw;
    float f = B.y * 0.3, k0 = frac(dot(orgO.xz, wind) * f);
    float calm = saturate((n.y - 0.85) / 0.1) * saturate(1 - 6 * max(0, dot(n.xz, wind))) * OrgFade(f * 4, fp);
    float3 warp = OrgN2((p + orgO.xz) * f * 0.1 + 13.1, calm);
    float3 patch = OrgLN2(p, 0.15, 17.3, 1);
    float dune = smoothstep(0.5, 3.2, hw + 1.3 * OrgLN2(p, 0.045, 23.9, 1).x + 0.5 * patch.x) * (1 - 0.6 * shore);
    float warm = dune * smoothstep(0.36, 0.5, (base.r - base.b) / max(base.r, 1e-3));
    float ripple = B.x * calm * 1.2 * dune * cos(6.2831853 * (dot(p, wind) * f + k0 + 0.5 * warp.x));
    float3 bump = OrgFbm2(p, A.y, A.x, 3, fp);
    float fg = OrgRungW(2, fp);
    float3 grain = OrgLN2(p, OrgF(2), 2.9, fg);
    float2 g = bump.yz + ripple * (wind + 0.05 * f * warp.yz) + 0.3 * A.z * fg * grain.yz;
    float fm = OrgRungW(1, fp);
    float3 mid = OrgLN2(p, OrgF(1), 5.7, fm);
    float3 sand = lerp(base, ToLinear(_StabbySand.rgb), 1 - 0.5 * dune) * lerp(1, float3(1.03, 0.95, 0.78), warm);
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
    float2 p = wp.xz, pa = p + orgO.xz, wind = _StabbySurface.zw, side = float2(-wind.y, wind.x);
    float3 drift = OrgFbm2(p, A.y, A.x, 3, fp);
    float fs = OrgFade(B.y, fp);
    float3 sz = OrgN2(float2(dot(pa, wind) * B.y * 0.3, dot(pa, side) * B.y) + 7.7, fs);
    float ridge = 1 - abs(sz.x), sgn = sz.x >= 0 ? -1 : 1;
    float2 sg = sgn * (sz.y * 0.3 * wind + sz.z * side);
    float fg = OrgRungW(2, fp);
    float3 grain = OrgLN2(p, OrgF(2), 4.4, fg);
    float hd = drift.x * A.y;
    float hollow = saturate(-hd * 3) + 0.4 * fs * (1 - ridge);
    float fb = OrgFade(0.7, fp), ff = OrgRungW(0, fp), fx = OrgRungW(1, fp);
    float bm = OrgLN2(p, 0.7, 85.1, fb).x;
    float fn = OrgN2(float2(dot(pa, wind) * 0.9, dot(pa, side) * 2.6) + 3.1, ff).x;
    float fx3 = OrgLN2(p, OrgF(1), 87.3, fx).x;
    float conv = clamp(-10 * tmCurv, -0.7, 0.7) + 1.4 * hd + 0.45 * fs * (ridge - 0.5) + 1.2 * (1 - n.y) - 0.3 * dot(n.xz, wind) - 0.6 * dot(drift.yz, wind);
    float sv = 0.22 * OrgLN2(p, 0.045, 81.3, 1).x - conv + 0.2 * fb * bm + 0.13 * ff * fn + 0.07 * fx * fx3;
    float e = 0.02 + 0.35 * saturate(1.5 * fp);
    float lie = smoothstep(-e, e, sv + 0.42);
    float3 snow = TmSnowAlbedo(saturate(hollow) * (0.5 + B.w * 4)) * (1 + A.z * fg * grain.x * 0.5);
    float ft = OrgFade(1.7, fp), fh = OrgFade(0.33, fp), f1 = OrgFade(0.9, fp), f2 = OrgRungW(0, fp), f3 = OrgRungW(1, fp);
    float3 tuft = OrgLN2(p, 1.7, 44.1, ft);
    float hv = OrgLN2(p, 0.33, 46.7, fh).x, m1 = OrgLN2(p, 0.9, 51.3, f1).x, m2 = OrgLN2(p, OrgF(0), 53.9, f2).x, m3 = OrgLN2(p, OrgF(1), 57.1, f3).x;
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
    float f1 = OrgFade(0.333, fp), f2 = OrgFade(1.43, fp), f3 = OrgRungW(1, fp);
    float3 s = f1 * OrgLN2(p, 0.333, 41.7, f1);
    float3 c = f2 * OrgLN2(p, 1.43, 5.3, f2);
    float3 g = f3 * OrgLN2(p, OrgF(1), 17.3, f3);
    float2 d = 0.07 * s.yz + 0.05 * c.yz + 0.04 * g.yz;
    return float3(0.1 * s.x + 0.6 * c.x + 0.5 * g.x, d);
}

TmOut TmEarth(float3 wp, float3 n, float3 base, float fp)
{
    float2 p = wp.xz, pa = p + orgO.xz;
    float sn = length(n.xz);
    float2 d = n.xz / max(sn, 1e-3);
    float fr = OrgRungW(0, fp) * saturate(sn * 3);
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
            float3 r = OrgNoise2(float2(dot(pa, e) * 0.35, dot(pa, s) * 2.5) + 11.3 * k);
            rut += wk * r.x;
            rg += wk * (r.y * 0.35 * e + r.z * 2.5 * s);
            ws += wk;
        }
        rut *= fr / ws;
        rg *= fr / ws;
    }
    float fc = OrgFade(1.1, fp), fg = OrgRungW(1, fp), ff = OrgRungW(2, fp);
    float3 clod = OrgLN2(p, 1.1, 8.7, fc);
    float3 grain = OrgLN2(p, OrgF(1), 14.9, fg);
    float3 fine = OrgLN2(p, OrgF(2), 3.3, ff);
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

float2 TmFloorCover(float2 p, float4 fo, float fp)
{
    float d = smoothstep(0.0, 0.75, fo.r + fo.g + 0.02 * OrgL2(p, 0.031, 91.3).x);
    float fe = OrgFade(1.1, fp), fs = OrgRungW(0, fp);
    float2 wq = p + 0.3 * float2(OrgL2(p, 1.1, 85.3).x, OrgL2(p, 1.1, 86.9).x);
    float pn = 0.08 * OrgL2(p, 0.42, 93.7).x + 0.5 * fe * OrgL2(wq, 1.4, 95.1).x + 0.42 * fs * OrgLN2(p, OrgF(0), 96.7, fs).x;
    float cue = d + 0.22 * smoothstep(0.1, 0.6, fo.b) * d + clamp(5 * tmCurv, -0.15, 0.15) * d;
    float fm = OrgFade(0.42, fp);
    return float2(max(lerp(0.7 * smoothstep(0.1, 0.9, cue), smoothstep(0.2, 0.8, cue + 0.3 * pn), fm), 0.6 * smoothstep(0.55, 0.95, d)), d);
}

float TmLitter(inout TmOut k, float3 wp, float4 fo, float fp, float cov)
{
    [branch] if (cov > 0.01)
    {
        float2 p = wp.xz;
        float nd = fo.g / max(fo.r + fo.g, 1e-3), bd = 1 - nd;
        float fh = OrgFade(0.5, fp), fd = OrgFade(1.6, fp);
        float3 hm = OrgLN2(p, 0.5, 27.1, fh), dr = OrgLN2(p, 1.6, 29.3, fd);
        float sd = smoothstep(0.05, 0.75, fo.r + fo.g), wf = smoothstep(0.1, 0.6, fo.b);
        float dk = saturate(0.6 * sd + 0.25 * wf + clamp(3 * tmCurv, -0.12, 0.12) - 0.1 + 0.04 * hm.x);
        float3 base = k.albedo, c = 0;
        float2 g = 0.1 * hm.yz + 0.05 * dr.yz;
        [branch] if (bd > 0.01)
        {
            float fa = OrgRungW(2, fp), fc = OrgRungW(0, fp), fl = OrgRungW(1, fp), fx = OrgRungW(2, fp);
            float3 cl = OrgLN2(p, OrgF(0), 63.1, fc), lf = OrgLN2(p, OrgF(1), 67.3, fl), lx = OrgLN2(p, OrgF(2), 71.9, fx);
            float mo = 0.5 * dr.x + 0.05 * hm.x + 0.15 * (0.5 - dk) + 0.3 * fc * cl.x + 0.2 * fl * lf.x + 0.1 * fx * lx.x;
            float pile = smoothstep(-0.55, 0.3, mo + 0.15 * fl * lf.x);
            float t = saturate(0.5 + 0.55 * (fc * cl.x + 0.8 * fl * lf.x + 0.5 * fx * lx.x - 0.4 * dr.x));
            float3 leaf = lerp(float3(0.094, 0.056, 0.032), float3(0.15, 0.088, 0.045), t);
            float3 hum = lerp(float3(0.074, 0.049, 0.029), float3(0.06, 0.04, 0.025), dk);
            float3 cb = lerp(hum, leaf, pile) * (1 + 0.24 * fx * lx.x + 0.14 * fl * lf.x);
            cb = lerp(cb, lerp(hum, leaf, 0.55), 0.35 * (1 - fa));
            float4 dl = TmFleck(p, float2x2(1, 0.6, -0.6, 1), 14, fp, 53, 0.1, 0.3, 0.3);
            float4 sg = TmFleck(p, float2x2(1, -0.4, 0.4, 1), 27, fp, 59, 0.08, 0.2, 0.25);
            cb = lerp(cb, float3(0.19, 0.12, 0.066) * (1 + 0.15 * dl.x), 0.7 * dl.w);
            cb = lerp(cb, hum * 0.75, 0.6 * sg.w * (1 - dl.w));
            g += bd * (0.06 * dl.yz + 0.03 * sg.yz);
            c += bd * cb;
            g += bd * (0.05 * fc * cl.yz + 0.04 * fl * lf.yz + 0.02 * fx * lx.yz);
        }
        [branch] if (nd > 0.01)
        {
            float fn = OrgRungW(2, fp);
            float3 mc = float3(0.068, 0.048, 0.034) * (1.06 - 0.12 * dk + 0.02 * hm.x + 0.1 * dr.x);
            float3 cn = mc;
            [branch] if (fn > 0)
            {
                float2 g1, g2, g3;
                float3 wv = float3(2.5 * dr.x + 1.5 * hm.x, 0, 0);
                float3 b1 = TmBlade(p, float2(0.8, 0.6), 22, wv, g1);
                float3 b2 = TmBlade(p, float2(-0.6, 0.8), 31, wv * 1.3, g2);
                float3 b3 = TmBlade(p, float2(0.2, -0.98), 47, wv * 0.7, g3);
                float t = max(b1.x, max(b2.x, b3.x));
                float3 sc = lerp(float3(0.045, 0.033, 0.025), float3(0.11, 0.072, 0.047), smoothstep(-0.3, 0.6, t));
                cn = lerp(mc, sc * (1 + 0.14 * hm.x), 0.75 * fn);
                g += nd * fn * 0.03 * (b1.x > b2.x ? g1 / 22 : g2 / 31);
            }
            float4 cone = TmFleck(p, float2x2(1, 0.7, -0.7, 1), 2.2, fp, 101, 0.26, 0.4, 0.1);
            cn = lerp(cn, float3(0.05, 0.03, 0.018), 0.85 * cone.w);
            g += nd * 0.1 * cone.yz;
            c += nd * cn;
        }
        float fm = OrgFade(0.4, fp), fs = OrgFade(1.3, fp), fg = OrgRungW(1, fp);
        float3 m1 = OrgLN2(p, 0.4, 97.3, fm), m2 = OrgLN2(p, 1.3, 98.9, fs), mg = OrgLN2(p, OrgF(1), 99.7, fg);
        float fe = OrgRungW(0, fp), fe2 = OrgRungW(1, fp);
        float3 e1 = OrgLN2(p, OrgF(0), 102.7, fe), e2 = OrgLN2(p, OrgF(1), 104.3, fe2);
        float mv = 0.25 * m1.x + 0.55 * m2.x + 0.1 * fg * mg.x + 0.16 * fe * e1.x + 0.08 * fe2 * e2.x;
        float ms = mv + 0.5 * fo.b - 0.2;
        float moss = wf * smoothstep(-0.2, 0.32, ms);
        c *= 1 - 0.18 * wf * smoothstep(-0.55, -0.05, ms) * (1 - moss);
        [branch] if (moss > 0.01)
        {
            float3 mossC = lerp(float3(0.035, 0.06, 0.018), float3(0.085, 0.115, 0.03), saturate(0.5 + 0.5 * mg.x * fg + 0.3 * m2.x));
            c = lerp(c, mossC, moss);
            g = lerp(g, 0.18 * (0.7 * m1.yz + 0.3 * m2.yz) + 0.05 * fg * mg.yz, moss);
        }
        float fc = 1 - OrgFade(0.12, fp);
        c = lerp(c, lerp(float3(0.098, 0.064, 0.036), float3(0.072, 0.052, 0.036), nd) * (1.05 - 0.1 * dk), fc);
        float fw = OrgRungW(0, fp), fr = OrgRungW(1, fp), fq = OrgFade(0.9, fp);
        float2 tw = p + 0.16 * fw * float2(OrgLN2(p, OrgF(0), 131.3, fw).x, OrgLN2(p, OrgF(0), 133.7, fw).x);
        tw += 0.05 * fr * float2(OrgLN2(p, OrgF(1), 135.1, fr).x, OrgLN2(p, OrgF(1), 136.9, fr).x);
        float tc = smoothstep(-0.15, 0.45, OrgLN2(p, 0.9, 137.9, fq).x) * (1 - 0.4 * sd);
        float4 t1 = TmFleck(tw, float2x2(1, 0.45, -0.45, 1), 1.3, fp, 113, 0.08, 0.45, 0.6 * tc);
        float4 t2 = TmFleck(tw, float2x2(1, -0.7, 0.7, 1), 3.4, fp, 117, 0.06, 0.4, 0.65 * tc);
        float4 lv = TmFleck(p, float2x2(1, 0.2, -0.2, 1), 2.3, fp, 121, 0.12, 0.3, 0.35);
        c = lerp(c, lerp(float3(0.17, 0.105, 0.05), float3(0.13, 0.11, 0.05), saturate(0.5 + lv.x)), 0.55 * lv.w * bd);
        float tuft = saturate(t1.w + t2.w) * (1 - 0.7 * sd) * OrgRungW(0, fp);
        [branch] if (tuft > 0.01)
        {
            float fj = OrgFade(1.7, fp), fk = OrgRungW(1, fp);
            float er = fj * OrgLN2(tw, 1.7, 139.3, fj).x + 0.55 * fk * OrgLN2(tw, OrgF(1), 141.7, fk).x;
            tuft *= smoothstep(-0.12, 0.32, er);
        }
        float lb = dot(base, float3(0.2126, 0.7152, 0.0722)), lc = dot(c, float3(0.2126, 0.7152, 0.0722));
        float mid = (1 - OrgRungW(0, fp)) * (1 - smoothstep(0.55, 0.95, sd));
        c *= lerp(1, min((lb + 0.4 * (lc - lb)) / max(lc, 1e-4), 2.5), mid);
        cov *= 1 - 0.5 * tuft;
        k.albedo = lerp(k.albedo, c, cov);
        k.grad = lerp(k.grad, float3(g.x, 0, g.y), cov);
    }
    return cov;
}

void TmStoneCover(inout TmOut o, float3 wp, float3 n, float4 cw, float fp, float stone, float m)
{
    float fm = OrgFade(0.6, fp), fl = OrgRungW(1, fp);
    float4 patch = OrgLN3(wp, 0.6, 51.3, fm);
    float4 streak = OrgLN3(wp * float3(1, 0.12, 1), OrgF(1), 61.7, fl);
    float top = smoothstep(0.45, 0.9, n.y), side = 1 - top, wet = cw.x + 0.4 * cw.y, dry = cw.z + 0.5 * cw.y;
    float moss = stone * wet * top * smoothstep(-0.15, 0.35, fm * patch.x + 0.2 * fl * streak.x);
    float lichen = stone * saturate(wet + 0.3 * cw.z) * smoothstep(-0.2, 0.5, n.y) * fm * smoothstep(0.1, 0.45, patch.x + 0.3 * fl * streak.x) * (1 - moss);
    float stain = stone * saturate(0.4 + 0.4 * dry + 0.3 * wet) * side * smoothstep(0.4226, 0.2588, n.y) * fl * smoothstep(0.15, 0.6, streak.x) * smoothstep(-0.4, 0.2, -patch.x) * (1 - moss);
    float dust = stone * dry * top * fm * smoothstep(0, 0.5, patch.x) * (1 - moss);
    float frost = stone * cw.w * smoothstep(0.55, 0.85, n.y);
    float3 mossC = lerp(float3(0.035, 0.06, 0.018), float3(0.085, 0.115, 0.03), saturate(0.5 + fm * patch.y));
    float lum = dot(o.albedo, float3(0.2126, 0.7152, 0.0722));
    o.albedo = lerp(o.albedo, m * mossC, moss);
    o.albedo = lerp(o.albedo, lum * float3(1.06, 1.06, 0.9) * 1.2, 0.4 * lichen);
    o.albedo *= 1 - 0.22 * stain;
    o.albedo = lerp(o.albedo, m * ToLinear(_StabbySand.rgb), 0.3 * dust);
    o.albedo = lerp(o.albedo, m * float3(0.75, 0.79, 0.84), frost);
    o.grad *= 1 - 0.6 * moss - frost;
    tmRough = lerp(tmRough, _StabbyCoatRough.w, frost) + _StabbyLook.w * (moss + 0.5 * lichen);
}

float2 TmWet(float3 wp, float hw, float fp)
{
    float fw = OrgFade(0.8, fp);
    float3 tide = OrgLN2(wp.xz, 0.11, 7.7, 1);
    float3 dry = OrgLN2(wp.xz, 0.8, 29.1, fw);
    float t = saturate(hw / (1.75 + 0.35 * tide.x));
    float edge = smoothstep(0.2, 1, t);
    float wet = (1 - smoothstep(0.12, 1, t)) * saturate(1 + edge * (0.5 * fw * dry.x + 0.12 * tide.x));
    return float2(wet, (1 - smoothstep(0.6, 4.2 + 0.5 * tide.x, hw + 0.6 * fw * dry.x)) * (1 - wet));
}

TmOut TmEvaluate(float3 wp, float3 n, float3 coatColor, float off, float rockMix, float stone)
{
    float3 grassColor = coatColor;
    float2 o2 = OrgOrigin(_WorldSpaceCameraPos);
    orgO = float3(o2.x, 0, o2.y);
    float3 dx = ddx(wp), dy = ddy(wp);
    float la = length(dx), lb = length(dy);
    float fp = max(max(sqrt(la * lb), 0.4 * max(la, lb)), 1e-4);
    float period = max(_StabbyStrataPeriod, 1);
    float u0 = (wp.y + off) / period;
    float2 dux = float2(ddx(u0), 0), duy = float2(ddy(u0), 0);
    float3 v = normalize(_WorldSpaceCameraPos - orgO - wp);
    float2 wa = wp.xz + orgO.xz;
    float fj = OrgFade(0.27, fp);
    float3 jn = OrgN2(wa * 0.27 + 41.7, fj);
    float4 cw = SAMPLE_TEXTURE2D_LOD(_StabbyCoat, sampler_StabbyCoat, wa * _StabbyCoatUV.x + _StabbyCoatUV.y, 0);
    float hw = wp.y - SAMPLE_TEXTURE2D_LOD(_StabbyWaterLevel, sampler_StabbyWaterLevel, wa * _StabbyCoatUV.x + _StabbyCoatUV.y, 0).r;
    float sh = wp.y + 0.15 * OrgLN2(wp.xz, 0.035, 19.3, step(wp.y, 3)).x;
    float shore = smoothstep(2.1, 0.9, sh);
    float fq = OrgFade(1.7, fp);
    float ss = sh + 0.12 * fj * jn.x + 0.08 * OrgLN2(wa, 0.12, 29.7, OrgFade(0.12, fp)).x + 0.25 * fq * OrgLN2(wa, 1.7, 33.1, fq).x;
    float sb = smoothstep(3.4, 0.9, ss);
    coatColor = lerp(coatColor, ToLinear(_StabbySand.rgb), smoothstep(3.8, 1.2, ss));
    cw = saturate(cw * (1 + 0.2 * fj * (1 - shore) * float4(jn.x, -jn.x, jn.x * 0.3, -jn.x)));
    cw /= max(dot(cw, 1), 1e-3);
    cw *= cw * cw;
    cw /= max(dot(cw, 1), 1e-6);
    cw = lerp(cw, float4(0, 0, 1, 0), sb);
    float fk = OrgFade(1.3, fp);
    float3 jk = OrgN2(wa * 1.3 + 13.9, fk);
    float br = 0.12 * fj * jn.x + 0.35 * fk * jk.x;
    float sl = n.y;
    float4 fo = SAMPLE_TEXTURE2D_LOD(_StabbyForest, sampler_StabbyForest, wa * _StabbyCoatUV.x + _StabbyCoatUV.y, 0);
    float2 fl2 = saturate(cw.x + cw.y) * TmFloorCover(wp.xz, fo, fp);
    float fb = fl2.y * (1 - stone);
    float bf = br * (1 - 0.85 * fb);
    float m = saturate(max((rockMix * (1 - 0.8 * fb) - 0.5) * 1.6 + 0.5 + bf, 1 - smoothstep(0.5, 0.72, sl + 0.4 * bf)));
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
    float lc = 0;
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
        float can = smoothstep(0.08, 0.35, fo.r + fo.g) * cw.x;
        k.albedo = lerp(k.albedo, dot(k.albedo, float3(0.2126, 0.7152, 0.0722)) * float3(1.25, 0.95, 0.68) * 0.67, 0.15 * can);
        float cov = TmLitter(k, wp, fo, fp, fl2.x);
        lc = cov * up;
        mr = TmMicro(wp.xz, fp) * dot(cw, float4(1, 1, 0.3, 0.5));
        k.albedo *= 1 + 0.1 * mr.x;
        k.grad += float3(mr.y, 0, mr.z);
        float bare = saturate(saturate(cw.y + cw.z) * (1 - cw.x) + can) * (1 - cov);
        [branch] if (can * (1 - cov) > 0.01)
        {
            float4 s1 = TmFleck(wp.xz, float2x2(1, 0.3, -0.3, 1), 9, fp, 43, 0.08, 0.2, 0.15 + 0.35 * can);
            float4 s2 = TmFleck(wp.xz, float2x2(1, -0.8, 0.8, 1), 15, fp, 47, 0.08, 0.18, 0.1 + 0.3 * can);
            float sp = saturate(s1.w + s2.w) * (1 - cov);
            k.albedo = lerp(k.albedo, k.albedo * lerp(float3(0.62, 0.52, 0.42), float3(1.25, 1.08, 0.85), saturate(0.5 + 0.8 * (s1.x + s2.x))), 0.6 * sp);
            k.grad += (1 - cov) * 0.04 * float3(s1.y + s2.y, 0, s1.z + s2.z);
        }
        [branch] if (bare > 0.01)
        {
            float4 g1 = TmFleck(wp.xz, float2x2(1, 0, 0, 1), 41, fp, 23, 0.12, 0.32, 1);
            float4 g2 = TmFleck(wp.xz, float2x2(1, 0.5, -0.5, 1), 13, fp, 31, 0.15, 0.38, 0.55);
            float4 g3 = TmFleck(wp.xz, float2x2(1, -0.75, 0.75, 1), 19, fp, 37, 0.12, 0.25, 0.12);
            k.albedo *= 1 + bare * (0.06 * g1.x + 0.05 * g2.x - 0.12 * g3.w);
            k.grad += bare * 0.12 * float3(g2.y, 0, g2.z);
        }
        tmGrass = c * up * cw.x * (1 - cov);
        o.albedo += c * lerp(bank * (1 + 0.1 * mr.x), k.albedo, up);
        o.grad += c * (up * k.grad + (1 - up) * ew * e.grad);
    }
    scr *= (1 - lc) * (1 - 0.85 * fb);
    [branch] if (scr > 0.01)
    {
        float3 sm = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(0.5, 0.5), 12).rgb;
        float3 sc = lerp(bank, dot(sm, float3(0.2126, 0.7152, 0.0722)) * float3(1.04, 1, 0.92), 0.55 - 0.25 * ew);
        o.albedo = lerp(o.albedo, sc * (1 + 0.3 * mr.x * (1 - ew)), 0.55 * scr);
        o.grad += 0.55 * scr * ew * e.grad;
    }
    float wet = 0;
    [branch] if (hw < 4.8)
    {
        float2 wd = TmWet(wp, hw, fp);
        wet = wd.x;
        float damp = wd.y;
        tmGrass *= 1 - 0.85 * smoothstep(0.45, 1, wet);
        float lum = dot(o.albedo, float3(0.2126, 0.7152, 0.0722));
        o.albedo = max(0, lerp(lum, o.albedo, 1 + 0.3 * wet + 0.12 * damp)) * (1 - 0.38 * wet - 0.15 * damp);
        tmRough *= 1 - 0.55 * wet - 0.12 * damp;
    }
    float3 g = (o.grad - n * dot(o.grad, n)) * (1 - 0.5 * wet);
    o.grad = normalize(n - g);
    tmRough = saturate(tmRough);
    return o;
}

#endif
