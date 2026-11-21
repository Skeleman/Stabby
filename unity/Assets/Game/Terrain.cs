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
        public float4 Stitch;
    }

    [MaterialProperty("_StitchMask")]
    public struct TerrainStitch : IComponentData
    {
        public float Value;
    }

    struct TerrainSite
    {
        public int2 Cell;
        public AABB Box;
        public int Finest;
    }

    struct TerrainChunk
    {
        public int Shown, Built, Pending, Stitch;
        public Entity Entity;
    }

    static class TerrainLods
    {
        public const int Cells = 32, Count = 7, Coarse = Count - 1;
        public static float Step(int lod) => 0.125f * (1 << lod);
        public static int Quads(int lod) => (Cells * 8) >> lod;
        public static int Vertices(int m) => (m + 1) * (m + 1) + 4 * m;
        public static int Indices(int m) => m * m * 6 + 4 * m * 12;
    }

    struct NativeEdits : IEditList
    {
        public NativeList<FeatureEdit> Items;
        public int Count => Items.Length;
        public FeatureEdit At(int k) => Items[k];
    }

    struct GroundColumns : IColumnField
    {
        public Ground Ground;
        public float StepCells, Top;

        public float Height(int i, int k) => Ground.GridHeight(math.clamp(i * StepCells, 0, Top), math.clamp(k * StepCells, 0, Top));
    }

    struct TerrainSink : INetsSink
    {
        public NativeList<TerrainVertex> Verts;
        public NativeList<int> Tris;
        public NativeArray<int> Ints;
        public NativeArray<float> Floats;

        public void Begin(int ints, int floats)
        {
            Verts.Clear();
            Tris.Clear();
            Ints = new NativeArray<int>(ints, Allocator.Temp);
            Floats = new NativeArray<float>(floats, Allocator.Temp);
        }

        public int GetInt(int k) => Ints[k];
        public void SetInt(int k, int v) => Ints[k] = v;
        public float GetFloat(int k) => Floats[k];
        public void SetFloat(int k, float v) => Floats[k] = v;

        public int AddVertex(float x, float y, float z, float nx, float ny, float nz)
        {
            Verts.Add(new TerrainVertex { Pos = new float3(x, y, z), Normal = new float3(nx, ny, nz) });
            return Verts.Length - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            Tris.Add(a); Tris.Add(b); Tris.Add(c);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct TerrainMeshJob : IJob
    {
        public Ground Ground;
        public BlobAssetReference<TerrainLookData> Look;
        public int Cx, Cy, M;
        public float Step;
        public NativeList<TerrainVertex> Verts;
        public NativeList<int> Tris;
        public NativeArray<float3> Bounds;

        public void Execute()
        {
            ref var g = ref Ground.Blob.Value;
            ref var look = ref Look.Value;
            int n = g.N, m = M, w = m + 1;
            float cm = g.CellMeters, top = n - 1, s = Step * cm;
            var chunk = new NetsChunk { X0 = (int)(Cx / Step), Z0 = (int)(Cy / Step), Cells = m, StepM = s };
            var sink = new TerrainSink { Verts = Verts, Tris = Tris };
            var columns = new GroundColumns { Ground = Ground, StepCells = Step, Top = top };
            var edits = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            float cx0 = Cx * cm, cz0 = Cy * cm, cx1 = cx0 + m * s, cz1 = cz0 + m * s;
            for (int f = 0; f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MeshedIn(s, cx0, cz0, cx1, cz1)) edits.Items.Add(e);
            }
            if (edits.Count > 0) SurfaceNets.Edited(columns, edits, chunk, ref sink);
            else SurfaceNets.Heightfield(columns, chunk, ref sink);

            float x0m = Cx * cm, z0m = Cy * cm, x1m = x0m + m * s, z1m = z0m + m * s;
            bool stitch = Step < TerrainLods.Step(TerrainLods.Coarse);
            var coarse = new GroundColumns { Ground = Ground, StepCells = 2 * Step, Top = top };
            var near = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            for (int f = 0; stitch && f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MeshedIn(2 * s, cx0 - m * s, cz0 - m * s, cx1 + m * s, cz1 + m * s)) near.Items.Add(e);
            }
            int mc = m / 2;
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    if (i != 0 && k != 0 && i != m && k != m) continue;
                    int col = 3 * (k * w + i), ci = (chunk.X0 - 1 + i) >> 1, ck = (chunk.Z0 - 1 + k) >> 1;
                    int ic = ci - (chunk.X0 / 2 - 1), kc = ck - (chunk.Z0 / 2 - 1);
                    bool xe = i == 0 || i == m, ze = k == 0 || k == m;
                    int bits = xe && ze ? 16 << ((i == m ? 1 : 0) + (k == m ? 2 : 0)) : i == 0 ? 1 : i == m ? 2 : k == 0 ? 4 : 8;
                    for (int v = sink.Ints[col + 2], j = sink.Ints[col], e = v + sink.Ints[col + 1] - j; v < e; v++, j++)
                    {
                        var p = Verts[v];
                        if (i == 0) p.Pos.x = x0m; else if (i == m) p.Pos.x = x1m;
                        if (k == 0) p.Pos.z = z0m; else if (k == m) p.Pos.z = z1m;
                        if (stitch)
                        {
                            SurfaceNets.CoarseVertex(coarse, near, 2 * s, ci, j >> 1, ck, out float tx, out float ty, out float tz);
                            if (ic == 0) tx = x0m; else if (ic == mc) tx = x1m;
                            if (kc == 0) tz = z0m; else if (kc == mc) tz = z1m;
                            p.Stitch = new float4(tx, ty, tz, bits);
                        }
                        Verts[v] = p;
                    }
                }

            float3 lo = float.MaxValue, hi = float.MinValue;
            for (int v = 0; v < Verts.Length; v++)
            {
                var p = Verts[v];
                float gx = math.clamp(p.Pos.x / cm, 0, top), gy = math.clamp(p.Pos.z / cm, 0, top);
                int ax = math.min((int)gx, n - 2), ay = math.min((int)gy, n - 2), a = ay * n + ax;
                float off = TerrainLook.Bilinear(look.Offsets[a], look.Offsets[a + 1], look.Offsets[a + n], look.Offsets[a + n + 1], gx - ax, gy - ay);
                var col = TerrainLook.Shade(TerrainLook.Bilinear(look.Colors[a], look.Colors[a + 1], look.Colors[a + n], look.Colors[a + n + 1], gx - ax, gy - ay), p.Pos.y, p.Normal.y);
                float cover = Ground.GridHeight(gx, gy) - p.Pos.y;
                p.Color = new float4(col.xyz, 1 - 0.55f * math.smoothstep(6f, 45f, cover));
                p.Strata = new float2(off, math.max(col.w, math.smoothstep(2f, 6f, math.abs(cover))));
                Verts[v] = p;
                lo = math.min(lo, p.Pos);
                hi = math.max(hi, p.Pos);
            }

            int ringCount = 4 * m;
            var ring = new NativeArray<int>(ringCount, Allocator.Temp);
            int q = 0;
            for (int i = 0; i < m; i++) ring[q++] = i;
            for (int k = 0; k < m; k++) ring[q++] = k * w + m;
            for (int i = m; i > 0; i--) ring[q++] = m * w + i;
            for (int k = m; k > 0; k--) ring[q++] = k * w;
            float drop = s * 0.5f + 20f;
            int first = Verts.Length;
            var rim = new NativeArray<int>(ringCount, Allocator.Temp);
            for (int r = 0; r < ringCount; r++)
            {
                int c = 3 * ring[r], best = sink.Ints[c + 2];
                float gap = float.MaxValue;
                for (int u = best, e = u + sink.Ints[c + 1] - sink.Ints[c]; u < e; u++)
                {
                    var t = Verts[u].Pos;
                    float d = math.abs(Ground.GridHeight(math.clamp(t.x / cm, 0, top), math.clamp(t.z / cm, 0, top)) - t.y);
                    if (d < gap) { gap = d; best = u; }
                }
                rim[r] = best;
                var v = Verts[best];
                v.Pos.y -= drop;
                v.Stitch.y -= drop;
                Verts.Add(v);
                lo = math.min(lo, v.Pos);
            }
            for (int r = 0; r < ringCount; r++)
            {
                int a = rim[r], b = rim[(r + 1) % ringCount];
                int a2 = first + r, b2 = first + (r + 1) % ringCount;
                Tris.Add(a); Tris.Add(b); Tris.Add(a2);
                Tris.Add(b); Tris.Add(b2); Tris.Add(a2);
                Tris.Add(a); Tris.Add(a2); Tris.Add(b);
                Tris.Add(b); Tris.Add(a2); Tris.Add(b2);
            }
            Bounds[0] = lo;
            Bounds[1] = hi;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct TerrainLodJob : IJob
    {
        [ReadOnly] public NativeArray<TerrainSite> Sites;
        public NativeArray<int> Wants;
        [ReadOnly] public NativeArray<float4> Planes;
        [ReadOnly] public NativeArray<int> Grid;
        public int Side;
        public float3 Eye;
        public float Allowed, Far, Cm;

        public void Execute()
        {
            for (int c = 0; c < Sites.Length; c++)
            {
                var site = Sites[c];
                var box = site.Box;
                float d = math.max(1f, math.distance(Eye, math.clamp(Eye, box.Min, box.Max)));
                bool vis = d < Far;
                for (int p = 0; p < Planes.Length && vis; p++)
                    vis = math.dot(Planes[p].xyz, box.Center) + Planes[p].w + math.dot(math.abs(Planes[p].xyz), box.Extents) >= 0;
                int lod = -1;
                if (vis)
                {
                    float allowed = Allowed * d;
                    lod = site.Finest;
                    while (lod < TerrainLods.Coarse && TerrainLods.Step(lod + 1) * Cm <= allowed) lod++;
                }
                Wants[c] = lod;
            }
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int c = 0; c < Sites.Length; c++)
                {
                    int want = Wants[c];
                    if (want < 0) continue;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int n = Neighbour(Sites[c].Cell, dx, dz);
                            if (n < 0 || Wants[n] < 0 || Wants[n] + 1 >= want) continue;
                            want = Wants[n] + 1;
                            changed = true;
                        }
                    Wants[c] = want;
                }
            }
        }

        int Neighbour(int2 cell, int dx, int dz)
        {
            int x = cell.x / TerrainLods.Cells + dx, z = cell.y / TerrainLods.Cells + dz;
            return x < 0 || z < 0 || x >= Side || z >= Side ? -1 : Grid[z * Side + x];
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
            public List<AABB> Boxes;
            public List<MeshParts> Lakes, Rivers;
            public MeshParts Sea;
            public StrataLook Strata;
        }

        struct Pending
        {
            public int Chunk, Lod, Vertices;
            public JobHandle Handle;
            public NativeList<TerrainVertex> Verts;
            public NativeList<int> Tris;
            public NativeArray<float3> Bounds;
        }

        Task<Prepared> preparing;
        Prepared prepared;
        BlobAssetReference<TerrainLookData> look;
        NativeArray<TerrainChunk> chunks;
        NativeArray<TerrainSite> sites;
        NativeArray<int> wants;
        NativeArray<float4> lodPlanes;
        JobHandle lodHandle;
        float3 lodEye;
        quaternion lodTurn;
        float lodFov = -1f;
        NativeArray<int> grid;
        int side;
        Mesh[] meshes;
        BatchMeshID[] meshIds;
        readonly List<Pending> pending = new List<Pending>();
        readonly List<Mesh> statics = new List<Mesh>();
        readonly Plane[] planes = new Plane[6];
        readonly int[] lodCounts = new int[TerrainLods.Count];
        readonly Stopwatch frame = new Stopwatch(), since = new Stopwatch();
        EntitiesGraphicsSystem graphics;
        BatchMaterialID material;
        Ground ground;
        int coarseApplied, waterApplied, frameVertices, built, evicted;
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
            ref var src = ref ground.Blob.Value.Edits;
            var edits = new FeatureEdit[src.Length];
            for (int f = 0; f < edits.Length; f++) edits[f] = src[f];
            since.Restart();
            preparing = Task.Run(() => Prepare(world, defs, materials, edits));
        }

        static Prepared Prepare(GeneratedWorld world, IReadOnlyList<BiomeDef> defs, string materials, FeatureEdit[] edits)
        {
            int n = world.Config.Size, cells = TerrainLods.Cells;
            float cm = world.Config.CellMeters, coarse = TerrainLods.Step(TerrainLods.Coarse), skirt = coarse * cm * 0.5f + 20f, w = cells * cm;
            var detail = new TerrainDetail(world);
            var all = new EditArray { Items = edits };
            var p = new Prepared { Cells = new List<int2>(), Boxes = new List<AABB>(), Lakes = new List<MeshParts>() };
            for (int cy = 0; cy < n; cy += cells)
                for (int cx = 0; cx < n; cx += cells)
                    if (HasLand(world, cx, cy))
                    {
                        p.Cells.Add(new int2(cx, cy));
                        detail.ChunkRange(all, cm, cx, cy, cells, coarse, out float lo, out float hi);
                        p.Boxes.Add(new MinMaxAABB { Min = new float3(cx * cm, lo - skirt, cy * cm), Max = new float3(cx * cm + w, hi, cy * cm + w) });
                        p.Lakes.Add(WaterGeometry.Lakes(world, cx, cy, cells));
                    }
            p.Rivers = WaterGeometry.Rivers(world, detail);
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
            GameStatus.Lines["terrain~prep"] = $"terrain prepared at {since.ElapsedMilliseconds} ms";
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
            sites = new NativeArray<TerrainSite>(count, Allocator.Persistent);
            wants = new NativeArray<int>(count, Allocator.Persistent);
            lodPlanes = new NativeArray<float4>(6, Allocator.Persistent);
            for (int k = 0; k < count; k++) wants[k] = -1;
            side = (ground.Blob.Value.N + TerrainLods.Cells - 1) / TerrainLods.Cells;
            grid = new NativeArray<int>(side * side, Allocator.Persistent);
            for (int k = 0; k < grid.Length; k++) grid[k] = -1;
            meshes = new Mesh[count * TerrainLods.Count];
            meshIds = new BatchMeshID[count * TerrainLods.Count];
            for (int c = 0; c < count; c++)
            {
                var cell = prepared.Cells[c];
                sites[c] = new TerrainSite { Cell = cell, Box = prepared.Boxes[c], Finest = Featured(cell) ? 0 : 1 };
                chunks[c] = new TerrainChunk { Shown = -1, Entity = Entity.Null };
                grid[cell.y / TerrainLods.Cells * side + cell.x / TerrainLods.Cells] = c;
            }
        }

        bool Featured(int2 cell)
        {
            ref var g = ref ground.Blob.Value;
            float x0 = cell.x * g.CellMeters, z0 = cell.y * g.CellMeters, x1 = x0 + TerrainLods.Cells * g.CellMeters, z1 = z0 + TerrainLods.Cells * g.CellMeters;
            for (int f = 0; f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MaxX > x0 && e.MinX < x1 && e.MaxZ > z0 && e.MinZ < z1) return true;
            }
            return false;
        }

        void Coarse()
        {
            for (; nextCoarse < chunks.Length && pending.Count < BuildBudget; nextCoarse++) Request(nextCoarse, TerrainLods.Coarse);
        }

        void Request(int c, int lod)
        {
            var ch = chunks[c];
            var site = sites[c];
            int m = TerrainLods.Quads(lod), verts = TerrainLods.Vertices(m);
            var vl = new NativeList<TerrainVertex>(verts, Allocator.Persistent);
            var tl = new NativeList<int>(TerrainLods.Indices(m), Allocator.Persistent);
            var bounds = new NativeArray<float3>(2, Allocator.Persistent);
            var job = new TerrainMeshJob { Ground = ground, Look = look, Cx = site.Cell.x, Cy = site.Cell.y, M = m, Step = TerrainLods.Step(lod), Verts = vl, Tris = tl, Bounds = bounds };
            pending.Add(new Pending { Chunk = c, Lod = lod, Vertices = verts, Handle = job.Schedule(), Verts = vl, Tris = tl, Bounds = bounds });
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
            var site = sites[p.Chunk];
            float3 lo = p.Bounds[0], hi = p.Bounds[1];
            p.Bounds.Dispose();
            var mesh = new Mesh { name = $"Terrain {site.Cell.x},{site.Cell.y} x{TerrainLods.Step(p.Lod)}" };
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferParams(p.Verts.Length, Layout);
            mesh.SetVertexBufferData(p.Verts.AsArray(), 0, 0, p.Verts.Length, 0, quiet);
            mesh.SetIndexBufferParams(p.Tris.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(p.Tris.AsArray(), 0, 0, p.Tris.Length, quiet);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, p.Tris.Length) { bounds = new Bounds((lo + hi) * 0.5f, hi - lo), vertexCount = p.Verts.Length }, quiet);
            mesh.bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            p.Verts.Dispose();
            p.Tris.Dispose();
            int slot = p.Chunk * TerrainLods.Count + p.Lod;
            meshes[slot] = mesh;
            meshIds[slot] = graphics.RegisterMesh(mesh);
            built++;
            ch.Pending &= ~(1 << p.Lod);
            ch.Built |= 1 << p.Lod;
            MinMaxAABB box = site.Box;
            if (math.any(lo < box.Min - 0.01f) || math.any(hi > box.Max + 0.01f)) UnityEngine.Debug.LogWarning($"terrain chunk {site.Cell} lod {p.Lod} mesh {lo}..{hi} outside bounds {box.Min}..{box.Max}");
            if (ch.Entity == Entity.Null)
            {
                ch.Entity = Spawn(meshIds[slot], site.Box);
                EntityManager.AddComponentData(ch.Entity, new TerrainStitch());
                ch.Shown = p.Lod;
                coarseApplied++;
            }
            chunks[p.Chunk] = ch;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        };

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
            GameStatus.Lines["terrain~ready"] = $"terrain ready at {since.ElapsedMilliseconds} ms";
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
            lodHandle.Complete();
            int visible = 0, behind = 0;
            System.Array.Clear(lodCounts, 0, lodCounts.Length);
            for (int c = 0; c < chunks.Length; c++)
            {
                var ch = chunks[c];
                int want = wants[c];
                if (want < 0) continue;
                for (int l = 0; l < want - 1; l++)
                    if ((ch.Built & (1 << l)) != 0) { Evict(c * TerrainLods.Count + l); ch.Built &= ~(1 << l); chunks[c] = ch; }
                if ((ch.Built & (1 << want)) == 0 && (ch.Pending & (1 << want)) == 0 && pending.Count < BuildBudget) { Request(c, want); ch = chunks[c]; }
                int lod = want;
                if ((ch.Built & (1 << want)) == 0) behind++;
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
            for (int c = 0; c < chunks.Length; c++)
            {
                var ch = chunks[c];
                if (ch.Shown < 0 || ch.Entity == Entity.Null) continue;
                int mask = StitchMask(c);
                if (mask == ch.Stitch) continue;
                EntityManager.SetComponentData(ch.Entity, new TerrainStitch { Value = mask });
                ch.Stitch = mask;
                chunks[c] = ch;
            }
            GameStatus.TerrainBehind = behind;
            ScheduleLods(cam);
            GameStatus.Lines["terrain"] = $"terrain chunks {visible}/{chunks.Length} visible, {built} meshes built, {built - evicted} live, by LOD {string.Join("/", lodCounts)}";
        }

        void ScheduleLods(Camera cam)
        {
            var t = cam.transform;
            float3 eye = t.position;
            quaternion turn = t.rotation;
            if (math.distancesq(eye, lodEye) < 0.0001f && math.abs(math.dot(turn.value, lodTurn.value)) > 0.999999f && cam.fieldOfView == lodFov) return;
            lodEye = eye;
            lodTurn = turn;
            lodFov = cam.fieldOfView;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int k = 0; k < 6; k++) lodPlanes[k] = new float4(planes[k].normal, planes[k].distance);
            float k2 = 2f * math.tan(math.radians(cam.fieldOfView) * 0.5f);
            lodHandle = new TerrainLodJob { Sites = sites, Wants = wants, Grid = grid, Side = side, Planes = lodPlanes, Eye = eye, Allowed = TargetQuadScreen * k2, Far = cam.farClipPlane, Cm = ground.Blob.Value.CellMeters }.Schedule();
        }

        int StitchMask(int c)
        {
            int x = sites[c].Cell.x / TerrainLods.Cells, z = sites[c].Cell.y / TerrainLods.Cells, mask = 0, lod = chunks[c].Shown;
            if (Coarser(x - 1, z, lod)) mask |= 1;
            if (Coarser(x + 1, z, lod)) mask |= 2;
            if (Coarser(x, z - 1, lod)) mask |= 4;
            if (Coarser(x, z + 1, lod)) mask |= 8;
            for (int q = 0; q < 4; q++)
            {
                int dx = (q & 1) == 0 ? -1 : 1, dz = (q & 2) == 0 ? -1 : 1;
                if (Coarser(x + dx, z, lod) || Coarser(x, z + dz, lod) || Coarser(x + dx, z + dz, lod)) mask |= 16 << q;
            }
            return mask;
        }

        bool Coarser(int x, int z, int lod)
        {
            if (x < 0 || z < 0 || x >= side || z >= side) return false;
            int n = grid[z * side + x];
            return n >= 0 && chunks[n].Shown > lod;
        }

        void Evict(int slot)
        {
            graphics.UnregisterMesh(meshIds[slot]);
            Object.Destroy(meshes[slot]);
            meshes[slot] = null;
            evicted++;
        }

        protected override void OnDestroy()
        {
            lodHandle.Complete();
            foreach (var p in pending)
            {
                p.Handle.Complete();
                p.Verts.Dispose();
                p.Tris.Dispose();
                p.Bounds.Dispose();
            }
            pending.Clear();
            Owned.Release(preparing, p => p.Look.Dispose());
            for (int k = 0; meshes != null && k < meshes.Length; k++)
                if (meshes[k] != null) Object.Destroy(meshes[k]);
            foreach (var mesh in statics) Object.Destroy(mesh);
            if (chunks.IsCreated) chunks.Dispose();
            if (grid.IsCreated) grid.Dispose();
            if (sites.IsCreated) sites.Dispose();
            if (wants.IsCreated) wants.Dispose();
            if (lodPlanes.IsCreated) lodPlanes.Dispose();
            if (look.IsCreated) look.Dispose();
        }
    }
}
