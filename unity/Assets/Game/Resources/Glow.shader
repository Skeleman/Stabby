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

            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                Glow g = _Glows[inst];
                uint c = id % 6;
                float2 q = float2(c == 1 || c == 2 || c == 4 ? 1 : -1, c == 2 || c == 4 || c == 5 ? 1 : -1);
                float3 wp = g.posSize.xyz + (UNITY_MATRIX_V[0].xyz * q.x + UNITY_MATRIX_V[1].xyz * q.y) * g.posSize.w;
                Varyings o;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = q;
                o.color = g.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = saturate(1 - dot(i.uv, i.uv));
                float3 c = i.color.rgb * (i.color.a * r * r * r);
                return half4(MixFogColor(c, 0, i.fog), 0);
            }
            ENDHLSL
        }
    }
}
