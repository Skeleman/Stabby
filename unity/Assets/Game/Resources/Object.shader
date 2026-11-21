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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float4 _StabbySky, _StabbyGround, _StabbyFieldUV, _StabbySand;
            float _StabbyStrataPeriod;
            TEXTURE2D(_StabbyStrata);
            SAMPLER(sampler_StabbyStrata);
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBiome);
            SAMPLER(sampler_StabbyBiome);

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

            #include "Assets/Viewer/Shaders/TerrainMaterials.hlsl"
            #include "Assets/Viewer/Shaders/Bark.hlsl"
            #include "Assets/Viewer/Shaders/Look.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tone : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fog : TEXCOORD2;
                float3 local : TEXCOORD3;
                float3 normalOS : TEXCOORD4;
                float3 axisWS : TEXCOORD5;
                float4 tone : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                float4x4 m = UNITY_MATRIX_M;
                float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
                bool isLog = _BaseColor.a > 0.5 && _BaseColor.a < 1.5;
                float3 c = isLog ? float3(i.positionOS.x, i.positionOS.y - 0.5, i.positionOS.z) : i.positionOS.xyz;
                o.local = c * scale;
                o.normalOS = i.normalOS;
                o.axisWS = normalize(isLog ? m._m00_m10_m20 : m._m01_m11_m21);
                o.tone = i.tone;
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
                float4 mn = TmNoise3(wp * 2.3, int3(0, 0, 0)), mf = TmNoise3(wp * 9.1 + 17.3, int3(0, 0, 0));
                float mossAmt = code <= 2 ? tone.y : 0;
                float patch = smoothstep(0.5, 0.8, 0.5 + 0.5 * mn.x + 0.2 * mossAmt * age - 0.15 + 0.25 * mf.x);
                float moss = 0.9 * saturate(mossAmt * smoothstep(0.35, 0.85, n.y + 0.3 * mn.y) * patch * 1.3);
                float3 mossC = lerp(float3(0.07, 0.09, 0.03), float3(0.16, 0.18, 0.07), 0.5 + 0.5 * mf.y);
                float decay = lerp(1, 0.62, age * age);
                [branch] if (code == 3 || code == 5)
                {
                    float4 cl = TmNoise3(wp * 3.3 + 1.7, int3(0, 0, 0));
                    float3 dirt = float3(0.115, 0.07, 0.038) * (0.75 + 0.4 * mn.y) * (0.8 + 0.35 * cl.x);
                    float3 nz = TmNoise3(wp * 6.1 + 4.4, int3(0, 0, 0)).yzw;
                    float pebble = smoothstep(0.55, 0.75, TmNoise3(wp * 13.7, int3(0, 0, 0)).x);
                    float3 contact = lerp(dirt * 0.4, gc * 0.25, 0.35);
                    albedo = lerp(dirt, gc * 0.5, 0.22 * saturate(tone.y * (0.7 + 0.6 * mn.x)));
                    albedo = code == 5 ? lerp(ToLinear(float3(0.43, 0.41, 0.38)) * (0.7 + 0.45 * mf.x), dirt, 0.25 + 0.2 * mn.z) : lerp(albedo, ToLinear(float3(0.42, 0.4, 0.37)), 0.5 * pebble * (1 - tone.y));
                    albedo = lerp(albedo, contact, 0.75 * tone.z);
                    float fib = smoothstep(0.82, 0.95, 1 - abs(TmNoise2(wp.xz * 7 + wp.y * 3.1).x)) * (1 - tone.y) * TmFade(7, fp) * (code == 5 ? 0 : 1);
                    albedo = lerp(albedo, ToLinear(float3(0.32, 0.25, 0.18)), 0.6 * fib);
                    float3 g = (0.06 + 0.06 * (code == 5)) * nz * TmFade(6, fp) + 0.05 * cl.yzw * TmFade(3.3, fp);
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
                    float keep = TmFade(14, fp);
                    float3 wood = weathered * decay;
                    float2 g = 0;
                    if (tone.z > 0.5)
                    {
                        float2 q = float2(rc.x * 26, rc.y * 5) + along * 3;
                        float3 f = TmNoise2(q);
                        float fib = 1 - abs(f.x), sp = TmNoise2(rc * 9 + 3.1).x;
                        albedo = wood * (0.7 + 0.35 * lerp(0.6, fib, keep)) * (0.9 + 0.12 * sp);
                        float3 sc = TmNoise3(wp * 5.3 + 8.8, int3(0, 0, 0)).yzw;
                        float3 gg = (0.05 * keep * sign(f.x)) * float3(f.y * 26, f.z * 5, 0) + 0.04 * sc;
                        nOut = normalize(n - (gg - n * dot(gg, n)));
                    }
                    else
                    {
                        float wob = 0.06 * TmNoise2(rc * 6 + along).x;
                        float r = (radius + wob) * 14;
                        float ring = 0.5 + 0.5 * cos(r * 6.2831853);
                        float crack = smoothstep(0.92, 1, TmNoise2(float2(atan2(rc.y, rc.x) * 3, radius * 2)).x * 0.5 + 0.5);
                        albedo = wood * (1 - 0.18 * keep * ring) * (1 - 0.5 * crack * (0.4 + age));
                        float3 sc = TmNoise3(wp * 7.1 + 2.2, int3(0, 0, 0)).yzw;
                        float3 gg = 0.015 * sc * keep;
                        nOut = normalize(n - (gg - n * dot(gg, n)));
                    }
                    albedo = lerp(albedo, albedo * float3(0.62, 0.5, 0.4), 0.5 * smoothstep(0.45, 1, age) * (0.6 + 0.4 * mn.z));
                    albedo = lerp(albedo, mossC, moss * 0.7);
                    return;
                }
                bool spruce = code == 1;
                float2 mm = float2(atan2(rc.y, rc.x) * radius, along);
                float tn, fresh = 0;
                float3 bk = spruce ? SpruceBark(mm, fp, tn, fresh) : OakBark(mm, fp, tn);
                albedo = baseColor * (spruce ? 0.62 + 0.5 * tn : 0.5 + 0.6 * tn);
                albedo = spruce ? lerp(albedo, albedo * float3(1.35, 0.95, 0.75), fresh * (1 - age)) : lerp(albedo, dot(albedo, lw) * float3(1.04, 1, 0.94), 0.4 * saturate(tn));
                albedo = lerp(albedo, dot(albedo, lw) * float3(1, 0.97, 0.92), 0.45 * age) * decay;
                float4 ln = TmNoise3(wp * 1.7 + 51.3, int3(0, 0, 0));
                float loss = smoothstep(0.62, 0.42, 1 - age + 0.35 * ln.x) * (0.5 + 0.5 * smoothstep(-0.2, 0.3, ln.x));
                float3 fibre = TmNoise2(float2(mm.x * 30, mm.y * 3));
                float3 bare = weathered * decay * (0.75 + 0.3 * (1 - abs(fibre.x)));
                albedo = lerp(albedo, bare, loss);
                albedo = lerp(albedo, mossC, moss);
                float4 lc = TmNoise3(wp * 4.7 + 31.1, int3(0, 0, 0));
                float lic = smoothstep(0.5, 0.68, lc.x) * (spruce ? 0.25 : 0.4) * (0.6 + 0.6 * age) * (1 - moss) * BarkFade(4.7, fp);
                albedo = lerp(albedo, dot(albedo, lw) * float3(0.9, 1.03, 0.78) * (2.1 + 0.3 * lc.y), 0.6 * saturate(lic));
                float2 g = lerp(bk.yz, 0.08 * fibre.yz * float2(30, 3) * BarkFade(30, fp), loss);
                float depth = (spruce ? 0.012 : 0.01) * (1 - 0.6 * moss);
                nOut = Bumped(n, axis, sgn, g, depth);
            }

            float3 Dressed(float3 albedo, float4 tone, float3 wp, float3 n, float h, float fp)
            {
                float lum = dot(albedo, float3(0.3, 0.59, 0.11));
                float3 tint = lerp(float3(1.04, 0.98, 0.9), float3(1.08, 0.95, 0.8), 0.5 + 0.5 * tone.z);
                albedo = lerp(lum * tint, albedo, 0.25) * (1 + 0.22 * tone.y);
                float band = 1 - smoothstep(0.04, 0.4, h);
                return lerp(albedo, albedo * float3(0.5, 0.46, 0.4), 0.75 * band);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                clip(fade.x - Bayer(i.positionCS.xy));
                float4 base = _BaseColor;
                float3 wp = i.positionWS;
                float la = length(ddx(wp)), lb = length(ddy(wp));
                float fp = max(max(sqrt(la * lb), 0.4 * max(la, lb)), 1e-4);
                float3 n = normalize(i.normalWS);
                float3 albedo;
                tmRough = LookRough();
                [branch] if (base.a < 0.5)
                {
                    float off = SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).a;
                    float period = max(_StabbyStrataPeriod, 1);
                    float u0 = (wp.y + off) / period;
                    float2 dux = float2(ddx(u0), 0), duy = float2(ddy(u0), 0);
                    [branch] if (_StabbyDetailOff > 0)
                        albedo = SAMPLE_TEXTURE2D_GRAD(_StabbyStrata, sampler_StabbyStrata, float2(u0, 0.5), dux, duy).rgb;
                    else
                    {
                        float2 o2 = TmOrigin(_WorldSpaceCameraPos);
                        tmO = float3(o2.x, 0, o2.y);
                        TmOut m = TmRock(wp - tmO, n, normalize(_WorldSpaceCameraPos - wp), fp, off, dux, duy, 1);
                        float4 cw = SAMPLE_TEXTURE2D_LOD(_StabbyCoat, sampler_StabbyCoat, wp.xz * _StabbyCoatUV.x + _StabbyCoatUV.y, 0);
                        TmStoneCover(m, wp - tmO, n, cw / max(dot(cw, 1), 1e-3), fp, 1, 1);
                        float3 g = m.grad - n * dot(m.grad, n);
                        n = normalize(n - g);
                        albedo = m.albedo;
                    }
                    albedo *= base.rgb;
                    [branch] if (i.tone.x > 0.5) albedo = Dressed(albedo, i.tone, wp - tmO, n, i.local.y, fp);
                }
                else
                {
                    float3 gw = ToLinear(_StabbyGround.rgb);
                    [branch] if (_StabbyFieldUV.z > 0) gw = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
                    Wood(i.local, n, normalize(i.axisWS), base.rgb, base.a < 1.5, i.tone, fp, wp, gw, albedo, n);
                }
                float above = max(wp.y - fade.z, 0), size = max(fade.w, 0.1);
                float ao = lerp(0.72, 1, smoothstep(0, min(0.12, 0.2 * size), above)) * lerp(0.75, 1, smoothstep(0, min(0.9, 0.6 * size), above)) * lerp(1, 0.5, saturate(-n.y));
                Light l = GetMainLight(TransformWorldToShadowCoord(wp));
                float3 bounce = _StabbyGround.rgb;
                [branch] if (_StabbyFieldUV.z > 0)
                {
                    float3 gc = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
                    gc = lerp(gc, ToLinear(_StabbySand.rgb), smoothstep(6, 2, fade.z));
                    float3 lit = gc * (_MainLightColor.rgb * saturate(_MainLightPosition.y) + _StabbySky.rgb);
                    bounce = lerp(_StabbyGround.rgb, lit, smoothstep(2.5 * size + 2, 0, above));
                }
                float3 ambient = _StabbySky.a > 0 || _StabbyGround.a > 0 ? lerp(bounce, _StabbySky.rgb, n.y * 0.5 + 0.5) : lerp(float3(0.25, 0.24, 0.22), float3(0.45, 0.5, 0.58), n.y * 0.5 + 0.5);
                float3 c = LookColor(albedo, 1) * (l.color * saturate(dot(n, l.direction)) * l.shadowAttenuation * lerp(1, ao, 0.4) + ambient * ao) + LookSheen(n, normalize(_WorldSpaceCameraPos - wp), l.direction, l.color * l.shadowAttenuation, saturate(tmRough)) * ao;
                return half4(MixFog(c, i.fog), 1);
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
                o.positionCS = cs;
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
