float BarkFade(float freq, float fp) { return saturate(2 - 4 * freq * fp); }

float2 SmoothD(float e0, float e1, float x)
{
    float t = saturate((x - e0) / (e1 - e0));
    return float2(t * t * (3 - 2 * t), 6 * t * (1 - t) / (e1 - e0));
}

float3 OakBark(float2 m, float fp, out float tone)
{
    float3 w = TmNoise2(m * float2(2.2, 0.5) + 3.7);
    float2 q = float2(m.x * 11 + 1.1 * w.x, m.y * 1.3);
    float3 a = TmNoise2(q);
    float2 s = SmoothD(0.03, 0.3, abs(a.x));
    float2 dq = s.y * sign(a.x) * a.yz;
    float2 g = float2(dq.x * (11 + 2.42 * w.y), dq.x * 0.55 * w.z + dq.y * 1.3);
    float h = s.x;
    float fc = BarkFade(9, fp);
    float3 c = TmNoise2(m * float2(3, 7) + 11.3);
    float2 k = SmoothD(0.8, 0.96, 1 - abs(c.x));
    h -= 0.5 * fc * s.x * k.x;
    g += 0.5 * fc * s.x * k.y * sign(c.x) * c.yz * float2(3, 7);
    float ff = BarkFade(28, fp);
    float3 f = TmNoise2(m * float2(28, 12) + 5.1);
    h += 0.12 * ff * f.x;
    g += 0.12 * ff * f.yz * float2(28, 12);
    tone = h;
    return float3(h, g * BarkFade(11, fp));
}

float3 SpruceBark(float2 m, float fp, out float tone, out float fresh)
{
    float3 w = TmNoise2(m * float2(1.3, 0.6) + 9.1);
    float3 v = TmNoise2(m * float2(2.3, 1.9) + 4.7);
    float2 wg = 0.35 * w.yz * float2(1.3, 0.6);
    float2 vg = 0.3 * v.yz * float2(2.3, 1.9);
    float3 k = TmNoise2(m * float2(6.5, 3.1) + 1.3);
    float2 kg = 0.45 * k.yz * float2(6.5, 3.1);
    float qy = m.y * 5.5 + 0.35 * w.x + 0.3 * v.x + 0.45 * k.x;
    float row = floor(qy);
    float sx = 0.75 + 0.5 * TmHash(int2((int)row, 37));
    float qx = (m.x * 8 + 0.35 * w.x + 0.45 * v.x) * sx + TmHash(int2((int)row, 71)) * 3;
    float2 dqx = sx * (float2(8, 0) + wg + 1.5 * vg), dqy = float2(0, 5.5) + wg + vg + kg;
    float2 q = float2(qx, qy);
    float2 cell = floor(q), f = q - cell;
    float r = TmHash((int2)cell + int2(0, 1931));
    float r2 = TmHash((int2)cell + int2(577, 3));
    float3 e = TmNoise2(m * float2(21, 15) + 6.1);
    float skew = (r2 - 0.5) * 0.5;
    f.x = saturate(f.x + skew * (f.y - 0.5));
    float ex = min(f.x, 1 - f.x) + 0.06 * e.x, ey = min(f.y, 1 - f.y) + 0.05 * e.x + 0.14 * saturate(2 * v.x - 0.3);
    float2 lx = SmoothD(0, 0.06 + 0.08 * r2, ex), ly = SmoothD(0, 0.05 + 0.08 * r, ey);
    lx.y *= sign(0.5 - f.x);
    ly.y *= sign(0.5 - f.y);
    float tilt = 0.55 + 0.45 * (1 - f.y) * (0.5 + r);
    float h = lx.x * ly.x * tilt;
    float2 df = float2(lx.y * ly.x * tilt, lx.x * ly.y * tilt - lx.x * ly.x * 0.45 * (0.5 + r));
    float2 eg = e.yz * float2(21, 15) * lx.x * ly.x * 0.06;
    float2 g = df.x * dqx + df.y * dqy + eg;
    float ff = BarkFade(14, fp);
    float3 fn = TmNoise2(m * float2(14, 9) + 2.3);
    h += 0.08 * ff * fn.x;
    g += 0.08 * ff * fn.yz * float2(14, 9);
    tone = lx.x * ly.x * (0.8 + 0.35 * r) + 0.1 * fn.x + 0.08 * v.x;
    fresh = smoothstep(0.72, 0.95, r) * lx.x * ly.x;
    return float3(h, g * BarkFade(8, fp));
}
