Shader "Stabby/VertexLit"
{
    Properties { _StitchMask ("Stitch mask", Float) = 0 }
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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _StitchMask;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float, _StitchMask)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _StitchMask UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _StitchMask)
            #endif

            float4 _StabbySky, _StabbyGround;
            float _StabbyStrataPeriod;
            TEXTURE2D(_StabbyStrata);
            SAMPLER(sampler_StabbyStrata);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 strata : TEXCOORD0; float4 stitch : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float3 strata : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 pos = ((uint)i.stitch.w & (uint)_StitchMask) != 0 ? i.stitch.xyz : i.positionOS.xyz;
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.strata = float3(i.strata, p.positionWS.y);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light l = GetMainLight();
                float3 ambient = lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5);
                float u = (i.strata.z + i.strata.x) / max(_StabbyStrataPeriod, 1);
                float3 rock = SAMPLE_TEXTURE2D(_StabbyStrata, sampler_StabbyStrata, float2(u, 0.5)).rgb;
                float3 c = lerp(i.color.rgb, rock, saturate(i.strata.y)) * (l.color * saturate(dot(n, l.direction)) + ambient) * i.color.a;
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
