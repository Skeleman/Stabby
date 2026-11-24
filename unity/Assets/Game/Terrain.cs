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
using Unity.Entities.Graphics;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Stabby.Game
{
    public struct TerrainLookData
    {
        public int N;
        public BlobArray<float3> Colors;
        public BlobArray<float> Offsets;
        public BlobArray<float> Lake;
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

    struct TerrainWrite
    {
        public Entity Entity;
        public int Kind;
        public float4 Value;
        public MaterialMeshInfo Mesh;
        public AABB Box;
    }

    [BurstCompile(CompileSynchronously = true)]
    struct TerrainWriteJob : IJob
    {
        [ReadOnly] public NativeList<TerrainWrite> Writes;
        public ComponentLookup<MaterialMeshInfo> Meshes;
        public ComponentLookup<RenderBounds> Bounds;
        public ComponentLookup<TerrainStitch> Stitch;

        public void Execute()
        {
            for (int i = 0; i < Writes.Length; i++)
            {
                var w = Writes[i];
                if (w.Kind == 0) Meshes.SetComponentEnabled(w.Entity, w.Value.x != 0);
                else if (w.Kind == 1) Stitch[w.Entity] = new TerrainStitch { Value = w.Value };
                else
                {
                    Meshes[w.Entity] = w.Mesh;
                    Bounds[w.Entity] = new RenderBounds { Value = w.Box };
                }
            }
        }
    }

    struct TerrainNode
    {
        public int Built, Pending, Drawn, Stitch, Vertices, Cast, Tier;
        public float MorphEnd;
        public Entity Entity, Caster;
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
        public const float SkirtBit = 4096f;
        public const int Tiers = 10;

        public static void Capacity(int tier, out int vertices, out int indices)
        {
            vertices = Vertices;
            indices = Indices;
            for (int k = 0; k < tier; k++)
            {
                vertices += vertices / 4;
                indices = (indices + indices / 4) / 3 * 3;
            }
        }

        public static int Tier(int vertices, int indices)
        {
            for (int k = 0; k < Tiers; k++)
            {
                Capacity(k, out int v, out int i);
                if (v >= vertices && i >= indices) return k;
            }
            return -1;
        }
        [ReadOnly] public NativeArray<int> Offset, Dim;
        [ReadOnly] public NativeArray<float2> Range;
        public float Top, Deep;

        public static float Step(int l) => NearField.NodeStep(l);
        public static float FineStep => 0.5f * NearField.NodeStep(0);
        public static float Size(int l) => NearField.NodeSize(l);
        public int Key(int l, int ix, int iz) => Offset[l] + iz * Dim[l] + ix;
        public bool Sunk(int key) => Range[key].y < Deep;

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
        public bool Smooth, Fine;
        public NativeList<TerrainVertex> Verts;
        public NativeList<int> Tris;
        public NativeArray<float3> Bounds;

        float Cover(NativeEdits edits, GroundColumns columns, float3 p, out float stone, out float boulderBase)
        {
            stone = 0;
            boulderBase = 0;
            for (int q = 0; q < edits.Count; q++)
            {
                var e = edits.Items[q];
                if (p.x >= e.MinX && p.x <= e.MaxX && p.z >= e.MinZ && p.z <= e.MaxZ)
                {
                    float cover = columns.At(p.x, p.z) - p.y;
                    if (e.Shape == FeatureEdit.Boulder) { stone = Lift(math.min(cover, 0)); boulderBase = e.Ay - ObjectField.BoulderLift * e.R; }
                    return cover;
                }
            }
            return 0;
        }

        static void Axis(int b0, int i, int m, float s2, out int c, out bool snap, out bool lerp, out float at)
        {
            int b = b0 + i;
            if (i == 0 || i == m)
            {
                c = (b >> 1) - 1;
                snap = true;
                lerp = (b >> 1) % m != 0;
                at = (b >> 1) * s2;
                return;
            }
            c = (b - 1) >> 1;
            int end = (b0 >> 1) / m * m + m;
            snap = c == end - 1;
            lerp = false;
            at = end * s2;
        }

        static float Fraction(float a, float b, float t) => b != a ? math.saturate((t - a) / (b - a)) : 0.5f;

        static float Lift(float cover) => cover < 0 ? math.smoothstep(0.3f, 0.9f, -cover) : math.smoothstep(2f, 6f, cover);

        static bool Open(NativeEdits edits, float x, float z)
        {
            for (int q = 0; q < edits.Count; q++)
            {
                var e = edits.Items[q];
                if (x >= e.MinX && x <= e.MaxX && z >= e.MinZ && z <= e.MaxZ) return false;
            }
            return true;
        }

        static float3 Coarse(GroundColumns coarse, NativeEdits near, float s2, int cx, int j, int cz, bool sx, float ax, bool sz, float az, out float3 nrm)
        {
            SurfaceNets.CoarseVertex(coarse, near, s2, cx, j, cz, out float x, out float y, out float z, out nrm.x, out nrm.y, out nrm.z);
            if (sx) x = ax;
            if (sz) z = az;
            if ((sx || sz) && Open(near, x, z)) y = coarse.At(x, z);
            return new float3(x, y, z);
        }

        static void Row(GroundColumns coarse, NativeEdits near, float s2, int cx, bool sx, bool lx, float ax, int cz, bool sz, float az, int j, out float3 pos, out float3 nrm)
        {
            pos = Coarse(coarse, near, s2, cx, j, cz, sx && !lx, ax, sz, az, out nrm);
            if (lx)
            {
                var q = Coarse(coarse, near, s2, cx + 1, j, cz, false, ax, sz, az, out float3 qn);
                float f = Fraction(pos.x, q.x, ax);
                pos = math.lerp(pos, q, f);
                nrm = math.lerp(nrm, qn, f);
            }
            if (sx) pos.x = ax;
        }

        const float SinterFadeM = 0.9f;

        static float Sinter(NativeEdits edits, NativeEdits crusts, float3 p)
        {
            float w = 0, clay = 0;
            for (int q = 0; q < crusts.Count; q++) w = math.max(w, crusts.Items[q].Crust(p.x, p.z));
            for (int q = 0; q < edits.Count; q++)
            {
                var e = edits.Items[q];
                if (e.Shape != FeatureEdit.Spring || e.Kind != FeatureEdit.Add || e.F < 0.5f || p.x < e.MinX || p.x > e.MaxX || p.z < e.MinZ || p.z > e.MaxZ) continue;
                float d = math.length(new float2(p.x - e.Ax, p.z - e.Az)) - e.W - e.R;
                clay = math.max(clay, 1 - math.smoothstep(-SinterFadeM, 0, d));
            }
            return w >= clay ? w : -clay;
        }

        static float Peat(NativeEdits edits, float3 p)
        {
            float peat = 0;
            for (int q = 0; q < edits.Count; q++)
            {
                var e = edits.Items[q];
                if (e.Shape != FeatureEdit.Spring || e.Kind != FeatureEdit.Carve || e.F > -0.5f || p.x < e.MinX || p.x > e.MaxX || p.z < e.MinZ || p.z > e.MaxZ) continue;
                float edge = ObjectField.BogMargin(-e.F), d = math.length(new float2(p.x - e.Ax, p.z - e.Az));
                d += ObjectField.BogPeatFadeM * (0.5f + 0.5f * noise.snoise(new float2(p.x, p.z) * 0.3f + e.S * 97f));
                peat = math.max(peat, 1 - math.smoothstep(edge - ObjectField.BogPeatFadeM, edge, d));
            }
            return peat;
        }

        public void Execute()
        {
            ref var g = ref Ground.Blob.Value;
            ref var look = ref Look.Value;
            int n = look.N, m = Fine ? 2 * TerrainTree.Quads : TerrainTree.Quads, w = m + 1;
            float cm = g.CellMeters, top = n - 1, s = Fine ? TerrainTree.FineStep : TerrainTree.Step(Level);
            var chunk = new NetsChunk { X0 = Ix * m, Z0 = Iz * m, Cells = m, StepM = s };
            var sink = new TerrainSink { Verts = Verts, Tris = Tris };
            var columns = new GroundColumns { Ground = Ground, StepM = s, Top = g.Top, Smooth = Smooth };
            var edits = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            float x0m = chunk.X0 * s, z0m = chunk.Z0 * s, x1m = x0m + m * s, z1m = z0m + m * s;
            for (int f = 0; f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MeshedIn(s, x0m, z0m, x1m, z1m)) edits.Items.Add(e.AtStep(s));
            }
            var crusts = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            for (int f = 0; f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (!e.Crusts) continue;
                float ext = e.CrustExtent;
                if (e.Ax + ext > x0m && e.Ax - ext < x1m && e.Az + ext > z0m && e.Az - ext < z1m) crusts.Items.Add(e);
            }
            if (edits.Count > 0) SurfaceNets.Edited(columns, edits, chunk, ref sink);
            else SurfaceNets.Heightfield(columns, chunk, ref sink);

            bool stitch = Level < TerrainTree.Root;
            var coarse = new GroundColumns { Ground = Ground, StepM = 2 * s, Top = g.Top, Smooth = Smooth };
            var near = new NativeEdits { Items = new NativeList<FeatureEdit>(Allocator.Temp) };
            for (int f = 0; stitch && f < g.Edits.Length; f++)
            {
                var e = g.Edits[f];
                if (e.MeshedIn(2 * s, x0m - m * s, z0m - m * s, x1m + m * s, z1m + m * s)) near.Items.Add(e.AtStep(2 * s));
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
            var rimGap = new NativeArray<float>(ringCount, Allocator.Temp);
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
                rimGap[r] = gap;
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

            for (int v = 0; v < kept; v++)
            {
                int i = meta[v].x, j = meta[v].y, k = meta[v].z;
                bool edge = i == 0 || k == 0 || i == m || k == m;
                if (!edge && !stitch) continue;
                bool xe = i == 0 || i == m, ze = k == 0 || k == m;
                int bits = !edge ? 0 : xe && ze ? 16 << ((i == m ? 1 : 0) + (k == m ? 2 : 0)) : i == 0 ? 1 : i == m ? 2 : k == 0 ? 4 : 8;
                var p = Verts[v];
                if (i == 0) p.Pos.x = x0m; else if (i == m) p.Pos.x = x1m;
                if (k == 0) p.Pos.z = z0m; else if (k == m) p.Pos.z = z1m;
                if (edge && Open(edits, p.Pos.x, p.Pos.z)) p.Pos.y = columns.At(p.Pos.x, p.Pos.z);
                if (stitch)
                {
                    Axis(chunk.X0, i, m, 2 * s, out int cx, out bool sx, out bool lx, out float ax);
                    Axis(chunk.Z0, k, m, 2 * s, out int cz, out bool sz, out bool lz, out float az);
                    Row(coarse, near, 2 * s, cx, sx, lx, ax, cz, sz && !lz, az, j >> 1, out float3 tp, out float3 tn);
                    if (lz)
                    {
                        Row(coarse, near, 2 * s, cx, sx, lx, ax, cz + 1, sz && !lz, az, j >> 1, out float3 tp1, out float3 tn1);
                        float f = Fraction(tp.z, tp1.z, az);
                        tp = math.lerp(tp, tp1, f);
                        tn = math.lerp(tn, tn1, f);
                    }
                    if (sz) tp.z = az;
                    p.StitchNormal = new half4(new float4(tn, 0));
                    p.Stitch = new float4(tp, bits);
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
                float cover = Cover(edits, columns, p.Pos, out float stone, out float boulderBase), lift = Lift(cover);
                p.Color = new float4(col.xyz, 1 - 0.55f * math.smoothstep(6f, 45f, cover) + stone);
                p.Strata = new float4(off + stone * (boulderBase - p.Pos.y), math.max(col.w, lift), 1 - lift, Sinter(edits, crusts, p.Pos));
                p.Stitch.w = math.floor(p.Stitch.w) + 0.5f * Peat(edits, p.Pos);
                if (stitch)
                {
                    float3 sn = new float4(p.StitchNormal).xyz;
                    float sx = math.clamp(p.Stitch.x / cm, 0, top), sz = math.clamp(p.Stitch.z / cm, 0, top);
                    int bx = math.min((int)sx, n - 2), bz = math.min((int)sz, n - 2), b = bz * n + bx;
                    var sc = TerrainLook.Shade(TerrainLook.Bilinear(look.Colors[b], look.Colors[b + 1], look.Colors[b + n], look.Colors[b + n + 1], sx - bx, sz - bz), p.Stitch.y, sn.y);
                    float sCover = Cover(near, coarse, p.Stitch.xyz, out float sStone, out _), sLift = Lift(sCover);
                    p.StitchNormal = new half4(new float4(sn, math.max(sc.w, sLift)));
                    p.StitchColor = new half4(new float4(sc.xyz, 1 - 0.55f * math.smoothstep(6f, 45f, sCover) + sStone));
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
                v.Stitch.w += TerrainTree.SkirtBit;
                Verts.Add(v);
                lo = math.min(lo, v.Pos);
            }
            for (int r = 0; r < ringCount; r++)
            {
                if (math.max(rimGap[r], rimGap[(r + 1) % ringCount]) > drop) continue;
                int a = rim[r], b = rim[(r + 1) % ringCount];
                int a2 = first + r, b2 = first + (r + 1) % ringCount;
                Tris.Add(a); Tris.Add(b); Tris.Add(a2);
                Tris.Add(b); Tris.Add(b2); Tris.Add(a2);
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
        [ReadOnly] public NativeHashSet<int> Fine;
        public NativeList<TerrainLeaf> Leaves;
        public float3 Eye;
        public float Allowed;
        [ReadOnly] public NativeArray<float4> Planes;

        float SplitDistance(int key)
        {
            var b = Tree.Box(key);
            float d = math.max(1f, math.distance(Eye, math.clamp(Eye, b.Min, b.Max)));
            float3 c = 0.5f * (b.Min + b.Max), e = 0.5f * (b.Max - b.Min);
            for (int p = 0; p < Planes.Length; p++)
                if (math.dot(Planes[p].xyz, c) + math.dot(math.abs(Planes[p].xyz), e) + Planes[p].w < 0) return d * OffscreenScale;
            return d;
        }

        public const float OffscreenScale = 2f;

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
                if (l == 0 || Tree.Sunk(key) || TerrainTree.Step(l) <= Allowed * SplitDistance(key)) continue;
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
                    int lb = l == 0 && Fine.Contains(key) ? -1 : l;
                    float s = TerrainTree.Size(l), x0 = c.x * s - 0.5f, z0 = c.y * s - 0.5f, x1 = c.x * s + s + 0.5f, z1 = c.y * s + s + 0.5f, cx = c.x * s + 0.5f * s, cz = c.y * s + 0.5f * s;
                    changed |= Balance(x0, cz, lb) | Balance(x1, cz, lb) | Balance(cx, z0, lb) | Balance(cx, z1, lb);
                    changed |= Balance(x0, z0, lb) | Balance(x1, z0, lb) | Balance(x0, z1, lb) | Balance(x1, z1, lb);
                }
            }
            Leaves.Sort();
        }

        bool Balance(float x, float z, int l)
        {
            int nb = LeafAt(x, z, out int nl);
            if (nb < 0 || nl <= l + 1) return false;
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
                for (int ix = 0; ix < d; ix++) stack.Add(Tree.Key(TerrainTree.Root, ix, iz));
        }

        void PushChildren(ref NativeList<int> stack, int key)
        {
            for (int q = 0; q < 4; q++)
            {
                int c = Tree.Child(key, q);
                if (c >= 0) stack.Add(c);
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
    [CreateAfter(typeof(ClientMapSystem))]
    public partial class TerrainViewSystem : SystemBase
    {
        TerrainViewDef quads = new TerrainViewDef();
        const float MorphStart = 0.6f;
        const int LakeCells = 32;
        public int ApplyVertices = 150000;
        public float ApplyMs = 4f;
        public bool SmoothOnly;
        public static bool Casting = true, HideSunk = true, SprayShadows = true;
        float viewPpm = 1000, viewQuality = 1;

        sealed class Prepared
        {
            public BlobBuilder Look;
            public List<MeshParts> Lakes, Rivers, Mist;
            public MeshParts Sea;
            public StrataLook Strata;
            public FieldLook Field;
            public int[] Offset, Dim;
            public float2[] Range;
            public float Deep;
        }

        struct Pending
        {
            public int Key, Generation;
            public JobHandle Handle;
            public NativeList<TerrainVertex> Verts;
            public NativeList<int> Tris;
            public NativeArray<float3> Bounds;

            public void Dispose()
            {
                Verts.Dispose();
                Tris.Dispose();
                Bounds.Dispose();
            }
        }

        Task<Prepared> preparing;
        Task<(List<MeshParts>, List<MeshParts>)> steaming;
        List<MeshParts> steam, pools;
        int steamApplied;
        bool hasMist;
        Light sun;
        IEnumerator<string> loading;
        public bool Loading => loading != null || look.IsCreated;
        Prepared prepared;
        TerrainLook.FieldUpload upload;
        const int UploadBytes = 2 << 20;
        BlobAssetReference<TerrainLookData> look, lookReady;
        BlobBake<TerrainLookData> lookBake;
        TerrainTree tree;
        NativeHashMap<int, TerrainNode> nodes;
        NativeHashSet<int> split, drawn, fine;
        NativeList<TerrainLeaf> leaves;
        NativeList<int> roots;
        NativeList<Entity> spare;
        Entity prototype, casterPrototype;
        const int SpareBatch = 64, SpareFill = 256;
        static readonly int[] PoolFill = { 64, 128, 96, 32, 24, 64, 48, 12, 8 };
        const int PoolReach = 2;
        NativeArray<int> zeros;
        readonly List<Mesh>[] pool = Lists<Mesh>();
        readonly List<BatchMeshID>[] poolIds = Lists<BatchMeshID>();
        readonly int[] created = new int[TerrainTree.Tiers];
        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
        NativeArray<float4> planes;
        readonly Plane[] frustum = new Plane[6];
        JobHandle lodHandle;
        bool lodScheduled, selected;
        int splitSeen;
        float3 lodEye;
        quaternion lodTurn;
        float lodFov = -1f;
        readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        readonly List<int> builtKeys = new List<int>();
        readonly List<Pending> pending = new List<Pending>();
        readonly List<Mesh> statics = new List<Mesh>();
        readonly int[] levelCounts = new int[TerrainTree.Levels];
        int shownDrawn, shownSunk, shownWanted, shownBehind, shownVertices, shownCasting, shownCast;
        readonly int[] shownLevels = new int[TerrainTree.Levels];
        readonly Stopwatch frame = new Stopwatch(), since = new Stopwatch();
        EntitiesGraphicsSystem graphics;
        BatchMaterialID material, waterMaterial, mistMaterial;
        Ground ground;
        int waterApplied, frameVertices, built, evicted, generation, poolMisses, oddBuilt;
        bool ready, rootsReady, builtSmooth;
        float worstMs, worstLoadMs, worstLodsMs, worstCollectMs;
        string loadPart = "";
        double partUpload, partCollect, partStatics, partLods, lodRequests, lodDraw, lodNodes, lodEvictMs, lodShadowMs;
        int lodEvicts, lodDeferred, lodShadows;
        const int EvictBudget = 32;

        NativeList<TerrainWrite> writes;
        ComponentLookup<MaterialMeshInfo> meshLookup;
        ComponentLookup<RenderBounds> boundsLookup;
        ComponentLookup<TerrainStitch> stitchLookup;

        protected override void OnCreate()
        {
            meshLookup = GetComponentLookup<MaterialMeshInfo>();
            boundsLookup = GetComponentLookup<RenderBounds>();
            stitchLookup = GetComponentLookup<TerrainStitch>();
            if (World.Name != GameSettings.ViewClient) Enabled = false;
        }

        protected override void OnUpdate()
        {
            frame.Restart();
            frameVertices = 0;
            if (look.IsCreated)
            {
                if (SmoothOnly != builtSmooth) Reset();
                if (upload != null && upload.Step()) upload = null;
                double tu = frame.Elapsed.TotalMilliseconds;
                Collect();
                double tc = frame.Elapsed.TotalMilliseconds;
                Roots(); Statics(); Steam(); Clock();
                double ts = frame.Elapsed.TotalMilliseconds;
                lodRequests = lodDraw = lodNodes = lodEvictMs = lodShadowMs = 0;
                lodEvicts = lodDeferred = lodShadows = 0;
                Lods();
                double lm = frame.Elapsed.TotalMilliseconds - ts;
                loadPart = null;
                (partUpload, partCollect, partStatics, partLods) = (tu, tc - tu, ts - tc, lm);
                Flush();
                if (lm > worstLodsMs) { worstLodsMs = (float)lm; GameStatus.Lines["terrain~lods"] = $"terrain worst lods {lm:F1} ms ({stage}: {LodParts()})"; }
                if (tc - tu > worstCollectMs) { worstCollectMs = (float)(tc - tu); GameStatus.Lines["terrain~collect"] = $"terrain worst collect {tc - tu:F1} ms ({stage}, {frameVertices} vertices)"; }
            }
            else
            {
                loadPart = "load";
                if (loading == null && preparing != null && preparing.IsCompleted) loading = Load();
                if (loading != null)
                {
                    bool more = loading.MoveNext();
                    loadPart = more ? $"load {loading.Current}" : "load natives";
                    if (!more) loading = null;
                    float lt = (float)frame.Elapsed.TotalMilliseconds;
                    if (lt > worstLoadMs) { worstLoadMs = lt; GameStatus.Lines["terrain~load"] = $"terrain worst load step {lt:F1} ms ({loadPart})"; }
                }
                else if (preparing == null) Begin();
            }
            float ms = (float)frame.Elapsed.TotalMilliseconds;
            GameStatus.TerrainMs = ms;
            GameStatus.TerrainStage = stage;
            GameStatus.TerrainVertices = frameVertices;
            if (ms > worstMs) { worstMs = ms; GameStatus.Lines["terrain~"] = $"terrain worst update {worstMs:F1} ms ({stage}: {Parts()})"; }
        }

        string Status() => $"terrain nodes {shownDrawn} drawn, {shownSunk} sunk hidden, {shownWanted} wanted, {shownBehind} behind, {built} built, {built - evicted} live, {shownVertices} drawn vertices, {shownCasting} casting ({shownCast} vertices), by level {string.Join("/", shownLevels)}";

        string Parts() => loadPart ?? $"upload {partUpload:F1}, collect {partCollect:F1}, statics {partStatics:F1}, lods {partLods:F1} ms ({LodParts()}), {frameVertices} vertices";

        string LodParts() => $"requests {lodRequests:F1}, draw {lodDraw:F1}, nodes {lodNodes:F1} [evict {lodEvicts} {lodEvictMs:F1} ms, {lodDeferred} deferred, shadow {lodShadows} {lodShadowMs:F1} ms]";

        static readonly int MistPxId = Shader.PropertyToID("_StabbyMistPx"), ColumnPxId = Shader.PropertyToID("_StabbyColumnPx"), ClockId = Shader.PropertyToID("_StabbyClock");
        static readonly int ShadowPxId = Shader.PropertyToID("_StabbyShadowPx"), ShadowEyeId = Shader.PropertyToID("_StabbyShadowEye");
        const double ClockWrap = 86400, FallbackTickRate = 60;
        public static double? ClockOverride;

        void Clock()
        {
            double clock = UnityEngine.Time.timeAsDouble;
            if (SystemAPI.TryGetSingleton(out NetworkTime time) && time.ServerTick.IsValid)
            {
                double rate = SystemAPI.TryGetSingleton(out ClientServerTickRate tr) && tr.SimulationTickRate > 0 ? tr.SimulationTickRate : FallbackTickRate;
                clock = (time.ServerTick.TickIndexForValidTick + (double)time.ServerTickFraction) / rate;
            }
            Shader.SetGlobalFloat(ClockId, (float)((ClockOverride ?? clock) % ClockWrap));
        }

        float Allowed(Camera cam)
        {
            var view = DetailView.Refresh(cam);
            quads.MistWindow(view.Quality, out float mistFade, out float mistFull);
            quads.SteamWindow(view.Quality, out float steamFade, out float steamFull);
            Shader.SetGlobalVector(MistPxId, new Vector4(mistFade, mistFull, steamFade, steamFull));
            quads.ColumnWindow(view.Quality, out float columnFade, out float columnFull);
            Shader.SetGlobalVector(ColumnPxId, new Vector4(columnFade, columnFull, 0, 0));
            viewPpm = view.PixelsPerMetre;
            viewQuality = view.Quality;
            Shader.SetGlobalVector(ShadowPxId, new Vector4(viewPpm, 0, 0, SprayShadows ? 1 : 0));
            Shader.SetGlobalVector(ShadowEyeId, cam.transform.position);
            return quads.Allowed(view.PixelsPerMetre, view.Quality);
        }

        static int BuildBudget => math.max(1, JobsUtility.JobWorkerCount / 2);
        string stage => !look.IsCreated ? "load" : !ready ? "initial" : "lod";

        void Begin()
        {
            var map = World.GetExistingSystemManaged<ClientMapSystem>();
            if (map.Map == null || map.Parts == null || !SystemAPI.TryGetSingleton(out ground)) return;
            var world = map.Map;
            var parts = map.Parts;
            var defs = map.Defs;
            quads = map.Objects?.Terrain ?? quads;
            string materials = ContentFiles.MaterialsPath;
            since.Restart();
            preparing = Task.Run(() => Prepare(world, parts.Detail, defs, materials, parts.Edits));
        }

        static Prepared Prepare(GeneratedWorld world, TerrainDetail detail, IReadOnlyList<BiomeDef> defs, string materials, FeatureEdit[] edits)
        {
            int n = world.Config.Size;
            float cm = world.Config.CellMeters;
            var p = new Prepared { Lakes = new List<MeshParts>(), Mist = new List<MeshParts>() };
            Pyramid(world, detail, edits, p);
            for (int cy = 0; cy < n; cy += LakeCells)
                for (int cx = 0; cx < n; cx += LakeCells)
                    if (HasLand(world, cx, cy)) p.Lakes.Add(WaterGeometry.Lakes(world, cx, cy, LakeCells));
            p.Rivers = WaterGeometry.Rivers(world, detail, p.Mist);
            p.Sea = WaterGeometry.Sea(n, cm);
            var colors = TerrainLook.BaseColors(world, defs);
            p.Look = new BlobBuilder(Allocator.Persistent);
            ref var root = ref p.Look.ConstructRoot<TerrainLookData>();
            root.N = n;
            var arr = p.Look.Allocate(ref root.Colors, colors.Length);
            for (int i = 0; i < colors.Length; i++) arr[i] = colors[i];
            var lib = ContentFiles.Materials(materials);
            p.Strata = TerrainLook.Strata(world, lib, defs, SameOffsets(world.Materials, lib) ? detail.Offsets : null);
            p.Deep = lib.Water == null ? float.NegativeInfinity : -math.max(lib.Water.ClarityM, lib.Water.ShoreM);
            var offs = p.Look.Allocate(ref root.Offsets, p.Strata.Offsets.Length);
            for (int i = 0; i < offs.Length; i++) offs[i] = p.Strata.Offsets[i];
            var lake = p.Look.Allocate(ref root.Lake, n * n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) lake[y * n + x] = WaterGeometry.LakeHeight(world, x, y);
            p.Field = TerrainLook.Field(detail, n, cm, colors, p.Strata.Offsets);
            return p;
        }

        static bool SameOffsets(MaterialLibrary a, MaterialLibrary b) =>
            a != null && a.Strata.Count > 0 && a.OffsetAmplitudeM == b.OffsetAmplitudeM && a.OffsetWavelengthM == b.OffsetWavelengthM && a.TiltPerKm == b.TiltPerKm;

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
            int d0 = p.Dim[0];
            float s0 = TerrainTree.Size(0);
            var cells = detail.Cells;
            Parallel.For(0, d0, iz =>
            {
                for (int ix = 0; ix < d0; ix++)
                {
                    float x0 = ix * s0, z0 = iz * s0, x1 = x0 + s0, z1 = z0 + s0;
                    NearField.Range(cells, x0, z0, x1, z1, out float lo, out float hi);
                    range[iz * d0 + ix] = new float2(lo, hi);
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
                        for (int q = 0; q < 4; q++)
                        {
                            int cx = 2 * ix + (q & 1), cz = 2 * iz + (q >> 1);
                            if (cx >= dc || cz >= dc) continue;
                            int c = p.Offset[l - 1] + cz * dc + cx;
                            lo = math.min(lo, range[c].x);
                            hi = math.max(hi, range[c].y);
                        }
                        int key = p.Offset[l] + iz * d + ix;
                        range[key] = new float2(lo, hi);
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

        IEnumerator<string> Load()
        {
            var task = preparing;
            preparing = null;
            if (!task.IsCompletedSuccessfully) { UnityEngine.Debug.LogException(task.Exception); yield break; }
            prepared = task.Result;
            GameStatus.Lines["terrain~prep"] = $"terrain prepared at {since.ElapsedMilliseconds} ms";
            lookBake = BlobBake<TerrainLookData>.Start(prepared.Look);
            yield return "blob";
            foreach (string step in TerrainLook.StrataSteps(prepared.Strata)) yield return step;
            while (!lookBake.IsCompleted) yield return "blob wait";
            lookReady = lookBake.Take();
            Shader.SetGlobalColor("_StabbySky", new Color(0.62f, 0.74f, 0.86f) * 0.45f);
            Shader.SetGlobalColor("_StabbyGround", new Color(0.14f, 0.14f, 0.13f));
            Shader.SetGlobalFloat("_StabbyCellMeters", ground.Blob.Value.CellMeters);
            graphics = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            var scene = UnityEngine.Object.FindFirstObjectByType<GameScene>();
            material = graphics.RegisterMaterial(scene != null ? scene.TerrainMaterial : null);
            yield return "material";
            waterMaterial = scene != null && scene.WaterMaterial != null ? graphics.RegisterMaterial(scene.WaterMaterial) : material;
            yield return "water material";
            bool mist = scene != null && scene.MistMaterial != null;
            hasMist = mist;
            if (mist) mistMaterial = graphics.RegisterMaterial(scene.MistMaterial);
            else prepared.Mist.Clear();
            yield return "mist material";
            tree = new TerrainTree
            {
                Offset = new NativeArray<int>(prepared.Offset, Allocator.Persistent),
                Dim = new NativeArray<int>(prepared.Dim, Allocator.Persistent),
                Range = new NativeArray<float2>(prepared.Range, Allocator.Persistent),
                Top = ground.Blob.Value.Top,
                Deep = prepared.Deep,
            };
            nodes = new NativeHashMap<int, TerrainNode>(1024, Allocator.Persistent);
            split = new NativeHashSet<int>(1024, Allocator.Persistent);
            drawn = new NativeHashSet<int>(1024, Allocator.Persistent);
            fine = new NativeHashSet<int>(64, Allocator.Persistent);
            leaves = new NativeList<TerrainLeaf>(1024, Allocator.Persistent);
            planes = new NativeArray<float4>(6, Allocator.Persistent);
            roots = new NativeList<int>(16, Allocator.Persistent);
            spare = new NativeList<Entity>(SpareBatch, Allocator.Persistent);
            int d = tree.Dim[TerrainTree.Root];
            for (int iz = 0; iz < d; iz++)
                for (int ix = 0; ix < d; ix++) roots.Add(tree.Key(TerrainTree.Root, ix, iz));
            FineNodes();
            builtSmooth = SmoothOnly;
            TerrainTree.Capacity(TerrainTree.Tiers - 1, out _, out int most);
            if (!zeros.IsCreated) zeros = new NativeArray<int>(most, Allocator.Persistent);
            while (Fill() >= 0 || spare.Length < 2 * SpareFill)
            {
                do
                {
                    int k = Fill();
                    if (k >= 0) Pool(k);
                    else Spare();
                }
                while ((Fill() >= 0 || spare.Length < 2 * SpareFill) && frame.Elapsed.TotalMilliseconds < ApplyMs);
                yield return $"pool {Fill()}/{spare.Length}";
            }
            upload = new TerrainLook.FieldUpload(prepared.Field, UploadBytes);
            prepared.Field = null;
            look = lookReady;
        }

        void FineNodes()
        {
            fine.Clear();
            ref var edits = ref ground.Blob.Value.Edits;
            float s0 = TerrainTree.Size(0);
            for (int f = 0; f < edits.Length; f++)
            {
                var e = edits[f];
                if (!e.Fine) continue;
                for (int iz = (int)math.floor(e.MinZ / s0); iz <= (int)math.floor(e.MaxZ / s0); iz++)
                    for (int ix = (int)math.floor(e.MinX / s0); ix <= (int)math.floor(e.MaxX / s0); ix++)
                    {
                        int k = tree.At((ix + 0.5f) * s0, (iz + 0.5f) * s0, 0);
                        if (k >= 0) fine.Add(k);
                    }
            }
        }

        bool Fine(int key, int l) => l == 0 && fine.Contains(key);

        bool FineAt(float x, float z)
        {
            int k = tree.At(x, z, 0);
            return k >= 0 && fine.Contains(k);
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
            var job = new TerrainMeshJob { Ground = ground, Look = look, Ix = c.x, Iz = c.y, Level = l, Fine = Fine(key, l), Smooth = builtSmooth, Verts = vl, Tris = tl, Bounds = bounds }.Schedule();
            pending.Add(new Pending { Key = key, Generation = generation, Handle = job, Verts = vl, Tris = tl, Bounds = bounds });
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
                if (stale) p.Dispose();
                else Apply(p);
            }
        }

        void Apply(Pending p)
        {
            var nd = Node(p.Key);
            int l = tree.Level(p.Key);
            var c = tree.Cell(p.Key, l);
            float3 lo = p.Bounds[0], hi = p.Bounds[1];
            int nv = p.Verts.Length, ni = p.Tris.Length;
            int need = TerrainTree.Tier(nv, ni), tier = need, capacity = ni;
            for (int k = need; need >= 0 && k <= need + PoolReach && k < TerrainTree.Tiers; k++)
                if (pool[k].Count > 0) { tier = k; break; }
            Mesh mesh;
            if (tier >= 0)
            {
                var meshesFree = pool[tier];
                var idsFree = poolIds[tier];
                if (meshesFree.Count == 0) { poolMisses++; Pool(tier); }
                TerrainTree.Capacity(tier, out _, out capacity);
                int last = meshesFree.Count - 1;
                mesh = meshesFree[last];
                nd.Mesh = idsFree[last];
                meshesFree.RemoveAt(last);
                idsFree.RemoveAt(last);
            }
            else
            {
                mesh = NewMesh(nv, ni);
                oddBuilt++;
                PoolStatus();
            }
            var b = new Bounds((lo + hi) * 0.5f, hi - lo);
            mesh.SetVertexBufferData(p.Verts.AsArray(), 0, 0, nv, 0, Quiet);
            mesh.SetIndexBufferData(p.Tris.AsArray(), 0, 0, ni, Quiet);
            if (capacity > ni) mesh.SetIndexBufferData(zeros, 0, ni, capacity - ni, Quiet);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, capacity) { bounds = b, vertexCount = nv }, Quiet);
            mesh.bounds = b;
            nd.Vertices = nv;
            nd.Tier = tier;
            if (p.Verts.Length > 30000) UnityEngine.Debug.LogWarning($"terrain big node L{l} {c} {p.Verts.Length} verts {p.Tris.Length / 3} tris y {lo.y}..{hi.y}");
            p.Verts.Dispose();
            p.Tris.Dispose();
            p.Bounds.Dispose();
            meshes[p.Key] = mesh;
            if (tier < 0) nd.Mesh = graphics.RegisterMesh(mesh);
            built++;
            nd.Pending = 0;
            nd.Built = 1;
            MinMaxAABB box = tree.Box(p.Key);
            if (math.any(lo < box.Min - 0.01f) || math.any(hi > box.Max + 0.01f)) UnityEngine.Debug.LogWarning($"terrain node L{l} {c} mesh {lo}..{hi} outside bounds {box.Min}..{box.Max}");
            nd.Entity = Take(nd.Mesh, box, out nd.Caster);
            nd.Drawn = 0;
            nd.Stitch = -1;
            nodes[p.Key] = nd;
        }

        static Mesh NewMesh(int vertices, int indices)
        {
            var mesh = new Mesh { name = "Terrain" };
            mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.SetVertexBufferParams(vertices, Layout);
            mesh.SetIndexBufferParams(indices, IndexFormat.UInt32);
            mesh.subMeshCount = 1;
            mesh.UploadMeshData(false);
            return mesh;
        }

        void Pool(int tier)
        {
            TerrainTree.Capacity(tier, out int v, out int i);
            var mesh = NewMesh(v, i);
            pool[tier].Add(mesh);
            poolIds[tier].Add(graphics.RegisterMesh(mesh));
            created[tier]++;
            PoolStatus();
        }

        int Fill()
        {
            for (int k = 0; k < PoolFill.Length; k++)
                if (created[k] < PoolFill[k]) return k;
            return -1;
        }

        void PoolStatus() => GameStatus.Lines["terrain~pool"] = $"terrain mesh pool created {string.Join("/", created)}, {poolMisses} misses, {oddBuilt} oversize";

        static List<T>[] Lists<T>()
        {
            var lists = new List<T>[TerrainTree.Tiers];
            for (int k = 0; k < lists.Length; k++) lists[k] = new List<T>();
            return lists;
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

        Entity Spawn(BatchMeshID mesh, AABB box, BatchMaterialID mat, ShadowCastingMode shadows = ShadowCastingMode.Off)
        {
            var em = EntityManager;
            var e = em.CreateEntity();
            RenderMeshUtility.AddComponents(e, em, new RenderMeshDescription(shadows), new MaterialMeshInfo(mat, mesh));
            em.SetComponentData(e, new RenderBounds { Value = box });
            em.AddComponentData(e, new LocalToWorld { Value = float4x4.identity });
            return e;
        }

        Entity Take(BatchMeshID mesh, AABB box, out Entity caster)
        {
            if (spare.Length == 0) Spare();
            int n = spare.Length;
            var e = spare[n - 2];
            caster = spare[n - 1];
            spare.ResizeUninitialized(n - 2);
            var info = new MaterialMeshInfo(material, mesh);
            Queue(new TerrainWrite { Entity = e, Kind = 2, Mesh = info, Box = box });
            Queue(new TerrainWrite { Entity = caster, Kind = 2, Mesh = info, Box = box });
            return e;
        }

        void Spare()
        {
            var em = EntityManager;
            if (prototype == Entity.Null)
            {
                prototype = Prototype(ShadowCastingMode.Off);
                casterPrototype = Prototype(ShadowCastingMode.ShadowsOnly);
            }
            using var made = em.Instantiate(prototype, SpareBatch, Allocator.Temp);
            using var casters = em.Instantiate(casterPrototype, SpareBatch, Allocator.Temp);
            for (int i = 0; i < SpareBatch; i++)
            {
                spare.Add(made[i]);
                spare.Add(casters[i]);
            }
        }

        Entity Prototype(ShadowCastingMode shadows)
        {
            var em = EntityManager;
            var e = Spawn(poolIds[0].Count > 0 ? poolIds[0][0] : default, default, material, shadows);
            em.AddComponentData(e, new TerrainStitch());
            em.AddComponent<Prefab>(e);
            em.SetComponentEnabled<MaterialMeshInfo>(e, false);
            return e;
        }

        void Statics()
        {
            if (ready || !rootsReady) return;
            var lakes = prepared.Lakes;
            int water = lakes.Count + prepared.Rivers.Count, total = water + prepared.Mist.Count;
            while (waterApplied < total)
            {
                var part = waterApplied < lakes.Count ? lakes[waterApplied] : waterApplied < water ? prepared.Rivers[waterApplied - lakes.Count] : prepared.Mist[waterApplied - water];
                if (part != null && !Spend(part.Verts.Length)) return;
                if (part != null && waterApplied < water) AddStatic(part, waterMaterial, 0);
                else if (part != null) AddStatic(part, mistMaterial, WaterGeometry.MistPad);
                waterApplied++;
            }
            AddStatic(prepared.Sea, waterMaterial, 0);
            GameStatus.Lines["terrain~ready"] = $"terrain ready at {since.ElapsedMilliseconds} ms";
            prepared = null;
            ready = true;
            GameStatus.TerrainBuilt = true;
        }

        void Steam()
        {
            if (!ready || (steam != null && steamApplied >= steam.Count + pools.Count)) return;
            if (steam == null)
            {
                if (steaming == null)
                {
                    var field = World.GetExistingSystemManaged<ObjectColumnsSystem>()?.Field;
                    if (field == null) return;
                    steaming = Task.Run(() => { var l = new List<MeshParts>(); var w = new List<MeshParts>(); WaterGeometry.Springs(field, l, w); return (l, w); });
                    return;
                }
                if (!steaming.IsCompleted) return;
                if (steaming.IsFaulted) { GameStatus.Failure = steaming.Exception.ToString(); steam = pools = new List<MeshParts>(); return; }
                (steam, pools) = steaming.Result;
                if (!hasMist) steam.Clear();
                steaming = null;
            }
            while (steamApplied < steam.Count + pools.Count)
            {
                bool water = steamApplied >= steam.Count;
                var part = water ? pools[steamApplied - steam.Count] : steam[steamApplied];
                if (!Spend(part.Verts.Length)) return;
                AddStatic(part, water ? waterMaterial : mistMaterial, water ? 0 : WaterGeometry.MistPad);
                steamApplied++;
            }
            GameStatus.Lines["terrain~steam"] = $"spring steam {steam.Count} pieces, {pools.Count} pool pieces";
        }

        bool Casts(MeshParts part, BatchMaterialID mat)
        {
            if (mat == mistMaterial) return true;
            if (part.Uv4 == null) return false;
            for (int i = 0; i < part.Uv4.Length; i++)
                if (part.Uv4[i].x > 0.5f) return true;
            return false;
        }

        void AddStatic(MeshParts part, BatchMaterialID mat, float pad)
        {
            var mesh = WaterGeometry.ToMesh(part);
            var id = graphics.RegisterMesh(mesh);
            statics.Add(mesh);
            var b = mesh.bounds;
            Spawn(id, new AABB { Center = b.center, Extents = (float3)b.extents + pad }, mat, Casts(part, mat) ? ShadowCastingMode.On : ShadowCastingMode.Off);
        }

        bool Coverable(int key)
        {
            if (Built(key)) return true;
            if (!split.Contains(key)) return false;
            for (int q = 0; q < 4; q++)
            {
                int c = tree.Child(key, q);
                if (c >= 0 && !Coverable(c)) return false;
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
                    all = c < 0 || Coverable(c);
                }
                if (all)
                {
                    for (int q = 0; q < 4; q++)
                    {
                        int c = tree.Child(key, q);
                        if (c >= 0) Draw(c);
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
                if (key < 0) return -1;
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
                splitSeen = split.Count;
            }
            double t0 = frame.Elapsed.TotalMilliseconds;
            int behind = selected ? 0 : 1;
            for (int k = 0; k < leaves.Length; k++)
            {
                int key = leaves[k].Key;
                var nd = Node(key);
                if (nd.Built != 0) continue;
                behind++;
                if (nd.Pending == 0 && pending.Count < BuildBudget) Request(key);
            }
            if (pending.Count < BuildBudget)
                foreach (int key in split)
                {
                    if (pending.Count >= BuildBudget) break;
                    var nd = Node(key);
                    if (nd.Built == 0 && nd.Pending == 0) Request(key);
                }
            double t1 = frame.Elapsed.TotalMilliseconds;
            drawn.Clear();
            for (int r = 0; r < roots.Length; r++) Draw(roots[r]);
            double t2 = frame.Elapsed.TotalMilliseconds, evictMs = 0, shadowMs = 0;
            int evicts = 0, deferred = 0, shadows = 0;
            builtKeys.Clear();
            builtKeys.AddRange(meshes.Keys);
            Array.Clear(levelCounts, 0, levelCounts.Length);
            float allowed = Allowed(cam);
            float3 eye = cam.transform.position;
            float reach = Casting && UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.shadowDistance : 0;
            int vertices = 0, casting = 0, castVertices = 0, sunk = 0;
            if (sun == null) sun = RenderSettings.sun != null ? RenderSettings.sun : Array.Find(UnityEngine.Object.FindObjectsByType<Light>(), l => l.type == LightType.Directional);
            float3 sunDir = sun != null ? (float3)sun.transform.forward : new float3(0, -1, 0);
            float sunRun = math.length(sunDir.xz);
            float2 upsun = sunRun > 1e-4f ? -sunDir.xz / sunRun : float2.zero;
            float tanE = math.max(-sunDir.y / math.max(sunRun, 1e-4f), 0.2f);
            float floor = float.MaxValue;
            if (reach > 0)
                foreach (int key in builtKeys)
                {
                    MinMaxAABB fb = tree.Box(key);
                    if (fb.Min.y < floor && math.length(math.max(0, math.max(fb.Min - eye, eye - fb.Max))) < reach) floor = fb.Min.y;
                }
            foreach (int key in builtKeys)
            {
                var nd = nodes[key];
                bool want = drawn.Contains(key);
                if (!want && !InTree(key))
                {
                    if (evicts == 0 || (evicts < EvictBudget && frame.Elapsed.TotalMilliseconds < ApplyMs))
                    {
                        double te = frame.Elapsed.TotalMilliseconds;
                        Evict(key);
                        evicts++;
                        evictMs += frame.Elapsed.TotalMilliseconds - te;
                    }
                    else
                    {
                        deferred++;
                        if (nd.Drawn != 0 || nd.Cast != 0)
                        {
                            if (nd.Drawn != 0) Show(nd.Entity, false);
                            if (nd.Cast != 0) Show(nd.Caster, false);
                            nd.Drawn = 0;
                            nd.Cast = 0;
                            nodes[key] = nd;
                        }
                    }
                    continue;
                }
                bool show = want && !(HideSunk && tree.Sunk(key));
                sunk += want && !show ? 1 : 0;
                if (show != (nd.Drawn != 0))
                {
                    Show(nd.Entity, show);
                    nd.Drawn = show ? 1 : 0;
                }
                MinMaxAABB nb = tree.Box(key);
                int cast = show && CastsNear(nb, eye, reach, floor, upsun, tanE) && Resolved(nb, eye, nd.Cast != 0) ? 1 : 0;
                if (cast != nd.Cast)
                {
                    double tsh = frame.Elapsed.TotalMilliseconds;
                    Show(nd.Caster, cast != 0);
                    shadows++;
                    shadowMs += frame.Elapsed.TotalMilliseconds - tsh;
                }
                nd.Cast = cast;
                casting += cast;
                castVertices += cast * nd.Vertices;
                if (show)
                {
                    int l = tree.Level(key);
                    levelCounts[l]++;
                    vertices += nd.Vertices;
                    int mask = StitchMask(key, l);
                    bool thin = Fine(key, l);
                    float end = thin ? TerrainTree.Step(0) / allowed : l < TerrainTree.Root ? TerrainTree.Step(l + 1) / allowed : 0;
                    if (mask != nd.Stitch || end != nd.MorphEnd)
                    {
                        var stitch = new float4(mask, MorphStart * end, end, thin ? TerrainTree.FineStep : TerrainTree.Step(l));
                        Queue(new TerrainWrite { Entity = nd.Entity, Kind = 1, Value = stitch });
                        Queue(new TerrainWrite { Entity = nd.Caster, Kind = 1, Value = stitch });
                        nd.Stitch = mask;
                        nd.MorphEnd = end;
                    }
                }
                nodes[key] = nd;
            }
            (lodRequests, lodDraw, lodNodes, lodEvictMs, lodShadowMs) = (t1 - t0, t2 - t1, frame.Elapsed.TotalMilliseconds - t2, evictMs, shadowMs);
            (lodEvicts, lodDeferred, lodShadows) = (evicts, deferred, shadows);
            GameStatus.TerrainBehind = behind;
            (shownDrawn, shownSunk, shownWanted, shownBehind, shownVertices, shownCasting, shownCast) = (drawn.Count - sunk, sunk, leaves.Length, behind, vertices, casting, castVertices);
            Array.Copy(levelCounts, shownLevels, levelCounts.Length);
            if (!GameStatus.Live.ContainsKey("terrain")) GameStatus.Live["terrain"] = Status;
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
            float allowed = Allowed(cam);
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            for (int p = 0; p < 6; p++) planes[p] = new float4(frustum[p].normal, frustum[p].distance);
            lodHandle = new TerrainSelectJob { Tree = tree, Split = split, Fine = fine, Leaves = leaves, Eye = eye, Allowed = allowed, Planes = planes }.Schedule();
            lodScheduled = true;
        }

        int StitchMask(int key, int l)
        {
            var c = tree.Cell(key, l);
            float s = TerrainTree.Size(l), x0 = c.x * s, z0 = c.y * s, x1 = x0 + s, z1 = z0 + s, o = 0.5f;
            float xa = x0 + 0.25f * s, xb = x0 + 0.75f * s, za = z0 + 0.25f * s, zb = z0 + 0.75f * s;
            int mask = 0;
            bool thin = Fine(key, l);
            mask |= Edge(x0 - o, za, x0 - o, zb, l, 1, thin);
            mask |= Edge(x1 + o, za, x1 + o, zb, l, 2, thin);
            mask |= Edge(xa, z0 - o, xb, z0 - o, l, 4, thin);
            mask |= Edge(xa, z1 + o, xb, z1 + o, l, 8, thin);
            for (int q = 0; q < 4; q++)
            {
                float xo = (q & 1) == 0 ? x0 - o : x1 + o, xi = (q & 1) == 0 ? x0 + o : x1 - o;
                float zo = (q & 2) == 0 ? z0 - o : z1 + o, zi = (q & 2) == 0 ? z0 + o : z1 - o;
                mask |= Edge(xo, zi, xi, zo, l, 16 << q, thin) | Edge(xo, zo, xo, zo, l, 16 << q, thin);
            }
            return mask;
        }

        int Edge(float xa, float za, float xb, float zb, int l, int bit, bool thin)
        {
            int a = DrawnLevel(xa, za), b = DrawnLevel(xb, zb), mask = 0;
            bool fa = a == 0 && FineAt(xa, za), fb = b == 0 && FineAt(xb, zb);
            if (a > l || b > l || thin && (a == 0 && !fa || b == 0 && !fb)) mask |= bit;
            if ((a >= 0 && a < l) || (b >= 0 && b < l) || l == 0 && !thin && (fa || fb)) mask |= bit << 8;
            return mask;
        }

        bool Resolved(MinMaxAABB nb, float3 eye, bool was)
        {
            float dist = math.length(math.max(0, math.max(nb.Min - eye, eye - nb.Max)));
            return ShadowViewSystem.Map.Holds(math.cmax(nb.Max - nb.Min), dist, viewQuality, was);
        }

        static bool CastsNear(MinMaxAABB nb, float3 eye, float reach, float floor, float2 upsun, float tanE)
        {
            float run = (nb.Max.y - math.min(floor, nb.Min.y)) / tanE;
            int steps = (int)math.clamp(math.ceil(run / math.max(math.cmin((nb.Max - nb.Min).xz), 1e-3f)), 1, 16);
            for (int k = 0; k <= steps; k++)
            {
                float2 o = -upsun * run * k / steps;
                float3 shift = new float3(o.x, 0, o.y);
                if (math.length(math.max(0, math.max(nb.Min + shift - eye, eye - nb.Max - shift))) < reach) return true;
            }
            return false;
        }

        public void NearMeshes(float3 eye, float reach, List<Mesh> into)
        {
            into.Clear();
            if (!ready || !drawn.IsCreated) return;
            foreach (int key in drawn)
                if (tree.Level(key) == 0 && tree.Distance(key, eye) < reach && meshes.TryGetValue(key, out var m)) into.Add(m);
        }

        public int[] Audit()
        {
            var r = new int[12];
            if (!ready || !drawn.IsCreated) return r;
            var em = EntityManager;
            em.CompleteAllTrackedJobs();
            double area = 0, total = 0;
            for (int k = 0; k < roots.Length; k++) total += TerrainTree.Size(TerrainTree.Root) * (double)TerrainTree.Size(TerrainTree.Root);
            foreach (int key in drawn)
            {
                float s = TerrainTree.Size(tree.Level(key));
                area += s * (double)s;
                var nd = nodes[key];
                if (nd.Drawn == 0) continue;
                r[0]++;
                var e = nd.Entity;
                if (e == Entity.Null || !em.Exists(e) || !em.IsComponentEnabled<MaterialMeshInfo>(e)) { r[1]++; continue; }
                if (em.GetComponentData<MaterialMeshInfo>(e).MeshID != nd.Mesh) r[2]++;
                MinMaxAABB box = tree.Box(key);
                AABB want = box;
                var rb = em.GetComponentData<RenderBounds>(e).Value;
                if (math.any(math.abs(rb.Center - want.Center) > 0.01f) || math.any(math.abs(rb.Extents - want.Extents) > 0.01f)) r[3]++;
                if (em.HasComponent<WorldRenderBounds>(e))
                {
                    var wb = em.GetComponentData<WorldRenderBounds>(e).Value;
                    if (math.any(math.abs(wb.Center - want.Center) > 0.01f) || math.any(math.abs(wb.Extents - want.Extents) > 0.01f)) r[4]++;
                }
            }
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainStitch>(), ComponentType.ReadOnly<MaterialMeshInfo>());
            r[5] = q.CalculateEntityCount() - r[0] - shownCasting;
            r[6] = (int)math.max(0, total - area);
            (r[7], r[8], r[9], r[10], r[11]) = (splitSeen, shownWanted, drawn.Count, lodScheduled ? 1 : 0, rootsReady ? 1 : 0);
            return r;
        }

        public bool Seam(float3 eye, float near, float far, out float3 at, out float3 across)
        {
            at = across = default;
            if (!ready || !drawn.IsCreated) return false;
            float best = 0;
            foreach (int key in drawn)
            {
                var nd = nodes[key];
                int l = tree.Level(key);
                if (nd.Drawn == 0 || l > 2 || (nd.Stitch & 15) == 0) continue;
                MinMaxAABB box = tree.Box(key);
                for (int e = 0; e < 4; e++)
                {
                    if ((nd.Stitch & (1 << e)) == 0) continue;
                    float3 n = e == 0 ? new float3(-1, 0, 0) : e == 1 ? new float3(1, 0, 0) : e == 2 ? new float3(0, 0, -1) : new float3(0, 0, 1);
                    float3 c = (box.Min + box.Max) * 0.5f;
                    float3 p = new float3(c.x + n.x * (box.Max.x - box.Min.x) * 0.5f, 0, c.z + n.z * (box.Max.z - box.Min.z) * 0.5f);
                    float d = math.distance(p.xz, eye.xz);
                    float relief = box.Max.y - box.Min.y;
                    if (d < near || d > far || relief <= best) continue;
                    best = relief;
                    at = p;
                    across = n;
                }
            }
            return best > 0;
        }

        void Show(Entity e, bool show) => Queue(new TerrainWrite { Entity = e, Kind = 0, Value = show ? 1 : 0 });

        void Queue(TerrainWrite w)
        {
            if (!writes.IsCreated) writes = new NativeList<TerrainWrite>(64, Allocator.TempJob);
            writes.Add(w);
        }

        void Flush()
        {
            if (!writes.IsCreated) return;
            meshLookup.Update(this);
            boundsLookup.Update(this);
            stitchLookup.Update(this);
            Dependency = new TerrainWriteJob { Writes = writes, Meshes = meshLookup, Bounds = boundsLookup, Stitch = stitchLookup }.Schedule(Dependency);
            writes.Dispose(Dependency);
            writes = default;
        }

        void Evict(int key)
        {
            var nd = nodes[key];
            if (nd.Entity != Entity.Null)
            {
                Show(nd.Entity, false);
                if (nd.Cast != 0) Show(nd.Caster, false);
                spare.Add(nd.Entity);
                spare.Add(nd.Caster);
            }
            if (nd.Tier < 0)
            {
                graphics.UnregisterMesh(nd.Mesh);
                UnityEngine.Object.Destroy(meshes[key]);
            }
            else
            {
                pool[nd.Tier].Add(meshes[key]);
                poolIds[nd.Tier].Add(nd.Mesh);
            }
            meshes.Remove(key);
            evicted++;
            if (nd.Pending != 0) nodes[key] = new TerrainNode { Pending = 1, Entity = Entity.Null };
            else nodes.Remove(key);
        }

        protected override void OnDestroy()
        {
            if (GameStatus.Live.Remove("terrain")) GameStatus.Lines["terrain"] = Status();
            if (lodScheduled) lodHandle.Complete();
            if (writes.IsCreated) writes.Dispose();
            foreach (var p in pending)
            {
                p.Handle.Complete();
                p.Dispose();
            }
            pending.Clear();
            Owned.Release(preparing, p => p.Look.Dispose());
            lookBake.Release();
            foreach (var mesh in meshes.Values) UnityEngine.Object.Destroy(mesh);
            if (zeros.IsCreated) zeros.Dispose();
            foreach (var free in pool)
                foreach (var mesh in free) UnityEngine.Object.Destroy(mesh);
            foreach (var mesh in statics) UnityEngine.Object.Destroy(mesh);
            if (tree.Offset.IsCreated) { tree.Offset.Dispose(); tree.Dim.Dispose(); tree.Range.Dispose(); }
            if (nodes.IsCreated) nodes.Dispose();
            if (split.IsCreated) split.Dispose();
            if (spare.IsCreated) spare.Dispose();
            if (drawn.IsCreated) drawn.Dispose();
            if (fine.IsCreated) fine.Dispose();
            if (leaves.IsCreated) leaves.Dispose();
            if (planes.IsCreated) planes.Dispose();
            if (roots.IsCreated) roots.Dispose();
            if (look.IsCreated) look.Dispose();
            else if (lookReady.IsCreated) lookReady.Dispose();
        }
    }
}
