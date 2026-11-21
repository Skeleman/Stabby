Shader "Stabby/Water"
{
    Properties
    {
        _Shallow ("Shallow", Color) = (0.2, 0.42, 0.44, 1)
        _Clarity ("Depth for full colour (m)", Float) = 3
        _Shore ("Soft shore depth (m)", Float) = 0.25
        _Foam ("Foam depth (m)", Float) = 0.2
        _WaveScale ("Wave scale (m)", Float) = 18
        _WaveSpeed ("Wave speed", Float) = 0.6
        _WaveStrength ("Wave strength", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Shallow;
            float _Clarity, _Shore, _Foam, _WaveScale, _WaveSpeed, _WaveStrength;
            CBUFFER_END

            float4 _StabbySky;

            float Hash(float2 p)
            {
                p = frac(p * float2(0.3183099, 0.3678794) + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * (p.x + p.y));
            }

            float3 NoiseD(float2 x)
            {
                float2 i = floor(x), f = x - i;
                float2 u = f * f * (3 - 2 * f), du = 6 * f * (1 - f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                float v = a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
                float2 g = du * float2(b - a + (a - b - c + d) * u.y, c - a + (a - b - c + d) * u.x);
                return float3(v, g);
            }

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 color : COLOR; float fog : TEXCOORD1; float4 screen : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.color = i.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.screen = ComputeScreenPos(p.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 toCam = _WorldSpaceCameraPos - i.positionWS;
                float dist = length(toCam);
                float3 v = toCam / dist;
                float2 uv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surface = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float3 bed = _WorldSpaceCameraPos - v * (dist * scene / max(surface, 0.001));
                float depth = max(0, i.positionWS.y - bed.y);

                float t = _Time.y * _WaveSpeed;
                float2 q = i.positionWS.xz / _WaveScale;
                float3 w1 = NoiseD(q + float2(t, t * 0.7));
                float3 w2 = NoiseD(q * 2.3 + float2(-t * 1.3, t * 0.4));
                float fade = saturate(1.5 - dist / (_WaveScale * 120));
                float2 g = w1.yz + 0.5 * w2.yz;
                UNITY_BRANCH if (fade > 0)
                    g += 0.2 * fade * NoiseD(q * 7.1 + float2(t * 2.1, -t * 1.7)).yz;
                g *= _WaveStrength * (0.35 + 0.65 * fade);
                float3 n = normalize(float3(-g.x, 1, -g.y));

                float3 deep = i.color.rgb;
                float3 shallow = _Shallow.rgb;
                float k = saturate(depth / max(_Clarity, 0.01));
                float3 body = lerp(shallow, deep, k * (2 - k));

                Light l = GetMainLight();
                float3 sky = _StabbySky.rgb;
                float ndv = saturate(dot(n, v));
                float fres = 0.02 + 0.98 * pow(1 - ndv, 5);
                float3 c = body * (l.color * saturate(dot(n, l.direction)) * 0.6 + sky * 0.5);
                c = lerp(c, sky, fres * 0.8);
                float3 h = normalize(l.direction + v);
                c += l.color * pow(saturate(dot(n, h)), 240) * 1.5;

                float edge = saturate(1 - depth / max(_Foam, 0.01));
                float foam = 0;
                UNITY_BRANCH if (edge > 0)
                    foam = edge * smoothstep(0.55, 0.75, NoiseD(i.positionWS.xz * 0.8 + t * 0.5).x + 0.5 * edge);
                c = lerp(c, l.color * 0.9 + sky * 0.2, foam * 0.7);

                float alpha = lerp(lerp(0.8, 0.97, k), 1, fres * 0.6) * saturate(depth / max(_Shore, 0.01));
                alpha = max(alpha, foam * 0.7 * saturate(depth / 0.05));
                return half4(MixFog(c, i.fog), saturate(alpha));
            }
            ENDHLSL
        }
    }
}
