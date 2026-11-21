Shader "Stabby/VertexLit"
{
    Properties { _StitchMask ("Stitch mask", Vector) = (0, 0, 0, 0) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _StitchMask;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _StitchMask)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _StitchMask UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _StitchMask)
            #endif

            float4 _StabbySky, _StabbyGround, _StabbyFieldUV, _StabbySand, _StabbyGrassFar;
            float _StabbyStrataPeriod, _StabbyCellMeters;
            TEXTURE2D(_StabbyStrata);
            SAMPLER(sampler_StabbyStrata);
            TEXTURE2D(_StabbySlope);
            SAMPLER(sampler_StabbySlope);
            TEXTURE2D(_StabbyBiome);
            SAMPLER(sampler_StabbyBiome);
            TEXTURE2D(_StabbyBed);
            SAMPLER(sampler_StabbyBed);

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }

            #include "TerrainMaterials.hlsl"
            #include "Look.hlsl"
            #include "Assets/Game/Grass/Wind.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 strata : TEXCOORD0; float4 stitch : TEXCOORD1; float4 stitchNormal : TEXCOORD2; float4 stitchColor : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 strata : TEXCOORD2; float2 xz : TEXCOORD3; float field : TEXCOORD4; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float4 sm = _StitchMask;
                uint bits = (uint)i.stitch.w, mask = (uint)sm.x;
                float t = 0;
                if (sm.z > 0) t = saturate((distance(TransformObjectToWorld(i.positionOS.xyz), _WorldSpaceCameraPos) - sm.y) / (sm.z - sm.y));
                if ((bits & (mask >> 8)) != 0) t = 0;
                if ((bits & mask & 255u) != 0) t = 1;
                VertexPositionInputs p = GetVertexPositionInputs(lerp(i.positionOS.xyz, i.stitch.xyz, t));
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(normalize(lerp(i.normalOS, i.stitchNormal.xyz, t)));
                o.color = lerp(i.color, i.stitchColor, t);
                o.strata = float4(i.strata.x, lerp(i.strata.y, i.stitchNormal.w, t), p.positionWS.y, i.strata.z);
                o.xz = p.positionWS.xz - TmOrigin(_WorldSpaceCameraPos);
                o.field = sm.w > 0 ? lerp(saturate(sm.w / _StabbyCellMeters - 1), saturate(2 * sm.w / _StabbyCellMeters - 1), t) : 1;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 color = i.color.rgb;
                float off = i.strata.x, mix = i.strata.y, h = i.strata.z, f = saturate(i.strata.w), stone = saturate(i.color.a - 1), ao = min(i.color.a, 1);
                if (f > 0)
                {
                    float2 xa = i.xz + TmOrigin(_WorldSpaceCameraPos);
                    float2 sl = SAMPLE_TEXTURE2D(_StabbySlope, sampler_StabbySlope, xa * _StabbyFieldUV.x + _StabbyFieldUV.y).rg;
                    float4 bo = SAMPLE_TEXTURE2D_LOD(_StabbyBiome, sampler_StabbyBiome, xa * _StabbyFieldUV.z + _StabbyFieldUV.w, 0);
                    float3 fn = normalize(float3(-sl.x, 1, -sl.y)), fc;
                    float fr = 0;
                    if (h <= 0) fc = ToLinear(_StabbySand.rgb) * lerp(0.62, 0.45, saturate(-h / 30));
                    else
                    {
                        float slope = sqrt(max(0, 1 - fn.y * fn.y)) / max(fn.y, 0.05), shore = saturate(1 - h / 6);
                        fc = ToLinear(bo.rgb);
                        fr = saturate((slope - 0.25) / 0.35) * (1 - shore);
                    }
                    n = normalize(lerp(n, fn, f * i.field));
                    color = lerp(color, fc, f);
                    off = lerp(off, bo.a, f);
                    mix = lerp(mix, fr, f);
                }
                if (f < 1)
                {
                    float2 xa = i.xz + TmOrigin(_WorldSpaceCameraPos);
                    float bed = SAMPLE_TEXTURE2D_LOD(_StabbyBed, sampler_StabbyBed, xa * _StabbyFieldUV.x + _StabbyFieldUV.y, 0).r;
                    mix = lerp(mix, min(mix, 0.5), (1 - f) * (1 - stone) * smoothstep(0.8, 0.97, n.y) * smoothstep(-1, 0, h - bed));
                    ao *= lerp(1, lerp(0.5, 1, saturate((h - bed) / 0.8)) * lerp(0.7, 1, saturate(n.y + 0.8)), stone);
                }
                TmOut m = TmEvaluate(float3(i.xz.x, h, i.xz.y), n, color, off, mix, stone);
                n = m.grad;
                [branch] if (_StabbyGrassFar.x > 0 && tmGrass > 0.01)
                {
                    float2 xa = i.xz + TmOrigin(_WorldSpaceCameraPos);
                    float far = smoothstep(0.7, 1, distance(float3(xa.x, h, xa.y), _WorldSpaceCameraPos) / _StabbyGrassFar.x);
                    float gust = WindGust(xa, _Time.y);
                    m.albedo *= 1 + tmGrass * (_StabbyGrassFar.y + (gust - 0.5) * 0.3 * far * (0.4 + _StabbyWind.z));
                }
                float2 xv = i.xz + TmOrigin(_WorldSpaceCameraPos);
                Light l = GetMainLight(TransformWorldToShadowCoord(float3(xv.x, h, xv.y)));
                l.color *= l.shadowAttenuation;
                float3 v = normalize(_WorldSpaceCameraPos - float3(xv.x, h, xv.y));
                float3 ambient = lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5);
                float3 c = (LookColor(m.albedo, 1) * (l.color * saturate(dot(n, l.direction)) + ambient) + LookSheen(n, v, l.direction, l.color, LookRough())) * ao;
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
