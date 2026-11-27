using System;
using System.Collections.Generic;
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

        public void Add(MeshParts d, int id, bool lean = false)
        {
            int attr = Attrs.Length, floats = 3, vc = d.Verts.Length;
            Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Position));
            var norms = lean ? null : d.Norms;
            var extra = new[] { d.Uv1, d.Uv2, d.Uv3, d.Uv4 };
            int nAt = Channel(VertexAttribute.Normal, norms != null, false, 3, ref floats);
            bool cHalf = lean && d.Cols != null && HalfExact(d.Cols);
            int cAt = Channel(VertexAttribute.Color, d.Cols != null, cHalf, 4, ref floats);
            bool tHalf = lean && d.Uvs != null && HalfExact(d.Uvs);
            int tAt = Channel(VertexAttribute.TexCoord0, d.Uvs != null, tHalf, 2, ref floats);
            var eAt = new int[4];
            var eHalf = new bool[4];
            for (int u = 0; u < 4; u++)
            {
                eHalf[u] = lean && extra[u] != null && HalfExact(extra[u]);
                eAt[u] = Channel(VertexAttribute.TexCoord1 + u, extra[u] != null, eHalf[u], 4, ref floats);
            }
            int vertex = Vertices.Length;
            Vertices.ResizeUninitialized(vertex + vc * floats);
            var dst = Vertices.AsArray().Reinterpret<uint>();
            for (int v = 0; v < vc; v++)
            {
                int o = vertex + v * floats;
                var p = d.Verts[v];
                dst[o] = math.asuint(p.x); dst[o + 1] = math.asuint(p.y); dst[o + 2] = math.asuint(p.z);
                if (nAt >= 0) { var q = norms[v]; dst[o + nAt] = math.asuint(q.x); dst[o + nAt + 1] = math.asuint(q.y); dst[o + nAt + 2] = math.asuint(q.z); }
                if (cAt >= 0) { var c = d.Cols[v]; Put(dst, o + cAt, c.r, c.g, c.b, c.a, cHalf); }
                if (tAt >= 0)
                {
                    var t = d.Uvs[v];
                    if (tHalf) dst[o + tAt] = math.f32tof16(t.x) | (math.f32tof16(t.y) << 16);
                    else { dst[o + tAt] = math.asuint(t.x); dst[o + tAt + 1] = math.asuint(t.y); }
                }
                for (int u = 0; u < 4; u++)
                    if (eAt[u] >= 0) { var e = extra[u][v]; Put(dst, o + eAt[u], e.x, e.y, e.z, e.w, eHalf[u]); }
            }
            bool wide = vc > 65535;
            int index = wide ? Wide.Length : Narrow.Length;
            if (wide) foreach (var t in d.Tris) Wide.Add(t);
            else foreach (var t in d.Tris) Narrow.Add((ushort)t);
            Add(d.Name, new MeshPiece { Id = id, Vertex = vertex, VertexCount = vc, Floats = floats, Index = index, IndexCount = d.Tris.Length, Attr = attr, AttrCount = Attrs.Length - attr, Wide = wide, AllBounds = true });
        }

        int Channel(VertexAttribute at, bool present, bool half, int dim, ref int floats)
        {
            if (!present) return -1;
            Attrs.Add(new VertexAttributeDescriptor(at, half ? VertexAttributeFormat.Float16 : VertexAttributeFormat.Float32, dim));
            int offset = floats;
            floats += half ? dim / 2 : dim;
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
