Shader "Stabby/Sky"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Viewer/Shaders/Sky.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float StarHash(int3 q)
            {
                uint h = (uint)q.x * 73856093u ^ (uint)q.y * 19349663u ^ (uint)q.z * 83492791u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return (h & 0xffffffu) / 16777216.0;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 c = StabbySkyColor(d);
                float up = saturate(d.y * 60 + 0.5);

                float3 sun = normalize(_StabbySunDir.xyz);
                float cs = saturate(dot(d, sun));
                float sunUp = saturate(sun.y * 30 + 0.6);
                c += _StabbySunTint.rgb * sunUp * up * (smoothstep(0.99985, 0.99993, cs) * 1.6 + pow(cs, 600) * 0.35 + pow(cs, 24) * 0.06);

                float3 moon = normalize(_StabbyMoonDir.xyz);
                float cm = saturate(dot(d, moon));
                float glow = _StabbyMoonDir.w;
                c += _StabbyMoonTint.rgb * glow * up * (pow(cm, 18) * 0.6 + pow(cm, 300) * 1.4 + smoothstep(0.99988, 0.99994, cm) * 30);

                float3 g = d * 260;
                float3 f = frac(g) - 0.5;
                float r = StarHash((int3)floor(g));
                float star = step(0.9965, r) * saturate(1 - length(f) * 2.4) * (0.3 + 3 * (r - 0.9965) / 0.0035);
                c += _StabbyStars * star * saturate(d.y * 5 - 0.1) * float3(0.9, 0.95, 1);

                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
