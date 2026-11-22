Shader "Stabby/FallMist"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Game/Grass/Wind.hlsl"

            float4 _StabbySky;
            float _StabbyMistOff;
            float4 _StabbyMistPx;

            static const float2x2 Turn = float2x2(0.8, -0.6, 0.6, 0.8);
            static const float SprayPeriod = 2.6, MistPeriod = 8, MistRise = 0.7, MistReach = 2.2, WindDrift = 1.6;

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

            float ScreenPx(float size, float dist) { return size / max(dist, 0.01) * abs(UNITY_MATRIX_P[1][1]) * 540; }

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 size : TEXCOORD0; float4 corner : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; float4 puff : TEXCOORD3; float4 corner : TEXCOORD4; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float2 fd = normalize(i.color.rg * 2 - 1);
                float3 f3 = float3(fd.x, 0, fd.y), s3 = float3(fd.y, 0, -fd.x);
                float ph = i.color.a, kind = i.corner.z, tm = _Time.y;
                float2 c = i.corner.xy, wind = WindPush(wp.xz, tm, ph);
                float4 puff;
                UNITY_BRANCH if (kind < 1.5)
                {
                    float rad = i.size.x, h = i.size.y;
                    bool spray = kind < 0.5;
                    float period = (spray ? SprayPeriod : MistPeriod) * (0.75 + 0.5 * frac(ph * 7.31));
                    float t = frac(tm / period + ph);
                    float3 dr = Noise(float2(ph * 17.3, tm * 0.15 + ph * 5.1));
                    float rise = spray ? rad * (0.1 + 0.8 * t * (2 - t)) : rad * 0.3 + min(h * MistRise, rad * MistReach) * t;
                    float ahead = spray ? rad * (0.2 + 0.9 * t) : rad * (0.5 * t + 0.15 * dr.x);
                    wp += float3(0, rise, 0) + f3 * ahead + s3 * (rad * 0.35 * dr.y) + float3(wind.x, 0, wind.y) * (rad * WindDrift * t);
                    float size = rad * (spray ? 0.45 + 0.6 * t : 0.6 + 0.7 * t);
                    float px = ScreenPx(rad, length(_WorldSpaceCameraPos - wp));
                    size *= px < _StabbyMistPx.x ? 0 : 1;
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                    wp += (right * c.x + up * c.y) * size;
                    puff = float4(t, smoothstep(0, 0.15, t) * (1 - smoothstep(0.45, 1, t)), rad, kind);
                }
                else
                {
                    float hw = i.size.x, tall = i.size.y, vy = c.y * 0.5 + 0.5;
                    float px = ScreenPx(tall, length(_WorldSpaceCameraPos - wp));
                    wp += (s3 * (c.x * hw) + float3(0, vy * tall, 0) + float3(wind.x, 0, wind.y) * (vy * vy * tall * 0.4)) * (px < _StabbyMistPx.x ? 0 : 1);
                    puff = float4(hw / max(tall, 0.01), 1, tall, kind);
                }
                float4 cs = TransformWorldToHClip(wp);
                o.positionCS = _StabbyMistOff > 0 ? float4(2, 2, 2, 1) : cs;
                o.positionWS = wp;
                o.color = i.color;
                o.fog = ComputeFogFactor(cs.z);
                o.screen = ComputeScreenPos(cs);
                o.puff = puff;
                o.corner = float4(c, 0, 0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 uv = i.screen.xy / i.screen.w, p = i.corner.xy;
                float kind = i.puff.w, rad = i.puff.z, ph = i.color.a, flowQ = i.color.b, tm = _Time.y, e2 = dot(p, p);
                float n, dens, op;
                UNITY_BRANCH if (kind < 1.5)
                {
                    UNITY_BRANCH if (e2 >= 1 || i.puff.y <= 0) return half4(0, 0, 0, 0);
                    float t = i.puff.x;
                    float2 q = p * 1.4 + ph * 31.7 + float2(tm * 0.13, -tm * 0.21 - t * 1.5);
                    float3 n1 = Noise(q);
                    float2 pw = p + 0.25 * n1.yz;
                    float3 n2 = Noise(mul(Turn, pw) * 2.9 + ph * 13.1 + float2(-tm * 0.37, tm * 0.19));
                    n = n1.x + 0.5 * n2.x;
                    UNITY_BRANCH if (kind < 0.5) n += 0.3 * Noise(mul(pw, Turn) * 6.3 + float2(ph * 5.3, -tm * 0.9)).x;
                    dens = saturate((0.7 - dot(pw, pw) + 1.2 * n) * 1.6) * saturate((1 - e2) * 2.5);
                    dens *= dens * i.puff.y;
                    op = kind < 0.5 ? 0.5 + 0.25 * flowQ : 0.3 + 0.15 * flowQ;
                }
                else
                {
                    float vy = p.y * 0.5 + 0.5;
                    float2 q = float2(p.x * 2.4 * i.puff.x, vy * 2.4) + ph * 7.1 + float2(0, -tm * 0.7);
                    float3 n1 = Noise(q);
                    float3 n2 = Noise(float2(q.x * 2.3 + 0.5 * n1.y, q.y * 1.4 - tm * 0.5) + 5.1);
                    n = n1.x + 0.55 * n2.x;
                    float edge = 1 - p.x * p.x * p.x * p.x;
                    dens = saturate((1 - vy) * (1 - vy) * 1.3 - 0.25 + 1.1 * n) * edge * smoothstep(0, 0.1, vy);
                    float2 fd = normalize(i.color.rg * 2 - 1);
                    op = (0.3 + 0.15 * flowQ) * saturate(abs(dot(v.xz, fd)) * 1.6 + 0.15);
                }
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surface = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float soft = saturate((scene - surface) / min(rad * 0.35, 2.5));
                float near = saturate((surface - _ProjectionParams.y - 0.5) / min(rad, 6));
                float px = smoothstep(_StabbyMistPx.x, _StabbyMistPx.y, ScreenPx(rad, dist));
                Light l = GetMainLight();
                float sun = saturate(l.direction.y * 2 + 0.1);
                float fwd = pow(saturate(dot(-v, l.direction)), 4);
                float3 c = _StabbySky.rgb * 0.9 + 0.12 + l.color * sun * (0.6 + 0.9 * fwd);
                c *= (0.88 + 0.12 * saturate(n * 0.5 + 0.5)) * (1 - 0.18 * dens);
                return half4(MixFog(c, i.fog), dens * soft * near * px * op);
            }
            ENDHLSL
        }
    }
}
