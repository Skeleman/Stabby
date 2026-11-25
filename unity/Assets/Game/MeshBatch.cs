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

        public void Add(MeshParts d, int id)
        {
            int attr = Attrs.Length, floats = 3;
            Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Position));
            if (d.Norms != null) { Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Normal)); floats += 3; }
            if (d.Cols != null) { Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4)); floats += 4; }
            if (d.Uvs != null) { Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)); floats += 2; }
            var extra = new[] { d.Uv1, d.Uv2, d.Uv3, d.Uv4 };
            for (int u = 0; u < 4; u++)
                if (extra[u] != null) { Attrs.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1 + u, VertexAttributeFormat.Float32, 4)); floats += 4; }
            int vc = d.Verts.Length, vertex = Vertices.Length;
            Vertices.ResizeUninitialized(vertex + vc * floats);
            var dst = Vertices.AsArray();
            for (int v = 0, o = vertex; v < vc; v++)
            {
                var p = d.Verts[v];
                dst[o++] = p.x; dst[o++] = p.y; dst[o++] = p.z;
                if (d.Norms != null) { var n = d.Norms[v]; dst[o++] = n.x; dst[o++] = n.y; dst[o++] = n.z; }
                if (d.Cols != null) { var c = d.Cols[v]; dst[o++] = c.r; dst[o++] = c.g; dst[o++] = c.b; dst[o++] = c.a; }
                if (d.Uvs != null) { var t = d.Uvs[v]; dst[o++] = t.x; dst[o++] = t.y; }
                for (int u = 0; u < 4; u++)
                    if (extra[u] != null) { var t = extra[u][v]; dst[o++] = t.x; dst[o++] = t.y; dst[o++] = t.z; dst[o++] = t.w; }
            }
            bool wide = vc > 65535;
            int index = wide ? Wide.Length : Narrow.Length;
            if (wide) foreach (var t in d.Tris) Wide.Add(t);
            else foreach (var t in d.Tris) Narrow.Add((ushort)t);
            Add(d.Name, new MeshPiece { Id = id, Vertex = vertex, VertexCount = vc, Floats = floats, Index = index, IndexCount = d.Tris.Length, Attr = attr, AttrCount = Attrs.Length - attr, Wide = wide, AllBounds = true });
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

        static int frame = -1, vertices, meshes;

        public static bool Take(int v, int m)
        {
            int f = Time.frameCount;
            if (f != frame)
            {
                frame = f;
                vertices = meshes = 0;
            }
            if (meshes > 0 && (vertices + v > Vertices || meshes + m > Meshes)) return false;
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
        int next, first, count, cost;

        public MeshBatch(MeshPack pack) => this.pack = pack;

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
                foreach (var m in made) m.bounds = m.GetSubMesh(0).bounds;
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
