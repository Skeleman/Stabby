using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    public static class CloudMap
    {
        const int Size = 1024;
        const float NearM = 4000f;
        static readonly int MapId = Shader.PropertyToID("_StabbyCloudMap"), AtId = Shader.PropertyToID("_StabbyCloudMapAt"), SizeId = Shader.PropertyToID("_StabbyCloudMapSize");
        static readonly int OffId = Shader.PropertyToID("_StabbyCloudsOff"), WeatherId = Shader.PropertyToID("_StabbyWeather");
        static RenderTexture map;
        static Material material;
        static CommandBuffer cmd;

        public static void Enable()
        {
            Disable();
            var shader = Shader.Find("Hidden/Stabby/CloudMap");
            if (shader == null) return;
            material = new Material(shader) { hideFlags = HideFlags.DontSave };
            map = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBHalf, 0) { name = "CloudMap", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false, hideFlags = HideFlags.DontSave };
            map.Create();
            cmd = new CommandBuffer { name = "CloudMap" };
            RenderPipelineManager.beginCameraRendering += Draw;
        }

        public static void Disable()
        {
            RenderPipelineManager.beginCameraRendering -= Draw;
            Shader.SetGlobalVector(AtId, Vector4.zero);
            cmd?.Release();
            cmd = null;
            if (map != null) { map.Release(); Object.DestroyImmediate(map); }
            map = null;
            if (material != null) Object.DestroyImmediate(material);
            material = null;
        }

        static void Draw(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            if (Shader.GetGlobalFloat(OffId) > 0 || Shader.GetGlobalVector(WeatherId).x <= 0) return;
            var p = cam.transform.position;
            cmd.Clear();
            cmd.SetGlobalVector(AtId, new Vector4(p.x, p.z, NearM, 1));
            cmd.SetGlobalVector(SizeId, new Vector4(Size, Size, 1f / Size, 1f / Size));
            cmd.SetRenderTarget(map);
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
            cmd.SetGlobalTexture(MapId, map);
            context.ExecuteCommandBuffer(cmd);
        }
    }
}
