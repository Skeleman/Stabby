using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.Viewer;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Stabby.Game
{
    public struct MeshPiece
    {
        public int Id, Vertex, VertexCount, Floats, Index, IndexCount, Attr, AttrCount;
        public bool Wide, AllBounds;
    }

    public sealed class MeshPack : IDisposable
    {
        public NativeList<float> Vertices;
        public NativeList<ushort> Narrow;
        public NativeList<int> Wide;
        public NativeList<VertexAttributeDescriptor> Attrs;
        public NativeList<MeshPiece> Pieces;
        public readonly List<string> Names = new List<string>();

        public MeshPack(int floats = 0, int narrow = 0)
            : this(new NativeList<float>(floats, Allocator.Persistent), new NativeList<ushort>(narrow, Allocator.Persistent), new NativeList<int>(0, Allocator.Persistent))
        {
        }

        public MeshPack(NativeList<float> vertices, NativeList<ushort> narrow, NativeList<int> wide)
        {
            Vertices = vertices;
            Narrow = narrow;
            Wide = wide;
            Attrs = new NativeList<VertexAttributeDescriptor>(16, Allocator.Persistent);
            Pieces = new NativeList<MeshPiece>(64, Allocator.Persistent);
        }

        public int Count => Pieces.Length;

        public int Layout(VertexAttributeDescriptor[] layout)
        {
            int at = Attrs.Length;
            foreach (var a in layout) Attrs.Add(a);
            return at;
        }

        public void Add(string name, MeshPiece piece)
        {
            Pieces.Add(piece);
            Names.Add(name);
        }

        public void Add(string name, int id, int attr, int attrCount, int floats, float[] vertices, ushort[] indices)
        {
            int vertex = Vertices.Length, index = Narrow.Length;
            foreach (var v in vertices) Vertices.Add(v);
            foreach (var i in indices) Narrow.Add(i);
            Add(name, new MeshPiece { Id = id, Vertex = vertex, VertexCount = vertices.Length / floats, Floats = floats, Index = index, IndexCount = indices.Length, Attr = attr, AttrCount = attrCount });
        }

        sealed class Plan
        {
            public readonly List<VertexAttributeDescriptor> Attrs = new List<VertexAttributeDescriptor>();
            public Vector3[] Norms;
            public Vector4[][] Extra;
            public int Floats = 3, N, C, T;
            public readonly int[] E = new int[4];
            public bool CHalf, THalf;
            public readonly bool[] EHalf = new bool[4];
            public int Attr, Vertex, Index;
        }

        public void Add(IReadOnlyList<(MeshParts Part, int Id, bool Lean)> parts)
        {
            var plans = new Plan[parts.Count];
            Parallel.For(0, parts.Count, i => plans[i] = PlanOf(parts[i].Part, parts[i].Lean));
            int vertex = Vertices.Length, narrow = Narrow.Length, wide = Wide.Length;
            for (int i = 0; i < parts.Count; i++)
            {
                var d = parts[i].Part;
                var p = plans[i];
                p.Attr = Attrs.Length;
                foreach (var a in p.Attrs) Attrs.Add(a);
                p.Vertex = vertex;
                vertex += d.Verts.Length * p.Floats;
                bool w = d.Verts.Length > 65535;
                p.Index = w ? wide : narrow;
                if (w) wide += d.Tris.Length;
                else narrow += d.Tris.Length;
                Add(d.Name, new MeshPiece { Id = parts[i].Id, Vertex = p.Vertex, VertexCount = d.Verts.Length, Floats = p.Floats, Index = p.Index, IndexCount = d.Tris.Length, Attr = p.Attr, AttrCount = p.Attrs.Count, Wide = w, AllBounds = true });
            }
            Vertices.ResizeUninitialized(vertex);
            Narrow.ResizeUninitialized(narrow);
            Wide.ResizeUninitialized(wide);
            var dst = Vertices.AsArray().Reinterpret<uint>();
            var na = Narrow.AsArray();
            var wa = Wide.AsArray();
            Parallel.For(0, parts.Count, i => Write(parts[i].Part, plans[i], dst, na, wa));
        }

        static Plan PlanOf(MeshParts d, bool lean)
        {
            var p = new Plan();
            p.Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Position));
            p.Norms = lean ? null : d.Norms;
            p.Extra = new[] { d.Uv1, d.Uv2, d.Uv3, d.Uv4 };
            p.N = Channel(p, VertexAttribute.Normal, p.Norms != null, false, 3);
            p.CHalf = lean && d.Cols != null && HalfExact(d.Cols);
            p.C = Channel(p, VertexAttribute.Color, d.Cols != null, p.CHalf, 4);
            p.THalf = lean && d.Uvs != null && HalfExact(d.Uvs);
            p.T = Channel(p, VertexAttribute.TexCoord0, d.Uvs != null, p.THalf, 2);
            for (int u = 0; u < 4; u++)
            {
                p.EHalf[u] = lean && p.Extra[u] != null && HalfExact(p.Extra[u]);
                p.E[u] = Channel(p, VertexAttribute.TexCoord1 + u, p.Extra[u] != null, p.EHalf[u], 4);
            }
            return p;
        }

        static void Write(MeshParts d, Plan p, NativeArray<uint> dst, NativeArray<ushort> narrow, NativeArray<int> wide)
        {
            int floats = p.Floats, vc = d.Verts.Length;
            var norms = p.Norms;
            var extra = p.Extra;
            for (int v = 0; v < vc; v++)
            {
                int o = p.Vertex + v * floats;
                var q0 = d.Verts[v];
                dst[o] = math.asuint(q0.x); dst[o + 1] = math.asuint(q0.y); dst[o + 2] = math.asuint(q0.z);
                if (p.N >= 0) { var q = norms[v]; dst[o + p.N] = math.asuint(q.x); dst[o + p.N + 1] = math.asuint(q.y); dst[o + p.N + 2] = math.asuint(q.z); }
                if (p.C >= 0) { var c = d.Cols[v]; Put(dst, o + p.C, c.r, c.g, c.b, c.a, p.CHalf); }
                if (p.T >= 0)
                {
                    var t = d.Uvs[v];
                    if (p.THalf) dst[o + p.T] = math.f32tof16(t.x) | (math.f32tof16(t.y) << 16);
                    else { dst[o + p.T] = math.asuint(t.x); dst[o + p.T + 1] = math.asuint(t.y); }
                }
                for (int u = 0; u < 4; u++)
                    if (p.E[u] >= 0) { var e = extra[u][v]; Put(dst, o + p.E[u], e.x, e.y, e.z, e.w, p.EHalf[u]); }
            }
            if (vc > 65535) for (int k = 0; k < d.Tris.Length; k++) wide[p.Index + k] = d.Tris[k];
            else for (int k = 0; k < d.Tris.Length; k++) narrow[p.Index + k] = (ushort)d.Tris[k];
        }

        static int Channel(Plan p, VertexAttribute at, bool present, bool half, int dim)
        {
            if (!present) return -1;
            p.Attrs.Add(new VertexAttributeDescriptor(at, half ? VertexAttributeFormat.Float16 : VertexAttributeFormat.Float32, dim));
            int offset = p.Floats;
            p.Floats += half ? dim / 2 : dim;
            return offset;
        }

        static void Put(NativeArray<uint> dst, int o, float x, float y, float z, float w, bool half)
        {
            if (half)
            {
                dst[o] = math.f32tof16(x) | (math.f32tof16(y) << 16);
                dst[o + 1] = math.f32tof16(z) | (math.f32tof16(w) << 16);
            }
            else
            {
                dst[o] = math.asuint(x); dst[o + 1] = math.asuint(y); dst[o + 2] = math.asuint(z); dst[o + 3] = math.asuint(w);
            }
        }

        static bool Exact(float f) => math.f16tof32(math.f32tof16(f)) == f;

        static bool HalfExact(Color[] vs)
        {
            foreach (var c in vs)
                if (!Exact(c.r) || !Exact(c.g) || !Exact(c.b) || !Exact(c.a)) return false;
            return true;
        }

        static bool HalfExact(Vector2[] vs)
        {
            foreach (var t in vs)
                if (!Exact(t.x) || !Exact(t.y)) return false;
            return true;
        }

        static bool HalfExact(Vector4[] vs)
        {
            foreach (var t in vs)
                if (!Exact(t.x) || !Exact(t.y) || !Exact(t.z) || !Exact(t.w)) return false;
            return true;
        }

        public void Dispose()
        {
            if (Vertices.IsCreated) Vertices.Dispose();
            if (Narrow.IsCreated) Narrow.Dispose();
            if (Wide.IsCreated) Wide.Dispose();
            if (Attrs.IsCreated) Attrs.Dispose();
            if (Pieces.IsCreated) Pieces.Dispose();
        }
    }

    public static class MeshBudget
    {
        public const int Vertices = 300000, Meshes = 48;

        static int frame = -1, vertices, meshes, denied;
        public static int Frames, Saturated, DeniedMax;
        public static long DeniedTotal;

        public static void Reset()
        {
            Frames = Saturated = DeniedMax = 0;
            DeniedTotal = 0;
        }

        public static string Report() => $"mesh budget: {Saturated} of {Frames} applying frames saturated, queue when saturated mean {(Saturated > 0 ? DeniedTotal / (double)Saturated : 0):F1} max {DeniedMax} batches";

        public static bool Take(int v, int m)
        {
            int f = Time.frameCount;
            if (f != frame)
            {
                frame = f;
                vertices = meshes = denied = 0;
                Frames++;
            }
            if (meshes > 0 && (vertices + v > Vertices || meshes + m > Meshes))
            {
                if (denied++ == 0) Saturated++;
                DeniedTotal++;
                if (denied > DeniedMax) DeniedMax = denied;
                return false;
            }
            vertices += v;
            meshes += m;
            return true;
        }
    }

    [BurstCompile]
    struct MeshFillJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> Vertices;
        [ReadOnly] public NativeArray<ushort> Narrow;
        [ReadOnly] public NativeArray<int> Wide;
        [ReadOnly] public NativeArray<VertexAttributeDescriptor> Attrs;
        [ReadOnly] public NativeArray<MeshPiece> Pieces;
        public int First;
        public Mesh.MeshDataArray Out;

        public void Execute(int i)
        {
            var p = Pieces[First + i];
            var d = Out[i];
            d.SetVertexBufferParams(p.VertexCount, Attrs.GetSubArray(p.Attr, p.AttrCount));
            d.GetVertexData<float>().CopyFrom(Vertices.GetSubArray(p.Vertex, p.VertexCount * p.Floats));
            d.SetIndexBufferParams(p.IndexCount, p.Wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
            if (p.Wide) d.GetIndexData<int>().CopyFrom(Wide.GetSubArray(p.Index, p.IndexCount));
            else d.GetIndexData<ushort>().CopyFrom(Narrow.GetSubArray(p.Index, p.IndexCount));
            d.subMeshCount = 1;
            if (!p.AllBounds)
            {
                d.SetSubMesh(0, new SubMeshDescriptor(0, p.IndexCount));
                return;
            }
            float3 lo = float.MaxValue, hi = float.MinValue;
            for (int v = 0, o = p.Vertex; v < p.VertexCount; v++, o += p.Floats)
            {
                var q = new float3(Vertices[o], Vertices[o + 1], Vertices[o + 2]);
                lo = math.min(lo, q);
                hi = math.max(hi, q);
            }
            var box = p.VertexCount > 0 ? new Bounds((lo + hi) * 0.5f, hi - lo) : default;
            d.SetSubMesh(0, new SubMeshDescriptor(0, p.IndexCount) { bounds = box, firstVertex = 0, vertexCount = p.VertexCount }, MeshUpdateFlags.DontRecalculateBounds);
        }
    }

    public sealed class MeshBatch : IDisposable
    {
        readonly MeshPack pack;
        readonly List<Mesh> made = new List<Mesh>();
        JobSlot slot;
        Mesh.MeshDataArray data;
        readonly bool gpuOnly;
        int next, first, count, cost;

        public MeshBatch(MeshPack pack, bool gpuOnly = false)
        {
            this.pack = pack;
            this.gpuOnly = gpuOnly;
        }

        public bool Done => next >= pack.Count && !slot.Busy;

        public bool Pump(List<Mesh> meshes, List<int> ids)
        {
            meshes.Clear();
            ids.Clear();
            bool got = false;
            if (slot.Done && MeshBudget.Take(cost, count))
            {
                slot.Finish();
                for (int i = 0; i < count; i++)
                {
                    made.Add(new Mesh { name = pack.Names[first + i] });
                    ids.Add(pack.Pieces[first + i].Id);
                }
                Mesh.ApplyAndDisposeWritableMeshData(data, made);
                foreach (var m in made)
                {
                    m.bounds = m.GetSubMesh(0).bounds;
                    if (gpuOnly) m.UploadMeshData(true);
                }
                meshes.AddRange(made);
                made.Clear();
                got = true;
            }
            if (!slot.Busy && next < pack.Count) Schedule();
            return got;
        }

        void Schedule()
        {
            var pieces = pack.Pieces;
            first = next;
            cost = 0;
            while (next < pieces.Length && (next == first || (cost + pieces[next].VertexCount <= MeshBudget.Vertices && next - first < MeshBudget.Meshes)))
                cost += pieces[next++].VertexCount;
            count = next - first;
            data = Mesh.AllocateWritableMeshData(count);
            slot.Start(new MeshFillJob
            {
                Vertices = pack.Vertices.AsArray(), Narrow = pack.Narrow.AsArray(), Wide = pack.Wide.AsArray(), Attrs = pack.Attrs.AsArray(), Pieces = pieces.AsArray(), First = first, Out = data,
            }.Schedule(count, 1));
        }

        public void Dispose()
        {
            if (!slot.Busy) return;
            slot.Finish();
            data.Dispose();
        }
    }
}
