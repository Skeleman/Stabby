using System.Collections.Generic;
using Stabby.World;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Stabby.Game
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GrassViewSystem : SystemBase
    {
        sealed class MeshBuffers
        {
            public GraphicsBuffer Verts, Indices;
            public int Tris, Seen;
        }

        sealed class GrassPass : ScriptableRenderPass
        {
            sealed class Data
            {
                public Material Material;
                public GraphicsBuffer Args;
            }

            Material material;
            GraphicsBuffer args;

            public GrassPass() { renderPassEvent = RenderPassEvent.AfterRenderingOpaques; }

            public void Setup(Material m, GraphicsBuffer a)
            {
                material = m;
                args = a;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var res = frame.Get<UniversalResourceData>();
                using var builder = graph.AddRasterRenderPass<Data>("Stabby Grass", out var data);
                data.Material = material;
                data.Args = args;
                builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) => ctx.cmd.DrawProceduralIndirect(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, d.Args, 0));
            }
        }

        static readonly int VertsId = Shader.PropertyToID("_Verts"), IndicesId = Shader.PropertyToID("_Indices"), ArgsId = Shader.PropertyToID("_Args"),
            BladesId = Shader.PropertyToID("_Blades"), PlanesId = Shader.PropertyToID("_Planes"), EyeId = Shader.PropertyToID("_Eye"),
            GrassAId = Shader.PropertyToID("_GrassA"), GrassBId = Shader.PropertyToID("_GrassB"), GrassCId = Shader.PropertyToID("_GrassC"), LayoutId = Shader.PropertyToID("_Layout");

        static readonly int[] TextureIds = { Shader.PropertyToID("_StabbyCoat"), Shader.PropertyToID("_StabbySlope"), Shader.PropertyToID("_StabbyBiome") };
        static readonly int[] VectorIds = { Shader.PropertyToID("_StabbySand"), Shader.PropertyToID("_StabbyFieldUV"), Shader.PropertyToID("_StabbyCoatUV"), Shader.PropertyToID("_StabbySurface"), Shader.PropertyToID("_StabbyStrataWobble") };
        static readonly int CoatAId = Shader.PropertyToID("_StabbyCoatA"), CoatBId = Shader.PropertyToID("_StabbyCoatB"), DetailOffId = Shader.PropertyToID("_StabbyDetailOff");

        public static bool Paused;

        ComputeShader scatter;
        Material material;
        GrassPass pass;
        GraphicsBuffer blades, args;
        CommandBuffer cmd;
        TerrainViewSystem terrain;
        GrassDef grass;
        int kernel, stamp, lastNear = -1;
        bool hooked;
        readonly Dictionary<Mesh, MeshBuffers> cache = new Dictionary<Mesh, MeshBuffers>();
        readonly List<Mesh> near = new List<Mesh>(), stale = new List<Mesh>();
        readonly Plane[] planes = new Plane[6];
        readonly Vector4[] planeVecs = new Vector4[6], coatA = new Vector4[4], coatB = new Vector4[4];
        readonly List<Vector4> arrayTmp = new List<Vector4>(4);
        readonly uint[] reset = { 0, 1, 0, 0 };
        readonly int[] layout = new int[4];

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) Enabled = false;
        }

        protected override void OnUpdate()
        {
            if (hooked) return;
            var lib = World.GetExistingSystemManaged<ObjectColumnsSystem>()?.Library;
            terrain = World.GetExistingSystemManaged<TerrainViewSystem>();
            if (lib == null || terrain == null) return;
            scatter = Resources.Load<ComputeShader>("GrassScatter");
            var shader = Shader.Find("Stabby/GrassBlade");
            if (scatter == null || shader == null || !SystemInfo.supportsComputeShaders)
            {
                GameStatus.Lines["~grass view"] = "grass: shaders missing or compute unsupported";
                Enabled = false;
                return;
            }
            grass = lib.Foliage.Grass;
            var wind = lib.Foliage.Wind;
            float a = wind.DirectionDeg * Mathf.Deg2Rad;
            Shader.SetGlobalVector("_StabbyWind", new Vector4(Mathf.Cos(a), Mathf.Sin(a), wind.Strength, wind.SwayHz));
            Shader.SetGlobalVector("_StabbyGust", new Vector4(1f / wind.GustScaleM, wind.GustSpeed, wind.GustStrength, 0));
            kernel = scatter.FindKernel("Scatter");
            blades = new GraphicsBuffer(GraphicsBuffer.Target.Structured, grass.MaxBlades, 64) { name = "Grass blades" };
            args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 4, 4) { name = "Grass args" };
            args.SetData(reset);
            material = new Material(shader) { name = "Grass blades" };
            material.SetBuffer(BladesId, blades);
            material.SetInteger("_GrassMax", grass.MaxBlades);
            material.SetVector("_GrassLook", new Vector4(grass.Bend, grass.TipLight, grass.Translucency, 0));
            cmd = new CommandBuffer { name = "Grass scatter" };
            pass = new GrassPass();
            RenderPipelineManager.beginCameraRendering += OnCamera;
            hooked = true;
        }

        void OnCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (Paused || cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            for (int i = 0; i < TextureIds.Length; i++) if (Shader.GetGlobalTexture(TextureIds[i]) == null) return;
            Vector3 eye = cam.transform.position;
            terrain.NearMeshes(eye, grass.ReachM + 64f, near);
            if (near.Count != lastNear)
            {
                lastNear = near.Count;
                GameStatus.Lines["~grass view"] = $"grass: {lastNear} near terrain meshes scattered per camera, {grass.MaxBlades} blade slots";
            }
            stamp++;
            cmd.Clear();
            cmd.SetBufferData(args, reset);
            for (int i = 0; i < TextureIds.Length; i++) cmd.SetComputeTextureParam(scatter, kernel, TextureIds[i], Shader.GetGlobalTexture(TextureIds[i]));
            for (int i = 0; i < VectorIds.Length; i++) cmd.SetComputeVectorParam(scatter, VectorIds[i], Shader.GetGlobalVector(VectorIds[i]));
            CopyArray(CoatAId, coatA);
            CopyArray(CoatBId, coatB);
            cmd.SetComputeVectorArrayParam(scatter, CoatAId, coatA);
            cmd.SetComputeVectorArrayParam(scatter, CoatBId, coatB);
            cmd.SetComputeFloatParam(scatter, DetailOffId, Shader.GetGlobalFloat(DetailOffId));
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++) planeVecs[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            cmd.SetComputeVectorArrayParam(scatter, PlanesId, planeVecs);
            float fpScale = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1, cam.pixelHeight);
            cmd.SetComputeVectorParam(scatter, EyeId, new Vector4(eye.x, eye.y, eye.z, fpScale));
            cmd.SetComputeVectorParam(scatter, GrassAId, new Vector4(grass.PerM2, grass.NearM, grass.ReachM, grass.SlopeMax));
            cmd.SetComputeVectorParam(scatter, GrassBId, new Vector4(grass.HeightMinM, grass.HeightMaxM, grass.WidthM, grass.CoatMin));
            cmd.SetComputeVectorParam(scatter, GrassCId, new Vector4(grass.TipLight, grass.MaxBlades, grass.Bend, 0));
            cmd.SetComputeBufferParam(scatter, kernel, ArgsId, args);
            cmd.SetComputeBufferParam(scatter, kernel, BladesId, blades);
            foreach (var mesh in near)
            {
                var mb = Buffers(mesh);
                if (mb == null) continue;
                mb.Seen = stamp;
                cmd.SetComputeBufferParam(scatter, kernel, VertsId, mb.Verts);
                cmd.SetComputeBufferParam(scatter, kernel, IndicesId, mb.Indices);
                layout[0] = mesh.GetVertexBufferStride(0);
                layout[1] = mesh.GetVertexAttributeOffset(VertexAttribute.Color);
                layout[2] = mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0);
                layout[3] = mb.Tris;
                cmd.SetComputeIntParams(scatter, LayoutId, layout);
                cmd.DispatchCompute(scatter, kernel, (mb.Tris + 63) / 64, 1, 1);
            }
            ctx.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            Sweep();
            pass.Setup(material, args);
            data.scriptableRenderer.EnqueuePass(pass);
        }

        void CopyArray(int id, Vector4[] into)
        {
            Shader.GetGlobalVectorArray(id, arrayTmp);
            for (int i = 0; i < into.Length; i++) into[i] = i < arrayTmp.Count ? arrayTmp[i] : Vector4.zero;
        }

        MeshBuffers Buffers(Mesh mesh)
        {
            if (cache.TryGetValue(mesh, out var mb)) return mb;
            if (mesh.indexFormat != IndexFormat.UInt32 || (mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0 || (mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) == 0) return null;
            mb = new MeshBuffers { Verts = mesh.GetVertexBuffer(0), Indices = mesh.GetIndexBuffer(), Tris = (int)(mesh.GetIndexCount(0) / 3) };
            cache[mesh] = mb;
            return mb;
        }

        void Sweep()
        {
            stale.Clear();
            foreach (var kv in cache)
                if (kv.Key == null || stamp - kv.Value.Seen > 240) stale.Add(kv.Key);
            foreach (var m in stale)
            {
                var mb = cache[m];
                mb.Verts?.Dispose();
                mb.Indices?.Dispose();
                cache.Remove(m);
            }
        }

        protected override void OnDestroy()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnCamera;
            foreach (var mb in cache.Values)
            {
                mb.Verts?.Dispose();
                mb.Indices?.Dispose();
            }
            cache.Clear();
            blades?.Dispose();
            args?.Dispose();
            cmd?.Release();
            if (material != null) Object.Destroy(material);
        }
    }
}
