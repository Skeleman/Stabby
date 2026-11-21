using System;
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
        public float4 Strata;
        public float4 Stitch;
        public half4 StitchNormal, StitchColor;
    }

    [MaterialProperty("_StitchMask")]
    public struct TerrainStitch : IComponentData
    {
        public float4 Value;
    }

    struct TerrainNode
    {
        public int Built, Pending, Drawn, Stitch, Vertices;
        public float MorphEnd;
        public Entity Entity;
        public BatchMeshID Mesh;
    }

    struct TerrainLeaf : IComparable<TerrainLeaf>
    {
        public int Key;
        public float Dist;
        public int CompareTo(TerrainLeaf o) => Dist.CompareTo(o.Dist);
    }

    struct TerrainTree
    {
        public const int Levels = NearField.NodeLevels, Root = Levels - 1, Quads = NearField.NodeQuads;
        public const int Vertices = (Quads + 1) * (Quads + 1) + 4 * Quads, Indices = Quads * Quads * 6 + 4 * Quads * 12;
        [ReadOnly] public NativeArray<int> Offset, Dim;
        [ReadOnly] public NativeArray<float2> Range;
        [ReadOnly] public NativeArray<byte> Land;
        public float Top;

        public static float Step(int l) => NearField.NodeStep(l);
        public static float Size(int l) => NearField.NodeSize(l);
        public int Key(int l, int ix, int iz) => Offset[l] + iz * Dim[l] + ix;

        public int Level(int key)
        {
            int l = 0;
            while (l < Root && key >= Offset[l + 1]) l++;
            return l;
        }

        public int2 Cell(int key, int l)
        {
            int r = key - Offset[l];
            return new int2(r % Dim[l], r / Dim[l]);
        }

        public int Child(int key, int q)
        {
            int l = Level(key);
            if (l == 0) return -1;
            var c = Cell(key, l) * 2 + new int2(q & 1, q >> 1);
            return c.x >= Dim[l - 1] || c.y >= Dim[l - 1] ? -1 : Key(l - 1, c.x, c.y);
        }

        public int Parent(int key)
        {
            int l = Level(key);
            if (l == Root) return -1;
            var c = Cell(key, l) / 2;
            return Key(l + 1, c.x, c.y);
        }

        public int At(float x, float z, int l) => x < 0 || z < 0 || x >= Top || z >= Top ? -1 : Key(l, (int)(x / Size(l)), (int)(z / Size(l)));

        public bool IsLand(int key) => key >= 0 && Land[key] != 0;

        public AABB Box(int key)
        {
            int l = Level(key);
            var c = Cell(key, l);
            float s = Size(l), lo = float.MaxValue, hi = float.MinValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = c.x + dx, z = c.y + dz;
                    if (x < 0 || z < 0 || x >= Dim[l] || z >= Dim[l]) continue;
                    var r = Range[Key(l, x, z)];
                    lo = math.min(lo, r.x);
                    hi = math.max(hi, r.y);
                }
            float skirt = Step(l) * 0.5f + 20f;
            return new MinMaxAABB { Min = new float3(c.x * s, lo - skirt - Step(l) - 1f, c.y * s), Max = new float3(c.x * s + s, hi + Step(l) + 1f, c.y * s + s) };
        }

        public float Distance(int key, float3 eye)
        {
            var b = Box(key);
            return math.max(1f, math.distance(eye, math.clamp(eye, b.Min, b.Max)));
        }
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
        public float StepM, Top;
        public bool Smooth;

        public float At(float x, float z)
        {
            x = math.clamp(x, 0, Top);
            z = math.clamp(z, 0, Top);
            return Smooth ? NearField.Smooth(Ground.Cells, x, z) : Ground.Surface(x, z);
        }

        public float Height(int i, int k) => At(i * StepM, k * StepM);
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
        public int Ix, Iz, Level;
        public bool Smooth;
        public NativeList<TerrainVertex> Verts;
        public NativeList<int> Tris;
        public NativeArray<float3> Bounds;

        float Cover(NativeEdits edits, GroundColumns columns, float3 p)
        {
            for (int q = 0; q < edits.Count; q++)
            {
                var e = edits.Items[q];
                if (p.x >= e.MinX && p.x <= e.MaxX && p.z >= e.MinZ && p.z <= e.MaxZ) return columns.At(p.x, p.z) - p.y;
            }
            return 0;
        }

        public void Execute()
        {
            ref var g = ref Ground.Blob.Value;
            ref var look = ref Look.Value;
            int n = look.N, m = TerrainTree.Quads, w = m + 1;
            float cm = g.CellMeters, top = n - 1, s = TerrainTree.Step(Level);
            var chunk = new NetsChunk { X0 = Ix * m, Z0 = Iz * m, Cells = m, StepM = s };
            var sink = new TerrainSink { Verts = Verts, Tris = Tris };
            var columns = new GroundColumns { Ground = Ground, StepM = s, Top = g.Top, Smooth = Smooth };
            var edits = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            float x0m = chunk.X0 * s, z0m = chunk.Z0 * s, x1m = x0m + m * s, z1m = z0m + m * s;
            for (int f = 0; f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MeshedIn(s, x0m, z0m, x1m, z1m)) edits.Items.Add(e);
            }
            if (edits.Count > 0) SurfaceNets.Edited(columns, edits, chunk, ref sink);
            else SurfaceNets.Heightfield(columns, chunk, ref sink);

            bool stitch = Level < TerrainTree.Root;
            var coarse = new GroundColumns { Ground = Ground, StepM = 2 * s, Top = g.Top, Smooth = Smooth };
            var near = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            for (int f = 0; stitch && f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MeshedIn(2 * s, x0m - m * s, z0m - m * s, x1m + m * s, z1m + m * s)) near.Items.Add(e);
            }

            int ringCount = 4 * m;
            var ring = new NativeArray<int>(ringCount, Allocator.Temp);
            int r0 = 0;
            for (int i = 0; i < m; i++) ring[r0++] = i;
            for (int k = 0; k < m; k++) ring[r0++] = k * w + m;
            for (int i = m; i > 0; i--) ring[r0++] = m * w + i;
            for (int k = m; k > 0; k--) ring[r0++] = k * w;
            var used = new NativeArray<int>(Verts.Length, Allocator.Temp);
            for (int t = 0; t < Tris.Length; t++) used[Tris[t]] = 1;
            var rim = new NativeArray<int>(ringCount, Allocator.Temp);
            for (int r = 0; r < ringCount; r++)
            {
                int c = 3 * ring[r], best = -1;
                float gap = float.MaxValue;
                for (int pass = 0; pass < 2 && best < 0; pass++)
                    for (int u = sink.Ints[c + 2], e = u + sink.Ints[c + 1] - sink.Ints[c]; u < e; u++)
                    {
                        if (pass == 0 && used[u] == 0) continue;
                        var t = Verts[u].Pos;
                        float d = math.abs(columns.At(t.x, t.z) - t.y);
                        if (d < gap) { gap = d; best = u; }
                    }
                rim[r] = best;
                used[best] = 1;
            }

            var meta = new NativeList<int3>(Verts.Length / 4 + 16, Allocator.Temp);
            int kept = 0;
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    int col = 3 * (k * w + i);
                    for (int v = sink.Ints[col + 2], j = sink.Ints[col], e = v + sink.Ints[col + 1] - j; v < e; v++, j++)
                    {
                        if (used[v] == 0) { used[v] = -1; continue; }
                        used[v] = kept;
                        Verts[kept++] = Verts[v];
                        meta.Add(new int3(i, j, k));
                    }
                }
            Verts.Resize(kept, NativeArrayOptions.UninitializedMemory);
            for (int t = 0; t < Tris.Length; t++) Tris[t] = used[Tris[t]];
            for (int r = 0; r < ringCount; r++) rim[r] = used[rim[r]];

            int mc = m / 2;
            for (int v = 0; v < kept; v++)
            {
                int i = meta[v].x, j = meta[v].y, k = meta[v].z;
                bool edge = i == 0 || k == 0 || i == m || k == m;
                if (!edge && !stitch) continue;
                int ci = (chunk.X0 - 1 + i) >> 1, ck = (chunk.Z0 - 1 + k) >> 1;
                int ic = ci - (chunk.X0 / 2 - 1), kc = ck - (chunk.Z0 / 2 - 1);
                bool xe = i == 0 || i == m, ze = k == 0 || k == m;
                int bits = !edge ? 0 : xe && ze ? 16 << ((i == m ? 1 : 0) + (k == m ? 2 : 0)) : i == 0 ? 1 : i == m ? 2 : k == 0 ? 4 : 8;
                var p = Verts[v];
                if (i == 0) p.Pos.x = x0m; else if (i == m) p.Pos.x = x1m;
                if (k == 0) p.Pos.z = z0m; else if (k == m) p.Pos.z = z1m;
                if (stitch)
                {
                    SurfaceNets.CoarseVertex(coarse, near, 2 * s, ci, j >> 1, ck, out float tx, out float ty, out float tz, out float nx, out float ny, out float nz);
                    p.StitchNormal = new half4(new float4(nx, ny, nz, 0));
                    if (ic == 0) tx = x0m; else if (ic == mc) tx = x1m;
                    if (kc == 0) tz = z0m; else if (kc == mc) tz = z1m;
                    p.Stitch = new float4(tx, ty, tz, bits);
                }
                Verts[v] = p;
            }

            float3 lo = float.MaxValue, hi = float.MinValue;
            for (int v = 0; v < Verts.Length; v++)
            {
                var p = Verts[v];
                float gx = math.clamp(p.Pos.x / cm, 0, top), gy = math.clamp(p.Pos.z / cm, 0, top);
                int ax = math.min((int)gx, n - 2), ay = math.min((int)gy, n - 2), a = ay * n + ax;
                float off = TerrainLook.Bilinear(look.Offsets[a], look.Offsets[a + 1], look.Offsets[a + n], look.Offsets[a + n + 1], gx - ax, gy - ay);
                var col = TerrainLook.Shade(TerrainLook.Bilinear(look.Colors[a], look.Colors[a + 1], look.Colors[a + n], look.Colors[a + n + 1], gx - ax, gy - ay), p.Pos.y, p.Normal.y);
                float cover = Cover(edits, columns, p.Pos), lift = math.smoothstep(2f, 6f, math.abs(cover));
                p.Color = new float4(col.xyz, 1 - 0.55f * math.smoothstep(6f, 45f, cover));
                p.Strata = new float4(off, math.max(col.w, lift), 1 - lift, 0);
                if (stitch)
                {
                    float3 sn = new float4(p.StitchNormal).xyz;
                    float sx = math.clamp(p.Stitch.x / cm, 0, top), sz = math.clamp(p.Stitch.z / cm, 0, top);
                    int bx = math.min((int)sx, n - 2), bz = math.min((int)sz, n - 2), b = bz * n + bx;
                    var sc = TerrainLook.Shade(TerrainLook.Bilinear(look.Colors[b], look.Colors[b + 1], look.Colors[b + n], look.Colors[b + n + 1], sx - bx, sz - bz), p.Stitch.y, sn.y);
                    float sCover = Cover(near, coarse, p.Stitch.xyz);
                    p.StitchNormal = new half4(new float4(sn, math.max(sc.w, math.smoothstep(2f, 6f, math.abs(sCover)))));
                    p.StitchColor = new half4(new float4(sc.xyz, 1 - 0.55f * math.smoothstep(6f, 45f, sCover)));
                }
                Verts[v] = p;
                lo = math.min(lo, p.Pos);
                hi = math.max(hi, p.Pos);
            }

            float drop = s * 0.5f + 20f;
            int first = Verts.Length;
            for (int r = 0; r < ringCount; r++)
            {
                var v = Verts[rim[r]];
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
    struct TerrainSelectJob : IJob
    {
        public TerrainTree Tree;
        public NativeHashSet<int> Split;
        public NativeList<TerrainLeaf> Leaves;
        public float3 Eye;
        public float Allowed;

        public void Execute()
        {
            Split.Clear();
            var stack = new NativeList<int>(256, Allocator.Temp);
            PushRoots(ref stack);
            while (stack.Length > 0)
            {
                int key = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                int l = Tree.Level(key);
                if (l == 0 || TerrainTree.Step(l) <= Allowed * Tree.Distance(key, Eye)) continue;
                Split.Add(key);
                PushChildren(ref stack, key);
            }
            for (bool changed = true; changed;)
            {
                changed = false;
                Collect(ref stack);
                for (int k = 0; k < Leaves.Length; k++)
                {
                    int key = Leaves[k].Key, l = Tree.Level(key);
                    var c = Tree.Cell(key, l);
                    float s = TerrainTree.Size(l), x0 = c.x * s - 0.5f, z0 = c.y * s - 0.5f, x1 = c.x * s + s + 0.5f, z1 = c.y * s + s + 0.5f, cx = c.x * s + 0.5f * s, cz = c.y * s + 0.5f * s;
                    changed |= Balance(x0, cz, l) | Balance(x1, cz, l) | Balance(cx, z0, l) | Balance(cx, z1, l);
                    changed |= Balance(x0, z0, l) | Balance(x1, z0, l) | Balance(x0, z1, l) | Balance(x1, z1, l);
                }
            }
            Leaves.Sort();
        }

        bool Balance(float x, float z, int l)
        {
            int nb = LeafAt(x, z, out int nl);
            if (nb < 0 || nl <= l + 1 || !Tree.IsLand(nb)) return false;
            Split.Add(nb);
            return true;
        }

        int LeafAt(float x, float z, out int l)
        {
            l = TerrainTree.Root;
            int key = Tree.At(x, z, l);
            if (key < 0) return -1;
            while (l > 0 && Split.Contains(key)) key = Tree.At(x, z, --l);
            return key;
        }

        void PushRoots(ref NativeList<int> stack)
        {
            int d = Tree.Dim[TerrainTree.Root];
            for (int iz = 0; iz < d; iz++)
                for (int ix = 0; ix < d; ix++)
                {
                    int key = Tree.Key(TerrainTree.Root, ix, iz);
                    if (Tree.IsLand(key)) stack.Add(key);
                }
        }

        void PushChildren(ref NativeList<int> stack, int key)
        {
            for (int q = 0; q < 4; q++)
            {
                int c = Tree.Child(key, q);
                if (Tree.IsLand(c)) stack.Add(c);
            }
        }

        void Collect(ref NativeList<int> stack)
        {
            Leaves.Clear();
            stack.Clear();
            PushRoots(ref stack);
            while (stack.Length > 0)
            {
                int key = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                if (Split.Contains(key)) PushChildren(ref stack, key);
                else Leaves.Add(new TerrainLeaf { Key = key, Dist = Tree.Distance(key, Eye) });
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TerrainViewSystem : SystemBase
    {
        public float TargetQuadScreen = 0.01f;
        const float MorphStart = 0.6f;
        const int LakeCells = 32;
        public int ApplyVertices = 150000;
        public float ApplyMs = 4f;
        public bool SmoothOnly;

        sealed class Prepared
        {
            public BlobBuilder Look;
            public List<MeshParts> Lakes, Rivers;
            public MeshParts Sea;
            public StrataLook Strata;
            public FieldLook Field;
            public int[] Offset, Dim;
            public float2[] Range;
            public byte[] Land;
        }

        struct Pending
        {
            public int Key, Generation;
            public JobHandle Handle;
            public NativeList<TerrainVertex> Verts;
            public NativeList<int> Tris;
            public NativeArray<float3> Bounds;
        }

        Task<Prepared> preparing;
        Prepared prepared;
        BlobAssetReference<TerrainLookData> look;
        TerrainTree tree;
        NativeHashMap<int, TerrainNode> nodes;
        NativeHashSet<int> split, drawn;
        NativeList<TerrainLeaf> leaves;
        NativeList<int> roots;
        JobHandle lodHandle;
        bool lodScheduled, selected;
        float3 lodEye;
        quaternion lodTurn;
        float lodFov = -1f;
        readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        readonly List<int> builtKeys = new List<int>();
        readonly List<Pending> pending = new List<Pending>();
        readonly List<Mesh> statics = new List<Mesh>();
        readonly int[] levelCounts = new int[TerrainTree.Levels];
        readonly Stopwatch frame = new Stopwatch(), since = new Stopwatch();
        EntitiesGraphicsSystem graphics;
        BatchMaterialID material, waterMaterial;
        Ground ground;
        int waterApplied, frameVertices, built, evicted, generation;
        bool ready, rootsReady, builtSmooth;
        float worstMs;

        protected override void OnCreate()
        {
            if (World.Name != GameSettings.ViewClient) Enabled = false;
        }

        protected override void OnUpdate()
        {
            frame.Restart();
            frameVertices = 0;
            if (look.IsCreated)
            {
                if (SmoothOnly != builtSmooth) Reset();
                Collect(); Roots(); Statics(); Lods();
            }
            else if (preparing == null) Begin();
            else if (preparing.IsCompleted) Load();
            float ms = (float)frame.Elapsed.TotalMilliseconds;
            GameStatus.TerrainFrame = $"terrain {ms:F1} ms {stage}, {frameVertices} vertices";
            if (ms > worstMs) { worstMs = ms; GameStatus.Lines["terrain~"] = $"terrain worst update {worstMs:F1} ms ({stage})"; }
        }

        static int BuildBudget => math.max(1, JobsUtility.JobWorkerCount / 2);
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
            int n = world.Config.Size;
            float cm = world.Config.CellMeters;
            var detail = new TerrainDetail(world);
            var p = new Prepared { Lakes = new List<MeshParts>() };
            Pyramid(world, detail, edits, p);
            for (int cy = 0; cy < n; cy += LakeCells)
                for (int cx = 0; cx < n; cx += LakeCells)
                    if (HasLand(world, cx, cy)) p.Lakes.Add(WaterGeometry.Lakes(world, cx, cy, LakeCells));
            p.Rivers = WaterGeometry.Rivers(world);
            p.Sea = WaterGeometry.Sea(n, cm);
            var colors = TerrainLook.BaseColors(world, defs);
            p.Look = new BlobBuilder(Allocator.Persistent);
            ref var root = ref p.Look.ConstructRoot<TerrainLookData>();
            root.N = n;
            var arr = p.Look.Allocate(ref root.Colors, colors.Length);
            for (int i = 0; i < colors.Length; i++) arr[i] = colors[i];
            p.Strata = TerrainLook.Strata(world, MaterialFile.Load(materials));
            var offs = p.Look.Allocate(ref root.Offsets, p.Strata.Offsets.Length);
            for (int i = 0; i < offs.Length; i++) offs[i] = p.Strata.Offsets[i];
            p.Field = TerrainLook.Field(detail, n, cm, colors, p.Strata.Offsets);
            return p;
        }

        static void Pyramid(GeneratedWorld world, TerrainDetail detail, FeatureEdit[] edits, Prepared p)
        {
            int n = world.Config.Size;
            float cm = world.Config.CellMeters, top = (n - 1) * cm;
            p.Offset = new int[TerrainTree.Levels + 1];
            p.Dim = new int[TerrainTree.Levels];
            for (int l = 0; l < TerrainTree.Levels; l++)
            {
                p.Dim[l] = math.max(1, (int)math.ceil(top / TerrainTree.Size(l)));
                p.Offset[l + 1] = p.Offset[l] + p.Dim[l] * p.Dim[l];
            }
            var range = p.Range = new float2[p.Offset[TerrainTree.Levels]];
            var land = p.Land = new byte[range.Length];
            int d0 = p.Dim[0];
            float s0 = TerrainTree.Size(0);
            var cells = detail.Cells;
            var sea = world.Sea;
            Parallel.For(0, d0, iz =>
            {
                for (int ix = 0; ix < d0; ix++)
                {
                    float x0 = ix * s0, z0 = iz * s0, x1 = x0 + s0, z1 = z0 + s0;
                    NearField.Range(cells, x0, z0, x1, z1, out float lo, out float hi);
                    range[iz * d0 + ix] = new float2(lo, hi);
                    int i0 = math.min((int)(x0 / cm), n - 1), i1 = math.min((int)math.ceil(x1 / cm), n - 1);
                    int k0 = math.min((int)(z0 / cm), n - 1), k1 = math.min((int)math.ceil(z1 / cm), n - 1);
                    for (int k = k0; k <= k1 && land[iz * d0 + ix] == 0; k++)
                        for (int i = i0; i <= i1; i++)
                            if (!sea[k * n + i]) { land[iz * d0 + ix] = 1; break; }
                }
            });
            foreach (var e in edits)
            {
                int ix0 = math.clamp((int)(e.MinX / s0), 0, d0 - 1), ix1 = math.clamp((int)(e.MaxX / s0), 0, d0 - 1);
                int iz0 = math.clamp((int)(e.MinZ / s0), 0, d0 - 1), iz1 = math.clamp((int)(e.MaxZ / s0), 0, d0 - 1);
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        var r = range[iz * d0 + ix];
                        range[iz * d0 + ix] = new float2(math.min(r.x, e.MinY), math.max(r.y, e.MaxY));
                    }
            }
            for (int l = 1; l < TerrainTree.Levels; l++)
            {
                int d = p.Dim[l], dc = p.Dim[l - 1];
                for (int iz = 0; iz < d; iz++)
                    for (int ix = 0; ix < d; ix++)
                    {
                        float lo = float.MaxValue, hi = float.MinValue;
                        byte any = 0;
                        for (int q = 0; q < 4; q++)
                        {
                            int cx = 2 * ix + (q & 1), cz = 2 * iz + (q >> 1);
                            if (cx >= dc || cz >= dc) continue;
                            int c = p.Offset[l - 1] + cz * dc + cx;
                            lo = math.min(lo, range[c].x);
                            hi = math.max(hi, range[c].y);
                            any |= land[c];
                        }
                        int key = p.Offset[l] + iz * d + ix;
                        range[key] = new float2(lo, hi);
                        land[key] = any;
                    }
            }
        }

        static bool HasLand(GeneratedWorld w, int cx, int cy)
        {
            int n = w.Config.Size;
            for (int y = cy; y <= math.min(cy + LakeCells, n - 1); y++)
                for (int x = cx; x <= math.min(cx + LakeCells, n - 1); x++)
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
            TerrainLook.ApplyField(prepared.Field);
            Shader.SetGlobalColor("_StabbySky", new Color(0.62f, 0.74f, 0.86f) * 0.9f);
            Shader.SetGlobalColor("_StabbyGround", new Color(0.3f, 0.27f, 0.22f));
            Shader.SetGlobalFloat("_StabbyCellMeters", ground.Blob.Value.CellMeters);
            graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            var scene = UnityEngine.Object.FindFirstObjectByType<GameScene>();
            material = graphics.RegisterMaterial(scene != null ? scene.TerrainMaterial : null);
            waterMaterial = scene != null && scene.WaterMaterial != null ? graphics.RegisterMaterial(scene.WaterMaterial) : material;
            tree = new TerrainTree
            {
                Offset = new NativeArray<int>(prepared.Offset, Allocator.Persistent),
                Dim = new NativeArray<int>(prepared.Dim, Allocator.Persistent),
                Range = new NativeArray<float2>(prepared.Range, Allocator.Persistent),
                Land = new NativeArray<byte>(prepared.Land, Allocator.Persistent),
                Top = ground.Blob.Value.Top,
            };
            nodes = new NativeHashMap<int, TerrainNode>(1024, Allocator.Persistent);
            split = new NativeHashSet<int>(1024, Allocator.Persistent);
            drawn = new NativeHashSet<int>(1024, Allocator.Persistent);
            leaves = new NativeList<TerrainLeaf>(1024, Allocator.Persistent);
            roots = new NativeList<int>(16, Allocator.Persistent);
            int d = tree.Dim[TerrainTree.Root];
            for (int iz = 0; iz < d; iz++)
                for (int ix = 0; ix < d; ix++)
                {
                    int key = tree.Key(TerrainTree.Root, ix, iz);
                    if (tree.IsLand(key)) roots.Add(key);
                }
            builtSmooth = SmoothOnly;
        }

        TerrainNode Node(int key) => nodes.TryGetValue(key, out var v) ? v : new TerrainNode { Entity = Entity.Null };

        bool Built(int key) => nodes.TryGetValue(key, out var v) && v.Built != 0;

        void Roots()
        {
            int missing = 0;
            for (int r = 0; r < roots.Length; r++)
            {
                var nd = Node(roots[r]);
                if (nd.Built != 0) continue;
                missing++;
                if (nd.Pending == 0 && pending.Count < BuildBudget) Request(roots[r]);
            }
            rootsReady = missing == 0;
            if (!rootsReady) GameStatus.TerrainBehind = missing;
        }

        void Reset()
        {
            generation++;
            builtSmooth = SmoothOnly;
            builtKeys.Clear();
            builtKeys.AddRange(meshes.Keys);
            foreach (int key in builtKeys) Evict(key);
            nodes.Clear();
            drawn.Clear();
            rootsReady = false;
        }

        void Request(int key)
        {
            var nd = Node(key);
            int l = tree.Level(key);
            var c = tree.Cell(key, l);
            var vl = new NativeList<TerrainVertex>(TerrainTree.Vertices, Allocator.Persistent);
            var tl = new NativeList<int>(TerrainTree.Indices, Allocator.Persistent);
            var bounds = new NativeArray<float3>(2, Allocator.Persistent);
            var job = new TerrainMeshJob { Ground = ground, Look = look, Ix = c.x, Iz = c.y, Level = l, Smooth = builtSmooth, Verts = vl, Tris = tl, Bounds = bounds };
            pending.Add(new Pending { Key = key, Generation = generation, Handle = job.Schedule(), Verts = vl, Tris = tl, Bounds = bounds });
            nd.Pending = 1;
            nodes[key] = nd;
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
                bool stale = p.Generation != generation;
                if (!stale && !Spend(TerrainTree.Vertices)) return;
                p.Handle.Complete();
                pending.RemoveAt(k--);
                if (stale) { p.Verts.Dispose(); p.Tris.Dispose(); p.Bounds.Dispose(); }
                else Apply(p);
            }
        }

        void Apply(Pending p)
        {
            var nd = Node(p.Key);
            int l = tree.Level(p.Key);
            var c = tree.Cell(p.Key, l);
            float3 lo = p.Bounds[0], hi = p.Bounds[1];
            p.Bounds.Dispose();
            var mesh = new Mesh { name = $"Terrain L{l} {c.x},{c.y}" };
            if (l == 0) { mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw; mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw; }
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            mesh.SetVertexBufferParams(p.Verts.Length, Layout);
            mesh.SetVertexBufferData(p.Verts.AsArray(), 0, 0, p.Verts.Length, 0, quiet);
            mesh.SetIndexBufferParams(p.Tris.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(p.Tris.AsArray(), 0, 0, p.Tris.Length, quiet);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, p.Tris.Length) { bounds = new Bounds((lo + hi) * 0.5f, hi - lo), vertexCount = p.Verts.Length }, quiet);
            mesh.bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            nd.Vertices = p.Verts.Length;
            if (p.Verts.Length > 30000) UnityEngine.Debug.LogWarning($"terrain big node L{l} {c} {p.Verts.Length} verts {p.Tris.Length / 3} tris y {lo.y}..{hi.y}");
            p.Verts.Dispose();
            p.Tris.Dispose();
            meshes[p.Key] = mesh;
            nd.Mesh = graphics.RegisterMesh(mesh);
            built++;
            nd.Pending = 0;
            nd.Built = 1;
            MinMaxAABB box = tree.Box(p.Key);
            if (math.any(lo < box.Min - 0.01f) || math.any(hi > box.Max + 0.01f)) UnityEngine.Debug.LogWarning($"terrain node L{l} {c} mesh {lo}..{hi} outside bounds {box.Min}..{box.Max}");
            nd.Entity = Spawn(nd.Mesh, box, material);
            EntityManager.AddComponentData(nd.Entity, new TerrainStitch());
            EntityManager.AddComponent<DisableRendering>(nd.Entity);
            nd.Drawn = 0;
            nd.Stitch = -1;
            nodes[p.Key] = nd;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float16, 4),
        };

        Entity Spawn(BatchMeshID mesh, AABB box, BatchMaterialID mat)
        {
            var em = EntityManager;
            var e = em.CreateEntity();
            RenderMeshUtility.AddComponents(e, em, new RenderMeshDescription(ShadowCastingMode.Off), new MaterialMeshInfo(mat, mesh));
            em.SetComponentData(e, new RenderBounds { Value = box });
            em.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
            return e;
        }

        void Statics()
        {
            if (ready || !rootsReady) return;
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
            Spawn(id, new AABB { Center = b.center, Extents = b.extents }, waterMaterial);
        }

        bool Coverable(int key)
        {
            if (Built(key)) return true;
            if (!split.Contains(key)) return false;
            for (int q = 0; q < 4; q++)
            {
                int c = tree.Child(key, q);
                if (tree.IsLand(c) && !Coverable(c)) return false;
            }
            return true;
        }

        void Draw(int key)
        {
            if (split.Contains(key))
            {
                bool all = true;
                for (int q = 0; q < 4 && all; q++)
                {
                    int c = tree.Child(key, q);
                    all = !tree.IsLand(c) || Coverable(c);
                }
                if (all)
                {
                    for (int q = 0; q < 4; q++)
                    {
                        int c = tree.Child(key, q);
                        if (tree.IsLand(c)) Draw(c);
                    }
                    return;
                }
            }
            if (Built(key)) drawn.Add(key);
        }

        bool InTree(int key)
        {
            int parent = tree.Parent(key);
            return parent < 0 || split.Contains(parent);
        }

        int DrawnLevel(float x, float z)
        {
            for (int l = TerrainTree.Root; l >= 0; l--)
            {
                int key = tree.At(x, z, l);
                if (!tree.IsLand(key)) return -1;
                if (drawn.Contains(key)) return l;
            }
            return -1;
        }

        void Lods()
        {
            var cam = Camera.main;
            if (cam == null || !rootsReady) return;
            if (lodScheduled)
            {
                if (!lodHandle.IsCompleted) return;
                lodHandle.Complete();
                lodScheduled = false;
                selected = true;
            }
            int behind = selected ? 0 : 1;
            for (int k = 0; k < leaves.Length; k++)
            {
                int key = leaves[k].Key;
                var nd = Node(key);
                if (nd.Built != 0) continue;
                behind++;
                if (nd.Pending == 0 && pending.Count < BuildBudget) Request(key);
            }
            drawn.Clear();
            for (int r = 0; r < roots.Length; r++) Draw(roots[r]);
            builtKeys.Clear();
            builtKeys.AddRange(meshes.Keys);
            Array.Clear(levelCounts, 0, levelCounts.Length);
            float allowed = TargetQuadScreen * 2f * math.tan(math.radians(cam.fieldOfView) * 0.5f);
            int vertices = 0;
            foreach (int key in builtKeys)
            {
                var nd = nodes[key];
                bool want = drawn.Contains(key);
                if (!want && !InTree(key)) { Evict(key); continue; }
                if (want != (nd.Drawn != 0))
                {
                    if (want) EntityManager.RemoveComponent<DisableRendering>(nd.Entity);
                    else EntityManager.AddComponent<DisableRendering>(nd.Entity);
                    nd.Drawn = want ? 1 : 0;
                }
                if (want)
                {
                    int l = tree.Level(key);
                    levelCounts[l]++;
                    vertices += nd.Vertices;
                    int mask = StitchMask(key, l);
                    float end = l < TerrainTree.Root ? TerrainTree.Step(l + 1) / allowed : 0;
                    if (mask != nd.Stitch || end != nd.MorphEnd)
                    {
                        EntityManager.SetComponentData(nd.Entity, new TerrainStitch { Value = new float4(mask, MorphStart * end, end, TerrainTree.Step(l)) });
                        nd.Stitch = mask;
                        nd.MorphEnd = end;
                    }
                }
                nodes[key] = nd;
            }
            GameStatus.TerrainBehind = behind;
            GameStatus.Lines["terrain"] = $"terrain nodes {drawn.Count} drawn, {leaves.Length} wanted, {behind} behind, {built} built, {built - evicted} live, {vertices} drawn vertices, by level {string.Join("/", levelCounts)}";
            ScheduleLods(cam);
        }

        void ScheduleLods(Camera cam)
        {
            var t = cam.transform;
            float3 eye = t.position;
            quaternion turn = t.rotation;
            if (selected && math.distancesq(eye, lodEye) < 0.0001f && math.abs(math.dot(turn.value, lodTurn.value)) > 0.999999f && cam.fieldOfView == lodFov) return;
            lodEye = eye;
            lodTurn = turn;
            lodFov = cam.fieldOfView;
            float allowed = TargetQuadScreen * 2f * math.tan(math.radians(cam.fieldOfView) * 0.5f);
            lodHandle = new TerrainSelectJob { Tree = tree, Split = split, Leaves = leaves, Eye = eye, Allowed = allowed }.Schedule();
            lodScheduled = true;
        }

        int StitchMask(int key, int l)
        {
            var c = tree.Cell(key, l);
            float s = TerrainTree.Size(l), x0 = c.x * s, z0 = c.y * s, x1 = x0 + s, z1 = z0 + s, o = 0.5f;
            float xa = x0 + 0.25f * s, xb = x0 + 0.75f * s, za = z0 + 0.25f * s, zb = z0 + 0.75f * s;
            int mask = 0;
            mask |= Edge(x0 - o, za, x0 - o, zb, l, 1);
            mask |= Edge(x1 + o, za, x1 + o, zb, l, 2);
            mask |= Edge(xa, z0 - o, xb, z0 - o, l, 4);
            mask |= Edge(xa, z1 + o, xb, z1 + o, l, 8);
            for (int q = 0; q < 4; q++)
            {
                float xo = (q & 1) == 0 ? x0 - o : x1 + o, xi = (q & 1) == 0 ? x0 + o : x1 - o;
                float zo = (q & 2) == 0 ? z0 - o : z1 + o, zi = (q & 2) == 0 ? z0 + o : z1 - o;
                mask |= Edge(xo, zi, xi, zo, l, 16 << q) | Edge(xo, zo, xo, zo, l, 16 << q);
            }
            return mask;
        }

        int Edge(float xa, float za, float xb, float zb, int l, int bit)
        {
            int a = DrawnLevel(xa, za), b = DrawnLevel(xb, zb), mask = 0;
            if (a > l || b > l) mask |= bit;
            if ((a >= 0 && a < l) || (b >= 0 && b < l)) mask |= bit << 8;
            return mask;
        }

        public void NearMeshes(float3 eye, float reach, List<Mesh> into)
        {
            into.Clear();
            if (!ready || !drawn.IsCreated) return;
            foreach (int key in drawn)
                if (tree.Level(key) == 0 && tree.Distance(key, eye) < reach && meshes.TryGetValue(key, out var m)) into.Add(m);
        }

        void Evict(int key)
        {
            var nd = nodes[key];
            if (nd.Entity != Entity.Null) EntityManager.DestroyEntity(nd.Entity);
            graphics.UnregisterMesh(nd.Mesh);
            UnityEngine.Object.Destroy(meshes[key]);
            meshes.Remove(key);
            evicted++;
            if (nd.Pending != 0) nodes[key] = new TerrainNode { Pending = 1, Entity = Entity.Null };
            else nodes.Remove(key);
        }

        protected override void OnDestroy()
        {
            if (lodScheduled) lodHandle.Complete();
            foreach (var p in pending)
            {
                p.Handle.Complete();
                p.Verts.Dispose();
                p.Tris.Dispose();
                p.Bounds.Dispose();
            }
            pending.Clear();
            Owned.Release(preparing, p => p.Look.Dispose());
            foreach (var mesh in meshes.Values) UnityEngine.Object.Destroy(mesh);
            foreach (var mesh in statics) UnityEngine.Object.Destroy(mesh);
            if (tree.Offset.IsCreated) { tree.Offset.Dispose(); tree.Dim.Dispose(); tree.Range.Dispose(); tree.Land.Dispose(); }
            if (nodes.IsCreated) nodes.Dispose();
            if (split.IsCreated) split.Dispose();
            if (drawn.IsCreated) drawn.Dispose();
            if (leaves.IsCreated) leaves.Dispose();
            if (roots.IsCreated) roots.Dispose();
            if (look.IsCreated) look.Dispose();
        }
    }
}
