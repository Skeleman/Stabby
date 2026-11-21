#ifndef STABBY_LOOK_INCLUDED
#define STABBY_LOOK_INCLUDED

float4 _StabbyLook;

float LookRough() { return _StabbyLook.y; }

float3 LookColor(float3 c, float allow)
{
    float t = _StabbyLook.x * allow;
    float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
    float s = (mx - mn) / max(mx, 1e-5);
    if (t <= 0 || s <= t) return c;
    float o = s - t;
    float k = (t + o / (1 + 2.5 * o)) / s;
    float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
    return lum + (c - lum) * k;
}

float3 LookSheen(float3 n, float3 v, float3 ld, float3 lc, float rough)
{
    if (_StabbyLook.y <= 0) return 0;
    float r = max(rough, 0.05), a = r * r, e = max(2 / (a * a) - 2, 1);
    float nl = saturate(dot(n, ld)), nv = saturate(dot(n, v));
    float f = _StabbyLook.z + (1 - _StabbyLook.z) * pow(1 - nv, 5) * (1 - r);
    return lc * (nl * f * pow(saturate(dot(n, normalize(ld + v))), e) * (e + 8) / 25.13);
}

#endif
