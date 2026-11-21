Shader "Stabby/GrassBlade"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Game/Grass/Wind.hlsl"

            struct Blade { float4 p0; float4 p1; float4 p2; float4 p3; };
            StructuredBuffer<Blade> _Blades;
            uint _GrassMax;
            float4 _GrassLook;
            float4 _StabbySky, _StabbyGround;

            struct Varyings { float4 positionCS : SV_POSITION; float3 color : COLOR; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float4 look : TEXCOORD2; };

            Varyings vert(uint vid : SV_VertexID)
            {
                Varyings o = (Varyings)0;
                uint bi = vid / 21, local = vid - bi * 21, tri = local / 3, corner = local - tri * 3;
                if (bi >= _GrassMax) { o.positionCS = float4(0, 0, -2, 1); return o; }
                Blade b = _Blades[bi];
                uint q = tri >> 1, level;
                float side;
                if (tri == 6) { level = corner == 2 ? 4 : 3; side = corner == 0 ? -1 : corner == 1 ? 1 : 0; }
                else if ((tri & 1) == 0) { level = corner == 2 ? q + 1 : q; side = corner == 1 ? 1 : -1; }
                else { level = corner == 1 ? q : q + 1; side = corner == 0 ? -1 : 1; }
                float t = level * 0.25, h = b.p0.w;
                float2 face = float2(cos(b.p1.x), sin(b.p1.x)), across = float2(-face.y, face.x);
                float2 push = WindPush(b.p0.xz, _Time.y, b.p1.z) + face * _GrassLook.x;
                float lean = length(push);
                float2 off = push * t * t * h;
                float rise = h * t * rsqrt(1 + lean * lean * t * t);
                float wid = b.p1.y * pow(saturate(1 - t), 0.7);
                float3 pos = b.p0.xyz + float3(off.x + across.x * side * wid * 0.5, rise, off.y + across.y * side * wid * 0.5);
                float3 tangent = normalize(float3(push.x * 2 * t, 1, push.y * 2 * t));
                float3 bn = normalize(cross(float3(across.x, 0, across.y), tangent));
                bn *= dot(bn, _WorldSpaceCameraPos - pos) < 0 ? -1 : 1;
                o.normalWS = normalize(lerp(b.p3.xyz, bn, 0.6 * t));
                o.positionWS = pos;
                o.positionCS = TransformWorldToHClip(pos);
                o.color = b.p2.rgb * (1 + _GrassLook.y * b.p2.w * t * t);
                o.look = float4(t, b.p1.w, b.p2.w, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light l = GetMainLight();
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 ambient = lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5);
                float back = _GrassLook.z * saturate(dot(-l.direction, v)) * i.look.x * i.look.z;
                float3 c = i.color * (l.color * (saturate(dot(n, l.direction)) + back) + ambient) * i.look.y;
                return half4(MixFog(c, i.look.w), 1);
            }
            ENDHLSL
        }
    }
}
