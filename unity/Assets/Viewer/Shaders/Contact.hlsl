#ifndef STABBY_CONTACT
#define STABBY_CONTACT

TEXTURE2D(_StabbyContact);
SAMPLER(sampler_StabbyContact);
float4 _StabbyContactUV;

float FieldShare(float cover)
{
    return 1 - (cover < 0 ? smoothstep(0.3, 0.9, -cover) : smoothstep(2, 6, cover));
}

float3 ContactTint(float3 g, float ice)
{
    float l = dot(g, float3(0.2126, 0.7152, 0.0722));
    return max(0, lerp(l.xxx, g, 1.2)) * lerp(0.36, 0.82, ice);
}

float3 ContactTint(float3 g)
{
    return ContactTint(g, 0);
}

float ContactBed(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_StabbyBed, sampler_StabbyBed, xz * _StabbyFieldUV.x + _StabbyFieldUV.y, 0).r;
}

float3 ContactRock()
{
    float3 sm = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(0.5, 0.5), 12).rgb;
    return lerp(sm, dot(sm, float3(0.2126, 0.7152, 0.0722)) * float3(1.02, 1, 0.97), 0.85);
}

float3 ContactColor(float2 xz, float h, float ice)
{
    float3 g = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
    g = lerp(g, ToLinear(_StabbySand.rgb), smoothstep(6, 2, h));
    g = lerp(g, ContactRock(), smoothstep(2, 6, ContactBed(xz) - h));
    return ContactTint(lerp(g, _StabbyIce.rgb * float3(0.86, 0.94, 1.0), ice), ice);
}

float3 ContactColor(float2 xz, float h)
{
    return ContactColor(xz, h, 0);
}

float3 BounceGround(float3 g, float2 xz, float h)
{
    float3 rock = ContactRock();
    float3 lum = float3(0.2126, 0.7152, 0.0722);
    rock *= min(1, dot(g, lum) / max(dot(rock, lum), 1e-4));
    return lerp(g, rock, smoothstep(2, 6, ContactBed(xz) - h));
}

float4 ContactAll(float2 xz)
{
    [branch] if (_StabbyContactUV.w <= 0) return 0;
    float2 uv = xz * _StabbyContactUV.x + _StabbyContactUV.yz;
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 10);
    return SAMPLE_TEXTURE2D_LOD(_StabbyContact, sampler_StabbyContact, uv, 0) * edge;
}

float2 ContactAt(float2 xz)
{
    return ContactAll(xz).rg;
}

float3 LushTint(float3 g)
{
    float l = dot(g, float3(0.2126, 0.7152, 0.0722));
    return max(0, lerp(l.xxx, g, 1.25)) * float3(0.6, 0.76, 0.52);
}

float3 ContactGround(float3 albedo, float2 xz, float h, float ny, float ice)
{
    float4 ka = ContactAll(xz);
    [branch] if (max(max(ka.x, ka.y), max(ka.z, ka.w)) < 0.004) return albedo;
    float cave = _StabbyFieldUV.x > 0 ? smoothstep(2, 6, ContactBed(xz) - h) : 0;
    float2 kg = float2(lerp(ka.x, lerp(ka.w, ka.z, smoothstep(-0.3, 0.3, ny)), cave), ka.y * (1 - cave));
    [branch] if (max(kg.x, kg.y) < 0.004) return albedo;
    float n = OrgNoise2(xz * 1.9 + 3.7).x + 0.5 * OrgNoise2(xz * 6.3 - 11.2).x;
    float g = saturate(kg.y * (0.8 + 0.5 * n));
    albedo = lerp(albedo, LushTint(albedo), 0.8 * g * g * (3 - 2 * g));
    float k = saturate(kg.x * (1.05 + 0.8 * n));
    return lerp(albedo, ContactTint(albedo, ice), 0.75 * k * k * (3 - 2 * k));
}

float3 ContactGround(float3 albedo, float2 xz, float h, float ny)
{
    return ContactGround(albedo, xz, h, ny, 0);
}

float ContactBand(float size, float thick)
{
    return min(min(0.5, 0.14 + 0.22 * size), 0.8 * thick);
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size, float thick, float dir, float ice)
{
    float above = dir * (wp.y - ground);
    [branch] if (above > 0.8) return albedo;
    float n = OrgNoise2(wp.xz * 2.3 + wp.y * 1.7).x + 0.5 * OrgNoise2(float2(wp.x - wp.z, wp.y) * 7.1 + 5.3).x;
    float band = ContactBand(size, thick) * (1 + 0.4 * n);
    float k = 1 - smoothstep(-0.04, band, above);
    float3 c = ContactColor(wp.xz, ground, ice), lw = float3(0.2126, 0.7152, 0.0722);
    c *= min(1, lerp(0.45, 0.85, ice) * dot(albedo, lw) / max(dot(c, lw), 1e-4));
    return lerp(albedo, c, 0.8 * k * k);
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size, float thick, float dir)
{
    return ContactObject(albedo, wp, ground, size, thick, dir, 0);
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size, float thick)
{
    return ContactObject(albedo, wp, ground, size, thick, 1);
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size)
{
    return ContactObject(albedo, wp, ground, size, 1e4);
}

#endif
