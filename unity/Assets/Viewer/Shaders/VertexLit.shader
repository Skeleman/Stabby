Shader "Stabby/VertexLit"
{
    Properties { _StitchMask ("Stitch mask", Vector) = (0, 0, 0, 0) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #include "TerrainStitch.hlsl"

            float4 _StabbySky, _StabbyGround, _StabbyFieldUV, _StabbySand, _StabbyGrassFar;
            float4 _StabbySinterA, _StabbySinterB, _StabbySinterC, _StabbyMudClay, _StabbyMudGrey, _StabbyMudSurface, _StabbyPeatWet, _StabbyPeatDark, _StabbyPeatFibre;
            float _StabbyStrataPeriod, _StabbyCellMeters;
            TEXTURE2D(_StabbyStrata);
            SAMPLER(sampler_StabbyStrata);
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBiome);
            SAMPLER(sampler_StabbyBiome);
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

            #include "TerrainMaterials.hlsl"
            #include "Look.hlsl"
            #include "Assets/Game/Grass/Wind.hlsl"
            #include "Contact.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 strata : TEXCOORD0; float4 stitch : TEXCOORD1; float4 stitchNormal : TEXCOORD2; float4 stitchColor : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 strata : TEXCOORD2; float2 xz : TEXCOORD3; float field : TEXCOORD4; float sinter : TEXCOORD5; float peat : TEXCOORD6; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float4 sm = _StitchMask;
                float lift = ((uint)i.stitch.w & 4096u) != 0 ? sm.w * 0.5 + 20 : 0;
                float t = StitchT(i.positionOS.xyz + float3(0, lift, 0), i.stitch);
                VertexPositionInputs p = GetVertexPositionInputs(lerp(i.positionOS.xyz, i.stitch.xyz, t));
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(normalize(lerp(i.normalOS, i.stitchNormal.xyz, t)));
                o.color = lerp(i.color, i.stitchColor, t);
                o.strata = float4(i.strata.x, lerp(i.strata.y, i.stitchNormal.w, t), p.positionWS.y + lift, i.strata.z);
                o.xz = p.positionWS.xz - OrgOrigin(_WorldSpaceCameraPos);
                o.field = sm.w > 0 ? lerp(saturate(sm.w / _StabbyCellMeters - 1), saturate(2 * sm.w / _StabbyCellMeters - 1), t) : 1;
                o.sinter = i.strata.w;
                o.peat = frac(i.stitch.w) * 2;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float SinterRim(float p) { return smoothstep(0.55, 0.93, p) * (1 - smoothstep(0.93, 1, p)); }
            float4 SinterN(float3 p, float f, float c, float w) { float4 r = 0; [branch] if (w > 0) r = OrgRL3(p, f, c, 1); return r; }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 color = i.color.rgb;
                float off = i.strata.x, mix = i.strata.y, h = i.strata.z, f = saturate(i.strata.w), stone = saturate(i.color.a - 1), ao = min(i.color.a, 1);
                if (f > 0)
                {
                    float2 xa = i.xz + OrgOrigin(_WorldSpaceCameraPos);
                    float2 sl = SAMPLE_TEXTURE2D(_StabbySlope, sampler_StabbySlope, xa * _StabbyFieldUV.x + _StabbyFieldUV.y).rg;
                    float4 bo = SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, xa * _StabbyFieldUV.z + _StabbyFieldUV.w, 0);
                    float3 fn = normalize(float3(-sl.x, 1, -sl.y)), fc;
                    float fr = 0;
                    if (h <= 0) fc = ToLinear(_StabbySand.rgb) * lerp(0.62, 0.45, saturate(-h / 30));
                    else
                    {
                        float slope = sqrt(max(0, 1 - fn.y * fn.y)) / max(fn.y, 0.05), shore = saturate(1 - h / 6);
                        fc = ToLinear(bo.rgb);
                        fr = saturate((slope - 0.25) / 0.35) * (1 - shore);
                    }
                    n = normalize(lerp(n, fn, f * i.field));
                    color = lerp(color, fc, f);
                    off = lerp(off, bo.a, f);
                    mix = lerp(mix, fr, f);
                }
                if (f < 1)
                {
                    float2 xa = i.xz + OrgOrigin(_WorldSpaceCameraPos);
                    float bed = SAMPLE_TEXTURE2D_LOD(_StabbyBed, sampler_StabbyBed, xa * _StabbyFieldUV.x + _StabbyFieldUV.y, 0).r;
                    mix = lerp(mix, min(mix, 0.5), (1 - f) * (1 - stone) * smoothstep(0.8, 0.97, n.y) * smoothstep(-1, 0, h - bed));
                    ao *= lerp(1, lerp(0.5, 1, saturate((h - bed) / 0.8)) * lerp(0.7, 1, saturate(n.y + 0.8)), stone);
                }
                {
                    float2 xc = (i.xz + OrgOrigin(_WorldSpaceCameraPos)) * _StabbyFieldUV.x + _StabbyFieldUV.y;
                    float du = 3 * _StabbyFieldUV.y, d = du / _StabbyFieldUV.x;
                    float2 ge = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, xc + float2(du, 0), 0).rg, gw = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, xc - float2(du, 0), 0).rg;
                    float2 gn = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, xc + float2(0, du), 0).rg, gs = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, xc - float2(0, du), 0).rg;
                    tmCurv = (ge.x - gw.x + gn.y - gs.y) / (2 * d);
                    float2 gm = 0.25 * (ge + gw + gn + gs);
                    float gl = length(gm), coh = gl / max(0.25 * (length(ge) + length(gw) + length(gn) + length(gs)), 1e-5);
                    tmShore = float4(gm / max(gl, 1e-5), gl, smoothstep(0.004, 0.014, gl) * smoothstep(0.32, 0.18, gl) * smoothstep(0.82, 0.96, coh));
                }
                float3 ng = n;
                TmOut m = TmEvaluate(float3(i.xz.x, h, i.xz.y), n, color, off, mix, stone);
                n = m.grad;
                [branch] if (i.sinter > 0.01)
                {
                    float4 ra = _StabbyRockA, rb = _StabbyRockB;
                    ra.y = OrgF(0);
                    float3 q = float3(i.xz.x, h, i.xz.y), lw = float3(0.2126, 0.7152, 0.0722);
                    float3 dx = ddx(q), dy = ddy(q);
                    float la = length(dx), lb = length(dy);
                    float fp = max(max(sqrt(la * lb), 0.4 * max(la, lb)), 1e-4);
                    float s = saturate(i.sinter), t = smoothstep(0.05, 0.5, 1 - ng.y);
                    float4 wa = SinterN(q, ra.y * 0.45, 43.7, 1), wb = SinterN(q, ra.y * 0.45, 47.1, 1);
                    float4 ma = SinterN(q, rb.z, 51.3, 1), mb = SinterN(q, rb.z, 53.9, 1);
                    float3 qw = q + float3(wa.x, 0, wb.x) * (0.55 / ra.y), qm = q + float3(ma.x, 0, mb.x) * (0.5 / rb.z);
                    float4 zone = SinterN(qm, ra.w * 0.3, 61.7, 1), chan = SinterN(qm, ra.w * 0.9, 67.3, 1), spot = SinterN(qm, ra.w * 0.5, 63.1, 1), sect = SinterN(qm, ra.w * 0.15, 59.9, 1);
                    float4 bump = OrgFbm3(qm + (qw - q), ra.w, 1, 3, fp);
                    float gw = OrgRungW(0, fp), fw2 = OrgRungW(1, fp), fw3 = OrgRungW(2, fp);
                    float4 grain = SinterN(qw, ra.y, 71.3, gw);
                    float3 qf = qw + float3(grain.y, 0, grain.w) * (0.4 / ra.y);
                    float4 fine = SinterN(qf, OrgF(1), 77.9, fw2), micro = SinterN(qf + float3(fine.w, 0, fine.y) * (0.25 / ra.y), OrgF(2), 83.1, fw3);
                    float bv = bump.x * ra.w;
                    float bleach = smoothstep(0, 0.3, s * (1 + 0.6 * zone.x + 0.25 * bv));
                    float crust = smoothstep(0.42, 0.72, s + 0.22 * zone.x + 0.1 * bv + 0.05 * gw * grain.x);
                    float vent = smoothstep(0.7, 0.95, s + 0.12 * zone.x);
                    float near = smoothstep(0.45, 0.85, s + 0.1 * zone.x), cwid = 0.09 + 0.1 * vent;
                    float chv = abs(chan.x + 0.2 * gw * grain.x + 0.1 * fw2 * fine.x);
                    float runoff = smoothstep(cwid, 0.35 * cwid, chv) * near * smoothstep(-0.15, 0.25, sect.x) * smoothstep(-0.4, 0.0, spot.x) * crust;
                    float core = smoothstep(0.6 * cwid, 0, chv), outlet = runoff * vent;
                    float tf = rb.x * 2, ftf = OrgFade(tf, fp), tr = smoothstep(0.15, 0.55, 1 - ng.y);
                    float w1 = (h + 0.06 * ma.x + 0.03 * wa.x + 0.01 * gw * grain.x) * tf;
                    float dr = (SinterRim(frac(w1 + 0.02)) - SinterRim(frac(w1 - 0.02))) * 25;
                    float3 terr = 0.02 * dr * tf * ftf * tr * crust * near * (float3(0, 1, 0) + 0.18 * bump.yzw);
                    float cf = rb.z * 2, fcr = OrgFade(cf * 4, fp);
                    float4 cn = SinterN(qm + 0.25 * (qw - q), cf, 91.3, fcr);
                    float cv = cn.x + 0.3 * gw * grain.x, cw = 0.07 + 0.03 * fine.x * fw2;
                    float ck = saturate(abs(cv) / cw), crack = (1 - ck * ck * (3 - 2 * ck)) * fcr * crust * (1 - runoff);
                    float3 crg = 0.06 * ck * (1 - ck) * fcr * crust * (1 - runoff) * sign(cv) / cw * (cf * cn.yzw + 0.3 * ra.y * gw * grain.yzw);
                    float3 nod = (0.1 * fw2 * (0.7 + 0.3 * fine.x) * fine.yzw + 0.06 * fw3 * (0.7 + 0.3 * micro.x) * micro.yzw) * crust * (0.3 + 0.7 * vent) * (1 - runoff);
                    float rf = OrgF(1), w2 = (h + 0.04 * gw * grain.x / ra.y + 0.02 * wa.x / ra.y) * rf;
                    float3 rip = 0.004 * rf * fw2 * sin(6.2832 * w2) * runoff * t * t * float3(0, 1, 0);
                    float3 grad = 0.35 * bump.yzw * crust + 0.3 * ra.x * gw * grain.yzw * bleach + terr + crg + nod + rip;
                    n = normalize(lerp(n, ng, crust));
                    grad -= n * dot(grad, n);
                    float3 base = m.albedo;
                    float3 dead = lerp(dot(base, lw) * float3(1.08, 1.0, 0.84) * 1.2, _StabbyMudGrey.rgb, 0.4 + 0.2 * zone.x);
                    float pg = saturate(0.35 + 0.4 * zone.x - 0.2 * bv + 0.1 * sect.x);
                    float3 cream = lerp(_StabbySinterA.rgb, _StabbyMudGrey.rgb, 0.4 * pg) * (0.96 + 0.03 * bv + 0.02 * gw * grain.x - 0.05 * fw2 * saturate(-fine.x));
                    float3 mineral = lerp(_StabbySinterB.rgb, _StabbySinterC.rgb, saturate(0.35 * core + 0.65 * vent)) * lerp(1, 0.6, outlet * core) * (0.94 + 0.04 * gw * grain.x);
                    float3 a = lerp(base, dead, bleach);
                    a = lerp(a, cream, crust);
                    a = lerp(a, mineral, 0.85 * runoff);
                    m.albedo = a * (1 - 0.1 * crack);
                    tmRough = lerp(lerp(tmRough, _StabbySinterB.w, crust), 0.2, 0.7 * outlet);
                    tmGrass *= 1 - bleach;
                    n = normalize(n - grad);
                }
                [branch] if (i.sinter < -0.01)
                {
                    float3 q = float3(i.xz.x, h, i.xz.y);
                    float fp = max(max(length(ddx(q)), length(ddy(q))) * 0.7, 1e-4);
                    float4 big = OrgNoise3(q * 0.9 + 5.3, int3(0, 0, 0)), mid = OrgRL3(q, OrgF(0), 1.3, 0), fine = OrgRL3(q, OrgF(2), 8.9, 2);
                    float fm = OrgRungW(0, fp), ff = OrgRungW(2, fp);
                    float cover = smoothstep(0.3, 0.7, -i.sinter + 0.3 * big.x + 0.12 * mid.x * fm);
                    float wet = smoothstep(0.75, 1, -i.sinter + 0.15 * big.x);
                    float crack = smoothstep(0.08, 0, abs(fine.x + 0.3 * mid.x)) * ff * (1 - wet);
                    float3 a = lerp(_StabbyMudGrey.rgb, _StabbyMudClay.rgb, smoothstep(-0.4, 0.5, big.x + 0.5 * mid.x * fm));
                    a *= 0.92 + 0.08 * fine.x * ff - 0.25 * crack;
                    a = lerp(a, _StabbyMudSurface.rgb, 0.7 * wet);
                    float3 grad = 0.06 * mid.yzw * fm + 0.05 * fine.yzw * ff;
                    grad -= n * dot(grad, n);
                    m.albedo = lerp(m.albedo, a, cover);
                    tmRough = lerp(tmRough, lerp(0.9, _StabbyMudSurface.w, wet), cover);
                    n = normalize(n - grad * cover);
                }
                [branch] if (i.peat > 0.01)
                {
                    float3 q = float3(i.xz.x, h, i.xz.y);
                    float fp = max(max(length(ddx(q)), length(ddy(q))) * 0.7, 1e-4);
                    float4 big = OrgNoise3(q * 0.7 + 2.9, int3(0, 0, 0)), mid = OrgRL3(q, OrgF(0), 6.1, 0), fine = OrgRL3(q, OrgF(2), 4.4, 2);
                    float fm = OrgRungW(0, fp), ff = OrgRungW(2, fp);
                    float cover = smoothstep(0.35, 0.7, i.peat + 0.2 * big.x + 0.08 * mid.x * fm);
                    float wet = smoothstep(0.6, 1, i.peat + 0.15 * big.x + 0.06 * mid.x * fm);
                    float fib = smoothstep(0.3, 0.8, 0.6 * fine.x + 0.5 * mid.x) * ff * (1 - wet);
                    float3 a = lerp(_StabbyPeatWet.rgb, _StabbyPeatDark.rgb, wet * (0.75 + 0.25 * mid.x * fm));
                    a = lerp(a, _StabbyPeatFibre.rgb, 0.55 * fib) * (0.92 + 0.08 * fine.x * ff);
                    float3 grad = 0.05 * mid.yzw * fm * (1 - 0.6 * wet) + 0.03 * fine.yzw * ff;
                    grad -= n * dot(grad, n);
                    m.albedo = lerp(m.albedo, a, cover);
                    tmRough = lerp(tmRough, lerp(0.8, _StabbyPeatWet.w, wet), cover);
                    tmGrass *= 1 - cover;
                    n = normalize(n - grad * cover);
                }
                [branch] if (_StabbyGrassFar.x > 0 && tmGrass > 0.01)
                {
                    float2 xa = i.xz + OrgOrigin(_WorldSpaceCameraPos);
                    float dr = distance(float3(xa.x, h, xa.y), _WorldSpaceCameraPos) / _StabbyGrassFar.x;
                    float far = smoothstep(0.7, 1, dr);
                    float3 bt = lerp(dot(m.albedo, float3(0.2126, 0.7152, 0.0722)).xxx, m.albedo, 0.74) * float3(1.0, 0.96, 0.86);
                    m.albedo = lerp(m.albedo, bt, tmGrass * smoothstep(0.4, 1, dr));
                    float gust = WindGust(xa, _Time.y);
                    m.albedo *= 1 + tmGrass * (_StabbyGrassFar.y + (gust - 0.5) * 0.3 * far * (0.4 + _StabbyWind.z));
                }
                float2 xv = i.xz + OrgOrigin(_WorldSpaceCameraPos);
                m.albedo = ContactGround(m.albedo, xv, h);
                float2 sj = 0.08 * float2(OrgL2(xv, 0.9, 113.1).x, OrgL2(xv, 0.9, 117.9).x) + 0.03 * float2(OrgL2(xv, OrgF(0), 121.3).x, OrgL2(xv, OrgF(0), 125.7).x);
                Light l = GetMainLight(TransformWorldToShadowCoord(float3(xv.x + sj.x, h, xv.y + sj.y)), float3(xv.x, h, xv.y), half4(1, 1, 1, 1));
                l.color *= l.shadowAttenuation;
                float3 v = normalize(_WorldSpaceCameraPos - float3(xv.x, h, xv.y));
                float3 ambient = lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5);
                float3 c = (LookColor(m.albedo, 1) * (l.color * saturate(dot(n, l.direction)) + ambient) + LookSheen(n, v, l.direction, l.color, tmRough)) * ao;
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "TerrainStitch.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 stitch : TEXCOORD1; float4 stitchNormal : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float lift = ((uint)i.stitch.w & 4096u) != 0 ? _StitchMask.w * 0.5 + 20 : 0;
                i.positionOS.y += lift;
                i.stitch.y += lift;
                float t = StitchT(i.positionOS.xyz, i.stitch);
                float3 wp = TransformObjectToWorld(lerp(i.positionOS.xyz, i.stitch.xyz, t));
                float3 wn = TransformObjectToWorldNormal(normalize(lerp(i.normalOS, i.stitchNormal.xyz, t)));
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
