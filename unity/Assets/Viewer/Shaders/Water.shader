Shader "Stabby/Water"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4 _StabbyLiquid[80];
        float4 _StabbyShadowPx;
        float4 _StabbyShadowEye;

        float4 Liquid(float id, int row) { return _StabbyLiquid[clamp((int)round(id), 0, 7) * 10 + row]; }

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

        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 chan : TEXCOORD0; float4 reach : TEXCOORD1; float4 fall : TEXCOORD2; float4 tint : TEXCOORD3; float4 curt : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Viewer/Shaders/LocalLights.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Viewer/Shaders/Look.hlsl"
            #include "Assets/Viewer/Shaders/Sky.hlsl"

            float4 _StabbySky, _StabbyFieldUV;
            float4 _StabbyTintSlot;
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            static const float2x2 Turn = float2x2(0.8, -0.6, 0.6, 0.8);
            static const float2x2 Veer = float2x2(0.96, -0.28, 0.28, 0.96);
            static const float FlowPeriod = 3;
            static const float SlopeRange = 0.1;
            static const float MudFeather = 4, PoolFoamFeather = 0.35;
            static const float CurtainPxLo = 6, CurtainPxHi = 24, LipPx = 1.5;
            static const float CurtainPath = 6, CurtainBend = 0.05, CurtainSpill = 1.5, CurtainFlutter = 0.07, CurtainRipple = 0.06, CurtainBulge = 0.05;
            static const int SwTrains = 7;
            static const float SwTrainL[7] = { 1, 0.66, 0.47, 0.31, 0.21, 0.14, 0.095 };
            static const float2 SwTrainD[7] = { float2(0.8, 0.6), float2(0.96, -0.28), float2(0.53, 0.848), float2(0.99, 0.141), float2(0.6, 0.8), float2(0.894, 0.447), float2(0.707, 0.707) };
            static const float SwTrainInvL[7] = { 1, 1.5152, 2.1277, 3.2258, 4.7619, 7.1429, 10.526 };
            static const float SwTrainRate[7] = { 1, 1.2309, 1.4586, 1.7961, 2.1822, 2.6726, 3.2444 };
            static const float SwTrainP[7] = { 1, 0.72, 0.55, 0.4, 0.29, 0.2, 0.14 };
            static const float SwTrainS[7] = { 0.6, 0.6, 0.65, 0.7, 0.8, 0.85, 0.9 };
            static const float SwTrainPh[7] = { 0, 0.37, 0.71, 0.19, 0.53, 0.83, 0.29 };
            static const float2 SwGroupDir = float2(0.8, 0.6), SwWarpA = float2(0.6, 0.8), SwWarpB = float2(-0.8, 0.6);
            static const float SwGroupDepth = 0.3, SwGroupL = 260, SwGroupSpeed = 4, SwWarpGain = 0.25, SwWarpAL = 3100, SwWarpBL = 2700, SwPeak = 3.3 * 1.3;
            static const float SwStart = 12, SwFull = 4, SwRef = 8, SwBreaking = 0.39, SwSharp = 2.5, SwMinDepth = 0.05, SwShoreSlope = 0.05, SwG = 9.81, SwShoreFade = 0.3, SwPeakShare = 0.5;
            static const float SeaSnap = 2, SwellPxLo = 0.75, SwellPxHi = 2.5;
            static const float2x2 Spin = float2x2(-0.737, -0.675, 0.675, -0.737);
            static const int ChopOctaves = 6;
            static const float ChopLacunarity = 1.93, ChopGain = 0.78, ChopPxLo = 1.5, ChopPxHi = 4, ChopWarp = 0.35, ChopStrength = 0.5, ChopBroad = 0.45, ChopFloorA2 = 0.0015, ChopLostBlur = 0.15;
            static const float SetBase = 1.25, SetGain = 2.2, GlitPx = 2, GlitExp = 300, GlitGain = 4, GlitRate = 0.7, GlitTilt = 0.08, GlitTiltMax = 0.35, LocalSpecMax = 300, LocalStretch = 5, RippleLit = 0.9, GrazeFlat = 0.2;
            static const float FarPx = 6, FarStep = 3, FarShare = 0.6, FarDrift = 0.08, GustLo = 0.8, GustHi = 1.2, GustDrift = 0.05;
            static const float2 SetDir[6] = { float2(0.82, 0.57), float2(-0.34, 0.94), float2(0.98, -0.21), float2(-0.71, -0.70), float2(0.26, -0.96), float2(-0.93, 0.36) };
            static const float SetFreq[6] = { 1, 1.37, 1.93, 2.71, 3.83, 5.29 };
            static const float SetSteep[6] = { 0.30, 0.24, 0.18, 0.13, 0.09, 0.06 };
            static const int SsrSteps = 8;
            static const float SsrStart = 0.3, SsrGrow = 1.8, SsrFres = 0.05, ChromaSplit = 0.25, CausticDepth = 2.5;

            float4 _StabbySwellSea, _StabbySwellLake;
            float _StabbyWaterLookOff;

            float SwWave(float phase, float sharp, out float slope)
            {
                float f = frac(phase);
                float tri = abs(f - 0.5) * 4 - 1;
                float s = tri * (1.5 - 0.5 * tri * tri);
                slope = (1 + sharp * s) * (1.5 - 1.5 * tri * tri) * 4 * (f < 0.5 ? -1 : 1);
                return s + sharp * ((s + 1) * (s + 1) * 0.5 - 1 - s);
            }

            float SwShoreWeight(float d) { return saturate((SwStart - d) / (SwStart - SwFull)); }
            float SwShoreAmp(float4 p, float d) { return d <= 0 ? 0 : min(p.w * pow(SwRef / d, 0.25), SwBreaking * d) * SwShoreWeight(d); }
            float SwShoreLength(float4 p, float d) { return p.z * sqrt(SwG * max(d, SwMinDepth)); }

            float Swell(float4 p, float2 xz, float t, float d, float sd, float2 dir, float s, float lo, out float2 grad, out float crest)
            {
                grad = 0;
                crest = 0;
                if (d <= 0) return 0;
                float h = 0, open = (1 - SwShoreFade * SwShoreWeight(d)) * min(1, SwBreaking * d / max(p.x * SwPeak * SwPeakShare, 1e-4));
                UNITY_BRANCH if (open > 0 && p.x > 0)
                {
                    float dg, dwa, dwb;
                    float group = 1 + SwGroupDepth * SwWave((dot(xz, SwGroupDir) - t * SwGroupSpeed) / SwGroupL, 0, dg);
                    float warp = SwWarpGain * (SwWave(dot(xz, SwWarpA) / SwWarpAL, 0, dwa) + SwWave(dot(xz, SwWarpB) / SwWarpBL, 0, dwb));
                    float2 wg = SwWarpGain * (dwa * SwWarpA / SwWarpAL + dwb * SwWarpB / SwWarpBL);
                    float sum = 0, il = 1 / p.y, tr = t * sqrt(SwG * il / TWO_PI), gate = p.y / (s * lo), oa = p.x * open;
                    float2 sg = 0;
                    UNITY_UNROLL for (int k = 0; k < SwTrains; k++)
                    {
                        float a = oa * SwTrainP[k] * saturate(gate * SwTrainL[k] - 1), dk;
                        float2 kd = SwTrainD[k] * (il * SwTrainInvL[k]);
                        sum += a * SwWave(dot(xz, kd) - tr * SwTrainRate[k] + SwTrainPh[k] + warp * (k + 1), SwTrainS[k], dk);
                        sg += a * dk * (kd + wg * (k + 1));
                    }
                    h += group * sum;
                    grad += group * sg + SwGroupDepth * dg * SwGroupDir / SwGroupL * sum;
                }
                float amp = SwShoreAmp(p, d);
                UNITY_BRANCH if (amp > 0)
                {
                    float c = SwShoreLength(p, d), sharp = saturate(1 - d / SwSharp), ds;
                    float on = saturate((0.5 * c / s - lo) / lo);
                    float w = SwWave(2 * max(sd, 0) / c + t / p.z, sharp, ds);
                    h += amp * on * w;
                    grad -= 2 * amp * on * ds * dir / c;
                    crest = on * sharp * SwShoreWeight(d) * smoothstep(0.7, 0.97, w);
                }
                return h;
            }

            float4 SwellParamsOf(float kind) { return kind > 0.5 ? _StabbySwellSea : _StabbySwellLake; }

            float SsrDepth(float2 suv) { return LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(suv), 0).r, _ZBufferParams); }

            float4 Ssr(float3 p, float3 r, float dist)
            {
                float stepL = SsrStart * (1 + dist * 0.03), along = 0;
                UNITY_LOOP for (int k = 0; k < SsrSteps; k++)
                {
                    along += stepL;
                    float4 cs = TransformWorldToHClip(p + r * along);
                    if (cs.w <= 0) break;
                    float4 sp = ComputeScreenPos(cs);
                    float2 suv = sp.xy / sp.w;
                    if (any(suv < 0) || any(suv > 1)) break;
                    float gap = cs.w - SsrDepth(suv);
                    if (gap > 0 && gap < stepL * 2 + 0.3)
                    {
                        float2 e = saturate(min(suv, 1 - suv) * 8);
                        float3 col = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(suv), 0).rgb;
                        return float4(col, e.x * e.y * (1 - 0.5 * k / (float)SsrSteps));
                    }
                    stepL *= SsrGrow;
                }
                return 0;
            }

            float Caustics(float2 p, float t)
            {
                float2 w = p + 0.6 * Noise(p * 0.5 + float2(t * 0.11, 3.7)).yz;
                float a = 1 - abs(Noise(w + float2(t * 0.3, -t * 0.2)).x) * 3;
                float b = 1 - abs(Noise(mul(Turn, w) * 1.37 + float2(-t * 0.25, t * 0.35) + 9.1).x) * 3;
                return pow(saturate(a), 3) * 0.6 + pow(saturate(b), 3) * 0.5 + pow(saturate(min(a, b)), 2) * 0.8;
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

            float2 SeaChop(float2 q, float t, float wx, float fpq, float ride, out float lost)
            {
                float2 wq = q * 0.11;
                float3 w1 = Noise(mul(Veer, wq) + 31.7);
                float3 w2 = Noise(wq * 1.7 + 12.9);
                float m = saturate(0.5 + 0.9 * w2.x);
                float2 p0 = q + 1.4 * float2(w1.x, w2.x);
                float2 g = 0;
                float bw = 1 - smoothstep(ChopPxLo, ChopPxHi * 2, 1 / fpq);
                UNITY_BRANCH if (bw > 0)
                    g = 0.6 * bw * mul(Noise(mul(Turn, p0) * ChopBroad + float2(t * 0.05, 7.7)).yz, Turn);
                float2 warp = 0;
                float2x2 r = Veer;
                float f = 1, a = 1;
                lost = 0;
                UNITY_LOOP for (int k = 0; k < ChopOctaves; k++)
                {
                    float on = smoothstep(ChopPxLo, ChopPxHi, 1 / (f * fpq));
                    float amp = a * (k < 2 ? 1.15 - 0.65 * m : (0.3 + 0.6 * m) * ride);
                    UNITY_BRANCH if (on <= 0)
                    {
                        for (; k < ChopOctaves; k++)
                        {
                            lost += amp * amp;
                            a *= ChopGain;
                            amp = a * (k < 1 ? 1.15 - 0.65 * m : (0.3 + 0.6 * m) * ride);
                        }
                        break;
                    }
                    lost += amp * amp * (1 - on);
                    {
                        float2 p = mul(r, p0) * f + warp + r[0] * (t * sqrt(SwG * wx * f / TWO_PI)) + k * 17.3;
                        float3 nz = Noise(p);
                        float ridge = k >= 2 && k <= 4 ? 0.6 : 0;
                        float2 d = nz.yz * (1 - ridge * (1 + nz.x * rsqrt(nz.x * nz.x + 0.02)));
                        g += mul(d, r) * (amp * on);
                        warp = ChopWarp * nz.yz;
                    }
                    f *= ChopLacunarity;
                    a *= ChopGain;
                    r = mul(Spin, r);
                }
                float2 sw = 0;
                float lean = 0.6 + 0.4 * ride;
                UNITY_UNROLL for (int j = 0; j < 6; j++)
                {
                    float kq = TWO_PI * SetBase * SetFreq[j];
                    float on = smoothstep(ChopPxLo, ChopPxHi, TWO_PI / (kq * fpq));
                    float sa = SetSteep[j] * SetGain * lean;
                    lost += sa * sa * (1 - on);
                    UNITY_BRANCH if (on > 0)
                        sw += SetDir[j] * (sa * on * cos(dot(SetDir[j], p0) * kq + t * sqrt(SwG * wx * kq) + j * 2.39996));
                }
                UNITY_BRANCH if (lost > 0.01)
                {
                    float lv = log2(max(fpq * FarPx, 1e-4)) / log2(FarStep);
                    float l0 = floor(lv), bl = smoothstep(0, 1, lv - l0);
                    float f0 = pow(FarStep, -l0);
                    float2 d0 = mul(Noise(mul(Turn, p0) * f0 + FarDrift * float2(t, 0.6 * t) + l0 * 7.1).yz, Turn);
                    float2 d1 = Noise(mul(Veer, p0) * (f0 / FarStep) + FarDrift * float2(-0.7 * t, t) + l0 * 7.1 + 3.3).yz;
                    sw += lerp(d0, d1, bl) * (sqrt(lost) * FarShare * rsqrt((1 - bl) * (1 - bl) + bl * bl));
                    lost *= 1 - FarShare * FarShare;
                }
                float gust = lerp(GustLo, GustHi, smoothstep(-0.4, 0.4, Noise(wq * 0.6 - GustDrift * float2(t, 0.7 * t) + 4.1).x));
                lost *= gust * gust;
                return (g + sw) * (gust * (0.55 + 0.6 * smoothstep(-0.35, 0.45, w1.x + 0.5 * w2.x)));
            }

            float Glitter(float2 xz, float fpr, float3 n, float3 h, float t, float tilt, float2 axis, float stretch)
            {
                float lv = log2(max(fpr * GlitPx, 1e-4));
                float l0 = floor(lv), bl = lv - l0;
                float s = 0;
                UNITY_UNROLL for (int j = 0; j < 2; j++)
                {
                    float size = exp2(l0 + j);
                    int2 ci = (int2)floor(xz / size);
                    float2 b = Grad(ci + int2(419, 7 + 53 * j)) * 0.5 + 0.5;
                    float ph = t * GlitRate * (0.6 + 0.8 * b.x) + b.y;
                    int life = (int)floor(ph);
                    float2 a = Grad(ci + int2(7919 * life, 31 * j + 913));
                    a += (stretch - 1) * dot(a, axis) * axis;
                    float3 nf = normalize(n + float3(a.x, 0, a.y) * tilt);
                    s += (j == 0 ? 1 - bl : bl) * sin(PI * frac(ph)) * pow(saturate(dot(nf, h)), GlitExp);
                }
                return s;
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

            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; float4 chan : TEXCOORD3; float along : TEXCOORD4; float4 fall : TEXCOORD5; float warm : TEXCOORD6; float4 tint : TEXCOORD7; float4 curt : TEXCOORD8; float4 swell : TEXCOORD9; };

            float4 Mixed(Varyings i, int row)
            {
                float3 t = i.tint.xyz;
                return Liquid(i.warm, row) * (1 - t.x - t.y - t.z) + Liquid(_StabbyTintSlot.x, row) * t.x + Liquid(_StabbyTintSlot.y, row) * t.y + Liquid(_StabbyTintSlot.z, row) * t.z;
            }

            float ScreenPx(float size, float dist) { return size / max(dist, 0.01) * abs(UNITY_MATRIX_P[1][1]) * 540; }

            half4 Curtain(Varyings i, float3 v, float dist, float2 uv)
            {
                float4 L0 = Mixed(i, 0), L1 = Mixed(i, 1), L7 = Mixed(i, 7), L8 = Mixed(i, 8), L9 = Mixed(i, 9);
                float u = saturate(i.curt.y), h = i.curt.z, y = u * h, x = i.chan.x * i.chan.y;
                float2 fd = normalize(i.color.rg * 2 - 1), sd = float2(fd.y, -fd.x);
                float flowQ = saturate(log2(1 + i.curt.w) / 9);
                float tau = sqrt(2 * y / 9.81), tm = _Time.y * L7.x;
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
                float white = saturate(aer + smoothstep(0.1, 0.6, str) * (0.2 + 0.45 * u) * (0.6 + 0.4 * flowQ)) * L8.w;
                float3 up = float3(0, 1, 0), f3 = float3(fd.x, 0, fd.y), s3d = float3(sd.x, 0, sd.y);
                float3 nb = normalize(f3 * (0.35 + u) + up * (1 - u) * 0.9);
                float3 tb = normalize(cross(s3d, nb));
                float3 n = normalize(nb + s3d * (0.5 * s1.y + 0.3 * s2.y + 0.2 * s3.y + 0.3 * warp.y) + tb * (0.15 * s1.z + 0.1 * s2.z + 0.2 * warp.z));
                n = dot(n, v) < 0 ? -n : n;
                Light l = GetMainLight();
                LocalGlint lg = LocalLightsGlint(i.positionWS, n, v, 120, 1, i.positionCS);
                float3 sky = _StabbySky.rgb;
                float ndv = saturate(dot(n, v));
                float edge = smoothstep(1, 0.82, abs(i.chan.x) + 0.08 * warp.x);
                float gap = smoothstep(-0.6, -0.15, s1.x + 0.4 * warp.x + 0.5 * flowQ);
                float spill = smoothstep(0, CurtainSpill, y);
                float sheet = edge * lerp(0.2, 1, gap) * lerp(0.4, 1, spill);
                float path = CurtainPath * (0.25 + flowQ) * sheet / max(ndv, 0.35);
                float3 trans = exp(log(max(L1.rgb, 0.01)) * (path * L0.w));
                float dk = 1 - exp(-path * L0.w * 0.3);
                float3 teal = L1.rgb * float3(0.26, 0.4, 0.42);
                float3 tint = lerp(teal, L0.rgb, smoothstep(0.05, 0.4, dk));
                float3 body = LookColor(tint, 1.3) * (l.color * saturate(l.direction.y + 0.2) * 0.6 + sky * 0.5 + lg.diffuse * 0.6);
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
                c += L1.rgb * l.color * fwdS * thinL * 0.7 * (1 - 0.5 * white);
                float lit = saturate(dot(n, l.direction)) * 0.55 + 0.45;
                float3 froth = (l.color * lit * 0.85 + sky * 0.3 + lg.diffuse * 0.85) * lerp(float3(1, 1, 1), 0.6 + L1.rgb * 0.5, 0.25 * (1 - u)) * (0.88 + 0.12 * s2.x) * L9.rgb;
                c = lerp(c, froth, white * (0.75 + 0.25 * u));
                float lipW = 0.12 + 0.06 * Noise(float2(x * 0.7, 1.7)).x;
                float lip = (1 - smoothstep(lipW * 0.5, lipW, y)) * saturate(ScreenPx(lipW, dist) / LipPx);
                c = lerp(c, skyR * 1.15 + l.color * 0.25, lip * 0.8);
                float3 h3 = normalize(l.direction + v);
                float glint = pow(saturate(dot(n, h3)), 400) * smoothstep(0.35, 0.75, s2.x + 0.5 * s3.x + 0.3 * warp.x);
                c += l.color * (glint * 3 * detail + pow(saturate(dot(n, h3)), 120) * 0.3) * (1 - white);
                c += lg.spec * 0.3 * (1 - white);
                float alpha = saturate(lerp(0.9, 1, white)) * edge * lerp(0.35, 1, gap) * lerp(0.6, 1, spill) * L7.y;
                return half4(StabbyMixFog(c, i.positionWS, i.fog), alpha);
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                UNITY_BRANCH if (i.fall.w > 0.5)
                {
                    float u = saturate(i.curt.y), h = i.curt.z, wd = 2 * i.chan.y;
                    float move = smoothstep(CurtainPxLo, CurtainPxHi, ScreenPx(wd, length(_WorldSpaceCameraPos - wp)));
                    UNITY_BRANCH if (move > 0)
                    {
                        float2 fd = normalize(i.color.rg * 2 - 1);
                        float3 f3 = float3(fd.x, 0, fd.y), s3d = float3(fd.y, 0, -fd.x);
                        float tau = sqrt(2 * u * h / 9.81), ph = (tau - _Time.y * 0.8 * Liquid(i.reach.w, 7).x) * 2.2;
                        float3 fl = Noise(float2(i.reach.z * 0.03 + 1.3, ph * 0.3));
                        float3 er = Noise(float2(i.chan.x * 3.7 + i.reach.z * 0.1, ph + 9.1));
                        float flowQ = saturate(log2(1 + i.curt.w) / 9);
                        float side = fl.x * u * min(CurtainFlutter * wd, 0.04 * h);
                        float edge = er.x * smoothstep(0, 0.3, u) * CurtainRipple * wd * i.chan.x;
                        float bulge = 6.75 * u * (1 - u) * (1 - u) * flowQ * min(CurtainBulge * h, 0.25 * wd) + fl.y * u * 0.015 * h;
                        wp += (s3d * (side + edge) + f3 * bulge) * move;
                    }
                }
                float kind = i.color.a >= 0.99 ? 1 : 0;
                UNITY_BRANCH if (kind > 0 && i.fall.y > 0.5) wp.xz += floor(_WorldSpaceCameraPos.xz / SeaSnap) * SeaSnap;
                float lift = 0;
                UNITY_BRANCH if (i.color.a > 0.25 && i.fall.z > 0 && i.fall.x > 0 && _StabbyWaterLookOff < 0.5)
                {
                    float4 p = SwellParamsOf(kind);
                    p.x *= i.fall.x;
                    float2 fuv = wp.xz * _StabbyFieldUV.x + _StabbyFieldUV.y;
                    float d = wp.y - SAMPLE_TEXTURE2D_LOD(_StabbyBed, sampler_StabbyBed, fuv, 0).r;
                    float2 sl = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, fuv, 0).rg;
                    float sm = length(sl);
                    float amp = p.x * SwPeak + SwShoreAmp(p, d);
                    float w = smoothstep(SwellPxLo, SwellPxHi, ScreenPx(amp, length(_WorldSpaceCameraPos - wp)));
                    UNITY_BRANCH if (w > 0)
                    {
                        float2 gr;
                        float cr;
                        lift = w * Swell(p, wp.xz, _Time.y, d, d / SwShoreSlope, sl / max(sm, 1e-4), i.fall.z, 4, gr, cr);
                        wp.y += lift;
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
                o.tint = float4(round(i.reach.w) == 0 ? saturate(i.tint.xyz) : 0, i.tint.w);
                o.swell = float4(i.color.a > 0.25 && i.fall.z > 0 ? i.fall.x : 0, lift, kind, 0);
                o.fall = i.fall;
                o.curt = i.curt;
                return o;
            }

            half4 Sheet(Varyings i, float3 v, float dist, float2 uv)
            {
                float4 L0 = Mixed(i, 0), L1 = Mixed(i, 1), L2 = Mixed(i, 2), L3 = Mixed(i, 3), L4 = Mixed(i, 4), L5 = Mixed(i, 5), L6 = Mixed(i, 6), L7 = Mixed(i, 7);
                float opacity = L5.x, visc = L5.y;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surface = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float3 bed = _WorldSpaceCameraPos - v * (dist * scene / max(surface, 0.001));
                float2 fuv = i.positionWS.xz * _StabbyFieldUV.x + _StabbyFieldUV.y;
                float field = max(0, i.positionWS.y - SAMPLE_TEXTURE2D(_StabbyBed, sampler_StabbyBed, fuv).r);
                float far = i.color.a > 0.25 ? saturate(dist * L3.z - 1) : 0;
                float depth = lerp(max(0, i.positionWS.y - bed.y), field, far);

                float4 wv = L2;
                float t = _Time.y * wv.y * (1 - 0.7 * visc);
                float2 q = i.positionWS.xz * wv.x;
                float2 xz = i.positionWS.xz;
                float hot = 1 - L6.w;
                float fp = max(length(ddx(xz)), length(ddy(xz)));
                float fpa = sqrt(length(ddx(xz)) * length(ddy(xz)));
                float fade = saturate(1.5 - fp * wv.x * 10);
                float ripple = saturate(1.5 - fp * wv.x * (i.color.a >= 0.99 ? 4 : 1.6));
                float2 flow = i.color.rg * 2 - 1;
                float slope = i.color.b * SlopeRange;
                float plunge = i.chan.w * SlopeRange * L4.y;
                float white = max(smoothstep(1, 2.25, slope * L4.y), 0.85 * smoothstep(0.9, 2.1, plunge));
                float churn = max(smoothstep(0.3, 1.3, slope * L4.y), 0.8 * smoothstep(0.3, 1.3, plunge));
                bool river = i.chan.y > 0.5 && dot(flow, flow) > 0.01;
                float2 fd = river ? normalize(flow) : float2(1, 0), sd = float2(fd.y, -fd.x);
                float2 cw = float2(i.along, i.chan.x * i.chan.y);
                bool look = _StabbyWaterLookOff < 0.5;
                float speed = look && i.tint.w > 0 ? L4.x * i.tint.w : L4.x * (0.8 + 20 * slope);
                float ph = frac(_Time.y / FlowPeriod), wab = abs(1 - 2 * ph);
                float crest = 0, swH = 0;
                float2 swGrad = 0;
                float4 swp = SwellParamsOf(i.swell.z);
                float2 sl = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, fuv, 0).rg;
                float slm = length(sl);
                float2 sdir = sl / max(slm, 1e-4);
                float swDepth = max(0, field - i.swell.y);
                UNITY_BRANCH if (look && !river && i.color.a > 0.25)
                {
                    swp.x *= i.swell.x;
                    swH = Swell(swp, xz, _Time.y, swDepth, swDepth / SwShoreSlope, sdir, max(fpa * _ScreenParams.y / 1080, 1e-3), 3, swGrad, crest);
                }
                float2 g, wg = 0;
                float wl = 0, lost = 1.5 * (1 - ripple);
                bool chop = false;
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
                else if (look)
                {
                    float ride = lerp(0.55, 1.45, saturate(0.5 + 0.5 * swH / max(swp.x * SwPeak * 0.5, 1e-3)));
                    g = SeaChop(q, t, wv.x, fpa * _ScreenParams.y / 1080 * wv.x, ride, lost);
                    chop = true;
                }
                else g = StillWaves(q, t, fade, ripple);
                float swell = i.color.a >= 0.99 ? 1 : i.color.a * 2 * L4.z;
                float slick = 0;
                UNITY_BRANCH if (swell > 0)
                {
                    float2 wq = mul(Turn, xz * L3.x);
                    float2 sp = wq * float2(0.15, 1.4) + float2(_Time.y * 0.004, 0.6 * Noise(wq * 0.35 + 2.1).x);
                    float sn = Noise(sp).x + 0.5 * Noise(sp * float2(1.9, 2.1) + 5.3).x;
                    slick = swell * smoothstep(0.05, 0.45, sn) * saturate(2 - fp * L3.x * 20);
                }
                float gs = wv.z * (chop ? ChopStrength : 0.15 + 0.85 * ripple) * (1 - 0.85 * slick);
                g *= gs * (1 + white + 0.5 * churn);
                g += wv.z * 3 * wg;
                UNITY_BRANCH if (swell > 0)
                {
                    float2 sq = mul(Turn, xz * L3.x) + float2(_Time.y * 0.05, _Time.y * 0.02);
                    float2 sg = mul(Noise(sq).yz, Turn) + 0.7 * Noise(mul(sq, Turn) * 0.43 + float2(-_Time.y * 0.02, 1.7)).yz;
                    g += swell * L3.y * saturate(1.6 - fp * L3.x * 6) * (1.2 - 0.5 * ripple) * (1 - 0.5 * slick) * sg;
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
                UNITY_BRANCH if (L5.z > 0.01)
                {
                    float bt = _Time.y * (1 - 0.5 * visc);
                    float2 bp = xz * 1.4 + 0.3 * Noise(xz * 0.7 + 1.9).yz;
                    bub = Bubbles(bp, bt) + 0.6 * Bubbles(mul(Turn, xz) * 2.2 + 5.3, bt * 0.8);
                    bub *= L5.z * saturate(2 - fp * 5);
                }
                g += swGrad;
                g = g * (1 - visc) + bub.yz;
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
                UNITY_BRANCH if (look && rscene >= surface && any(ruv != uv))
                {
                    float2 split = (ruv - uv) * ChromaSplit;
                    under.r = SampleSceneColor(ruv + split).r;
                    under.b = SampleSceneColor(ruv - split).b;
                }
                float path = lerp(max(0, dist * (rscene / max(surface, 0.001) - 1)), field / max(v.y, 0.05), far);
                float3 shallowC = L1.rgb, deepC = L0.rgb;
                float3 trans = exp(log(max(shallowC, 0.01)) * (path * L0.w));

                Light l = GetMainLight();
                float3 sky = _StabbySky.rgb;
                float dk = 1 - exp(-path * L0.w * 0.3);
                float3 silt = float3(0.5, 0.44, 0.3) * dot(shallowC, 1.0 / 3);
                float3 teal = shallowC * lerp(float3(0.26, 0.4, 0.42), 0.7, hot);
                float3 tint = lerp(lerp(silt, teal, saturate(dk * 2.2 + hot)), deepC, smoothstep(0.25, 1, dk));
                float3 scatter = LookColor(tint, 1.3) * (l.color * saturate(l.direction.y + 0.2) * 0.6 + sky * 0.5);
                under *= lerp(float3(1.04, 0.99, 0.86), 1, saturate(dk * 3));
                UNITY_BRANCH if (look && opacity < 0.5)
                {
                    float bedDepth = swDepth;
                    float shallow = saturate(1 - bedDepth / CausticDepth) * saturate(bedDepth * 4);
                    float lum = dot(l.color, float3(0.3, 0.59, 0.11));
                    float sun = saturate(l.direction.y * 3) * saturate(lum) * shallow * saturate(1.5 - fp * 1.2) * (1 - far);
                    UNITY_BRANCH if (sun > 0.01)
                        under *= 1 + sun * 0.9 * Caustics(bed.xz * 0.9 + g * 0.4, _Time.y);
                }
                float sig = gs * (1 - visc);
                float mss = max(L7.w * L7.w, ChopFloorA2) + ChopLostBlur * lost * sig * sig;
                float e = clamp(2 / mss - 2, 8, 2000);
                float eL = min(e, LocalSpecMax);
                LocalGlint lg = LocalLightsGlint(i.positionWS, n, v, eL, LocalStretch, i.positionCS);
                float localLit = dot(lg.diffuse, 1.0 / 3);
                float ambientLum = dot(l.color * saturate(l.direction.y + 0.2) + sky, 1.0 / 3);
                float3 transUnder = lerp(trans, dot(trans, 1.0 / 3), localLit / (localLit + ambientLum + 1e-4));
                float3 c = under * transUnder + scatter * (1 - trans);
                UNITY_FLATTEN if (look)
                    c *= 1 - RippleLit * clamp(dot(g, l.direction.xz), -0.3, 0.3);
                float graze = GrazeFlat * saturate(1 - 2.5 * v.y);
                float3 nf = river || look ? normalize(lerp(n, float3(0, 1, 0), graze)) : n;
                float ndv = saturate(dot(nf, v));
                float fres = 0.02 + (river ? 0.98 : 0.6) * pow(1 - ndv, 5);
                float3 r = reflect(-v, nf);
                float3 skyR = lerp(sky * (river ? 1.35 : 1.02), sky * (river ? float3(0.8, 0.88, 1.05) : float3(0.62, 0.76, 0.98)), sqrt(saturate(r.y))) + l.color * pow(saturate(dot(r, l.direction)), 6) * 0.25;
                float3 refl = skyR;
                UNITY_BRANCH if (look && fres > SsrFres && opacity < 0.5)
                {
                    float4 hit = Ssr(i.positionWS, r, dist);
                    refl = lerp(skyR, hit.rgb, hit.a);
                }
                c = lerp(c, refl, fres);
                c = lerp(c, refl, slick * 0.2 * (1 - ndv));
                float3 hv = l.direction + v;
                float3 h = hv * rsqrt(max(dot(hv, hv), 1e-6));
                c += l.color * pow(saturate(dot(n, h)), e) * ((e + 8) / 25.13 * 0.05);
                c += lg.spec * ((eL + 8) / 25.13 * 0.05) + LookColor(tint, 1.3) * lg.diffuse * (0.6 * (1 - dot(trans, 1.0 / 3)) * (1 - fres));
                UNITY_BRANCH if (look && !river && opacity < 0.5 && dot(lg.color, 1) > 1e-3)
                {
                    float fpr = fpa * _ScreenParams.y / 1080;
                    float tilt = clamp(GlitTilt + sig * sqrt(lost + 0.2), GlitTilt, GlitTiltMax);
                    float3 hl = normalize(lg.dir + v);
                    float2 road = v.xz - lg.dir.xz;
                    road *= rsqrt(max(dot(road, road), 1e-8));
                    c += lg.color * (GlitGain * (1 - visc) * (1 - 0.6 * slick)) * Glitter(xz, fpr, n, hl, _Time.y, tilt, road, LocalStretch);
                }
                UNITY_BRANCH if (look && !river && opacity < 0.5 && l.direction.y > 0)
                {
                    float fpr = fpa * _ScreenParams.y / 1080;
                    float tilt = clamp(GlitTilt + sig * sqrt(lost + 0.2), GlitTilt, GlitTiltMax);
                    c += l.color * (GlitGain * (1 - visc) * (1 - 0.6 * slick) * saturate(l.direction.y * 4)) * Glitter(xz, fpr, n, h, _Time.y, tilt, float2(1, 0), 1);
                }

                float shore = depth / max(slm, 0.03);
                bool sea = i.color.a >= 0.99;
                float calm = river ? 0.25 + 0.5 * churn : sea ? 1 : 0.4;
                float band = saturate(1 - shore / max(wv.w, 0.01));
                float foam = 0;
                UNITY_BRANCH if (band > 0)
                {
                    float near = saturate(2 - dist / 250);
                    float3 ph = Noise(xz * 0.021 + 3.1);
                    float swash;
                    float run = look && !river ? 0.5 + 0.5 * SwWave(_Time.y / swp.z + 0.25 + 0.12 * ph.x, 0, swash) : 0.5 + 0.5 * sin(_Time.y * 0.5 + 5 * ph.x + 1.7 * Noise(xz * 0.09 + 8.3).x);
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
                UNITY_BRANCH if (sea && crest > 0.01)
                {
                    float cl = 0.6 + 0.6 * Noise(mul(Turn, xz) * 0.45 + float2(t * 0.2, 1.3)).x + 0.3 * Noise(xz * 1.7 + 4.1).x * fade;
                    foam = max(foam, 0.5 * crest * smoothstep(0.45, 0.85, cl) * saturate(1 - shore / max(2.5 * wv.w, 0.01)));
                }
                float slow = 1 - churn, inner = saturate(-i.chan.x * i.chan.z * 1.6);
                float eddy = inner * smoothstep(0.3, 0.9, abs(i.chan.x));
                float lw = slow * saturate(1.8 * inner - 0.1) * saturate(2.5 - fp * 3);
                UNITY_BRANCH if (river && lw > 0.01)
                {
                    float la = _Time.y * L4.x;
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
                    foam = max(foam, max(core, ring) * saturate(depth / PoolFoamFeather));
                }
                foam *= 1 - max(visc, 0.9 * opacity);
                c = lerp(c, l.color * 0.9 + sky * 0.2 + lg.diffuse * 0.9, foam * 0.7);
                UNITY_BRANCH if (opacity > 0.01)
                {
                    float mot = Noise(xz * 0.9 + 2.7).x + 0.5 * Noise(mul(Turn, xz) * 3.1 + 6.1).x;
                    float3 mc = lerp(L1.rgb, L0.rgb, saturate(0.45 + 0.6 * mot));
                    mc *= 1 + 0.3 * saturate(bub.x);
                    float3 lit = mc * (l.color * saturate(dot(n, l.direction)) * 0.9 + sky * 0.45 + lg.diffuse * 0.9);
                    lit = lerp(lit, skyR, fres * 0.5) + l.color * pow(saturate(dot(n, h)), 90) * 0.15 * (1 - L7.w);
                    c = lerp(c, lit, opacity);
                }
                float film = 0;
                UNITY_BRANCH if (L5.w > 0.01)
                {
                    float2 fq = mul(Turn, xz) * 0.4 + float2(_Time.y * 0.01, 0);
                    float fn = Noise(fq).x + 0.5 * Noise(fq * 2.3 + 7.1).x + 0.25 * Noise(xz * 3.7 + 2.9).x * saturate(2 - fp * 4);
                    film = L5.w * smoothstep(-0.05, 0.45, fn);
                    float3 fc = L6.rgb * (l.color * saturate(dot(n, l.direction)) * 0.8 + sky * 0.4 + lg.diffuse * 0.8) + l.color * pow(saturate(dot(n, h)), 40) * 0.08;
                    c = lerp(c, fc, film);
                }
                c += Mixed(i, 8).rgb;

                float alpha = lerp(saturate(max(saturate(depth * L1.w), foam * 0.7 * saturate(depth / 0.04))), saturate(depth * min(4, L1.w)) * saturate((1 - i.chan.z) * MudFeather), opacity);
                float clear = dot(trans, 1.0 / 3) * (1 - fres) * (1 - foam * 0.7) * (1 - opacity) * (1 - film);
                return half4(lerp(StabbyMixFog(c, i.positionWS, i.fog), c, clear), alpha);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 uv = i.screen.xy / i.screen.w;
                float b = i.curt.x;
                UNITY_BRANCH if (b > 0.999) return Curtain(i, v, dist, uv);
                half4 s = Sheet(i, v, dist, uv);
                UNITY_BRANCH if (b > 0.001) s = lerp(s, Curtain(i, v, dist, uv), b);
                return s;
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
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Assets/Viewer/Shaders/ShadowTexel.hlsl"

            float3 _LightDirection;

            struct Varyings { float4 positionCS : SV_POSITION; float4 sheet : TEXCOORD0; float2 shade : TEXCOORD1; };

            float Bayer(float2 p)
            {
                uint2 q = (uint2)p & 3u;
                static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[(q.x << 2) | q.y] + 0.5) / 16.0;
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float focal = _StabbyShadowPx.x, wd = 2 * i.chan.y;
                float flowQ = saturate(log2(1 + i.curt.w) / 9);
                float fade = ShadowTexelFade(wd, length(_StabbyShadowEye.xyz - wp));
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, _LightDirection, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                bool off = i.curt.z <= 0 || focal <= 0 || _StabbyShadowPx.w <= 0;
                o.positionCS = off ? float4(0, 0, -2, 1) : cs;
                o.sheet = float4(i.chan.x * i.chan.y, saturate(i.curt.y) * i.curt.z, i.curt.x, Liquid(i.reach.w, 7).x);
                o.shade = float2(saturate(flowQ * 1.4 - 0.25) * fade * _StabbyShadowPx.w, i.chan.x);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(min(i.sheet.z - 0.5, i.shade.x - 0.001));
                float tau = sqrt(2 * max(i.sheet.y, 0) / 9.81), ph = tau - _Time.y * i.sheet.w * 0.8;
                float n = Noise(float2(i.sheet.x * 1.3, ph * 0.66)).x + 0.5 * Noise(float2(i.sheet.x * 2.6, ph * 1.2) + 7.7).x;
                float edge = smoothstep(1, 0.82, abs(i.shade.y));
                clip(saturate(i.shade.x * edge * (0.55 + 0.6 * n)) - Bayer(i.positionCS.xy));
                return 0;
            }
            ENDHLSL
        }
    }
}
