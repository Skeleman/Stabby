Shader "Stabby/Creature"
{
    Properties
    {
        _Fade ("Fade", Vector) = (1, 1, 0, 0)
        _FurBase ("Fur Base", Color) = (0.25, 0.12, 0.05, 1)
        _FurAccent ("Fur Accent", Color) = (0.8, 0.7, 0.56, 1)
        _FurDark ("Fur Dark", Color) = (0.02, 0.015, 0.012, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _Fade;
        float4 _FurBase;
        float4 _FurAccent;
        float4 _FurDark;
        CBUFFER_END

        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _Fade)
            UNITY_DOTS_INSTANCED_PROP(float4, _FurBase)
            UNITY_DOTS_INSTANCED_PROP(float4, _FurAccent)
            UNITY_DOTS_INSTANCED_PROP(float4, _FurDark)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _Fade UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Fade)
        #define _FurBase UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _FurBase)
        #define _FurAccent UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _FurAccent)
        #define _FurDark UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _FurDark)
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
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

            #include "Assets/Viewer/Shaders/TerrainMaterials.hlsl"
            #include "Assets/Viewer/Shaders/Contact.hlsl"
            #include "Assets/Viewer/Shaders/Look.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tone : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 local : TEXCOORD2;
                float4 tone : TEXCOORD3;
                float3 origin : TEXCOORD4;
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
                float4x4 m = UNITY_MATRIX_M;
                float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
                o.local = float4(i.positionOS.xyz * scale, ComputeFogFactor(p.positionCS.z));
                o.tone = i.tone;
                o.origin = m._m03_m13_m23;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                clip(fade.x - Bayer(i.positionCS.xy));
                float3 wp = i.positionWS;
                float la = length(ddx(wp)), lb = length(ddy(wp));
                float fp = max(max(sqrt(la * lb), 0.4 * max(la, lb)), 1e-4);
                float3 n = normalize(i.normalWS);
                float4x4 mm = UNITY_MATRIX_M;
                float3 ax = normalize(mm._m00_m10_m20), ay = normalize(mm._m01_m11_m21), az = normalize(mm._m02_m12_m22);
                float3 lp = i.local.xyz;
                bool eye = i.tone.w > 0.5 && i.tone.x > 1.5;

                float3 baseC = _FurBase.rgb, accentC = _FurAccent.rgb, darkC = _FurDark.rgb;
                float4 big = TmNoise3(wp * 1.7 + 13.1, int3(0, 0, 0));
                float4 mid = TmNoise3(lp * float3(9, 9, 4) + 3.3, int3(0, 0, 0));
                float edge = saturate(i.tone.y + 0.18 * mid.x + 0.08 * big.x);
                float3 albedo = lerp(baseC, accentC, smoothstep(0.38, 0.62, edge));
                albedo *= 1 + 0.1 * big.x + 0.06 * mid.x;
                float dorsal = smoothstep(0.55, 0.95, dot(n, float3(0, 1, 0)) + 0.1 * mid.y);
                albedo = lerp(albedo, albedo * float3(0.72, 0.68, 0.66), dorsal * (1 - i.tone.y));
                albedo = i.tone.x > 1.5 ? darkC * (1 + 0.15 * mid.x) : albedo;
                albedo = i.tone.x > 0.5 && i.tone.x <= 1.5 ? lerp(accentC, baseC, 0.15 + 0.1 * mid.x) : albedo;

                float3 freq = float3(110, 110, 16);
                float4 strand = TmNoise3(lp * freq, int3(0, 0, 0));
                float4 tuft = TmNoise3(lp * float3(34, 34, 6) + 7.7, int3(0, 0, 0));
                float fs = TmFade(110, fp) * (eye ? 0 : 1), ft = TmFade(34, fp) * (eye ? 0 : 1);
                albedo *= 1 + 0.16 * strand.x * fs + 0.1 * tuft.x * ft;
                float3 g = (strand.yzw * freq) * (0.0016 * fs) + (tuft.yzw * float3(34, 34, 6)) * (0.003 * ft);
                float3 gw = g.x * ax + g.y * ay + g.z * az;
                n = normalize(n - (gw - n * dot(gw, n)));
                tmRough = eye ? 0.12 : 0.9;

                float size = max(fade.w, 0.1), ground = fade.z;
                float thick = abs(mm._m10) + abs(mm._m11) + abs(mm._m12);
                [branch] if (_StabbyFieldUV.x > 0)
                {
                    ground += ContactBed(wp.xz) - ContactBed(i.origin.xz);
                    albedo = ContactObject(albedo, wp, ground, size, thick);
                }
                float above = max(wp.y - ground, 0);
                float ao = lerp(0.8, 1, smoothstep(0, min(0.6 * size, 0.5), above)) * lerp(1, 0.6, saturate(-n.y));
                Light l = GetMainLight(TransformWorldToShadowCoord(wp));
                float3 bounce = _StabbyGround.rgb;
                [branch] if (_StabbyFieldUV.z > 0)
                {
                    float3 gc = ToLinear(SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, wp.xz * _StabbyFieldUV.z + _StabbyFieldUV.w, 0).rgb);
                    gc = lerp(gc, ToLinear(_StabbySand.rgb), smoothstep(6, 2, fade.z));
                    float3 lit = gc * (_MainLightColor.rgb * saturate(_MainLightPosition.y) + _StabbySky.rgb);
                    bounce = lerp(_StabbyGround.rgb, lerp(_StabbyGround.rgb, lit, 0.5), smoothstep(2.5 * size + 2, 0, above));
                }
                float3 ambient = _StabbySky.a > 0 || _StabbyGround.a > 0 ? lerp(bounce, _StabbySky.rgb, n.y * 0.5 + 0.5) : lerp(float3(0.25, 0.24, 0.22), float3(0.45, 0.5, 0.58), n.y * 0.5 + 0.5);
                float3 v = normalize(_WorldSpaceCameraPos - wp);
                float wrap = eye ? saturate(dot(n, l.direction)) : saturate((dot(n, l.direction) + 0.35) / 1.35);
                float rim = eye ? 0 : pow(1 - saturate(dot(n, v)), 4) * 0.25 * saturate(dot(-v, l.direction) + 0.3);
                float3 c = LookColor(albedo, 1) * (l.color * (wrap + rim) * l.shadowAttenuation + ambient * ao);
                c += eye ? l.color * l.shadowAttenuation * 0.6 * pow(saturate(dot(n, normalize(l.direction + v))), 120) + ambient * 0.05 * pow(1 - saturate(dot(n, v)), 2) : LookSheen(n, v, l.direction, l.color * l.shadowAttenuation, tmRough) * ao * 0.5;
                return half4(MixFog(c, i.local.w), 1);
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
