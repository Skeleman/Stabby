Shader "Stabby/Foliage"
{
    Properties
    {
        _BaseColor ("Bark", Color) = (1, 1, 1, 3)
        _Leaf ("Leaf", Vector) = (0.2, 0.3, 0.1, 2)
        _Fade ("Fade", Vector) = (1, 1, 0, 1)
        _LeafAtlas ("Leaf atlas", 2D) = "white" {}
        _Imp ("Impostor blend", Float) = 0
        _ImpGrid ("Impostor grid", Vector) = (1, 1, 8, 0)
        _ImpA ("Impostor albedo", 2D) = "black" {}
        _ImpB ("Impostor normal", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Game/Grass/Wind.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _Leaf;
        float4 _Fade;
        float4 _ImpGrid;
        float _Imp;
        CBUFFER_END

        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
            UNITY_DOTS_INSTANCED_PROP(float4, _Leaf)
            UNITY_DOTS_INSTANCED_PROP(float4, _Fade)
            UNITY_DOTS_INSTANCED_PROP(float, _Imp)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
        #define _Leaf UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Leaf)
        #define _Fade UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Fade)
        #define _Imp UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Imp)
        #endif

        float _StabbyFoliageOff;
        TEXTURE2D(_LeafAtlas);
        SAMPLER(sampler_LeafAtlas);
        TEXTURE2D(_ImpA);
        TEXTURE2D(_ImpB);
        SAMPLER(sampler_ImpA);

        float Bayer(float2 p)
        {
            uint2 q = (uint2)p & 3u;
            static const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[(q.x << 2) | q.y] + 0.5) / 16.0;
        }

        struct FoliageIn
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 uv : TEXCOORD0;
            float4 wind : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 ImpostorVertex(FoliageIn i, out float2 uv)
        {
            float3 c = i.wind.xyz;
            float s = i.wind.w;
            float3 cam = TransformWorldToObject(_WorldSpaceCameraPos);
            float2 dv = cam.xz - c.xz;
            dv = dot(dv, dv) > 1e-6 ? normalize(dv) : float2(0, 1);
            float frames = _ImpGrid.z;
            float f = fmod(round(atan2(dv.x, dv.y) * frames / 6.2831853) + frames, frames);
            float cell = i.uv.z * frames + f;
            float cols = _ImpGrid.x;
            float2 at = float2(fmod(cell, cols), floor(cell / cols));
            uv = (at + 0.5 + 0.5 * _ImpGrid.y * i.uv.xy) / cols;
            float3 right = float3(-dv.y, 0, dv.x);
            return TransformObjectToWorld(c + (right * i.uv.x + float3(0, i.uv.y, 0)) * 0.5 * s);
        }

        float3 FoliageWind(FoliageIn i, out float3 normalWS)
        {
            float3 wp = TransformObjectToWorld(i.positionOS.xyz);
            normalWS = TransformObjectToWorldNormal(i.normalOS);
            float w = _Leaf.w;
            float sway = saturate(w), flutter = saturate(w - 1);
            [branch] if (sway <= 0) return wp;
            float3 root = UNITY_MATRIX_M._m03_m13_m23;
            float size = max(_Fade.w, 0.1);
            float seed = frac(dot(root.xz, float2(0.1373, 0.2719)));
            float t = _Time.y;
            float hz = _StabbyWind.w * rsqrt(max(size, 0.5)) * 1.6;
            float2 push = WindPush(root.xz, t, seed);
            float slow = sin(6.2831853 * (hz * t + seed));
            float h = saturate(i.positionOS.y);
            float2 trunk = push * (0.75 + 0.25 * slow) * (h * h) * size * 0.05;
            float gust = WindGust(root.xz, t);
            float2 dir = _StabbyWind.xy;
            float2 side = float2(-dir.y, dir.x);
            float bend = i.wind.x;
            float bph = 6.2831853 * (hz * 2.3 * t + i.wind.z);
            float amp = size * (0.012 + 0.03 * (_StabbyWind.z + _StabbyGust.z * gust));
            float2 limb = (dir * (0.6 + 0.4 * sin(bph)) + side * 0.5 * sin(bph * 0.73 + 1.7)) * bend * amp;
            float bob = sin(bph * 1.1 + 0.6) * bend * amp * 0.35;
            float3 off = float3(trunk.x + limb.x, bob - 0.5 * dot(trunk, trunk) / max(size, 0.1), trunk.y + limb.y) * sway;
            [branch] if (flutter > 0 && i.uv.w > 0.5 && i.uv.w < 1.5)
            {
                float fph = 6.2831853 * (t * (3.1 + 1.7 * i.wind.w) + i.wind.w * 11.0);
                float fl = i.wind.y * flutter * size * (0.004 + 0.012 * gust) * (sin(fph) + 0.5 * sin(fph * 2.37 + 1.3));
                off += normalWS * fl;
            }
            return wp + off;
        }
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            AlphaToMask On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _StabbySky, _StabbyGround, _StabbySand;
            float _StabbyStrataPeriod;
            TEXTURE2D(_StabbyStrata);
            SAMPLER(sampler_StabbyStrata);

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

            #include "Assets/Viewer/Shaders/TerrainMaterials.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 uv : TEXCOORD2;
                float fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(FoliageIn i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 n = 0;
                float3 wp;
                o.uv = i.uv;
                [branch] if (i.uv.w > 1.5)
                {
                    float2 uv;
                    wp = ImpostorVertex(i, uv);
                    o.uv.xy = uv;
                }
                else wp = FoliageWind(i, n);
                o.positionWS = wp;
                o.positionCS = _StabbyFoliageOff > 0 ? float4(0, 0, -2, 1) : TransformWorldToHClip(wp);
                o.normalWS = n;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float BarkFade(float freq, float fp) { return saturate(2 - 4 * freq * fp); }

            float2 SmoothD(float e0, float e1, float x)
            {
                float t = saturate((x - e0) / (e1 - e0));
                return float2(t * t * (3 - 2 * t), 6 * t * (1 - t) / (e1 - e0));
            }

            float3 OakBark(float2 m, float fp, out float tone)
            {
                float3 w = TmNoise2(m * float2(2.2, 0.5) + 3.7);
                float2 q = float2(m.x * 11 + 1.1 * w.x, m.y * 1.3);
                float3 a = TmNoise2(q);
                float2 s = SmoothD(0.03, 0.3, abs(a.x));
                float2 dq = s.y * sign(a.x) * a.yz;
                float2 g = float2(dq.x * (11 + 2.42 * w.y), dq.x * 0.55 * w.z + dq.y * 1.3);
                float h = s.x;
                float fc = BarkFade(9, fp);
                float3 c = TmNoise2(m * float2(3, 7) + 11.3);
                float2 k = SmoothD(0.8, 0.96, 1 - abs(c.x));
                h -= 0.5 * fc * s.x * k.x;
                g += 0.5 * fc * s.x * k.y * sign(c.x) * c.yz * float2(3, 7);
                float ff = BarkFade(28, fp);
                float3 f = TmNoise2(m * float2(28, 12) + 5.1);
                h += 0.12 * ff * f.x;
                g += 0.12 * ff * f.yz * float2(28, 12);
                tone = h;
                return float3(h, g * BarkFade(11, fp));
            }

            float3 SpruceBark(float2 m, float fp, out float tone, out float fresh)
            {
                float3 w = TmNoise2(m * float2(1.3, 0.6) + 9.1);
                float2 q = m * float2(8, 5.5) + 0.35 * w.x;
                float row = floor(q.y);
                q.x += TmHash(int2((int)row, 71));
                float2 cell = floor(q), f = q - cell;
                float r = TmHash((int2)cell + int2(0, 1931));
                float2 lx = SmoothD(0, 0.1, min(f.x, 1 - f.x)), ly = SmoothD(0, 0.16, min(f.y, 1 - f.y));
                lx.y *= sign(0.5 - f.x);
                ly.y *= sign(0.5 - f.y);
                float tilt = 0.55 + 0.45 * (1 - f.y) * (0.5 + r);
                float h = lx.x * ly.x * tilt;
                float2 df = float2(lx.y * ly.x * tilt, lx.x * ly.y * tilt - lx.x * ly.x * 0.45 * (0.5 + r));
                float2 wg = 0.35 * w.yz * float2(1.3, 0.6);
                float2 g = df.x * (float2(8, 0) + wg) + df.y * (float2(0, 5.5) + wg);
                float ff = BarkFade(14, fp);
                float3 fn = TmNoise2(m * float2(14, 9) + 2.3);
                h += 0.08 * ff * fn.x;
                g += 0.08 * ff * fn.yz * float2(14, 9);
                tone = lx.x * ly.x * (0.8 + 0.35 * r) + 0.1 * fn.x;
                fresh = smoothstep(0.72, 0.95, r) * lx.x * ly.x;
                return float3(h, g * BarkFade(8, fp));
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                float dither = Bayer(i.positionCS.xy);
                clip(fade.x - dither);
                float size = max(fade.w, 0.1);
                float3 wp = i.positionWS;
                bool imp = i.uv.w > 1.5;
                clip(imp ? _Imp - dither : dither - _Imp);
                float3 n = imp ? 0 : normalize(i.normalWS);
                float3 v = normalize(_WorldSpaceCameraPos - wp);
                bool leaf = i.uv.w > 0.5;
                float3 albedo;
                float trans = 0, ao = 1;
                float alpha = 1;
                float2 m = i.uv.xy * size, dm1 = ddx(m), dm2 = ddy(m);
                float3 dp1 = ddx(wp), dp2 = ddy(wp);
                Light l = GetMainLight(TransformWorldToShadowCoord(wp));
                float above = max(wp.y - fade.z, 0);
                [branch] if (imp)
                {
                    float4 a = SAMPLE_TEXTURE2D(_ImpA, sampler_ImpA, i.uv.xy);
                    clip(a.a - 0.4);
                    float4 b = SAMPLE_TEXTURE2D(_ImpB, sampler_ImpA, i.uv.xy) / a.a;
                    a.rgb /= a.a;
                    n = normalize(TransformObjectToWorldNormal(b.rgb * 2 - 1));
                    float shade = a.r * 1.5;
                    float3 leafColor = _Leaf.rgb * (1 + (a.g - 0.5) * float3(0.9, 0.35, -0.7));
                    albedo = lerp(leafColor, _BaseColor.rgb * 1.3, a.b) * shade;
                    trans = 1 - a.b;
                    ao = b.a;
                }
                else if (leaf)
                {
                    float4 a = SAMPLE_TEXTURE2D(_LeafAtlas, sampler_LeafAtlas, i.uv.xy);
                    alpha = a.a;
                    clip(a.a - 0.5);
                    float shade = a.r * 1.5;
                    float hue = a.g - 0.5;
                    float3 leafColor = _Leaf.rgb * (1 + hue * float3(0.9, 0.35, -0.7));
                    albedo = lerp(leafColor * shade, _BaseColor.rgb * shade * 1.3, a.b);
                    trans = 1 - a.b;
                    ao = i.uv.z;
                }
                else
                {
                    float fp = max(length(dm1), length(dm2));
                    bool spruce = i.uv.w > 0.125;
                    float tone, fresh = 0;
                    float3 b = spruce ? SpruceBark(m, fp, tone, fresh) : OakBark(m, fp, tone);
                    float3 lw = float3(0.2126, 0.7152, 0.0722);
                    albedo = _BaseColor.rgb * (spruce ? 0.62 + 0.5 * tone : 0.5 + 0.6 * tone);
                    albedo = spruce ? lerp(albedo, albedo * float3(1.35, 0.95, 0.75), fresh) : lerp(albedo, dot(albedo, lw) * float3(1.04, 1, 0.94), 0.4 * saturate(tone));
                    float moss = 0;
                    [branch] if (above < 2.6)
                    {
                        float3 sideN = normalize(float3(n.x, 0, n.z) + 1e-4), ld = normalize(float3(l.direction.x, 0, l.direction.z) + 1e-4);
                        float shadeSide = 0.5 - 0.5 * dot(sideN, ld);
                        float4 mn = TmNoise3(wp * 2.3, int3(0, 0, 0)), mf = TmNoise3(wp * 9.1 + 17.3, int3(0, 0, 0));
                        float reach = lerp(0.25, 1.6, shadeSide) + 0.45 * mn.x;
                        moss = saturate((reach - above) * 3 + 0.5 * mf.x) * smoothstep(0.15, 0.6, shadeSide + 0.35 * mn.x) * (spruce ? 0.8 : lerp(1.3, 0.7, saturate(tone)));
                        moss = saturate(moss);
                        albedo = lerp(albedo, float3(0.15, 0.19, 0.065) * (0.85 + 0.3 * mf.x), moss);
                    }
                    float4 lc = TmNoise3(wp * 4.7 + 31.1, int3(0, 0, 0));
                    float lic = smoothstep(0.42, 0.62, lc.x) * (spruce ? 0.35 : 0.55) * (1 - moss) * BarkFade(4.7, fp);
                    albedo = lerp(albedo, dot(albedo, lw) * float3(0.9, 1.03, 0.78) * (2.1 + 0.3 * lc.y), 0.6 * lic);
                    float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
                    float3 T = dp2perp * dm1.x + dp1perp * dm2.x, B = dp2perp * dm1.y + dp1perp * dm2.y;
                    float inv = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
                    float depth = (spruce ? 0.012 : 0.01) * (1 - 0.6 * moss);
                    n = normalize(n - depth * inv * (T * b.y + B * b.z));
                    ao = i.uv.z * (spruce ? 0.8 + 0.2 * tone : 0.7 + 0.3 * saturate(tone));
                }
                float groundAo = lerp(0.6, 1, smoothstep(0, min(1.2, 0.5 * size), above));
                ao *= groundAo;
                float ndl = dot(n, l.direction);
                float diffuse = lerp(saturate(ndl), saturate((ndl + 0.45) / 1.45), imp ? trans : leaf ? 1 : 0);
                float3 ambient = _StabbySky.a > 0 || _StabbyGround.a > 0 ? lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5) : lerp(float3(0.25, 0.24, 0.22), float3(0.45, 0.5, 0.58), n.y * 0.5 + 0.5);
                float back = pow(saturate(dot(-v, l.direction)), 4) * 0.55 + saturate(-ndl) * 0.2;
                float3 direct = l.color * l.shadowAttenuation;
                float3 c = albedo * (direct * diffuse * lerp(0.55, 1, ao) + ambient * ao) + albedo * _Leaf.rgb * 2.2 * direct * back * trans * ao;
                return half4(MixFog(c, i.fog), alpha);
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

            float3 _LightDirection;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(FoliageIn i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 wn;
                float3 wp = FoliageWind(i, wn);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = _StabbyFoliageOff > 0 || i.uv.w > 1.5 ? float4(0, 0, -2, 1) : cs;
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                clip(min(fade.y - 0.5, fade.x - Bayer(i.positionCS.xy)));
                [branch] if (i.uv.w > 0.5 && i.uv.w < 1.5) clip(SAMPLE_TEXTURE2D(_LeafAtlas, sampler_LeafAtlas, i.uv.xy).a - 0.5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Bake"
            Tags { "LightMode" = "StabbyBake" }
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _StabbyBakeCenter, _StabbyBakeFrame, _StabbyBakeGrid;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalOS : TEXCOORD0;
                float4 uv : TEXCOORD1;
            };

            struct BakeOut
            {
                half4 a : SV_Target0;
                half4 b : SV_Target1;
            };

            Varyings vert(FoliageIn i)
            {
                Varyings o;
                float s = _StabbyBakeCenter.w;
                float3 q = i.positionOS.xyz - _StabbyBakeCenter.xyz;
                float3 d = float3(_StabbyBakeFrame.x, 0, _StabbyBakeFrame.y);
                float3 r = float3(-_StabbyBakeFrame.y, 0, _StabbyBakeFrame.x);
                float2 xy = float2(dot(q, r), q.y) * 2 / s;
                float t = dot(q, d) / s;
                float2 uv = (_StabbyBakeFrame.zw + 0.5 + 0.5 * _StabbyBakeGrid.y * clamp(xy, -1, 1)) / _StabbyBakeGrid.x;
                float2 cs = uv * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                cs.y = -cs.y;
                #endif
                #if UNITY_REVERSED_Z
                float z = clamp(0.5 + t, 0.01, 0.99);
                #else
                float z = clamp(0.5 - t, 0.01, 0.99);
                #endif
                o.positionCS = float4(cs, z, 1);
                o.normalOS = i.normalOS;
                o.uv = i.uv;
                return o;
            }

            BakeOut frag(Varyings i)
            {
                BakeOut o;
                half4 a = half4(0.436, 0.5, 1, 1);
                [branch] if (i.uv.w > 0.5)
                {
                    float4 t = SAMPLE_TEXTURE2D_LOD(_LeafAtlas, sampler_LeafAtlas, i.uv.xy, 0);
                    clip(t.a - 0.5);
                    a = half4(t.rgb, 1);
                }
                o.a = a;
                o.b = half4(normalize(i.normalOS) * 0.5 + 0.5, i.uv.z);
                return o;
            }
            ENDHLSL
        }
    }
}
