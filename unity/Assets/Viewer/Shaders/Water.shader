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
            #include "Assets/Viewer/Shaders/Look.hlsl"

            float4 _StabbySky, _StabbyGround, _StabbyFieldUV, _StabbySand;
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

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; };

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
                float sd = max(0, i.positionWS.y - bed.y);
                float miss = smoothstep(1, 4, (sd - field) / (1 + 0.25 * field));
                float depth = lerp(sd, field, max(far, miss));

                float4 wv = _StabbyWaterWave;
                float t = _Time.y * wv.y;
                float2 q = i.positionWS.xz * wv.x;
                float fade = saturate(1.5 - dist * wv.x / 120);
                float2 flow = i.color.rg * 2 - 1;
                float slope = i.color.b * SlopeRange;
                float rapids = smoothstep(1, 2.25, slope * _StabbyWaterFlow.y);
                float2 g;
                UNITY_BRANCH if (dot(flow, flow) > 0.01)
                {
                    float ph = frac(_Time.y / FlowPeriod);
                    float2 drift = flow * (_StabbyWaterFlow.x * (0.8 + 20 * slope) * FlowPeriod * wv.x);
                    float2 a = Waves(q - drift * ph, t, fade);
                    float2 b = Waves(q - drift * frac(ph + 0.5) + 0.37, t, fade);
                    g = lerp(a, b, abs(1 - 2 * ph));
                }
                else g = Waves(q, t, fade);
                g *= wv.z * (0.35 + 0.65 * fade) * (1 + rapids);
                float swell = i.color.a >= 0.99 ? 1 : i.color.a * 2 * _StabbyWaterFlow.z;
                UNITY_BRANCH if (swell > 0)
                {
                    float2 sq = mul(Turn, i.positionWS.xz * _StabbyWaterSwell.x) + float2(_Time.y * 0.05, _Time.y * 0.02);
                    g += swell * _StabbyWaterSwell.y * mul(Noise(sq).yz, Turn);
                }
                float3 n = normalize(float3(-g.x, 1, -g.y));

                float k = saturate(depth * _StabbyWaterDeep.w);
                float3 body = LookColor(lerp(_StabbyWaterShallow.rgb, _StabbyWaterDeep.rgb, k * (2 - k)), 1.3);

                Light l = GetMainLight();
                float3 sky = _StabbySky.rgb;
                float ndv = saturate(dot(n, v));
                float fres = 0.02 + 0.98 * pow(1 - ndv, 5);
                float3 c = body * (l.color * saturate(dot(n, l.direction)) * 0.6 + sky * 0.5);
                c = lerp(c, sky, fres * 0.8);
                float3 h = normalize(l.direction + v);
                c += l.color * pow(saturate(dot(n, h)), 240) * 1.5;

                float2 sl = SAMPLE_TEXTURE2D_LOD(_StabbySlope, sampler_StabbySlope, fuv, 0).rg;
                float shore = depth / max(length(sl), 0.03);
                float band = saturate(1 - shore / max(wv.w, 0.01));
                float foam = 0;
                UNITY_BRANCH if (band + rapids > 0)
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
                    foam = max(foam, rapids * smoothstep(0.55, 1.05, lace));
                }
                c = lerp(c, l.color * 0.9 + sky * 0.2, foam * 0.7);

                float alpha = lerp(lerp(0.8, 0.97, k), 1, fres * 0.6) * saturate(depth * _StabbyWaterShallow.w);
                alpha = saturate(max(alpha, foam * 0.7 * saturate(depth / 0.15)));
                float3 sand = _StabbySand.rgb;
                sand = sand <= 0.04045 ? sand / 12.92 : pow((sand + 0.055) / 1.055, 2.4);
                float3 floorC = sand * lerp(0.62, 0.45, saturate(depth / 30)) * (l.color * saturate(l.direction.y) + lerp(_StabbyGround.rgb, sky, 0.75));
                c = lerp(c, lerp(floorC, c, alpha), miss);
                alpha = lerp(alpha, 1, miss);
                return half4(MixFog(c, i.fog), alpha);
            }
            ENDHLSL
        }
    }
}
