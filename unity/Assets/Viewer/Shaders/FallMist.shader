Shader "Stabby/FallMist"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Game/Grass/Wind.hlsl"
        #include "Assets/Viewer/Shaders/Clock.hlsl"

        float4 _StabbySky;
        float _StabbyMistOff;
        float4 _StabbyMistPx;
        float4 _StabbyColumnPx;
        float4 _StabbyShadowPx;
        float4 _StabbyShadowEye;
        float _StabbyClock;
        float4 _StabbyLiquid[160];

        static const float2x2 Turn = float2x2(0.8, -0.6, 0.6, 0.8);
        static const float SprayPeriod = 2.6, MistPeriod = 8, MistRise = 0.7, MistReach = 2.2, WindDrift = 1.6, SteamPeriod = 6.5, SteamOpacity = 0.14, JetPeriod = 1.7, ColumnSteamPeriod = 5.5, JetRamp = 1.5, SteamLinger = 9, JetOpacity = 0.75, JetStretch = 3.2, ColumnSteamOpacity = 0.3, JetCover = 0.1, SteamCover = 0.12;

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

        float ScreenPx(float size, float dist, float focal) { return size / max(dist, 0.01) * focal; }

        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 size : TEXCOORD0; float4 corner : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

        float3 Place(Attributes i, float3 eye, float focal, float3 right, float3 up, out float4 puff)
        {
            float3 wp = TransformObjectToWorld(i.positionOS.xyz);
            float2 fd = normalize(i.color.rg * 2 - 1);
            float3 f3 = float3(fd.x, 0, fd.y), s3 = float3(fd.y, 0, -fd.x);
            float ph = i.color.a, kind = i.corner.z, tm = StabbyTime;
            float2 c = i.corner.xy, wind = WindPush(wp.xz, tm, ph);
            UNITY_BRANCH if (kind > 3.5)
            {
                float rad = i.size.x, h = i.size.y, interval = i.corner.w, dur = i.color.r * interval;
                bool jet = i.color.b < 0.5;
                float te = frac(_StabbyClock / interval - i.color.g) * interval;
                float burst = smoothstep(0, JetRamp, te) * (1 - smoothstep(dur * 0.65, dur, te));
                float cloud = smoothstep(0, JetRamp * 2, te) * (1 - smoothstep(dur, dur + SteamLinger, te));
                float3 dr = Noise(float2(ph * 17.3, tm * 0.3 + ph * 5.1));
                float t, size, env, stretch = 1;
                if (jet)
                {
                    t = frac(tm / JetPeriod + ph);
                    float top = h * (0.35 + 0.65 * burst);
                    wp += float3(rad * 0.25 * dr.y * t, top * t * (1.5 - 0.5 * t), rad * 0.25 * dr.z * t) + float3(wind.x, 0, wind.y) * (top * 0.15 * t * t);
                    size = rad * (0.65 + 0.55 * t);
                    stretch = 1 + JetStretch * (1 - t);
                    env = burst * smoothstep(0, 0.03, t) * (1 - smoothstep(0.75, 1, t));
                }
                else
                {
                    t = frac(tm / (ColumnSteamPeriod * (0.8 + 0.4 * frac(ph * 7.31))) + ph);
                    wp += float3(rad * 1.2 * dr.y, h * (0.15 + 1.0 * t), rad * 1.2 * dr.z) + float3(wind.x, 0, wind.y) * (h * WindDrift * 0.5 * t * t);
                    size = rad * (1.3 + 2.2 * t);
                    env = cloud * smoothstep(0, 0.12, t) * (1 - smoothstep(0.4, 1, t));
                }
                float camD = length(eye - wp), px = ScreenPx(h, camD, focal);
                env *= smoothstep(_StabbyColumnPx.x, _StabbyColumnPx.y, px);
                size = min(size, camD * (jet ? JetCover : SteamCover)) * (env > 0.001 ? 1 : 0);
                wp += (right * c.x + up * c.y * stretch) * size;
                puff = float4(t, env, size, kind);
            }
            else if (kind > 2.5)
            {
                float rad = i.size.x, h = i.size.y;
                float period = SteamPeriod * (0.8 + 0.4 * frac(ph * 7.31));
                float t = frac(tm / period + ph);
                float3 dr = Noise(float2(ph * 17.3, tm * 0.2 + ph * 5.1));
                wp += float3(rad * 0.4 * dr.y, rad * 0.2 + h * t * (1.2 - 0.2 * t), rad * 0.4 * dr.z) + float3(wind.x, 0, wind.y) * (h * WindDrift * t * t);
                float size = rad * (0.35 + 0.6 * t);
                float px = ScreenPx(rad, length(eye - wp), focal);
                size *= px < _StabbyMistPx.z ? 0 : 1;
                wp += (right * c.x + up * c.y) * size;
                puff = float4(t, smoothstep(0, 0.2, t) * (1 - smoothstep(0.35, 1, t)), rad, kind);
            }
            else
            {
                float rad = i.size.x * (0.65 + 0.7 * frac(ph * 13.7)), h = i.size.y;
                bool spray = kind < 0.5;
                float period = (spray ? SprayPeriod : MistPeriod) * (0.6 + 0.8 * frac(ph * 7.31));
                float cy = tm / period + ph, t = frac(cy), k = floor(cy) * 3.71 + ph * 17.3;
                float ease = t * (2 - t), lift = 0.6 + 0.8 * frac(ph * 3.17 + k * 0.37);
                float d1 = Noise(float2(k, t * 1.7 + ph * 5.1)).x, d2 = Noise(float2(k * 1.9 + 4.3, t * 3.9 + 2.2)).x, d3 = Noise(float2(k * 0.7 - 2.9, t * 2.6 + 7.7)).x;
                float rise = spray ? rad * (0.1 + 0.8 * ease) * lift : rad * 0.2 + min(h * MistRise, rad * MistReach) * ease * lift;
                float ahead = spray ? rad * (0.2 + 0.9 * t + 0.4 * d1) : rad * (0.4 * ease + 0.5 * d1);
                float2 drift = _StabbyWind.xy * (_StabbyWind.z + _StabbyGust.z * WindGust(wp.xz + ph * 9.1, tm));
                wp += float3(0, rise + rad * 0.5 * d3 * t, 0) + f3 * ahead + s3 * (rad * 0.9 * d2 * (0.3 + t)) + float3(drift.x, 0, drift.y) * (rad * WindDrift * t);
                float size = rad * (spray ? 0.45 + 0.6 * t : 0.5 + 0.8 * ease);
                float px = ScreenPx(rad, length(eye - wp), focal);
                size *= px > _StabbyMistPx.x ? 1 : 0;
                wp += (right * c.x + up * c.y) * size;
                puff = float4(t, smoothstep(0, 0.15, t) * (1 - smoothstep(0.4, 1, t)), rad, kind);
            }
            return wp;
        }

        bool Empty(float4 puff, float2 p) { return dot(p, p) >= 1 || puff.y <= 0; }

        float Density(float4 puff, float4 color, float2 p, out float n, out float op)
        {
            float kind = puff.w, ph = color.a, flowQ = color.b, tm = StabbyTime, e2 = dot(p, p);
            float dens;
            float t = puff.x;
            float2 q = p * 1.4 + ph * 31.7 + float2(tm * 0.13, -tm * 0.21 - t * 1.5);
            float3 n1 = Noise(q);
            float2 pw = p + 0.25 * n1.yz;
            n = n1.x;
            UNITY_BRANCH if (kind < 3.5) n += 0.5 * Noise(mul(Turn, pw) * 2.9 + ph * 13.1 + float2(-tm * 0.37, tm * 0.19)).x;
            UNITY_BRANCH if (kind < 1.5) n += (kind < 0.5 ? 0.3 : 0.4) * Noise(mul(pw, Turn) * 6.3 + float2(ph * 5.3, -tm * 0.9)).x;
            dens = kind > 2.5 && kind < 3.5
                ? saturate((1.3 * n - 0.2 - 0.9 * dot(pw, pw)) * 2.2) * saturate((1 - e2) * 4)
                : kind > 0.5 && kind < 1.5 ? saturate((0.55 - 1.1 * dot(pw, pw) + 1.5 * n) * 1.5) * saturate((1 - e2) * 2.5)
                : saturate((0.7 - dot(pw, pw) + 1.2 * n) * 1.6) * saturate((1 - e2) * 2.5);
            dens *= dens * puff.y;
            op = kind > 3.5 ? (flowQ < 0.5 ? JetOpacity : ColumnSteamOpacity) : kind > 2.5 ? SteamOpacity : kind < 0.5 ? 0.4 + 0.2 * flowQ : 0.3 + 0.14 * flowQ;
            return dens;
        }

        float WindowPx(float4 puff, float dist, float focal)
        {
            float2 win = puff.w > 2.5 ? _StabbyMistPx.zw : _StabbyMistPx.xy;
            return puff.w > 3.5 ? 1 : smoothstep(win.x, win.y, ScreenPx(puff.z, dist, focal));
        }
        ENDHLSL

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; float4 puff : TEXCOORD3; float4 corner : TEXCOORD4; };

            float Focal() { return abs(UNITY_MATRIX_P[1][1]) * 540; }

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float4 puff;
                float3 wp = Place(i, _WorldSpaceCameraPos, Focal(), UNITY_MATRIX_V[0].xyz, UNITY_MATRIX_V[1].xyz, puff);
                float4 cs = TransformWorldToHClip(wp);
                o.positionCS = _StabbyMistOff > 0 ? float4(2, 2, 2, 1) : cs;
                o.positionWS = wp;
                o.color = i.color;
                o.fog = ComputeFogFactor(cs.z);
                o.screen = ComputeScreenPos(cs);
                o.puff = puff;
                o.corner = float4(i.corner.xy, i.corner.w, 0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 uv = i.screen.xy / i.screen.w, p = i.corner.xy;
                float kind = i.puff.w, rad = i.puff.z, flowQ = i.color.b;
                UNITY_BRANCH if (Empty(i.puff, p)) return half4(0, 0, 0, 0);
                float n, op;
                float dens = Density(i.puff, i.color, p, n, op);
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surface = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float soft = saturate((scene - surface) / min(rad * 0.35, 2.5));
                float near = saturate((surface - _ProjectionParams.y - 0.5) / min(rad, 6));
                float px = WindowPx(i.puff, dist, Focal());
                Light l = GetMainLight();
                float sun = saturate(l.direction.y * 2 + 0.1);
                float fwd = pow(saturate(dot(-v, l.direction)), 4);
                float3 c = _StabbySky.rgb * 0.9 + 0.12 + l.color * sun * (0.6 + 0.9 * fwd);
                c *= (0.88 + 0.12 * saturate(n * 0.5 + 0.5)) * (1 - 0.18 * dens);
                c *= kind > 3.5 && flowQ < 0.5 ? 1.08 : 1;
                c *= kind < 2.5 ? _StabbyLiquid[clamp((int)round(i.corner.z), 0, 15) * 10 + 9].rgb : 1;
                return half4(MixFog(c, i.fog), dens * soft * near * px * op);
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

            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float4 puff : TEXCOORD1; float3 corner : TEXCOORD2; };

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
                float4 puff;
                float focal = _StabbyShadowPx.x;
                float3 eye = _StabbyShadowEye.xyz;
                float3 wp = Place(i, eye, focal, UNITY_MATRIX_V[0].xyz, UNITY_MATRIX_V[1].xyz, puff);
                float fade = ShadowTexelFade(puff.z, length(eye - wp));
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, _LightDirection, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = _StabbyMistOff > 0 || focal <= 0 || _StabbyShadowPx.w <= 0 ? float4(0, 0, -2, 1) : cs;
                o.positionWS = wp;
                o.color = i.color;
                o.puff = puff;
                o.corner = float3(i.corner.xy, fade);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.corner.xy;
                clip(Empty(i.puff, p) ? -1 : i.corner.z - 0.001);
                float n, op;
                float dens = Density(i.puff, i.color, p, n, op);
                float px = WindowPx(i.puff, length(_StabbyShadowEye.xyz - i.positionWS), _StabbyShadowPx.x);
                clip(saturate(dens * op * px * i.corner.z * _StabbyShadowPx.w) - Bayer(i.positionCS.xy));
                return 0;
            }
            ENDHLSL
        }
    }
}
