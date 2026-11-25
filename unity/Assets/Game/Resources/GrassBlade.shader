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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma target 4.5
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Game/Grass/Wind.hlsl"
            #include "Assets/Viewer/Shaders/Look.hlsl"
            #include "Assets/Viewer/Shaders/LocalLights.hlsl"
            #include "Assets/Viewer/Shaders/CaveSky.hlsl"

            struct Blade { float4 p0; uint4 q; };
            StructuredBuffer<Blade> _Blades;
            uint _GrassMax, _GrassBase, _GrassTris;
            float4 _GrassLook;
            float4 _StabbySky, _StabbyGround;

            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float4 look : TEXCOORD2; float4 tint : TEXCOORD3; };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                uint tri = vid / 3, corner = vid - tri * 3, segs = _GrassTris >> 1;
                if (iid >= _GrassMax) { o.positionCS = float4(0, 0, -2, 1); return o; }
                Blade b = _Blades[_GrassBase + iid];
                uint q = tri >> 1, level;
                float side;
                if (tri == _GrassTris - 1) { level = corner == 2 ? segs + 1 : segs; side = corner == 0 ? -1 : corner == 1 ? 1 : 0; }
                else if ((tri & 1) == 0) { level = corner == 2 ? q + 1 : q; side = corner == 1 ? 1 : -1; }
                else { level = corner == 1 ? q : q + 1; side = corner == 0 ? -1 : 1; }
                bool head = (b.q.z >> 30) & 1;
                float tl = level / (float)(segs + 1), h = b.p0.w;
                float t = head ? (tl <= 0.5 ? tl * 1.44 : 0.72 + (tl - 0.5) * 0.56) : tl;
                float angle = (b.q.x & 0xffff) * (6.2831853 / 65535), seed = (b.q.x >> 16) * (1.0 / 65535);
                float bw = f16tof32(b.q.y), shade = ((b.q.y >> 16) & 0xff) * (1.0 / 255), fade = (b.q.y >> 24) * (1.0 / 255);
                float3 root = float3(b.q.z & 1023, (b.q.z >> 10) & 1023, (b.q.z >> 20) & 1023) * (1.0 / 1023);
                root *= root;
                float bend = (b.q.w & 0xff) * (1.0 / 255), dry = ((b.q.w >> 8) & 0xff) * (1.0 / 255);
                float2 nxz = float2((b.q.w >> 16) & 0xff, b.q.w >> 24) * (2.0 / 255) - 1;
                float3 gn = float3(nxz.x, sqrt(saturate(1 - dot(nxz, nxz))), nxz.y);
                float2 face = float2(cos(angle), sin(angle)), across = float2(-face.y, face.x);
                float2 push = WindPush(b.p0.xz, _Time.y, seed) + face * _GrassLook.x * (head ? 0.3 : 0.3 + 1.4 * bend);
                float lean = length(push);
                float2 off = push * t * t * h;
                float rise = h * t * rsqrt(1 + lean * lean * t * t);
                float wid = head ? bw * (t < 0.72 ? 0.3 : lerp(0.3, 0.8, saturate(1 - abs(t - 0.86) * 7.15))) : bw * pow(saturate(1 - t), 0.7 + 0.6 * dry);
                float lum = dot(root, float3(0.2126, 0.7152, 0.0722));
                float3 dryCol = head ? lum * float3(1.3, 1.16, 0.8) : lum * float3(1.45, 1.24, 0.74);
                float dw = head ? 0.85 * smoothstep(0.72, 0.97, t) : dry * smoothstep(0.55, 1, t);
                float3 pos = b.p0.xyz + float3(off.x + across.x * side * wid * 0.5, rise, off.y + across.y * side * wid * 0.5);
                float3 tangent = normalize(float3(push.x * 2 * t, 1, push.y * 2 * t));
                float3 bn = normalize(cross(float3(across.x, 0, across.y), tangent));
                bn *= dot(bn, _WorldSpaceCameraPos - pos) < 0 ? -1 : 1;
                o.normalWS = normalize(lerp(gn, bn, 0.6 * t));
                o.positionWS = pos;
                o.positionCS = TransformWorldToHClip(pos);
                float glow = 1 + _GrassLook.y * fade * t * t;
                o.color = float4(root * glow, fade * (0.3 + 0.4 * frac(seed * 13.7)));
                o.tint = float4(dryCol * glow, dw);
                o.look = float4(t, shade, fade, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light l = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1, 1, 1, 1));
                float sky = CaveSky(i.positionWS);
                l.color *= l.shadowAttenuation * sky;
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 ambient = lerp(_StabbyGround.rgb, _StabbySky.rgb, n.y * 0.5 + 0.5) * sky;
                float back = _GrassLook.z * saturate(dot(-l.direction, v)) * i.look.x * i.look.z;
                float occ = 1 - i.color.a * (1 - smoothstep(0, 0.5, i.look.x));
                float3 c = (LookColor(lerp(i.color.rgb, i.tint.rgb * (1 + 0.08 * l.shadowAttenuation), saturate(i.tint.a * lerp(0.3, 1.2, l.shadowAttenuation))), 1) * (l.color * (saturate(dot(n, l.direction)) + back) + ambient + LocalLights(i.positionWS, n, i.positionCS)) * occ + LookSheen(n, v, l.direction, l.color, LookRough())) * i.look.y;
                return half4(MixFogColor(c, unity_FogColor.rgb * sky, i.look.w), 1);
            }
            ENDHLSL
        }
    }
}
