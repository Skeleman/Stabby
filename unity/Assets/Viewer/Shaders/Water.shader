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
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            static const float2x2 Turn = float2x2(0.8, -0.6, 0.6, 0.8);
            static const float FlowPeriod = 3;
            static const float SlopeRange = 0.1;

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

            float2 Waves(float2 q, float t, float fade)
            {
                float2 g = Noise(q + float2(t, t * 0.7)).yz;
                g += 0.5 * mul(Noise(mul(Turn, q) * 2.3 + float2(-t * 1.3, t * 0.4)).yz, Turn);
                UNITY_BRANCH if (fade > 0)
                    g += 0.2 * fade * mul(Turn, Noise(mul(q, Turn) * 7.1 + float2(t * 2.1, -t * 1.7)).yz);
                return g;
            }

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 chan : TEXCOORD0; float4 reach : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; float4 chan : TEXCOORD3; float along : TEXCOORD4; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.color = i.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.screen = ComputeScreenPos(p.positionCS);
                o.chan = float4(i.chan, i.reach.xy);
                o.along = i.reach.z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 uv = i.screen.xy / i.screen.w;
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
                    float dr = speed * FlowPeriod * wv.x / stretch;
                    float2 a = Waves(cq - float2(dr * ph, 0), t, fade);
                    float2 b = Waves(cq - float2(dr * frac(ph + 0.5), 0) + 0.37, t, fade);
                    float2 gc = lerp(a, b, wab);
                    g = gc.x / stretch * fd + gc.y * sd;
                    UNITY_BRANCH if (white > 0.01)
                    {
                        float2 wq = cw * float2(0.16, 0.42);
                        wq.y += 0.6 * Noise(wq * float2(0.5, 0.35) + 3.3).x;
                        float wd = speed * FlowPeriod * 0.16;
                        float3 n1 = Noise(wq - float2(wd * ph, 0));
                        float3 n2 = Noise(wq - float2(wd * frac(ph + 0.5), 0) + 4.1);
                        float3 nw = lerp(n1, n2, wab) * rsqrt(wab * wab + (1 - wab) * (1 - wab));
                        float3 boil = 0;
                        UNITY_BRANCH if (fade > 0)
                            boil = Noise(mul(Turn, xz) * 1.7 + float2(t * 0.9, -t * 0.6)) * fade;
                        wl = 0.5 + nw.x + 0.35 * boil.x;
                        wg = white * (nw.y * 0.16 * fd + nw.z * 0.42 * sd + 0.25 * mul(boil.yz, Turn));
                    }
                }
                else if (dot(flow, flow) > 0.01)
                {
                    float2 drift = flow * (speed * FlowPeriod * wv.x);
                    float2 a = Waves(q - drift * ph, t, fade);
                    float2 b = Waves(q - drift * frac(ph + 0.5) + 0.37, t, fade);
                    g = lerp(a, b, wab);
                }
                else g = Waves(q, t, fade);
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
                float3 trans = exp(log(max(_StabbyWaterShallow.rgb, 0.01)) * (path * _StabbyWaterDeep.w));

                Light l = GetMainLight();
                float3 sky = _StabbySky.rgb;
                float3 tint = lerp(_StabbyWaterShallow.rgb * float3(0.2, 0.3, 0.33), _StabbyWaterDeep.rgb, saturate(path * _StabbyWaterDeep.w / 6));
                float3 scatter = LookColor(tint, 1.3) * (l.color * saturate(l.direction.y + 0.2) * 0.6 + sky * 0.5);
                float3 c = under * trans + scatter * (1 - trans);
                float ndv = saturate(dot(n, v));
                float fres = 0.02 + 0.98 * pow(1 - ndv, 5);
                float3 r = reflect(-v, n);
                float3 skyR = lerp(sky * 1.35, sky * float3(0.8, 0.88, 1.05), sqrt(saturate(r.y))) + l.color * pow(saturate(dot(r, l.direction)), 6) * 0.25;
                c = lerp(c, skyR, fres);
                c = lerp(c, skyR, slick * 0.2 * (1 - ndv));
                float e = lerp(900, 60, saturate(dist / 400));
                float3 h = normalize(l.direction + v);
                c += l.color * pow(saturate(dot(n, h)), e) * ((e + 8) / 25.13 * 0.05);

                float2 sl = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, fuv, 0).rg;
                float shore = depth / max(length(sl), 0.03);
                float band = saturate(1 - shore / max(wv.w, 0.01)) * (river ? 0.25 + 0.5 * churn : 1);
                float foam = 0;
                UNITY_BRANCH if (band > 0)
                {
                    float2 fp = i.positionWS.xz * 0.35;
                    float lace = 0.5 + Noise(mul(Turn, fp) + float2(t * 0.3, -t * 0.2)).x + 0.5 * Noise(mul(fp, Turn) * 2.7 - float2(t * 0.4, t * 0.1)).x;
                    float fine = Noise(mul(Turn, fp) * 7.3 + float2(t * 0.5, t * 0.7)).x;
                    lace = lerp(0.6, lace + 0.35 * fine * saturate(2 - dist / 120), saturate(2 - dist / 250));
                    float surf = 0.5 + 0.5 * sin(6.2832 * (shore / max(wv.w * 0.5, 0.01) + _Time.y * 0.15) + lace * 1.5);
                    float b = band * band * (3 - 2 * band);
                    float broad = 0.8 + 0.5 * Noise(mul(fp, Turn) * 0.23 + 3.7).x;
                    float rim = smoothstep(0.5 - 0.3 * saturate(lace * broad - 0.3), 0.85, band);
                    foam = max(rim * (0.8 + 0.4 * fine), smoothstep(0.35, 0.95, b * lace * broad)) + 0.3 * b * surf * surf * saturate(lace - 0.4);
                }
                foam = max(foam, white * smoothstep(0.5, 1.0, wl));
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
                foam = max(foam, 0.22 * streak);
                c = lerp(c, l.color * 0.9 + sky * 0.2, foam * 0.7);

                float alpha = saturate(max(saturate(depth * _StabbyWaterShallow.w), foam * 0.7 * saturate(depth / 0.15)));
                float clear = dot(trans, 1.0 / 3) * (1 - fres) * (1 - foam * 0.7);
                return half4(lerp(MixFog(c, i.fog), c, clear), alpha);
            }
            ENDHLSL
        }
    }
}
