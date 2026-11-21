#ifndef STABBY_CONTACT
#define STABBY_CONTACT

TEXTURE2D(_StabbyContact);
SAMPLER(sampler_StabbyContact);
float4 _StabbyContactUV;

float3 ContactTint(float3 g)
{
    float l = dot(g, float3(0.2126, 0.7152, 0.0722));
    return lerp(g, l.xxx, 0.45) * float3(0.37, 0.35, 0.33);
}

float3 ContactColor(float2 xz, float h)
{
    float3 g = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
    g = lerp(g, ToLinear(_StabbySand.rgb), smoothstep(6, 2, h));
    return ContactTint(lerp(g, dot(g, float3(0.2126, 0.7152, 0.0722)).xxx, 0.4));
}

float ContactBed(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_StabbyBed, sampler_StabbyBed, xz * _StabbyFieldUV.x + _StabbyFieldUV.y, 0).r;
}

float3 ContactGround(float3 albedo, float2 xz, float h)
{
    [branch] if (_StabbyContactUV.w <= 0) return albedo;
    float2 uv = xz * _StabbyContactUV.x + _StabbyContactUV.yz;
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 10);
    float k = SAMPLE_TEXTURE2D_LOD(_StabbyContact, sampler_StabbyContact, uv, 0).r * edge;
    [branch] if (k < 0.004) return albedo;
    float n = TmNoise2(xz * 1.9 + 3.7).x + 0.5 * TmNoise2(xz * 6.3 - 11.2).x;
    k = saturate(k * (1.05 + 0.8 * n));
    return lerp(albedo, ContactTint(albedo), 0.75 * k * k * (3 - 2 * k));
}

float3 ContactObject(float3 albedo, float3 wp, float ground, float size)
{
    float above = wp.y - ground;
    [branch] if (above > 0.8) return albedo;
    float n = TmNoise2(wp.xz * 2.3 + wp.y * 1.7).x + 0.5 * TmNoise2(float2(wp.x - wp.z, wp.y) * 7.1 + 5.3).x;
    float band = min(0.5, 0.14 + 0.22 * size) * (1 + 0.4 * n);
    float k = 1 - smoothstep(-0.04, band, above);
    return lerp(albedo, ContactColor(wp.xz, ground), 0.8 * k * k);
}

#endif
