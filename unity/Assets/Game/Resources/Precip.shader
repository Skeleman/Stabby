Shader "Stabby/Precip"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Viewer/Shaders/CaveSky.hlsl"

            float4 _StabbyRainColor, _StabbySnowColor;
            float4 _StabbyRain, _StabbySnow, _StabbyRainShift, _StabbySnowShift, _StabbyRainLook, _StabbySnowLook, _StabbyPrecipBox;
            float4 _StabbySky, _StabbySunTint, _StabbyRainNight;
            float4 _StabbyPrecipLights[4], _StabbyPrecipLightColors[4];

            float3 FireLight(float3 wp)
            {
                float3 sum = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float3 d = wp - _StabbyPrecipLights[k].xyz;
                    float f = saturate(1 - dot(d, d) * _StabbyPrecipLights[k].w);
                    sum += _StabbyPrecipLightColors[k].rgb * (f * f);
                }
                return sum;
            }
            TEXTURE2D(_StabbyShelter);
            SAMPLER(sampler_StabbyShelter);
            float4 _StabbyShelterMin, _StabbyShelterInv;

            float Open(float3 wp)
            {
                float2 uv = (wp.xz - _StabbyShelterMin.xz) * _StabbyShelterInv.xz;
                [branch] if (_StabbyShelterMin.w <= 0 || any(uv < 0) || any(uv > 1)) return 1;
                return saturate((wp.y - SAMPLE_TEXTURE2D_LOD(_StabbyShelter, sampler_StabbyShelter, uv, 0).r) * 2 + 0.5);
            }
            float _StabbyClock, _StabbyPrecipOff;

            struct Varyings { float4 positionCS : SV_POSITION; float3 uv : TEXCOORD0; float4 color : TEXCOORD1; float fog : TEXCOORD2; };

            uint PrecipHash(uint x)
            {
                x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
                return x;
            }

            float Unit(uint x) { return (PrecipHash(x) >> 8) * (1.0 / 16777216.0); }

            Varyings vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                uint c = id % 6;
                float2 q = float2(c == 1 || c == 2 || c == 4 ? 1 : -1, c == 2 || c == 4 || c == 5 ? 1 : -1);
                uint h = PrecipHash(inst * 4 + 1);
                float snow = Unit(h) < _StabbyPrecipBox.x ? 1 : 0;
                float4 move = snow > 0 ? _StabbySnow : _StabbyRain;
                float3 shift = snow > 0 ? _StabbySnowShift.xyz : _StabbyRainShift.xyz;
                float box = move.w;
                float3 s = float3(Unit(h + 11), Unit(h + 23), Unit(h + 37));
                float3 cam = _WorldSpaceCameraPos;
                float3 p = frac(s + shift - cam / box + 0.5) - 0.5;
                float3 wp = cam + p * box;
                float t = _StabbyClock;
                float period = 3 + floor(Unit(h + 41) * 4);
                float phase = Unit(h + 53) * 6.2831853;
                float sway = _StabbyPrecipBox.y * snow;
                wp.xz += sway * float2(sin(t * 6.2831853 / period + phase), cos(t * 6.2831853 / (period * 2) + phase * 1.7));
                float open = Open(wp);
                float3 toCam = wp - cam;
                float dist = length(toCam);
                float pixel = dist * 2 / (UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                float4 look = snow > 0 ? _StabbySnowLook : _StabbyRainLook;
                float width = max(look.y, pixel);
                float thin = look.y / width;
                float3 view = toCam / max(dist, 1e-4);
                float3 axis, side;
                float len;
                [branch] if (snow > 0)
                {
                    axis = UNITY_MATRIX_V[1].xyz;
                    side = UNITY_MATRIX_V[0].xyz;
                    len = width;
                    thin *= thin;
                }
                else
                {
                    axis = normalize(move.xyz);
                    side = cross(axis, view);
                    side *= rsqrt(max(dot(side, side), 1e-6));
                    len = max(look.x, width);
                }
                wp += axis * (q.y * len * 0.5) + side * (q.x * width * 0.5);
                float edge = saturate((1 - length(p) * 2) * 4);
                float near = saturate((dist - 0.4) * 1.5);
                float4 tint = snow > 0 ? _StabbySnowColor : _StabbyRainColor;
                float3 light = _StabbySky.rgb * (2.2 + 3 * _StabbyRainNight.a) + _StabbySunTint.rgb * 0.45 + _StabbyRainNight.rgb * 2 + FireLight(wp) * 1.5;
                float cave = CaveSky(wp);
                o.positionCS = _StabbyPrecipOff > 0.5 || open <= 0 ? float4(0, 0, -1, 1) : TransformWorldToHClip(wp);
                o.uv = float3(q, snow);
                o.color = float4(tint.rgb * light, tint.a * thin * edge * near * cave * cave * open);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a = i.uv.z > 0.5 ? saturate(1 - dot(i.uv.xy, i.uv.xy)) : (1 - i.uv.x * i.uv.x) * smoothstep(1, 0.4, abs(i.uv.y));
                return half4(MixFog(i.color.rgb, i.fog), i.color.a * a);
            }
            ENDHLSL
        }
    }
}
