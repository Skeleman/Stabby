#ifndef STABBY_CONTACT
#define STABBY_CONTACT

TEXTURE2D(_StabbyContact);
SAMPLER(sampler_StabbyContact);
float4 _StabbyContactUV;

float3 ContactTint(float3 g)
{
    float l = dot(g, float3(0.2126, 0.7152, 0.0722));
    return max(0, lerp(l.xxx, g, 1.2)) * 0.36;
}

float ContactBed(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_StabbyBed, sampler_StabbyBed, xz * _StabbyFieldUV.x + _StabbyFieldUV.y, 0).r;
}

float3 ContactColor(float2 xz, float h)
{
    float3 g = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
    g = lerp(g, ToLinear(_StabbySand.rgb), smoothstep(6, 2, h));
    float3 sm = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(0.5, 0.5), 12).rgb;
    float3 rock = lerp(sm, dot(sm, float3(0.2126, 0.7152, 0.0722)) * float3(1.02, 1, 0.97), 0.85);
    g = lerp(g, rock, smoothstep(2, 6, ContactBed(xz) - h));
    return ContactTint(g);
}

float2 ContactAt(float2 xz)
{
    [branch] if (_StabbyContactUV.w <= 0) return 0;
    float2 uv = xz * _StabbyContactUV.x + _StabbyContactUV.yz;
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 10);
    return SAMPLE_TEXTURE2D_LOD(_StabbyContact, sampler_StabbyContact, uv, 0).rg * edge;
}

float3 LushTint(float3 g)
{
    float l = dot(g, float3(0.2126, 0.7152, 0.0722));
    return max(0, lerp(l.xxx, g, 1.25)) * float3(0.6, 0.76, 0.52);
}

float3 ContactGround(float3 albedo, float2 xz, float h)
{
    float2 kg = ContactAt(xz);
    [branch] if (max(kg.x, kg.y) < 0.004) return albedo;
    float n = OrgNoise2(xz * 1.9 + 3.7).x + 0.5 * OrgNoise2(xz * 6.3 - 11.2).x;
    float g = saturate(kg.y * (0.8 + 0.5 * n));
    albedo = lerp(albedo, LushTint(albedo), 0.8 * g * g * (3 - 2 * g));
    float k = saturate(kg.x * (1.05 + 0.8 * n));
    return lerp(albedo, ContactTint(albedo), 0.75 * k * k * (3 - 2 * k));
}

float ContactBand(float size, float thick)
{
    return min(min(0.5, 0.14 + 0.22 * size), 0.8 * thick);
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size, float thick)
{
    float above = wp.y - ground;
    [branch] if (above > 0.8) return albedo;
    float n = OrgNoise2(wp.xz * 2.3 + wp.y * 1.7).x + 0.5 * OrgNoise2(float2(wp.x - wp.z, wp.y) * 7.1 + 5.3).x;
    float band = ContactBand(size, thick) * (1 + 0.4 * n);
    float k = 1 - smoothstep(-0.04, band, above);
    float3 c = ContactColor(wp.xz, ground), lw = float3(0.2126, 0.7152, 0.0722);
    c *= min(1, 0.45 * dot(albedo, lw) / max(dot(c, lw), 1e-4));
    return lerp(albedo, c, 0.8 * k * k);
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size)
{
    return ContactObject(albedo, wp, ground, size, 1e4);
}

#endif
