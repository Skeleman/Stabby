Shader "Stabby/Object"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 0)
        _Fade ("Fade", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _Fade;
        CBUFFER_END

        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _Fade)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
        #define _Fade UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Fade)
        #endif

        float Bayer(float2 p)
        {
            uint2 q = (uint2)p & 3u;
            static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[(q.x << 2) | q.y] + 0.5) / 16.0;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _StabbySky, _StabbyGround, _StabbyFieldUV, _StabbySand, _StabbyIce;
            float4 _StabbyKit[42], _StabbyKitSrgb[42];
            float _StabbyClock;
            static float3 emberGlow;
            static float iceWrap;
            float _StabbyStrataPeriod;
            TEXTURE2D(_StabbyStrata);
            SAMPLER(sampler_StabbyStrata);
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBiome);
            SAMPLER(sampler_StabbyBiome);
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

            #include "Assets/Viewer/Shaders/TerrainMaterials.hlsl"
            #include "Assets/Viewer/Shaders/Contact.hlsl"
            #include "Assets/Viewer/Shaders/Bark.hlsl"
            #include "Assets/Viewer/Shaders/Look.hlsl"
            #include "Assets/Viewer/Shaders/LocalLights.hlsl"
            #include "Assets/Viewer/Shaders/CaveSky.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tone : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 pof : TEXCOORD2;
                float3 local : TEXCOORD3;
                float4 normalOS : TEXCOORD4;
                float3 axisWS : TEXCOORD5;
                float4 tone : TEXCOORD6;
                float3 origin : TEXCOORD7;
                float3 grain : TEXCOORD8;
                nointerpolation float seed : TEXCOORD9;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                [branch] if (_StabbyFieldUV.x > 0 && ((floor(i.tone.x * 0.5) >= 7 && floor(i.tone.x * 0.5) <= 10) || i.tone.w > 63.5))
                {
                    float3 org = UNITY_MATRIX_M._m03_m13_m23;
                    p.positionWS.y += _Fade.z - org.y + ContactBed(p.positionWS.xz) - ContactBed(org.xz);
                    p.positionCS = TransformWorldToHClip(p.positionWS);
                }
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.pof = float4(i.positionOS.xyz, ComputeFogFactor(p.positionCS.z));
                float4x4 m = UNITY_MATRIX_M;
                float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
                bool isLog = _BaseColor.a > 0.5 && _BaseColor.a < 1.5;
                float3 c = isLog ? float3(i.positionOS.x, i.positionOS.y - 0.5, i.positionOS.z) : i.positionOS.xyz;
                o.local = c * scale;
                o.normalOS = float4(i.normalOS, scale.x);
                o.axisWS = normalize(isLog ? m._m00_m10_m20 : m._m01_m11_m21);
                o.tone = i.tone;
                o.seed = frac(i.tone.w);
                o.origin = m._m03_m13_m23;
                o.grain = mul((float3x3)m, float3(i.tone.y, sqrt(saturate(1 - i.tone.y * i.tone.y - i.tone.z * i.tone.z)), i.tone.z));
                return o;
            }

            float3 Bumped(float3 n, float3 axis, float sgn, float2 g, float depth)
            {
                float3 b = cross(n, axis);
                float bl = length(b);
                b = bl > 1e-3 ? b / bl : normalize(cross(n, float3(0.31, 0.83, 0.47)));
                float3 t = cross(b, n);
                return normalize(n - depth * (g.x * sgn * b + g.y * t));
            }

            void Wood(float3 local, float3 n, float3 axis, float3 baseColor, bool isLog, float4 tone, float fp, float3 wp, float3 gc, out float3 albedo, out float3 nOut)
            {
                float3 lw = float3(0.2126, 0.7152, 0.0722);
                float along = isLog ? local.x : local.y;
                float2 rc = isLog ? local.yz : local.xz;
                float radius = max(length(rc), 1e-3);
                float sgn = isLog ? -1 : 1;
                int code = (int)round(tone.x);
                float age = tone.w;
                float3 weathered = lerp(ToLinear(float3(0.56, 0.5, 0.42)), ToLinear(float3(0.47, 0.46, 0.44)), age);
                float4 mn = OrgRP3(wp, OrgF(0), 0, 0), mf = OrgRP3(wp, OrgF(1), 17.3, 1);
                float mossAmt = code <= 2 ? tone.y : 0;
                float patch = smoothstep(0.5, 0.8, 0.5 + 0.5 * mn.x + 0.2 * mossAmt * age - 0.15 + 0.25 * mf.x);
                float moss = 0.9 * saturate(mossAmt * smoothstep(0.35, 0.85, n.y + 0.3 * mn.y) * patch * 1.3);
                float3 mossC = lerp(float3(0.07, 0.09, 0.03), float3(0.16, 0.18, 0.07), 0.5 + 0.5 * mf.y);
                float decay = lerp(1, 0.62, age * age);
                [branch] if (code == 3 || code == 5)
                {
                    float4 cl = OrgRP3(wp, OrgF(0), 1.7, 0);
                    float3 dirt = float3(0.115, 0.07, 0.038) * (0.75 + 0.4 * mn.y) * (0.8 + 0.35 * cl.x);
                    float3 nz = OrgRP3(wp, OrgF(1), 4.4, 1).yzw;
                    float pebble = smoothstep(0.55, 0.75, OrgRP3(wp, OrgF(2), 0, 2).x);
                    float3 contact = lerp(dirt * 0.4, gc * 0.25, 0.35);
                    albedo = lerp(dirt, gc * 0.5, 0.22 * saturate(tone.y * (0.7 + 0.6 * mn.x)));
                    albedo = code == 5 ? lerp(ToLinear(float3(0.43, 0.41, 0.38)) * (0.7 + 0.45 * mf.x), dirt, 0.25 + 0.2 * mn.z) : lerp(albedo, ToLinear(float3(0.42, 0.4, 0.37)), 0.5 * pebble * (1 - tone.y));
                    albedo = lerp(albedo, contact, 0.75 * tone.z);
                    float fib = smoothstep(0.82, 0.95, 1 - abs(OrgRP2(wp.xz, OrgF(1), wp.y * 3.1, 1).x)) * (1 - tone.y) * OrgRungW(1, fp) * (code == 5 ? 0 : 1);
                    albedo = lerp(albedo, ToLinear(float3(0.32, 0.25, 0.18)), 0.6 * fib);
                    float3 g = (0.06 + 0.06 * (code == 5)) * nz * OrgRungW(1, fp) + 0.05 * cl.yzw * OrgRungW(0, fp);
                    nOut = normalize(n - (g - n * dot(g, n)));
                    return;
                }
                [branch] if (code == 4)
                {
                    float2 m = float2(radius * 3.1 + 0.4 * wp.y, along);
                    float tn;
                    float3 b = OakBark(m * 2.5, fp, tn);
                    albedo = baseColor * 0.75 * (0.55 + 0.5 * tn) * decay;
                    albedo = lerp(albedo, lerp(float3(0.06, 0.045, 0.032), gc, 0.4), saturate(tone.y * (0.6 + 0.6 * mf.x)));
                    nOut = Bumped(n, normalize(cross(n, float3(0, 1, 0)) + 1e-3), 1, b.yz, 0.006);
                    return;
                }
                [branch] if (code == 2)
                {
                    float keep = OrgRungW(2, fp);
                    float3 wood = weathered * decay;
                    float2 g = 0;
                    if (tone.z > 0.5)
                    {
                        float2 q = float2(rc.x * 26, rc.y * 5) + along * 3;
                        float3 f = OrgNoise2(q);
                        float fib = 1 - abs(f.x), sp = OrgRP2(rc, OrgF(1), 3.1, 1).x;
                        albedo = wood * (0.7 + 0.35 * lerp(0.6, fib, keep)) * (0.9 + 0.12 * sp);
                        float3 sc = OrgRP3(wp, OrgF(1), 8.8, 1).yzw;
                        float3 gg = (0.05 * keep * sign(f.x)) * float3(f.y * 26, f.z * 5, 0) + 0.04 * sc;
                        nOut = normalize(n - (gg - n * dot(gg, n)));
                    }
                    else
                    {
                        float wob = 0.06 * OrgRP2(rc, OrgF(1), along, 1).x;
                        float r = (radius + wob) * 14;
                        float ring = 0.5 + 0.5 * cos(r * 6.2831853);
                        float crack = smoothstep(0.92, 1, OrgNoise2(float2(atan2(rc.y, rc.x) * 3, radius * 2)).x * 0.5 + 0.5);
                        albedo = wood * (1 - 0.18 * keep * ring) * (1 - 0.5 * crack * (0.4 + age));
                        float3 sc = OrgRP3(wp, OrgF(1), 2.2, 1).yzw;
                        float3 gg = 0.015 * sc * keep;
                        nOut = normalize(n - (gg - n * dot(gg, n)));
                    }
                    albedo = lerp(albedo, albedo * float3(0.62, 0.5, 0.4), 0.5 * smoothstep(0.45, 1, age) * (0.6 + 0.4 * mn.z));
                    albedo = lerp(albedo, mossC, moss * 0.7);
                    return;
                }
                bool spruce = code == 1;
                float2 st = isLog ? float2(1, 1) : float2(1.8, 0.5);
                float2 mm = float2(atan2(rc.y, rc.x) * radius, along) * st;
                float tn, fresh = 0;
                float3 bk = spruce ? SpruceBark(mm, fp, tn, fresh) : OakBark(mm, fp, tn);
                tn = isLog ? tn : saturate(0.35 + 0.6 * tn + 0.12 * mf.x);
                albedo = baseColor * (spruce ? 0.62 + 0.5 * tn : 0.5 + 0.6 * tn);
                albedo = spruce ? lerp(albedo, albedo * float3(1.35, 0.95, 0.75), fresh * (1 - age)) : lerp(albedo, dot(albedo, lw) * float3(1.04, 1, 0.94), 0.4 * saturate(tn));
                albedo = lerp(albedo, dot(albedo, lw) * float3(1, 0.97, 0.92), 0.45 * age) * decay;
                float4 ln = OrgNoise3(wp * 1.7 + 51.3, int3(0, 0, 0));
                float loss = smoothstep(0.62, 0.42, 1 - age + 0.35 * ln.x) * (0.5 + 0.5 * smoothstep(-0.2, 0.3, ln.x));
                float3 fibre = OrgNoise2(float2(mm.x * 30, mm.y * 3));
                float3 bare = weathered * decay * (0.75 + 0.3 * (1 - abs(fibre.x)));
                albedo = lerp(albedo, bare, loss);
                albedo = lerp(albedo, mossC, moss);
                float4 lc = OrgRP3(wp, OrgF(1), 31.1, 1);
                float lic = smoothstep(0.5, 0.68, lc.x) * (spruce ? 0.25 : 0.4) * (0.6 + 0.6 * age) * (1 - moss) * BarkFade(4.7, fp);
                albedo = lerp(albedo, dot(albedo, lw) * float3(0.9, 1.03, 0.78) * (2.1 + 0.3 * lc.y), 0.6 * saturate(lic));
                float2 g = lerp(bk.yz * st, 0.08 * fibre.yz * float2(30, 3) * BarkFade(30, fp), loss);
                float depth = (spruce ? 0.012 : 0.01) * (1 - 0.6 * moss) * (isLog ? 1 : 0.55);
                nOut = Bumped(n, axis, sgn, g, depth);
            }

            TmOut KitStone(float3 p, float3 n, float3 sc, float4 tone, float fp)
            {
                float3 lw = float3(0.2126, 0.7152, 0.0722);
                float3 bed = normalize(float3(0.4 * tone.z, 1, 0.4 * tone.y));
                float f1 = OrgRungW(0, fp), f2 = OrgRungW(1, fp), f3 = OrgRungW(1, fp), f4 = OrgRungW(2, fp), f5 = OrgRungW(3, fp), fl = OrgRungW(2, fp);
                float4 lump = OrgRP3(p, OrgF(0), 3.1, 0);
                float4 dent = OrgNoise3(p * 0.9 + 47.3, int3(0, 0, 0));
                float4 mot = OrgRP3(p, OrgF(1), 11.7, 1);
                float4 grain = 0, fine = 0, xtal = 0;
                [branch] if (f3 > 0) grain = OrgRP3(p, OrgF(1), 23.3, 1);
                [branch] if (f4 > 0) fine = OrgRP3(p, OrgF(2), 41.9, 2);
                [branch] if (f5 > 0) xtal = OrgRP3(p, OrgF(3), 5.7, 3);
                float4 lam = OrgNoise3(float3(dot(p, bed) * 19 + 0.9 * mot.x, 0.37, 0.71), int3(0, 0, 0));
                float3 g = 0.07 * OrgFade(0.9, fp) * dent.yzw + 0.065 * f1 * lump.yzw + 0.032 * f2 * mot.yzw + 0.035 * f3 * grain.yzw + 0.03 * f4 * fine.yzw + 0.022 * f5 * xtal.yzw + 0.025 * fl * lam.y * bed;
                float fc = OrgRungW(2, fp), fg = OrgFade(1.3, fp);
                float4 cr = OrgRP3(p, OrgF(0), 61.1, 0);
                float gate = fg * fc * smoothstep(0.35, 0.7, OrgNoise3(p * 1.3 + 77.7, int3(0, 0, 0)).x) * saturate(0.5 + 1.5 * mot.x);
                float wc0 = 0.012 * saturate(0.4 + 0.6 * mot.x + 0.4 * f3 * grain.x), wc = wc0 + 3.7 * fp + 1e-4;
                float cx = cr.x + 0.06 * f2 * mot.x + 0.04 * f3 * grain.x, t = saturate(abs(cx) / wc);
                float crack = (1 - t * t * (3 - 2 * t)) * gate * (wc0 / wc);
                g += 0.002 * gate * (wc0 / wc) * 6 * t * (1 - t) / wc * (cx < 0 ? -1 : 1) * 3.7 * cr.yzw;
                float pit = f4 * smoothstep(0.55, 0.85, -fine.x - 0.3 * f3 * grain.x);
                float3 c = lerp(sc, dot(sc, lw) * lerp(float3(0.95, 0.99, 1.04), float3(1.08, 1, 0.88), 0.5 + 0.5 * tone.z), 0.45) * (0.76 + 0.14 * tone.y);
                c *= 1 + 0.16 * f1 * lump.x + 0.1 * f2 * mot.x + 0.07 * fl * lam.x + 0.09 * f3 * grain.x + 0.07 * f4 * fine.x;
                float lit = f5 * smoothstep(0.5, 0.85, xtal.x), dk = f5 * smoothstep(0.55, 0.9, -xtal.x);
                c = lerp(c, dot(c, lw) * 1.5, 0.3 * lit) * (1 - 0.35 * dk) * (1 - 0.22 * pit) * (1 - 0.45 * crack);
                TmOut o;
                o.albedo = c;
                o.grad = g;
                tmRough = LookRough() + _StabbyLook.w * (-0.25 + 0.25 * f3 * grain.x + 0.3 * pit);
                return o;
            }

            float2 CourseRows(float v, bool top)
            {
                float rh = top ? 0.013 : 0.038 - 0.055 * max(v, 0);
                return float2(top ? v / rh : log(rh / 0.038) / -0.055, rh);
            }

            float CourseH(float ux, float2 rr, float gw, out float3 id, out float2 dh, out float gap)
            {
                float rh = rr.y, y = rr.x + 0.1 * OrgNoise2(float2(ux * 11, 3.7)).x;
                float r = floor(y), fv = y - r;
                int ri = (int)r;
                float hr = OrgHash(int2(ri, 71));
                float lr = rh * (1.7 + 1.6 * hr);
                float x = ux / lr + hr * 7.3 + 0.12 * OrgNoise2(float2(ux * 7.1, r * 1.9 + 0.5)).x;
                float k = floor(x), fx = x - k;
                int ki = (int)k;
                float jk = 0.25 + 0.5 * OrgHash(int2(ki, ri * 13 + 5));
                bool right = fx >= jk;
                float jn = 0.25 + 0.5 * OrgHash(int2(right ? ki + 1 : ki - 1, ri * 13 + 5));
                float lo = right ? jk : jn - 1, hi = right ? 1 + jn : jk;
                float si = right ? k : k - 1;
                float hs = OrgHash(int2((int)si, ri * 7 + 3));
                float top = 1 - 0.22 * hs * hs;
                float du = min(fx - lo, hi - fx) * lr, dv = min(fv, top - fv) * rh;
                float2 dir = du < dv ? float2(fx - lo < hi - fx ? 1 : -1, 0) : float2(0, fv < top - fv ? 1 : -1);
                float3 nz = OrgNoise2(float2(ux, y * rh) * 160 + 9.1);
                float d = min(du, dv) + 0.45 * gw * nz.x;
                float e = saturate((d - 0.5 * gw) / (2.5 * gw));
                float dhd = e > 0 && e < 1 ? (2 - 2 * e) / (2.5 * gw) : 0;
                dh = dhd * (dir + 72 * gw * nz.yz);
                gap = 1 - saturate((d - 0.2 * gw) / gw);
                id = float3(r, si, hs);
                return e * (2 - e);
            }

            float3 WallCourse(float3 po, float3 no, float3 vo, float fpo, out float3 id, out float dark, out float cf)
            {
                float3 an = abs(no);
                bool top = no.y > 0.75;
                float3 ta = top || an.x > 0.8 ? float3(0, 0, 1) : float3(1, 0, 0);
                float3 tb = top ? float3(1, 0, 0) : float3(0, 1, 0);
                float3 nf = cross(ta, tb);
                nf *= dot(nf, no) < 0 ? -1 : 1;
                float2 uv = float2(dot(po, ta), dot(po, tb));
                float gw0 = 0.0028, gw = gw0 + 0.8 * fpo, depth = 0.006;
                float2 rr = CourseRows(uv.y, top);
                cf = saturate((rr.y / fpo - 3) / 5);
                float3 vt = float3(dot(vo, ta), dot(vo, tb), max(dot(vo, nf), 0.25));
                float2 dh;
                float gap;
                float h0 = CourseH(uv.x, rr, gw, id, dh, gap);
                uv -= vt.xy / vt.z * depth * (1 - h0) * cf;
                rr = CourseRows(uv.y, top);
                CourseH(uv.x, rr, gw, id, dh, gap);
                dark = lerp(1 - 0.6 * saturate(2 * gw0 / rr.y), 1 - 0.6 * gap * gw0 / gw, cf);
                return depth * cf * (dh.x * ta + dh.y * tb);
            }

            float Dressed(inout float3 albedo, float4 tone, float3 wp)
            {
                float lum = dot(albedo, float3(0.3, 0.59, 0.11));
                float3 tint = lerp(float3(1.04, 0.98, 0.9), float3(1.08, 0.95, 0.8), 0.5 + 0.5 * tone.z);
                albedo = lerp(lum * tint, albedo, 0.25) * (1 + 0.22 * tone.y);
                float on = OrgRP3(wp, OrgF(0), 0, 0).x + 0.5 * OrgRP3(wp, OrgF(1), 4.1, 1).x;
                float occ = saturate((1 - saturate(tone.x * 2 - 1)) * (1.2 + 0.7 * on));
                albedo = lerp(albedo, albedo * float3(0.5, 0.47, 0.43), occ);
                return occ;
            }

            float3 Built(float4 tone, float3 g, float3 q, inout float3 n, float fp, float4 th, float3 v, float3 wp, out float occ)
            {
                int mat = (int)floor(tone.x * 0.5);
                float open = saturate((tone.x - 2 * mat) * 2 - 1);
                float3 sd = float3(th.w * 37.1, th.w * 11.3, th.w * 23.7);
                float3 pg = q - g * dot(q, g) * 0.9 + sd;
                float4 big = OrgNoise3(q * 1.1 + sd, int3(0, 0, 0)), mid = OrgRP3(q, OrgF(0), sd + 3.1, 0);
                float4 str = OrgRP3(pg, OrgF(1), 0, 1), fib = OrgRP3(pg, OrgF(2), 7.7, 2);
                float fm = OrgRungW(0, fp), fs = OrgRungW(1, fp), ff = OrgRungW(2, fp);
                float3 a, grad;
                float shade = 0;
                bool cap = th.x > 3.5 && th.x < 7.5, side = th.x > 7.5;
                float rf = saturate((th.x - 4) / 3), rm = 3 * th.y;
                if (mat == 4)
                {
                    float yf = th.x, L = th.y, fpa = th.z * _StabbySurface.x, sk = th.w * 977;
                    int ck = (int)(th.w * 65536);
                    float3 ac = cross(g, n);
                    ac *= rsqrt(max(dot(ac, ac), 1e-8));
                    float su = dot(q, ac), sv = dot(q, g), dn = max(dot(v, n), 0.2);
                    float ridge = smoothstep(0.35, 0.15, abs(g.y));
                    float butt = smoothstep(-0.25, -0.55, dot(n, g)) * (1 - ridge), face = (1 - butt) * (1 - ridge);
                    float fa = saturate(2.5 - 137.5 * fpa), fb = saturate(2.5 - 275 * fpa), fc = saturate(2.5 - 45 * fpa);
                    float bu = su / 0.32 + 0.2 * OrgNoise2(float2(sv * 0.45, su * 0.6) + sk).x;
                    float bf = frac(bu);
                    int bi = (int)floor(bu);
                    float h1 = OrgHash(int3(bi, ck, 1)), h2 = OrgHash(int3(bi, ck, 2)), h3 = OrgHash(int3(bi, ck, 3)), h4 = OrgHash(int3(bi, ck, 4));
                    float s1 = su + (h2 - 0.5) * 0.175 * sv + 0.015 * OrgNoise2(float2(su * 2.3, sv * 0.5) + sk + 3.7).x;
                    float3 n1 = OrgNoise2(float2(s1 * 55, sv * 2.75) + h1 * 17);
                    float pd = 0.012 * fa * (0.5 - 0.5 * n1.x);
                    s1 -= dot(v, ac) / dn * pd;
                    float d = yf * L - dot(v, g) / dn * pd * face;
                    n1 = OrgNoise2(float2(s1 * 55, sv * 2.75) + h1 * 17);
                    float3 n2 = OrgNoise2(float2(s1 * 110, sv * 6.1) + h3 * 23 + 5.3);
                    float str2 = 0.6 * n1.x * fa + 0.4 * n2.x * fb;
                    float hem = (0.025 + 0.05 * h3 + 0.02 * n2.x * fb) * face;
                    clip(d - hem * fc * (0.6 + 0.4 * n1.x * fa) + 0.001);
                    float lip = smoothstep(L * 0.84 - 0.14, L * 0.84, d + 0.03 * n1.x + 0.02 * h1) * face;
                    float pn = OrgNoise2(float2(su * 0.45, sv * 0.35) + 11.3).x;
                    float patch = smoothstep(0.45, 0.65, 0.55 * pn + 0.5 * h1 + 0.05) * (1 - 0.5 * ridge);
                    a = lerp(_StabbyKit[10].rgb, _StabbyKit[11].rgb, patch) * (0.88 + 0.22 * h4) * (1 + 0.5 * str2);
                    float seam = smoothstep(0.06, 0.0, min(bf, 1 - bf) + 0.03 * n2.x) * fc;
                    a *= (1 - 0.3 * seam) * (1 - 0.45 * lip) * (1 - 0.2 * smoothstep(hem * 1.6 + 0.01, hem, d) * face);
                    Light ml = GetMainLight();
                    float4 mn = OrgRP3(q, OrgF(0), 5.7, 0);
                    float moss = smoothstep(0.1, -0.2, dot(n, ml.direction)) * smoothstep(0.25, 0.75, 0.8 * mn.x + 0.35 * lip + 0.2 * (1 - open)) * fm;
                    a = lerp(a, _StabbyKit[12].rgb * (0.8 + 0.3 * n2.x * fb), 0.6 * moss);
                    float4 st = OrgRP3(q, OrgF(4), sk, 4);
                    float fe = OrgRungW(4, fp);
                    float3 ab = _StabbyKit[13].rgb * (0.75 + 0.45 * smoothstep(0.1, 0.5, st.x) * fe - 0.4 * smoothstep(-0.1, -0.5, st.x) * fe) * (0.9 + 0.2 * h4);
                    a = lerp(a, ab, butt);
                    a = lerp(a, dot(a, float3(0.2126, 0.7152, 0.0722)), 0.15);
                    float gs = 0.6 * n1.y * 55 * fa + 0.4 * n2.y * 110 * fb;
                    grad = ac * (0.003 * gs - 0.2 * (bf - 0.5) * fc) + g * 0.3 * (1 - smoothstep(0.0, 0.12, d)) * face;
                    grad = lerp(grad, st.yzw * 0.15 * fe, butt);
                    shade = 0.5 * lip;
                }
                else if (mat == 3)
                {
                    a = _StabbyKit[7].rgb * (0.92 + 0.06 * mid.x * fm + 0.04 * fib.x * ff);
                    float stain = smoothstep(0.2, 0.8, big.x + 0.5 * mid.x) * 0.45 + (1 - open) * 0.3;
                    a = lerp(a, _StabbyKit[8].rgb, saturate(stain));
                    a = lerp(a, _StabbyKit[9].rgb, smoothstep(0.35, 0.0, abs(q.y + 0.1 * big.x)) * 0.3);
                    grad = 0.09 * mid.yzw * fm + 0.09 * fib.yzw * ff;
                }
                else if (mat == 5)
                {
                    a = _StabbyKit[14].rgb * (0.85 + 0.25 * mid.x);
                    a = lerp(a, _StabbyKit[15].rgb, smoothstep(0.2, 0.7, big.x + 0.4 * fib.x * ff) * 0.6);
                    grad = 0.12 * fib.yzw * ff;
                }
                else if (mat == 6)
                {
                    float h = th.w;
                    float3 dye = h < 0.3 ? _StabbyKitSrgb[16].rgb : h < 0.55 ? _StabbyKitSrgb[17].rgb : h < 0.8 ? _StabbyKitSrgb[18].rgb : _StabbyKitSrgb[19].rgb;
                    float4 wv = OrgRP3(q, OrgF(3), sd, 3);
                    float fw = OrgRungW(3, fp);
                    a = ToLinear(lerp(dye, _StabbyKitSrgb[19].rgb, 0.12 + 0.1 * saturate(big.x))) * (0.86 + 0.08 * str.x * fs + 0.05 * wv.x * fw);
                    a = lerp(a, dot(a, float3(0.2126, 0.7152, 0.0722)), 0.2);
                    grad = 0.12 * fib.yzw * ff + 0.04 * wv.yzw * fw;
                    if (cap)
                    {
                        float roll = frac(rf * rm / 0.028 + 0.15 * mid.x * fm);
                        a *= 1 - 0.45 * smoothstep(0.7, 0.95, roll) * fs - 0.2 * smoothstep(0.85, 1, rf);
                    }
                }
                else if (mat == 7 || mat == 10)
                {
                    float edge = 1;
                    float dust = smoothstep(0.35, 0.95, 0.15 + 0.45 * n.y + 0.5 * big.x + 0.35 * mid.x * fm + 0.15 * fib.x * ff);
                    a = lerp(_StabbyKit[20].rgb, _StabbyKit[21].rgb, dust) * (0.85 + 0.15 * fib.x * ff);
                    float crack = smoothstep(0.9, 0.98, 1 - abs(str.x + 0.2 * mid.x)) * fs * (1 - dust);
                    a = lerp(a, _StabbyKit[22].rgb, 0.6 * crack);
                    grad = 0.08 * mid.yzw * fm + 0.1 * fib.yzw * ff + 0.05 * str.yzw * fs * (1 - dust);
                    [branch] if (cap && abs(g.y) > 0.9)
                    {
                        float e = rf + 0.2 * big.x + 0.12 * mid.x * fm + 0.04 * fib.x * ff;
                        clip(0.97 - e);
                        a = lerp(_StabbyKit[23].rgb, a, smoothstep(0.15, 0.75, rf + 0.25 * big.x + 0.2 * mid.x * fm));
                        a = lerp(a, ContactColor(wp.xz, wp.y), 0.85 * smoothstep(0.45, 0.95, e));
                        grad *= 1 - smoothstep(0.5, 0.95, e);
                        edge = 1 - smoothstep(0.3, 0.8, e);
                    }
                    else if (cap)
                    {
                        float rr = rf * rm / 0.012 + 0.6 * mid.x * fm;
                        a = lerp(a, _StabbyKit[24].rgb, 0.5 * (0.5 + 0.5 * cos(rr * 6.2831853)) * OrgRungW(3, fp));
                    }
                    [branch] if (mat == 10)
                    {
                        float3 cell = floor((wp - q) * 7.3);
                        float ph = frac((cell.x * 0.1031 + cell.y * 0.1137 + cell.z * 0.1279) * 97.13);
                        float t = _StabbyClock + 0.6 * big.x;
                        float pulse = 0.72 + 0.16 * sin(6.2831853 * (t * 0.9 + ph)) + 0.12 * sin(6.2831853 * (t * 2.3 + 1.7 * ph + 0.5 * mid.x));
                        float heat = saturate(1.8 * crack + smoothstep(0.45, 0.1, dust) * (0.55 + 0.45 * fib.x * ff) * smoothstep(-0.2, 0.4, mid.x + 0.3 * big.x)) * edge;
                        a = lerp(a, a * 0.3, saturate(1.4 * heat));
                        emberGlow = lerp(_StabbyKit[37].rgb, _StabbyKit[38].rgb, heat * heat) * (2.5 * heat * heat * pulse);
                    }
                }
                else if (mat == 8)
                {
                    float h = th.w;
                    float3 hue = h < 0.4 ? _StabbyKit[25].rgb : h < 0.62 ? _StabbyKit[26].rgb : h < 0.84 ? _StabbyKit[27].rgb : _StabbyKit[28].rgb;
                    if (th.x > 8.5)
                    {
                        float t = saturate((th.x - 9) / 4);
                        float4 sp = OrgRP3(q, OrgF(3), sd, 3);
                        float fsp = OrgRungW(3, fp);
                        a = hue * (0.82 + 0.16 * big.x + 0.08 * mid.x * fm + 0.05 * fib.x * ff);
                        a = lerp(a, a * 0.55, smoothstep(0.55, 1, t + 0.15 * mid.x * fm));
                        a = lerp(a, _StabbyKit[29].rgb, 0.3 * smoothstep(0.3, 0.0, t + 0.08 * big.x));
                        float wart = h < 0.4 ? smoothstep(0.32, 0.5, sp.x + 0.1 * mid.x) * fsp * smoothstep(0.05, 0.35, t) : 0;
                        a = lerp(a, _StabbyKit[30].rgb * (0.9 + 0.1 * fib.x * ff), wart);
                        grad = 0.07 * mid.yzw * fm + 0.04 * fib.yzw * ff + 0.12 * sp.yzw * fsp * (0.3 + wart);
                    }
                    else if (cap)
                    {
                        float ang = atan2(q.z, q.x) * 18 + 1.5 * mid.x * fm + 0.5 * fib.x * ff;
                        float gill = 0.5 + 0.5 * cos(ang * 2), keep = OrgRungW(3, fp);
                        a = ToLinear(lerp(_StabbyKitSrgb[31].rgb, _StabbyKitSrgb[32].rgb, 0.5 + 0.3 * big.x)) * (1 - 0.35 * gill * keep);
                        a *= 0.5 + 0.5 * smoothstep(0.1, 0.9, rf);
                        grad = 0.05 * fib.yzw * ff;
                    }
                    else
                    {
                        a = _StabbyKit[33].rgb * (0.86 + 0.1 * str.x * fs + 0.06 * fib.x * ff + 0.06 * big.x);
                        a = lerp(a, _StabbyKit[34].rgb, 0.35 * (1 - open));
                        grad = 0.07 * str.yzw * fs + 0.04 * fib.yzw * ff;
                    }
                }
                else if (mat == 9)
                {
                    a = _StabbyKit[35].rgb * (0.8 + 0.22 * big.x + 0.16 * mid.x * fm + 0.12 * fib.x * ff);
                    a = lerp(a, _StabbyKit[36].rgb, 0.35 * smoothstep(0.1, 0.8, mid.x + 0.3 * big.x));
                    grad = 0.1 * mid.yzw * fm + 0.12 * fib.yzw * ff;
                    [branch] if (cap)
                    {
                        float e = rf + 0.25 * big.x + 0.16 * mid.x * fm + 0.05 * fib.x * ff;
                        clip(0.97 - e);
                        a = lerp(a, lerp(a, ContactColor(wp.xz, wp.y), 0.6), smoothstep(0.45, 0.95, e));
                        grad *= 1 - smoothstep(0.5, 0.95, e);
                    }
                    else
                    {
                        float yf = saturate(th.x);
                        clip(0.45 + 0.5 * (0.5 + 0.5 * str.x) + 0.15 * fib.x - yf);
                        a *= lerp(0.75, 1.25, yf);
                        grad = 0.1 * str.yzw * fs + 0.08 * fib.yzw * ff;
                    }
                }
                else if (mat == 11)
                {
                    float4 lp = OrgRP3(q, OrgF(1), sd + 1.7, 1);
                    float wet = smoothstep(0.1, -0.7, mid.x * fm + 0.4 * big.x) * 0.5 + (1 - open) * 0.4;
                    a = lerp(_StabbyKit[39].rgb, _StabbyKit[41].rgb, 0.35 + 0.25 * big.x) * (0.9 + 0.14 * fib.x * ff + 0.1 * big.x);
                    a = lerp(a, _StabbyKit[40].rgb, saturate(wet));
                    float grit = smoothstep(0.5, 0.85, lp.x + 0.35 * fib.x * ff) * fs;
                    a = lerp(a, _StabbyKit[41].rgb * 1.2, 0.5 * grit);
                    float3 cc = ContactColor(wp.xz, wp.y);
                    a = lerp(a, a * saturate(dot(cc, 1) / max(dot(a, 1), 1e-3)), 0.35 + 0.4 * (1 - open));
                    a = lerp(a, cc, 0.12);
                    grad = 0.26 * mid.yzw * fm + 0.16 * lp.yzw * fs + 0.1 * fib.yzw * ff;
                    shade = 0.35 * smoothstep(0.0, -0.6, mid.x) * fm;
                }
                else if (mat == 1 && th.x > 2.5 && th.x < 3.5)
                {
                    float rot = smoothstep(-0.3, 0.6, big.x + 0.5 * mid.x * fm);
                    a = lerp(_StabbyKit[5].rgb, _StabbyKit[0].rgb, 0.5 * rot) * (0.5 + 0.14 * str.x * fs + 0.12 * fib.x * ff);
                    float pith = smoothstep(0.75, 0.95, 1 - abs(fib.x + 0.3 * str.x)) * ff;
                    a *= 1 - 0.4 * pith;
                    a = lerp(a, _StabbyKit[40].rgb * (0.9 + 0.2 * mid.x), 0.55 * smoothstep(0.35, 0.9, n.y + 0.3 * big.x));
                    grad = 0.18 * str.yzw * fs + 0.16 * fib.yzw * ff + 0.05 * mid.yzw * fm;
                    shade = 0.3 * pith;
                }
                else if (side && mat == 1)
                {
                    float al = dot(q, g);
                    float3 sp = (q - g * al) * 15 + g * (al * 3) + sd, sp2 = sp * 2.3 + 5.1;
                    float4 bn = OrgNoise3(sp, int3(0, 0, 0)), bm = OrgNoise3(sp2, int3(0, 0, 0));
                    float fb = OrgRungW(2, fp), fb2 = OrgRungW(2, fp);
                    float rid = lerp(0.6, 1 - abs(bn.x), fb) * 0.65 + lerp(0.6, 1 - abs(bm.x), fb2) * 0.35;
                    a = _StabbyKit[0].rgb * (0.35 + 0.8 * smoothstep(0.15, 0.85, rid)) * (0.85 + 0.25 * big.x);
                    float lic = smoothstep(0.55, 0.85, 0.6 * big.x + 0.5 * mid.x * fm + 0.2 * rid) * saturate(0.4 + n.y);
                    a = lerp(a, dot(a, float3(0.2126, 0.7152, 0.0722)) * float3(0.92, 1.02, 0.8) * 1.7, 0.45 * lic);
                    float3 d1 = bn.yzw - g * dot(bn.yzw, g), d2 = bm.yzw - g * dot(bm.yzw, g);
                    grad = -(sign(bn.x) * 0.16 * fb) * d1 - (sign(bm.x) * 0.07 * fb2) * d2 + 0.04 * mid.yzw * fm;
                    shade = 0.35 * (1 - smoothstep(0.2, 0.6, rid)) * fb;
                    [branch] if (th.x > 8.5)
                    {
                        float3 pq = q - g * (al * 0.45) + sd;
                        float4 fu = OrgRP3(pq, OrgF(0), 4.3, 0), fv = OrgRP3(pq, OrgF(1), 8.9, 1);
                        float plate = smoothstep(0.0, 0.5, 1 - abs(fu.x) + 0.25 * fv.x * fs);
                        float3 e1 = fu.yzw - g * dot(fu.yzw, g), e2 = fv.yzw - g * dot(fv.yzw, g);
                        a *= 0.55 + 0.5 * plate;
                        grad += -(sign(fu.x) * 0.38 * fm) * e1 + 0.12 * e2 * fs;
                        shade = max(shade, 0.55 * (1 - plate) * fm);
                    }
                }
                else if (cap && mat == 1)
                {
                    float rr = (rf + 0.05 * mid.x * fm + 0.015 * fib.x * ff) * rm / 0.024;
                    float ring = 0.5 + 0.5 * cos(rr * 6.2831853), keep = OrgRungW(3, fp);
                    a = lerp(_StabbyKit[1].rgb, _StabbyKit[2].rgb, smoothstep(0.35, 0.75, rf + 0.1 * big.x));
                    a *= (1 - 0.22 * ring * keep) * (0.9 + 0.1 * fib.x * ff);
                    a = lerp(a, dot(a, float3(0.2126, 0.7152, 0.0722)) * float3(0.95, 0.95, 0.93), 0.35 + 0.25 * big.x);
                    float bark = smoothstep(0.9, 0.97, rf + 0.03 * fib.x * ff);
                    a = lerp(a, _StabbyKit[3].rgb, bark);
                    float chk = smoothstep(0.9, 0.98, 1 - abs(str.x + 0.3 * mid.x)) * fs * smoothstep(0.15, 0.5, rf) * (1 - bark);
                    a *= 1 - 0.5 * chk;
                    grad = 0.03 * fib.yzw * ff + 0.03 * mid.yzw * fm;
                }
                else
                {
                    float3 c0 = mat == 2 ? _StabbyKit[6].rgb : _StabbyKit[4].rgb;
                    float ring = sin((str.x * 3 + mid.x) * 6.283) * 0.5 + 0.5;
                    a = c0 * (0.8 + 0.18 * ring * fs + 0.12 * fib.x * ff + 0.12 * big.x);
                    float wet = smoothstep(0.3, 0.9, big.x + 0.4 * mid.x) * 0.3 + (1 - open) * 0.25;
                    a = lerp(a, _StabbyKit[5].rgb, saturate(wet));
                    a = lerp(a, a * float3(0.95, 1.05, 0.9) + 0.03, 0.4 * saturate(n.y) * fm);
                    a *= 1 - 0.45 * smoothstep(0.88, 0.97, 1 - abs(str.x)) * fs;
                    grad = 0.054 * str.yzw * fs + 0.06 * fib.yzw * ff + 0.043 * mid.yzw * fm;
                }
                grad -= n * dot(grad, n);
                n = normalize(n - grad);
                occ = max(saturate((1 - open) * (1.2 + 0.7 * (big.x + 0.5 * mid.x))), shade);
                return lerp(a, a * float3(0.5, 0.47, 0.43), occ);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                clip(fade.x - Bayer(i.positionCS.xy));
                i.tone.w -= i.tone.w > 63.5 ? 64 : 0;
                bool mossy = i.tone.w > 31.5;
                i.tone.w -= mossy ? 32 : 0;
                bool flat = i.tone.w > 15.5;
                i.tone.w -= flat ? 16 : 0;
                float4 base = _BaseColor;
                float3 wp = i.positionWS;
                float la = length(ddx(wp)), lb = length(ddy(wp));
                float fp = max(max(sqrt(la * lb), 0.4 * max(la, lb)), 1e-4);
                float3 n = normalize(i.normalWS);
                float3 albedo;
                float occ = 0;
                tmRough = LookRough();
                emberGlow = 0;
                iceWrap = 0;
                float3 gn = normalize(i.grain), qo = wp - i.origin, acr = cross(gn, n);
                acr *= rsqrt(max(dot(acr, acr), 1e-8));
                float yf = i.tone.w - i.seed, suq = dot(qo, acr);
                float2 dyf = float2(ddx(yf), ddy(yf));
                float3 qa = ddx(qo), qb = ddy(qo);
                float g11 = dot(qa, qa), g12 = dot(qa, qb), g22 = dot(qb, qb);
                float gy = (dyf.x * dyf.x * g22 - 2 * dyf.x * dyf.y * g12 + dyf.y * dyf.y * g11) / max(g11 * g22 - g12 * g12, 1e-20);
                float4 th = float4(yf, gy < 1e-6 ? 0.05 : clamp(rsqrt(gy), 0.01, 3), max(length(float2(ddx(suq), ddy(suq))), 1e-5), i.seed);
                [branch] if (base.a > 0.475 && base.a < 0.5)
                {
                    float3 q = wp - i.origin;
                    float bf = OrgRungW(0, fp);
                    float4 vein = OrgNoise3(q * float3(2.1, 0.6, 2.1) + i.seed * 13.7, int3(0, 0, 0)), fine = OrgRP3(q, OrgF(0), 6.1, 0);
                    float3 g = 0.08 * vein.yzw + 0.04 * bf * fine.yzw;
                    g -= n * dot(g, n);
                    n = normalize(n - g);
                    albedo = base.rgb * (0.85 + 0.12 * vein.x + 0.05 * bf * fine.x);
                    tmRough = 0.2 * LookRough();
                    iceWrap = (base.a - 0.48) / 0.015;
                }
                else if (base.a > 0.025 && base.a < 0.5)
                {
                    float3 q = wp - i.origin, v = normalize(_WorldSpaceCameraPos - wp);
                    bool cap = base.a > 0.26;
                    float glow = (base.a - 0.05 - (cap ? 0.22 : 0)) / 0.2;
                    float4 lump = OrgNoise3(q * 1.3 + i.seed * 17.1);
                    [branch] if (cap)
                    {
                        float2 cp = (q.xz + i.seed * 31.7) * 6.5, cf = floor(cp), rel = 0;
                        float best = 9;
                        [unroll] for (int y = -1; y <= 1; y++)
                        [unroll] for (int x = -1; x <= 1; x++)
                        {
                            int2 c = int2(cf) + int2(x, y);
                            float2 d = float2(c) + float2(OrgHash(c), OrgHash(c + int2(17, 5))) - cp;
                            float k = length(d) / (0.24 + 0.3 * OrgHash(c + int2(3, 29)));
                            if (k < best) { best = k; rel = d; }
                        }
                        float w = OrgFade(6.5, fp), up = smoothstep(-0.1, 0.5, n.y);
                        float capM = lerp(0.4, smoothstep(1, 0.86, best), w) * up, tip = lerp(0.45, saturate(1 - best * best), w);
                        float gill = smoothstep(0.62, 0.95, best) * (0.5 + 0.5 * sin(atan2(rel.y, rel.x) * 22)) * w;
                        float spot = smoothstep(0.5, 0.72, OrgNoise3(q * 26 + i.seed * 7.3).x) * OrgFade(26, fp);
                        float3 g = 0.1 * lump.yzw;
                        g.xz += 0.9 * w * up * smoothstep(1, 0.5, best) * rel / max(length(rel), 1e-4) * saturate(best);
                        g -= n * dot(g, n);
                        n = normalize(n - g);
                        albedo = lerp(lerp(base.rgb, float3(0.05, 0.045, 0.04), 0.75), base.rgb, capM) * (0.85 + 0.2 * lump.x);
                        tmRough = 0.5 * LookRough();
                        emberGlow = base.rgb * glow * (0.03 + capM * (0.3 + 1.5 * tip * tip + 0.5 * gill + 0.8 * spot)) + glow * capM * 0.3 * tip * tip * tip * tip;
                    }
                    else
                    {
                        int3 fc = int3(floor(q * 6 + i.seed * 13.3));
                        float3 tilt = float3(OrgHash(fc), OrgHash(fc + int3(11, 3, 7)), OrgHash(fc + int3(5, 19, 2))) - 0.5;
                        float3 g = 0.16 * OrgFade(6, fp) * tilt + 0.07 * lump.yzw;
                        g -= n * dot(g, n);
                        n = normalize(n - g);
                        float rim = pow(1 - saturate(abs(dot(n, v))), 2.5), ht = saturate(q.y / 1.8), tipT = ht * ht;
                        float inner = 0.65 + 0.35 * OrgNoise3(q * 3.1 + i.seed * 5.9).x;
                        float3 col = lerp(base.rgb, 0.5 * base.rgb + float3(0.42, 0.4, 0.55), 0.55 * tipT);
                        albedo = base.rgb * (0.32 + 0.08 * lump.x);
                        tmRough = 0.12;
                        emberGlow = col * glow * (0.2 + 1.8 * rim + 1.2 * tipT) * inner;
                    }
                }
                else if (base.a < 0.5 && i.tone.x > 1.75) albedo = Built(i.tone, gn, qo, n, fp, th, normalize(_WorldSpaceCameraPos - wp), wp, occ);
                else if (base.a < 0.5)
                {
                    float off = SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).a;
                    float period = max(_StabbyStrataPeriod, 1);
                    int3 cell = int3(floor(i.origin * 4.1));
                    float h1 = OrgHash(cell), h2 = OrgHash(cell + int3(7, 3, 5));
                    float3 tilt = normalize(float3(h1 - 0.5, 1.1, h2 - 0.5));
                    off += i.tone.x > 0.25 ? 0 : (0.15 + 0.7 * h2) * period + dot(wp - i.origin, tilt) - (wp.y - i.origin.y);
                    float u0 = (wp.y + off) / period;
                    float3 band = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(u0, 0.5), 0).rgb, mean = 0;
                    [unroll] for (int k = 1; k < 6; k++) mean += SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(u0 + k * 0.1667, 0.5), 0).rgb;
                    mean = (mean + band) * 0.1667;
                    float3 pull = lerp(1, clamp((mean + 0.02) / (band + 0.02), 0.6, 1.6), 0.6);
                    bool own = base.a > 0.006;
                    float2 dux = float2(ddx(u0), 0), duy = float2(ddy(u0), 0);
                    [branch] if (_StabbyDetailOff > 0)
                        albedo = own ? 1 : SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(u0, 0.5), dux, duy).rgb;
                    else
                    {
                        float2 o2 = OrgOrigin(_WorldSpaceCameraPos);
                        orgO = float3(o2.x, 0, o2.y);
                        float4 cw = TmCoatAt(wp.xz, fp, 3);
                        float4 cwn = cw / max(dot(cw, 1), 1e-3);
                        float surf = 1;
                        [branch] if (_StabbyFieldUV.x > 0) surf = smoothstep(6, 2, ContactBed(wp.xz) - wp.y);
                        cwn *= surf;
                        [branch] if (i.tone.x > 0.25)
                        {
                            bool course = i.tone.w > 1.5;
                            float calm = !course && i.tone.w >= 1 ? 0.67 : 0;
                            float4 tone = float4(i.tone.xyz, course ? i.tone.w - 2 : i.tone.w);
                            float3 cg = 0, cid = 0;
                            float dark = 1, cf = 0;
                            [branch] if (course)
                            {
                                float3 vo = TransformWorldToObjectDir(_WorldSpaceCameraPos - wp);
                                float3 go = WallCourse(i.pof.xyz, normalize(i.normalOS.xyz), vo, fp / max(i.normalOS.w, 1e-3), cid, dark, cf);
                                float lg = length(go);
                                cg = lg > 0 ? TransformObjectToWorldDir(go) * lg : 0;
                                tone.yz = lerp(tone.yz, float2(OrgHash(int2((int)cid.x * 31 + (int)cid.y, 17)), cid.z) * 2 - 1, cf);
                            }
                            int3 sk = int3(floor(tone.yzw * 977 + cid * 13));
                            float hj = OrgHash(sk), hk = OrgHash(sk + int3(3, 1, 7));
                            float us = (i.origin.y + off) / period + (hj - 0.5) * 0.25 * (1 - calm);
                            float3 sb = SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(us, 0.5), 0).rgb, sm = sb;
                            [unroll] for (int k = 1; k < 6; k++) sm += SAMPLE_TEXTURE2D_LOD(_StabbyStrata, sampler_StabbyStrata, float2(us + k * 0.1667, 0.5), 0).rgb;
                            float3 sc = own ? base.rgb * clamp((dot(sb, 1) + 0.02) / (dot(sm, 0.1667) + 0.02), 0.88, 1.12) : lerp(sb, sm * 0.1667, 0.5);
                            TmOut m = KitStone(wp - i.origin + float3(hj, hk, hj * hk) * 37 + cid * float3(5.3, 2.9, 7.1), n, sc, tone, fp);
                            TmStoneCover(m, wp - orgO, n, mossy ? float4(saturate(cwn.x + 0.55 * surf * (1 - cwn.z - cwn.w)), cwn.yzw) : cwn, fp, 1, 1);
                            float3 g = m.grad + cg;
                            g -= n * dot(g, n);
                            n = normalize(n - g);
                            albedo = m.albedo * dark;
                            pull = 1;
                        }
                        else
                        {
                            TmOut m = TmRock(wp - orgO, n, normalize(_WorldSpaceCameraPos - wp), fp, off, dux, duy, 1);
                            TmStoneCover(m, wp - orgO, n, cwn, fp, 1, 1);
                            float3 q = wp - orgO;
                            float bf = OrgRungW(0, fp);
                            float4 lump = OrgNoise3(q * 0.7 + h1 * 13.1, int3(0, 0, 0)), nub = OrgRP3(q, OrgF(0), 5.3, 0);
                            float3 g = m.grad + 0.1 * lump.yzw * 0.7 + 0.05 * bf * nub.yzw * 2.9;
                            g -= n * dot(g, n);
                            n = normalize(n - g);
                            float crust = smoothstep(0.15, 0.7, n.y) * bf * smoothstep(0.25, 0.6, nub.x + 0.6 * lump.x) * (1 - saturate(2 * (cwn.x + 0.4 * cwn.y)));
                            float lum = dot(m.albedo, float3(0.2126, 0.7152, 0.0722));
                            albedo = lerp(m.albedo, lum * lerp(float3(1.1, 1.12, 0.92), float3(1.35, 1.1, 0.62), h1 * h1), 0.35 * crust);
                        }
                    }
                    albedo *= own && i.tone.x > 0.25 && _StabbyDetailOff <= 0 ? 1 : base.rgb * pull;
                    [branch] if (i.tone.x > 0.25) occ = Dressed(albedo, i.tone, wp - orgO);
                }
                else
                {
                    float3 gw = _StabbyGround.rgb;
                    [branch] if (_StabbyFieldUV.z > 0) gw = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
                    Wood(i.local, n, normalize(i.axisWS), base.rgb, base.a < 1.5, i.tone, fp, wp, gw, albedo, n);
                }
                float size = max(abs(fade.w), 0.1), ground = fade.z, surface = 0, bedHere = 0;
                float4x4 mm = UNITY_MATRIX_M;
                float hang = mm._m11 < 0 ? -1 : 1;
                float thick = flat ? 0.05 : abs(mm._m10) + abs(mm._m11) + abs(mm._m12);
                [branch] if (_StabbyFieldUV.x > 0)
                {
                    float bed = ContactBed(i.origin.xz);
                    bedHere = ContactBed(wp.xz);
                    ground += (bedHere - bed) * smoothstep(6, 2, bed - fade.z);
                    surface = smoothstep(2, 6, bed - fade.z) * smoothstep(bedHere - 0.6, bedHere - 0.2, wp.y);
                    float3 deep = ContactObject(albedo, wp, ground, size, thick, hang, iceWrap > 0 ? 1 : 0);
                    [branch] if (surface > 0) deep = lerp(deep, ContactObject(albedo, wp, bedHere, size, thick, 1, iceWrap > 0 ? 1 : 0), surface);
                    albedo = deep;
                    [branch] if (fade.w < 0) albedo = ContactObject(albedo, wp, ground - fade.w, size, thick, -1, iceWrap > 0 ? 1 : 0);
                }
                float above = lerp(max(hang * (wp.y - ground), 0), max(wp.y - bedHere, 0), surface);
                [branch] if (fade.w < 0) above = min(above, max(ground - fade.w - wp.y, 0));
                float ao = lerp(0.85, 1, smoothstep(0, min(min(0.9, 0.6 * size), thick), above)) * lerp(1, 0.5, saturate(-n.y)) * (1 - 0.4 * occ);
                Light l = GetMainLight(TransformWorldToShadowCoord(wp), wp, half4(1, 1, 1, 1));
                float sky = CaveSky(wp);
                l.color *= sky;
                float3 bounce = _StabbyGround.rgb;
                [branch] if (_StabbyFieldUV.z > 0)
                {
                    float3 gc = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
                    gc = lerp(gc, ToLinear(_StabbySand.rgb), smoothstep(6, 2, fade.z));
                    [branch] if (_StabbyFieldUV.x > 0) gc = BounceGround(gc, wp.xz, fade.z);
                    float3 lit = gc * (_MainLightColor.rgb * saturate(_MainLightPosition.y) + _StabbySky.rgb);
                    bounce = lerp(_StabbyGround.rgb, lit, smoothstep(2.5 * size + 2, 0, above));
                }
                float3 ambient = (_StabbySky.a > 0 || _StabbyGround.a > 0 ? lerp(bounce, _StabbySky.rgb, n.y * 0.5 + 0.5) : lerp(float3(0.25, 0.24, 0.22), float3(0.45, 0.5, 0.58), n.y * 0.5 + 0.5)) * sky;
                float3 c = LookColor(albedo, 1) * (l.color * saturate((dot(n, l.direction) + iceWrap) / (1 + iceWrap)) * l.shadowAttenuation * lerp(1, ao, 0.4) + (ambient + LocalLightsWrap(wp, n, i.positionCS, iceWrap)) * ao) + LookSheen(n, normalize(_WorldSpaceCameraPos - wp), l.direction, l.color * l.shadowAttenuation, saturate(tmRough)) * ao + emberGlow;
                return half4(MixFogColor(c, unity_FogColor.rgb * sky, i.pof.w), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(i.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = _Fade.y < 0.5 ? float4(0, 0, -2, 1) : cs;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                clip(min(fade.y - 0.5, fade.x - Bayer(i.positionCS.xy)));
                return 0;
            }
            ENDHLSL
        }
    }
}
