Shader "Stabby/Glow"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Glow { float4 posSize; float4 color; };
            StructuredBuffer<Glow> _Glows;

            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; float4 color : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                Glow g = _Glows[inst];
                uint c = id % 6;
                float2 q = float2(c == 1 || c == 2 || c == 4 ? 1 : -1, c == 2 || c == 4 || c == 5 ? 1 : -1);
                float3 wp;
                float beam = g.posSize.w < 0 ? 1 : 0;
                [branch] if (beam > 0)
                {
                    float len = -g.posSize.w;
                    float3 side = cross(float3(0, 1, 0), g.posSize.xyz - _WorldSpaceCameraPos);
                    side *= rsqrt(max(dot(side, side), 1e-6));
                    wp = g.posSize.xyz + float3(0, q.y * len, 0) + side * (q.x * len * lerp(0.45, 0.2, q.y * 0.5 + 0.5));
                }
                else wp = g.posSize.xyz + (UNITY_MATRIX_V[0].xyz * q.x + UNITY_MATRIX_V[1].xyz * q.y) * g.posSize.w;
                Varyings o;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = float3(q, beam);
                o.color = g.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = saturate(1 - dot(i.uv.xy, i.uv.xy));
                float w = 1 - i.uv.x * i.uv.x;
                float b = w * w * smoothstep(-1, -0.6, i.uv.y) * smoothstep(1, 0.5, i.uv.y);
                float3 c = i.color.rgb * (i.color.a * (i.uv.z > 0.5 ? b : r * r * r));
                return half4(MixFogColor(c, 0, i.fog), 0);
            }
            ENDHLSL
        }
    }
}
