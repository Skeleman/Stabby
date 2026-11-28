Shader "Hidden/Stabby/CloudMap"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Viewer/Shaders/Clouds.hlsl"

            float4 _StabbyCloudMapSize;

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2 - 1, 0, 1);
            }

            float4 Frag(float4 pos : SV_POSITION) : SV_Target
            {
                float2 xz = CloudMapXZ(pos.xy * _StabbyCloudMapSize.zw);
                float r = length(xz - _StabbyCloudMapAt.xy) + _StabbyCloudMapAt.z;
                // Far texels span hundreds of metres radially, so the 200 m detail fades out there instead of aliasing into a dot grid.
                float footprint = r * r * 2 * _StabbyCloudMapSize.z / _StabbyCloudMapAt.z;
                float raw, shadeRaw;
                float body = CloudBody(xz, _StabbyCloudDrift.zw, 4, saturate(2 - footprint / 100), raw);
                float shade = CloudBody(xz, _StabbyCloudDrift.zw, 3, 0, shadeRaw);
                return float4(body, raw, shade, 0);
            }
            ENDHLSL
        }
    }
}
