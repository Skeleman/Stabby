Shader "Stabby/Water"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Viewer/Shaders/Look.hlsl"

            float4 _StabbySky, _StabbyFieldUV;
            float4 _StabbyWaterDeep, _StabbyWaterShallow, _StabbyWaterWave, _StabbyWaterSwell, _StabbyWaterFlow;
            float4 _StabbySpringWater, _StabbySpringDeep, _StabbyMudSurface, _StabbyMudDeep;
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            static const float2x2 Turn = float2x2(0.8, -0.6, 0.6, 0.8);
            static const float2x2 Veer = float2x2(0.96, -0.28, 0.28, 0.96);
            static const float FlowPeriod = 3;
            static const float SlopeRange = 0.1;
            static const float MudFeather = 4;
            static const float CurtainPxLo = 6, CurtainPxHi = 24, LipPx = 1.5;
            static const float CurtainPath = 6, CurtainBend = 0.05, CurtainSpill = 1.5, CurtainFlutter = 0.07, CurtainRipple = 0.06, CurtainBulge = 0.05;

            float2 Grad(int2 p)
            {
                uint2 q = asuint(p) * uint2(1597334673u, 3812015801u);
                uint h = (q.x ^ q.y) * 1597334673u;
                h ^= h >> 16;
                h *= 2246822519u;
                h ^= h >> 13;
                return float2(h & 0xffffu, h >> 16) / 32767.5 - 1;
            }

            float3 Noise(float2 p)
            {
                float2 fl = floor(p), f = p - fl;
                int2 i = (int2)fl;
                float2 u = f * f * f * (f * (f * 6 - 15) + 10), du = 30 * f * f * (f * (f - 2) + 1);
                float2 ga = Grad(i), gb = Grad(i + int2(1, 0)), gc = Grad(i + int2(0, 1)), gd = Grad(i + 1);
                float va = dot(ga, f), vb = dot(gb, f - float2(1, 0)), vc = dot(gc, f - float2(0, 1)), vd = dot(gd, f - 1);
                float k = va - vb - vc + vd;
                float v = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * k;
                float2 d = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) + du * (u.yx * k + float2(vb - va, vc - va));
                return float3(v, d);
            }

            float3 Bubbles(float2 p, float tm)
            {
                float2 c = floor(p), f = p - c;
                int2 ci = (int2)c;
                float2 h = Grad(ci) * 0.5 + 0.5, k = Grad(ci + int2(37, 11)) * 0.5 + 0.5;
                float2 dv = f - (0.3 + 0.4 * h);
                float d = length(dv);
                float life = frac(tm * (0.12 + 0.1 * k.x) + k.y);
                float r = 0.36 * sqrt(life), w = 0.025 + 0.05 * life;
                float x = (d - r) / w, e = exp(-x * x);
                float live = step(k.x, 0.7) * (1 - life);
                float dome = smoothstep(0.14, 0, d) * smoothstep(0.12, 0, life) * step(k.x, 0.7);
                return float3(e * live + dome, dv / max(d, 1e-4) * (-0.5 * x * e * live));
            }

            float2 Waves(float2 q, float t, float fade)
            {
                float2 g = Noise(q + float2(t, t * 0.7)).yz;
                g += 0.5 * mul(Noise(mul(Turn, q) * 2.3 + float2(-t * 1.3, t * 0.4)).yz, Turn);
                UNITY_BRANCH if (fade > 0)
                    g += 0.2 * fade * mul(Turn, Noise(mul(q, Turn) * 7.1 + float2(t * 2.1, -t * 1.7)).yz);
                return g;
            }

            float2 StillWaves(float2 q, float t, float fade, float ripple)
            {
                float2 wq = q * 0.11;
                float3 w1 = Noise(mul(Veer, wq) + 31.7);
                float3 w2 = Noise(wq * 1.7 + 12.9);
                float m = saturate(0.5 + 0.9 * w2.x);
                float2 rq = mul(q, Veer) + 1.4 * float2(w1.x, w2.x);
                float2 g = (1.15 - 0.65 * m) * Noise(rq + float2(t, t * 0.7)).yz;
                g += (0.3 + 0.6 * m) * mul(Noise(mul(Turn, rq) * 2.3 + float2(-t * 1.3, t * 0.4)).yz, Turn);
                UNITY_BRANCH if (fade > 0)
                    g += (0.12 + 0.18 * m) * fade * mul(Turn, Noise(mul(rq, Turn) * 7.1 + float2(t * 2.1, -t * 1.7)).yz);
                UNITY_BRANCH if (ripple < 1)
                    g += (1 - ripple) * 0.9 * mul(Noise(mul(Turn, rq) * 0.37 + float2(t * 0.4, 7.7)).yz, Veer);
                return g * (0.55 + 0.6 * smoothstep(-0.35, 0.45, w1.x + 0.5 * w2.x));
            }

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 chan : TEXCOORD0; float4 reach : TEXCOORD1; float4 fall : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; float4 chan : TEXCOORD3; float along : TEXCOORD4; float4 fall : TEXCOORD5; float warm : TEXCOORD6; };

            float ScreenPx(float size, float dist) { return size / max(dist, 0.01) * abs(UNITY_MATRIX_P[1][1]) * 540; }

            half4 Curtain(Varyings i, float3 v, float dist, float2 uv)
            {
                float u = saturate(i.fall.x), h = i.fall.y, y = u * h, x = i.chan.x * i.chan.y;
                float2 fd = normalize(i.color.rg * 2 - 1), sd = float2(fd.y, -fd.x);
                float flowQ = saturate(log2(1 + i.chan.z) / 9);
                float tau = sqrt(2 * y / 9.81), tm = _Time.y;
                float fp = max(length(ddx(float2(x, y))), length(ddy(float2(x, y))));
                float detail = saturate(1.5 - fp * 1.2);
                float2 wq = float2(x * 0.35, tau * 1.3 + 3.1);
                float3 warp = Noise(wq) + 0.5 * Noise(wq * 2.1 + 5.7);
                float2 sq = float2(x * 1.3 + 0.9 * warp.x, (tau - tm * 0.8) * 2.2);
                float3 s1 = Noise(sq * float2(1, 0.3));
                float3 s2 = Noise(sq * float2(2.6, 0.55) + 7.7);
                float3 s3 = Noise(float2(x * 4.1 + 1.3 * warp.x, (tau - tm * 1.1) * 3.4) + 13.1) * detail;
                float str = s1.x + 0.5 * s2.x + 0.3 * s3.x;
                float aer = saturate(smoothstep(0.25, 1, u) * (0.6 + 0.45 * flowQ) + 0.1 * flowQ);
                float white = saturate(aer + smoothstep(0.1, 0.6, str) * (0.2 + 0.45 * u) * (0.6 + 0.4 * flowQ));
                float3 up = float3(0, 1, 0), f3 = float3(fd.x, 0, fd.y), s3d = float3(sd.x, 0, sd.y);
                float3 nb = normalize(f3 * (0.35 + u) + up * (1 - u) * 0.9);
                float3 tb = normalize(cross(s3d, nb));
                float3 n = normalize(nb + s3d * (0.5 * s1.y + 0.3 * s2.y + 0.2 * s3.y + 0.3 * warp.y) + tb * (0.15 * s1.z + 0.1 * s2.z + 0.2 * warp.z));
                n = dot(n, v) < 0 ? -n : n;
                Light l = GetMainLight();
                float3 sky = _StabbySky.rgb;
                float ndv = saturate(dot(n, v));
                float edge = smoothstep(1, 0.82, abs(i.chan.x) + 0.08 * warp.x);
                float gap = smoothstep(-0.6, -0.15, s1.x + 0.4 * warp.x + 0.5 * flowQ);
                float spill = smoothstep(0, CurtainSpill, y);
                float sheet = edge * lerp(0.2, 1, gap) * lerp(0.4, 1, spill);
                float path = CurtainPath * (0.25 + flowQ) * sheet / max(ndv, 0.35);
                float3 trans = exp(log(max(_StabbyWaterShallow.rgb, 0.01)) * (path * _StabbyWaterDeep.w));
                float dk = 1 - exp(-path * _StabbyWaterDeep.w * 0.3);
                float3 teal = _StabbyWaterShallow.rgb * float3(0.26, 0.4, 0.42);
                float3 tint = lerp(teal, _StabbyWaterDeep.rgb, smoothstep(0.05, 0.4, dk));
                float3 body = LookColor(tint, 1.3) * (l.color * saturate(l.direction.y + 0.2) * 0.6 + sky * 0.5);
                float surface = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float2 bend = mul((float3x3)UNITY_MATRIX_V, n).xy * (CurtainBend * saturate(path / CurtainPath) / (1 + dist * 0.02));
                float2 ruv = uv + bend;
                UNITY_FLATTEN if (LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams) < surface) ruv = uv;
                float3 c = SampleSceneColor(ruv) * trans + body * (1 - trans);
                float fres = 0.04 + 0.96 * pow(1 - ndv, 5);
                float3 r = reflect(-v, n);
                float3 skyR = lerp(sky * 1.2, sky * float3(0.8, 0.88, 1.05), sqrt(saturate(r.y))) + l.color * pow(saturate(dot(r, l.direction)), 8) * 0.4;
                c = lerp(c, skyR, fres * (1 - white));
                float thinL = 1 - saturate(path / CurtainPath);
                float fwdS = pow(saturate(dot(-v, l.direction)), 3) + 0.35 * pow(1 - abs(dot(nb, l.direction)), 4);
                c += _StabbyWaterShallow.rgb * l.color * fwdS * thinL * 0.7 * (1 - 0.5 * white);
                float lit = saturate(dot(n, l.direction)) * 0.55 + 0.45;
                float3 froth = (l.color * lit * 0.85 + sky * 0.3) * lerp(float3(1, 1, 1), 0.6 + _StabbyWaterShallow.rgb * 0.5, 0.25 * (1 - u)) * (0.88 + 0.12 * s2.x);
                c = lerp(c, froth, white * (0.75 + 0.25 * u));
                float lipW = 0.12 + 0.06 * Noise(float2(x * 0.7, 1.7)).x;
                float lip = (1 - smoothstep(lipW * 0.5, lipW, y)) * saturate(ScreenPx(lipW, dist) / LipPx);
                c = lerp(c, skyR * 1.15 + l.color * 0.25, lip * 0.8);
                float3 h3 = normalize(l.direction + v);
                float glint = pow(saturate(dot(n, h3)), 400) * smoothstep(0.35, 0.75, s2.x + 0.5 * s3.x + 0.3 * warp.x);
                c += l.color * (glint * 3 * detail + pow(saturate(dot(n, h3)), 120) * 0.3) * (1 - white);
                float alpha = saturate(lerp(0.9, 1, white)) * edge * lerp(0.35, 1, gap) * lerp(0.6, 1, spill);
                return half4(MixFog(c, i.fog), alpha);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                UNITY_BRANCH if (i.fall.w > 0.5)
                {
                    float u = saturate(i.fall.x), h = i.fall.y, wd = 2 * i.chan.y;
                    float move = smoothstep(CurtainPxLo, CurtainPxHi, ScreenPx(wd, length(_WorldSpaceCameraPos - wp)));
                    UNITY_BRANCH if (move > 0)
                    {
                        float2 fd = normalize(i.color.rg * 2 - 1);
                        float3 f3 = float3(fd.x, 0, fd.y), s3d = float3(fd.y, 0, -fd.x);
                        float tau = sqrt(2 * u * h / 9.81), ph = (tau - _Time.y * 0.8) * 2.2;
                        float3 fl = Noise(float2(i.reach.z * 0.03 + 1.3, ph * 0.3));
                        float3 er = Noise(float2(i.chan.x * 3.7 + i.reach.z * 0.1, ph + 9.1));
                        float flowQ = saturate(log2(1 + i.reach.x) / 9);
                        float side = fl.x * u * min(CurtainFlutter * wd, 0.04 * h);
                        float edge = er.x * smoothstep(0, 0.3, u) * CurtainRipple * wd * i.chan.x;
                        float bulge = 6.75 * u * (1 - u) * (1 - u) * flowQ * min(CurtainBulge * h, 0.25 * wd) + fl.y * u * 0.015 * h;
                        wp += (s3d * (side + edge) + f3 * bulge) * move;
                    }
                }
                float4 cs = TransformWorldToHClip(wp);
                o.positionCS = cs;
                o.positionWS = wp;
                o.color = i.color;
                o.fog = ComputeFogFactor(cs.z);
                o.screen = ComputeScreenPos(cs);
                o.chan = float4(i.chan, i.reach.xy);
                o.along = i.reach.z;
                o.warm = i.reach.w;
                o.fall = i.fall;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 uv = i.screen.xy / i.screen.w;
                UNITY_BRANCH if (i.fall.w > 0.5) return Curtain(i, v, dist, uv);
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surface = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float3 bed = _WorldSpaceCameraPos - v * (dist * scene / max(surface, 0.001));
                float2 fuv = i.positionWS.xz * _StabbyFieldUV.x + _StabbyFieldUV.y;
                float field = max(0, i.positionWS.y - SAMPLE_TEXTURE2D(_StabbyBed, sampler_StabbyBed, fuv).r);
                float far = i.color.a > 0.25 ? saturate(dist * _StabbyWaterSwell.z - 1) : 0;
                float depth = lerp(max(0, i.positionWS.y - bed.y), field, far);

                float4 wv = _StabbyWaterWave;
                float t = _Time.y * wv.y;
                float2 q = i.positionWS.xz * wv.x;
                float2 xz = i.positionWS.xz;
                float hot = saturate(i.warm), mud = saturate(-i.warm);
                float fp = max(length(ddx(xz)), length(ddy(xz)));
                float fade = saturate(1.5 - fp * wv.x * 10);
                float ripple = saturate(1.5 - fp * wv.x * (i.color.a >= 0.99 ? 4 : 1.6));
                float2 flow = i.color.rg * 2 - 1;
                float slope = i.color.b * SlopeRange;
                float plunge = i.chan.w * SlopeRange * _StabbyWaterFlow.y;
                float white = max(smoothstep(1, 2.25, slope * _StabbyWaterFlow.y), 0.85 * smoothstep(0.9, 2.1, plunge));
                float churn = max(smoothstep(0.3, 1.3, slope * _StabbyWaterFlow.y), 0.8 * smoothstep(0.3, 1.3, plunge));
                bool river = i.chan.y > 0.5 && dot(flow, flow) > 0.01;
                float2 fd = river ? normalize(flow) : float2(1, 0), sd = float2(fd.y, -fd.x);
                float2 cw = float2(i.along, i.chan.x * i.chan.y);
                float speed = _StabbyWaterFlow.x * (0.8 + 20 * slope);
                float ph = frac(_Time.y / FlowPeriod), wab = abs(1 - 2 * ph);
                float2 g, wg = 0;
                float wl = 0;
                UNITY_BRANCH if (river)
                {
                    float stretch = 1.05 + 0.45 * churn;
                    float2 cq = cw * float2(1 / stretch, 1) * wv.x;
                    cq.y += 0.8 * Noise(cq * float2(0.9, 0.35) + 9.1).x + 0.9 * Noise(cw * float2(0.03, 0.09) + 2.3).x;
                    cq.x += 0.6 * Noise(cq * float2(0.35, 0.9) + 4.4).x;
                    float dr = speed * FlowPeriod * wv.x / stretch;
                    float2 a = Waves(cq - float2(dr * ph, 0), t, fade);
                    float2 b = Waves(cq - float2(dr * frac(ph + 0.5), 0) + 0.37, t, fade);
                    float2 gc = lerp(a, b, wab);
                    g = (gc.x / stretch * fd + gc.y * sd) * (0.45 + 0.9 * saturate(0.5 + 0.8 * Noise(cw * float2(0.05, 0.12) + 27.1).x));
                    UNITY_BRANCH if (white > 0.01)
                    {
                        float2 wq = cw * float2(0.16, 0.42);
                        wq.y += 0.6 * Noise(wq * float2(0.5, 0.35) + 3.3).x;
                        wq.x += 0.5 * Noise(wq * float2(0.3, 0.6) + 8.7).x;
                        float wd = speed * FlowPeriod * 0.16;
                        float2 w1 = wq - float2(wd * ph, 0), w2 = wq - float2(wd * frac(ph + 0.5), 0) + 4.1;
                        float3 n1 = Noise(w1) + 0.45 * Noise(w1 * float2(2.3, 1.7) + 1.9);
                        float3 n2 = Noise(w2) + 0.45 * Noise(w2 * float2(2.3, 1.7) + 1.9);
                        float msk = Noise(cw * float2(0.035, 0.08) + 21.3).x;
                        float3 nw = lerp(n1, n2, wab) * rsqrt(wab * wab + (1 - wab) * (1 - wab));
                        float3 boil = 0;
                        UNITY_BRANCH if (fade > 0)
                            boil = Noise(mul(Turn, xz) * 1.7 + float2(t * 0.9, -t * 0.6)) * fade;
                        wl = 0.5 + 0.2 * msk + nw.x * (0.75 + 0.5 * msk) + 0.35 * boil.x;
                        wg = white * (nw.y * 0.24 * fd + nw.z * 0.6 * sd + 0.35 * mul(boil.yz, Turn));
                    }
                }
                else if (dot(flow, flow) > 0.01)
                {
                    float2 drift = flow * (speed * FlowPeriod * wv.x);
                    float2 a = Waves(q - drift * ph, t, fade);
                    float2 b = Waves(q - drift * frac(ph + 0.5) + 0.37, t, fade);
                    g = lerp(a, b, wab);
                }
                else g = StillWaves(q, t, fade, ripple);
                float swell = i.color.a >= 0.99 ? 1 : i.color.a * 2 * _StabbyWaterFlow.z;
                float slick = 0;
                UNITY_BRANCH if (swell > 0)
                {
                    float2 wq = mul(Turn, xz * _StabbyWaterSwell.x);
                    float2 sp = wq * float2(0.15, 1.4) + float2(_Time.y * 0.004, 0.6 * Noise(wq * 0.35 + 2.1).x);
                    float sn = Noise(sp).x + 0.5 * Noise(sp * float2(1.9, 2.1) + 5.3).x;
                    slick = swell * smoothstep(0.05, 0.45, sn) * saturate(2 - fp * _StabbyWaterSwell.x * 20);
                }
                g *= wv.z * (0.15 + 0.85 * ripple) * (1 - 0.85 * slick) * (1 + white + 0.5 * churn);
                g += wv.z * 3 * wg;
                UNITY_BRANCH if (swell > 0)
                {
                    float2 sq = mul(Turn, xz * _StabbyWaterSwell.x) + float2(_Time.y * 0.05, _Time.y * 0.02);
                    float2 sg = mul(Noise(sq).yz, Turn) + 0.7 * Noise(mul(sq, Turn) * 0.43 + float2(-_Time.y * 0.02, 1.7)).yz;
                    g += swell * _StabbyWaterSwell.y * saturate(1.6 - fp * _StabbyWaterSwell.x * 6) * (1.2 - 0.5 * ripple) * (1 - 0.5 * slick) * sg;
                }
                float pool = 0, poolBoil = 0;
                UNITY_BRANCH if (river && i.fall.y > 0)
                {
                    float rn = (i.along - i.fall.x) / max(i.fall.z, 0.5) * (1 + 0.35 * i.chan.x * i.chan.x);
                    float3 pb = Noise(xz * 0.45 + float2(t * 0.7, -t * 0.5)) + 0.5 * Noise(mul(Turn, xz) * 1.3 - float2(t * 1.1, t * 0.4));
                    rn += 0.22 * Noise(xz * 0.17 + 5.3).x;
                    pool = saturate(1 - rn);
                    poolBoil = pb.x;
                    g += pool * wv.z * 2.5 * pb.yz;
                }
                float3 bub = 0;
                UNITY_BRANCH if (mud > 0.01)
                {
                    float2 bp = xz * 1.4 + 0.3 * Noise(xz * 0.7 + 1.9).yz;
                    bub = Bubbles(bp, _Time.y) + 0.6 * Bubbles(mul(Turn, xz) * 2.2 + 5.3, _Time.y * 0.8);
                    bub *= mud * saturate(2 - fp * 5);
                    g = g * (1 - 0.85 * mud) + bub.yz;
                }
                float3 n = normalize(float3(-g.x, 1, -g.y));
                float streak = 0;
                UNITY_BRANCH if (river && churn > 0.35)
                {
                    float sp = frac(_Time.y / 7), sw = abs(1 - 2 * sp);
                    float sdr = speed * 7 * 0.05;
                    float2 sq = cw * float2(0.12, 0.3);
                    sq.y += 0.5 * Noise(sq * float2(0.7, 0.3) + 11.3).x + 1.3 * Noise(cw * float2(0.02, 0.07) + 17.9).x;
                    float s1 = Noise(sq - float2(sdr * sp, 0)).x, s2 = Noise(sq - float2(sdr * frac(sp + 0.5), 0) + 5.1).x;
                    float acc = lerp(s1, s2, sw) * rsqrt(sw * sw + (1 - sw) * (1 - sw));
                    float sb = smoothstep(0.15, 0.5, Noise(cw * float2(0.21, 0.3) + float2(-sdr * 0.3 * _Time.y / 7, 6.7)).x);
                    streak = smoothstep(0.35, 0.9, churn) * sb * smoothstep(0.84, 0.97, 1 - abs(acc)) * saturate(1.6 - fp * 0.8);
                }

                float2 ruv = uv + g * (0.12 * saturate(depth) / (1 + surface * 0.08));
                float rscene = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                UNITY_FLATTEN if (rscene < surface) { ruv = uv; rscene = scene; }
                float3 under = SampleSceneColor(ruv);
                float path = lerp(max(0, dist * (rscene / max(surface, 0.001) - 1)), field / max(v.y, 0.05), far);
                float3 shallowC = lerp(_StabbyWaterShallow.rgb, _StabbySpringWater.rgb, hot), deepC = lerp(_StabbyWaterDeep.rgb, _StabbySpringDeep.rgb, hot);
                float3 trans = exp(log(max(shallowC, 0.01)) * (path * _StabbyWaterDeep.w));

                Light l = GetMainLight();
                float3 sky = _StabbySky.rgb;
                float dk = 1 - exp(-path * _StabbyWaterDeep.w * 0.3);
                float3 silt = float3(0.5, 0.44, 0.3) * dot(shallowC, 1.0 / 3);
                float3 teal = shallowC * lerp(float3(0.26, 0.4, 0.42), 0.7, hot);
                float3 tint = lerp(lerp(silt, teal, saturate(dk * 2.2 + hot)), deepC, smoothstep(0.25, 1, dk));
                float3 scatter = LookColor(tint, 1.3) * (l.color * saturate(l.direction.y + 0.2) * 0.6 + sky * 0.5);
                under *= lerp(float3(1.04, 0.99, 0.86), 1, saturate(dk * 3));
                float3 c = under * trans + scatter * (1 - trans);
                float3 nf = river ? normalize(lerp(n, float3(0, 1, 0), 0.8 * saturate(1 - 2.5 * v.y))) : n;
                float ndv = saturate(dot(nf, v));
                float fres = 0.02 + (river ? 0.98 : 0.6) * pow(1 - ndv, 5);
                float3 r = reflect(-v, nf);
                float3 skyR = lerp(sky * (river ? 1.35 : 1.02), sky * (river ? float3(0.8, 0.88, 1.05) : float3(0.62, 0.76, 0.98)), sqrt(saturate(r.y))) + l.color * pow(saturate(dot(r, l.direction)), 6) * 0.25;
                c = lerp(c, skyR, fres);
                c = lerp(c, skyR, slick * 0.2 * (1 - ndv));
                float e = lerp(900, 60, saturate(dist / 400));
                float3 h = normalize(l.direction + v);
                c += l.color * pow(saturate(dot(n, h)), e) * ((e + 8) / 25.13 * 0.05);

                float2 sl = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, fuv, 0).rg;
                float shore = depth / max(length(sl), 0.03);
                bool sea = i.color.a >= 0.99;
                float calm = river ? 0.25 + 0.5 * churn : sea ? 1 : 0.4;
                float band = saturate(1 - shore / max(wv.w, 0.01));
                float foam = 0;
                UNITY_BRANCH if (band > 0)
                {
                    float near = saturate(2 - dist / 250);
                    float2 sdir = sl / max(length(sl), 1e-4);
                    float3 ph = Noise(xz * 0.021 + 3.1);
                    float run = 0.5 + 0.5 * sin(_Time.y * 0.5 + 5 * ph.x + 1.7 * Noise(xz * 0.09 + 8.3).x);
                    float reach = calm * (0.3 + 0.7 * run * (0.75 + 0.5 * ph.x));
                    float2 fp = (xz + sdir * (wv.w * 0.6 * run)) * 0.35;
                    float lace = 0.5 + Noise(mul(Turn, fp) + float2(t * 0.3, -t * 0.2)).x + 0.5 * Noise(mul(fp, Turn) * 2.7 - float2(t * 0.4, t * 0.1)).x;
                    float fine = Noise(mul(Turn, fp) * 7.3 + float2(t * 0.5, t * 0.7)).x;
                    lace = lerp(0.6, lace + 0.35 * fine * saturate(2 - dist / 120), near);
                    float broad = 0.8 + 0.5 * Noise(mul(fp, Turn) * 0.23 + 3.7).x;
                    float sb = shore / max(wv.w, 0.01);
                    float lap = smoothstep(0.1 + 0.12 * saturate(lace), 0.02, sb - 0.04 * fine) * (0.55 + 0.45 * smoothstep(0.2, 0.8, lace * broad));
                    float wash = saturate(1 - sb / max(reach, 0.02));
                    float front = smoothstep(0.55, 0.9, wash) * (1 - smoothstep(0.9, 1, wash) * 0.5);
                    float patchy = smoothstep(0.4, 0.95, lace * broad + 0.25 * fine);
                    foam = river ? 0.5 * lap * smoothstep(0.5, 1, churn) * smoothstep(0.3, 0.8, lace * broad) : max(lap * (sea ? 0.85 : 0.6), (sea ? 1 : 0.55) * wash * patchy * (0.5 + 0.5 * front));
                }
                foam = max(foam, 0.7 * white * smoothstep(0.85, 1.4, wl));
                float slow = 1 - churn, inner = saturate(-i.chan.x * i.chan.z * 1.6);
                float eddy = inner * smoothstep(0.3, 0.9, abs(i.chan.x));
                float lw = slow * saturate(1.8 * inner - 0.1) * saturate(2.5 - fp * 3);
                UNITY_BRANCH if (river && lw > 0.01)
                {
                    float la = _Time.y * _StabbyWaterFlow.x;
                    float2 lp = float2((i.along - 0.6 * la) * 0.19, cw.y * 0.5);
                    float3 wn = Noise(lp * float2(0.8, 0.22) + 7.3);
                    lp.y += 1.6 * wn.x + 1.5 * Noise(float2(i.along * 0.025, cw.y * 0.08) + 19.1).x;
                    float ridge = smoothstep(0.7, 0.95, 1 - abs(Noise(lp).x) * 1.7);
                    float brk = smoothstep(0.02, 0.38, Noise(lp * float2(1.4, 0.6) + 2.9).x + 0.25 * wn.x);
                    float2 ep = float2(i.along + 0.3 * la, cw.y) * 0.14 + 0.8 * wn.yz;
                    float eb = Noise(ep).x + 0.35 * Noise(ep * 3.3 + 1.7).x;
                    float grain = lerp(0.7, saturate(0.55 + 0.9 * Noise(mul(Turn, xz) * 1.9 + float2(t * 0.2, 0)).x), fade);
                    foam = max(foam, lw * ridge * brk * grain);
                    foam = max(foam, 0.8 * eddy * slow * smoothstep(0.3, 0.7, eb) * grain);
                }
                foam = max(foam, 0.15 * streak);
                UNITY_BRANCH if (pool > 0)
                {
                    float churned = smoothstep(0.05, 0.55, poolBoil + 0.6 * pool - 0.35);
                    float core = smoothstep(0.6, 0.95, pool) * (0.6 + 0.4 * churned);
                    float ring = smoothstep(0.1, 0.6, pool) * churned * 0.55;
                    foam = max(foam, max(core, ring));
                }
                foam *= 1 - 0.9 * mud;
                c = lerp(c, l.color * 0.9 + sky * 0.2, foam * 0.7);
                UNITY_BRANCH if (mud > 0.01)
                {
                    float mot = Noise(xz * 0.9 + 2.7).x + 0.5 * Noise(mul(Turn, xz) * 3.1 + 6.1).x;
                    float3 mc = lerp(_StabbyMudSurface.rgb, _StabbyMudDeep.rgb, saturate(0.45 + 0.6 * mot));
                    mc *= 1 + 0.3 * saturate(bub.x);
                    float3 lit = mc * (l.color * saturate(dot(n, l.direction)) * 0.9 + sky * 0.45);
                    lit = lerp(lit, skyR, fres * 0.5) + l.color * pow(saturate(dot(n, h)), 90) * 0.15 * (1 - _StabbyMudSurface.w);
                    c = lerp(c, lit, mud);
                }

                float alpha = lerp(saturate(max(saturate(depth * _StabbyWaterShallow.w), foam * 0.7 * saturate(depth / 0.04))), saturate(depth * 4) * saturate((1 - i.chan.z) * MudFeather), mud);
                float clear = dot(trans, 1.0 / 3) * (1 - fres) * (1 - foam * 0.7) * (1 - mud);
                return half4(lerp(MixFog(c, i.fog), c, clear), alpha);
            }
            ENDHLSL
        }
    }
}
