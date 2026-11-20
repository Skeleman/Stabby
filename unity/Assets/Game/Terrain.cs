using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Stabby.Viewer;
using Stabby.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    public struct TerrainLookData
    {
        public int N;
        public BlobArray<float3> Colors;
        public BlobArray<float> Offsets;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TerrainVertex
    {
        public float3 Pos, Normal;
        public float4 Color;
        public float2 Strata;
    }

    struct TerrainChunk
    {
        public int2 Cell;
        public AABB Box;
        public int Want, Shown, Built, Pending;
        public Entity Entity;
    }

    static class TerrainLods
    {
        public const int Cells = 32, Count = 6, Coarse = Count - 1;
        public static float Step(int lod) => 0.25f * (1 << lod);
        public static int Quads(int lod) => (Cells * 4) >> lod;
        public static int Vertices(int m) => (m + 1) * (m + 1) + 4 * m;
        public static int Indices(int m) => m * m * 6 + 4 * m * 12;
    }

    [BurstCompile(CompileSynchronously = true)]
    struct TerrainMeshJob : IJob
    {
        public Ground Ground;
        public BlobAssetReference<TerrainLookData> Look;
        public int Cx, Cy, M;
        public float Step;
        public Mesh.MeshData Mesh;
        public NativeArray<float3> Bounds;

        public void Execute()
        {
            ref var g = ref Ground.Blob.Value;
            ref var look = ref Look.Value;
            int n = g.N, m = M, w = m + 1, gs = m + 3;
            float cm = g.CellMeters, top = n - 1;
            var hs = new NativeArray<float>(gs * gs, Allocator.Temp);
            for (int j = 0; j < gs; j++)
                for (int i = 0; i < gs; i++)
                    hs[j * gs + i] = Ground.GridHeight(math.clamp(Cx + (i - 1) * Step, 0, top), math.clamp(Cy + (j - 1) * Step, 0, top));

            var verts = Mesh.GetVertexData<TerrainVertex>();
            var idx = Mesh.GetIndexData<ushort>();
            float3 lo = float.MaxValue, hi = float.MinValue;
            for (int j = 0; j <= m; j++)
                for (int i = 0; i <= m; i++)
                {
                    float gx = math.min(Cx + i * Step, top), gy = math.min(Cy + j * Step, top);
                    int c = (j + 1) * gs + i + 1;
                    float h = hs[c];
                    var nrm = math.normalize(new float3(hs[c - 1] - hs[c + 1], 2 * Step * cm, hs[c - gs] - hs[c + gs]));
                    int x0 = math.min((int)gx, n - 2), y0 = math.min((int)gy, n - 2), a = y0 * n + x0;
                    float off = TerrainLook.Bilinear(look.Offsets[a], look.Offsets[a + 1], look.Offsets[a + n], look.Offsets[a + n + 1], gx - x0, gy - y0);
                    var col = TerrainLook.Shade(TerrainLook.Bilinear(look.Colors[a], look.Colors[a + 1], look.Colors[a + n], look.Colors[a + n + 1], gx - x0, gy - y0), h, nrm.y);
                    var p = new float3(gx * cm, h, gy * cm);
                    verts[j * w + i] = new TerrainVertex { Pos = p, Normal = nrm, Color = new float4(col.xyz, 1), Strata = new float2(off, col.w) };
                    lo = math.min(lo, p);
                    hi = math.max(hi, p);
                }
            int t = 0;
            for (int j = 0; j < m; j++)
                for (int i = 0; i < m; i++)
                {
                    int a = j * w + i;
                    idx[t++] = (ushort)a; idx[t++] = (ushort)(a + w); idx[t++] = (ushort)(a + 1);
                    idx[t++] = (ushort)(a + 1); idx[t++] = (ushort)(a + w); idx[t++] = (ushort)(a + w + 1);
                }

            int ringCount = 4 * m, first = w * w;
            float drop = Step * cm * 0.5f + 20f;
            var ring = new NativeArray<int>(ringCount, Allocator.Temp);
            int q = 0;
            for (int i = 0; i < m; i++) ring[q++] = i;
            for (int j = 0; j < m; j++) ring[q++] = j * w + m;
            for (int i = m; i > 0; i--) ring[q++] = m * w + i;
            for (int j = m; j > 0; j--) ring[q++] = j * w;
            for (int k = 0; k < ringCount; k++)
            {
                var v = verts[ring[k]];
                v.Pos.y -= drop;
                verts[first + k] = v;
                lo = math.min(lo, v.Pos);
            }
            for (int k = 0; k < ringCount; k++)
            {
                ushort a = (ushort)ring[k], b = (ushort)ring[(k + 1) % ringCount], a2 = (ushort)(first + k), b2 = (ushort)(first + (k + 1) % ringCount);
                idx[t++] = a; idx[t++] = b; idx[t++] = a2;
                idx[t++] = b; idx[t++] = b2; idx[t++] = a2;
                idx[t++] = a; idx[t++] = a2; idx[t++] = b;
                idx[t++] = b; idx[t++] = a2; idx[t++] = b2;
            }
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, t) { bounds = new Bounds((lo + hi) * 0.5f, hi - lo), vertexCount = verts.Length },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            Bounds[0] = lo;
            Bounds[1] = hi;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct TerrainLodJob : IJob
    {
        public NativeArray<TerrainChunk> Chunks;
        [ReadOnly] public NativeArray<float4> Planes;
        public float3 Eye;
        public float Allowed, Far, Cm;

        public void Execute()
        {
            for (int c = 0; c < Chunks.Length; c++)
            {
                var ch = Chunks[c];
                var box = ch.Box;
                float d = math.max(1f, math.distance(Eye, math.clamp(Eye, box.Min, box.Max)));
                bool vis = d < Far;
                for (int p = 0; p < Planes.Length && vis; p++)
                    vis = math.dot(Planes[p].xyz, box.Center) + Planes[p].w + math.dot(math.abs(Planes[p].xyz), box.Extents) >= 0;
                int lod = -1;
                if (vis)
                {
                    float allowed = Allowed * d;
                    lod = 0;
                    while (lod < TerrainLods.Coarse && TerrainLods.Step(lod + 1) * Cm <= allowed) lod++;
                }
                ch.Want = lod;
                Chunks[c] = ch;
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TerrainViewSystem : SystemBase
    {
        public float TargetQuadScreen = 0.01f;
        public int ApplyVertices = 150000;
        public float ApplyMs = 4f;

        sealed class Prepared
        {
            public BlobBuilder Look;
            public List<int2> Cells;
            public List<MeshParts> Lakes, Rivers;
            public MeshParts Sea;
            public StrataLook Strata;
        }

        struct Pending
        {
            public int Chunk, Lod, Vertices;
            public JobHandle Handle;
            public Mesh.MeshDataArray Data;
            public NativeArray<float3> Bounds;
        }

        Task<Prepared> preparing;
        Prepared prepared;
        BlobAssetReference<TerrainLookData> look;
        NativeArray<TerrainChunk> chunks;
        Mesh[] meshes;
        BatchMeshID[] meshIds;
        readonly List<Pending> pending = new List<Pending>();
        readonly List<Mesh> statics = new List<Mesh>();
        readonly Plane[] planes = new Plane[6];
        readonly int[] lodCounts = new int[TerrainLods.Count];
        readonly Stopwatch frame = new Stopwatch();
        EntitiesGraphicsSystem graphics;
        BatchMaterialID material;
        Ground ground;
        int coarseApplied, waterApplied, frameVertices, built;
        bool ready;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) Enabled = false;
        }

        protected override void OnUpdate()
        {
            frame.Restart();
            frameVertices = 0;
            if (look.IsCreated) { Collect(); Coarse(); Statics(); Lods(); }
            else if (preparing == null) Begin();
            else if (preparing.IsCompleted) Load();
            float ms = (float)frame.Elapsed.TotalMilliseconds;
            if (ms > worstMs) { worstMs = ms; GameStatus.Lines["terrain~"] = $"terrain worst update {worstMs:F1} ms ({stage})"; }
        }

        int nextCoarse;
        static int BuildBudget => math.max(1, JobsUtility.JobWorkerCount / 2);
        float worstMs;
        string stage => !look.IsCreated ? "load" : !ready ? "initial" : "lod";

        void Begin()
        {
            var map = World.GetExistingSystemManaged<ClientMapSystem>();
            if (map.Map == null || !SystemAPI.TryGetSingleton(out ground)) return;
            var world = map.Map;
            var defs = map.Defs;
            string materials = MaterialFile.DefaultPath;
            preparing = Task.Run(() => Prepare(world, defs, materials));
        }

        static Prepared Prepare(GeneratedWorld world, IReadOnlyList<BiomeDef> defs, string materials)
        {
            int n = world.Config.Size, cells = TerrainLods.Cells;
            var p = new Prepared { Cells = new List<int2>(), Lakes = new List<MeshParts>() };
            for (int cy = 0; cy < n; cy += cells)
                for (int cx = 0; cx < n; cx += cells)
                    if (HasLand(world, cx, cy))
                    {
                        p.Cells.Add(new int2(cx, cy));
                        p.Lakes.Add(WaterGeometry.Lakes(world, cx, cy, cells));
                    }
            p.Rivers = WaterGeometry.Rivers(world, new TerrainDetail(world));
            p.Sea = WaterGeometry.Sea(n, world.Config.CellMeters);
            var colors = TerrainLook.BaseColors(world, defs);
            p.Look = new BlobBuilder(Allocator.Persistent);
            ref var root = ref p.Look.ConstructRoot<TerrainLookData>();
            root.N = n;
            var arr = p.Look.Allocate(ref root.Colors, colors.Length);
            for (int i = 0; i < colors.Length; i++) arr[i] = colors[i];
            p.Strata = TerrainLook.Strata(world, MaterialFile.Load(materials));
            var offs = p.Look.Allocate(ref root.Offsets, p.Strata.Offsets.Length);
            for (int i = 0; i < offs.Length; i++) offs[i] = p.Strata.Offsets[i];
            return p;
        }

        static bool HasLand(GeneratedWorld w, int cx, int cy)
        {
            int n = w.Config.Size;
            for (int y = cy; y <= math.min(cy + TerrainLods.Cells, n - 1); y++)
                for (int x = cx; x <= math.min(cx + TerrainLods.Cells, n - 1); x++)
                    if (!w.Sea[y * n + x]) return true;
            return false;
        }

        void Load()
        {
            var task = preparing;
            preparing = null;
            if (!task.IsCompletedSuccessfully) { UnityEngine.Debug.LogException(task.Exception); return; }
            prepared = task.Result;
            look = prepared.Look.CreateBlobAssetReference<TerrainLookData>(Allocator.Persistent);
            prepared.Look.Dispose();
            TerrainLook.ApplyStrata(prepared.Strata);
            Shader.SetGlobalColor("_StabbySky", new Color(0.62f, 0.74f, 0.86f) * 0.9f);
            Shader.SetGlobalColor("_StabbyGround", new Color(0.3f, 0.27f, 0.22f));
            graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            var scene = Object.FindFirstObjectByType<GameScene>();
            material = graphics.RegisterMaterial(scene != null ? scene.TerrainMaterial : null);
            int count = prepared.Cells.Count;
            chunks = new NativeArray<TerrainChunk>(count, Allocator.Persistent);
            meshes = new Mesh[count * TerrainLods.Count];
            meshIds = new BatchMeshID[count * TerrainLods.Count];
            for (int c = 0; c < count; c++)
            {
                chunks[c] = new TerrainChunk { Cell = prepared.Cells[c], Want = -1, Shown = -1, Entity = Entity.Null };
            }
        }

        void Coarse()
        {
            for (; nextCoarse < chunks.Length && pending.Count < BuildBudget; nextCoarse++) Request(nextCoarse, TerrainLods.Coarse);
        }

        void Request(int c, int lod)
        {
            var ch = chunks[c];
            int m = TerrainLods.Quads(lod), verts = TerrainLods.Vertices(m);
            var data = Mesh.AllocateWritableMeshData(1);
            var md = data[0];
            md.SetVertexBufferParams(verts,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2));
            md.SetIndexBufferParams(TerrainLods.Indices(m), IndexFormat.UInt16);
            md.subMeshCount = 1;
            var bounds = new NativeArray<float3>(2, Allocator.Persistent);
            var job = new TerrainMeshJob { Ground = ground, Look = look, Cx = ch.Cell.x, Cy = ch.Cell.y, M = m, Step = TerrainLods.Step(lod), Mesh = md, Bounds = bounds };
            pending.Add(new Pending { Chunk = c, Lod = lod, Vertices = verts, Handle = job.Schedule(), Data = data, Bounds = bounds });
            ch.Pending |= 1 << lod;
            chunks[c] = ch;
        }

        bool Spend(int vertices)
        {
            if (frameVertices > 0 && (frameVertices + vertices > ApplyVertices || frame.Elapsed.TotalMilliseconds > ApplyMs)) return false;
            frameVertices += vertices;
            return true;
        }

        void Collect()
        {
            for (int k = 0; k < pending.Count; k++)
            {
                var p = pending[k];
                if (!p.Handle.IsCompleted) continue;
                if (!Spend(p.Vertices)) return;
                p.Handle.Complete();
                pending.RemoveAt(k--);
                Apply(p);
            }
        }

        void Apply(Pending p)
        {
            var ch = chunks[p.Chunk];
            float3 lo = p.Bounds[0], hi = p.Bounds[1];
            p.Bounds.Dispose();
            var mesh = new Mesh { name = $"Terrain {ch.Cell.x},{ch.Cell.y} x{TerrainLods.Step(p.Lod)}" };
            Mesh.ApplyAndDisposeWritableMeshData(p.Data, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            int slot = p.Chunk * TerrainLods.Count + p.Lod;
            meshes[slot] = mesh;
            meshIds[slot] = graphics.RegisterMesh(mesh);
            built++;
            ch.Pending &= ~(1 << p.Lod);
            ch.Built |= 1 << p.Lod;
            if (ch.Entity == Entity.Null)
            {
                float w = TerrainLods.Cells * ground.Blob.Value.CellMeters;
                var min = new float3(ch.Cell.x * ground.Blob.Value.CellMeters, lo.y - 40f, ch.Cell.y * ground.Blob.Value.CellMeters);
                ch.Box = new MinMaxAABB { Min = min, Max = new float3(min.x + w, hi.y + 40f, min.z + w) };
                ch.Entity = Spawn(meshIds[slot], ch.Box);
                ch.Shown = p.Lod;
                coarseApplied++;
            }
            chunks[p.Chunk] = ch;
        }

        Entity Spawn(BatchMeshID mesh, AABB box)
        {
            var em = EntityManager;
            var e = em.CreateEntity();
            RenderMeshUtility.AddComponents(e, em, new RenderMeshDescription(ShadowCastingMode.Off), new MaterialMeshInfo(material, mesh));
            em.SetComponentData(e, new RenderBounds { Value = box });
            em.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
            return e;
        }

        void Statics()
        {
            if (ready || coarseApplied < chunks.Length) return;
            var lakes = prepared.Lakes;
            int total = lakes.Count + prepared.Rivers.Count;
            while (waterApplied < total)
            {
                var part = waterApplied < lakes.Count ? lakes[waterApplied] : prepared.Rivers[waterApplied - lakes.Count];
                if (part != null && !Spend(part.Verts.Length)) return;
                if (part != null) AddStatic(part);
                waterApplied++;
            }
            AddStatic(prepared.Sea);
            prepared = null;
            ready = true;
            GameStatus.TerrainBuilt = true;
        }

        void AddStatic(MeshParts part)
        {
            var mesh = WaterGeometry.ToMesh(part);
            var id = graphics.RegisterMesh(mesh);
            statics.Add(mesh);
            var b = mesh.bounds;
            Spawn(id, new AABB { Center = b.center, Extents = b.extents });
        }

        void Lods()
        {
            var cam = Camera.main;
            if (cam == null || coarseApplied < chunks.Length) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var pl = new NativeArray<float4>(6, Allocator.TempJob);
            for (int k = 0; k < 6; k++) pl[k] = new float4(planes[k].normal, planes[k].distance);
            float k2 = 2f * math.tan(math.radians(cam.fieldOfView) * 0.5f);
            new TerrainLodJob { Chunks = chunks, Planes = pl, Eye = cam.transform.position, Allowed = TargetQuadScreen * k2, Far = cam.farClipPlane, Cm = ground.Blob.Value.CellMeters }.Run();
            pl.Dispose();
            int visible = 0;
            System.Array.Clear(lodCounts, 0, lodCounts.Length);
            for (int c = 0; c < chunks.Length; c++)
            {
                var ch = chunks[c];
                if (ch.Want < 0) continue;
                int want = ch.Want;
                if ((ch.Built & (1 << want)) == 0 && (ch.Pending & (1 << want)) == 0 && pending.Count < BuildBudget) { Request(c, want); ch = chunks[c]; }
                int lod = want;
                while ((ch.Built & (1 << lod)) == 0) lod++;
                if (lod != ch.Shown)
                {
                    EntityManager.SetComponentData(ch.Entity, new MaterialMeshInfo(material, meshIds[c * TerrainLods.Count + lod]));
                    ch.Shown = lod;
                    chunks[c] = ch;
                }
                visible++;
                lodCounts[lod]++;
            }
            GameStatus.Lines["terrain"] = $"terrain chunks {visible}/{chunks.Length} visible, {built} meshes, by LOD {string.Join("/", lodCounts)}";
        }

        protected override void OnDestroy()
        {
            foreach (var p in pending)
            {
                p.Handle.Complete();
                p.Data.Dispose();
                p.Bounds.Dispose();
            }
            pending.Clear();
            if (preparing != null && preparing.IsCompletedSuccessfully) preparing.Result.Look.Dispose();
            for (int k = 0; meshes != null && k < meshes.Length; k++)
                if (meshes[k] != null) Object.Destroy(meshes[k]);
            foreach (var mesh in statics) Object.Destroy(mesh);
            if (chunks.IsCreated) chunks.Dispose();
            if (look.IsCreated) look.Dispose();
        }
    }
}
