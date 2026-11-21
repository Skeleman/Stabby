Shader "Stabby/Object"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.5, 0.5, 0.5, 1)
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

        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise(float3 x)
        {
            float3 i = floor(x), f = x - i;
            f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
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

            float4 _StabbySky, _StabbyGround;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

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
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 fade = _Fade;
                clip(fade.x - Bayer(i.positionCS.xy));
                float3 wp = i.positionWS;
                float fp = max(length(fwidth(wp)), 1e-4);
                float3 n = normalize(i.normalWS);
                const float e = 0.15, freq = 2.2;
                float3 q = wp * freq;
                float n0 = Noise(q);
                float3 g = float3(Noise(q + float3(e, 0, 0)), Noise(q + float3(0, e, 0)), Noise(q + float3(0, 0, e))) - n0;
                float keep = saturate(1.5 - fp * freq * 2);
                n = normalize(n - (g - n * dot(g, n)) * (2.5 / e) * 0.25 * keep);
                float3 color = _BaseColor.rgb * (1 + 0.35 * (Noise(wp * 0.7 + 11.1) - 0.5) + 0.25 * (n0 - 0.5) * keep);
                Light l = GetMainLight(TransformWorldToShadowCoord(wp));
                float3 ambient = _StabbySky.a > 0 || _StabbyGround.a > 0 ? lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5) : lerp(float3(0.25, 0.24, 0.22), float3(0.45, 0.5, 0.58), n.y * 0.5 + 0.5);
                float3 c = color * (l.color * saturate(dot(n, l.direction)) * l.shadowAttenuation + ambient);
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
