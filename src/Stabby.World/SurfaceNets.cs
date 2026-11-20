using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public interface ISolidField
    {
        float Density(int i, int j, int k);
    }

    public interface IColumnField
    {
        float Height(int i, int k);
    }

    public interface INetsSink
    {
        void Begin(int ints, int floats);
        int GetInt(int k);
        void SetInt(int k, int v);
        float GetFloat(int k);
        void SetFloat(int k, float v);
        int AddVertex(float x, float y, float z, float nx, float ny, float nz);
        void AddTriangle(int a, int b, int c);
    }

    public struct HeightSolid<T> : ISolidField where T : struct, IColumnField
    {
        public T Columns;
        public float StepM;

        public float Density(int i, int j, int k) => Columns.Height(i, k) - j * StepM;
    }

    public struct DetailColumns : IColumnField
    {
        public TerrainDetail Detail;
        public float StepCells;

        public float Height(int i, int k) => Detail.HeightAt(i * StepCells, k * StepCells);
    }

    public struct NetsBuffers : INetsSink
    {
        public List<float> Verts;
        public List<int> Tris;
        int[] ints;
        float[] floats;

        public void Begin(int ints, int floats)
        {
            Verts = new List<float>();
            Tris = new List<int>();
            this.ints = new int[ints];
            this.floats = new float[floats];
        }

        public int GetInt(int k) => ints[k];
        public void SetInt(int k, int v) => ints[k] = v;
        public float GetFloat(int k) => floats[k];
        public void SetFloat(int k, float v) => floats[k] = v;

        public int AddVertex(float x, float y, float z, float nx, float ny, float nz)
        {
            Verts.Add(x); Verts.Add(y); Verts.Add(z); Verts.Add(nx); Verts.Add(ny); Verts.Add(nz);
            return Verts.Count / 6 - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            Tris.Add(a); Tris.Add(b); Tris.Add(c);
        }

        public bool SameAs(NetsBuffers o)
        {
            if (Verts.Count != o.Verts.Count || Tris.Count != o.Tris.Count) return false;
            for (int k = 0; k < Verts.Count; k++)
                if (BitConverter.SingleToInt32Bits(Verts[k]) != BitConverter.SingleToInt32Bits(o.Verts[k])) return false;
            for (int k = 0; k < Tris.Count; k++)
                if (Tris[k] != o.Tris[k]) return false;
            return true;
        }
    }

    public struct NetsChunk
    {
        public int X0, Z0, Cells;
        public float StepM;
    }

    public static class SurfaceNets
    {
        public static void HeightRange<T>(T columns, NetsChunk c, out int jMin, out int jMax) where T : struct, IColumnField
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = c.Z0 - 1; k <= c.Z0 + c.Cells; k++)
                for (int i = c.X0 - 1; i <= c.X0 + c.Cells; i++)
                {
                    float h = columns.Height(i, k);
                    lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
            jMin = (int)Math.Floor(lo / c.StepM) - 2;
            jMax = (int)Math.Floor(hi / c.StepM) + 3;
        }

        public static void General<TField, TSink>(TField field, NetsChunk c, int jMin, int jMax, ref TSink sink)
            where TField : struct, ISolidField where TSink : struct, INetsSink
        {
            int w = c.Cells + 1, layers = jMax - jMin, bx = c.X0 - 1, bz = c.Z0 - 1;
            sink.Begin(w * w * layers, 0);
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    int ci = bx + i, ck = bz + k;
                    for (int j = jMin; j < jMax; j++)
                    {
                        float d0 = field.Density(ci, j, ck), d1 = field.Density(ci + 1, j, ck);
                        float d2 = field.Density(ci, j + 1, ck), d3 = field.Density(ci + 1, j + 1, ck);
                        float d4 = field.Density(ci, j, ck + 1), d5 = field.Density(ci + 1, j, ck + 1);
                        float d6 = field.Density(ci, j + 1, ck + 1), d7 = field.Density(ci + 1, j + 1, ck + 1);
                        sink.SetInt((k * w + i) * layers + j - jMin, Cell(ref sink, ci, j, ck, c.StepM, d0, d1, d2, d3, d4, d5, d6, d7));
                    }
                }
            for (int k = 1; k < w; k++)
                for (int i = 1; i < w; i++)
                {
                    int ci = bx + i, ck = bz + k;
                    for (int j = jMin; j < jMax; j++)
                    {
                        bool p = field.Density(ci, j, ck) > 0;
                        int a = (k * w + i) * layers + j - jMin, dz = w * layers;
                        bool mid = j > jMin && j < jMax - 1;
                        if (mid && p != field.Density(ci + 1, j, ck) > 0) Quad(ref sink, p, a - 1 - dz, a - dz, a, a - 1);
                        if (p != field.Density(ci, j + 1, ck) > 0) Quad(ref sink, p, a - dz - layers, a - layers, a, a - dz);
                        if (mid && p != field.Density(ci, j, ck + 1) > 0) Quad(ref sink, p, a - layers - 1, a - 1, a, a - layers);
                    }
                }
        }

        public static void Heightfield<TCols, TSink>(TCols columns, NetsChunk c, ref TSink sink)
            where TCols : struct, IColumnField where TSink : struct, INetsSink
        {
            int w = c.Cells + 1, hw = w + 1, bx = c.X0 - 1, bz = c.Z0 - 1;
            float s = c.StepM;
            sink.Begin(3 * w * w, hw * hw);
            for (int k = 0; k < hw; k++)
                for (int i = 0; i < hw; i++)
                    sink.SetFloat(k * hw + i, columns.Height(bx + i, bz + k));
            int next = 0;
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    int ci = bx + i, ck = bz + k, col = 3 * (k * w + i);
                    float h00 = sink.GetFloat(k * hw + i), h10 = sink.GetFloat(k * hw + i + 1);
                    float h01 = sink.GetFloat((k + 1) * hw + i), h11 = sink.GetFloat((k + 1) * hw + i + 1);
                    float lo = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
                    int j = (int)Math.Floor(lo / s) - 2;
                    while (!AllInside(h00, h10, h01, h11, j - 1, s)) j--;
                    while (AllInside(h00, h10, h01, h11, j, s)) j++;
                    sink.SetInt(col, j);
                    sink.SetInt(col + 2, next);
                    for (; !AllOutside(h00, h10, h01, h11, j, s); j++)
                    {
                        float yb = j * s, yt = (j + 1) * s;
                        Cell(ref sink, ci, j, ck, s, h00 - yb, h10 - yb, h00 - yt, h10 - yt, h01 - yb, h11 - yb, h01 - yt, h11 - yt);
                        next++;
                    }
                    sink.SetInt(col + 1, j);
                }
            for (int k = 1; k < w; k++)
                for (int i = 1; i < w; i++)
                {
                    int col = 3 * (k * w + i), cx = col - 3, cz = col - 3 * w, cxz = cz - 3;
                    int jLo = Math.Min(Math.Min(sink.GetInt(col), sink.GetInt(cx)), Math.Min(sink.GetInt(cz), sink.GetInt(cxz)));
                    int jHi = Math.Max(Math.Max(sink.GetInt(col + 1), sink.GetInt(cx + 1)), Math.Max(sink.GetInt(cz + 1), sink.GetInt(cxz + 1)));
                    float h = sink.GetFloat(k * hw + i), hx = sink.GetFloat(k * hw + i + 1), hz = sink.GetFloat((k + 1) * hw + i);
                    for (int j = jLo; j < jHi; j++)
                    {
                        float yb = j * s;
                        bool p = h - yb > 0;
                        if (p != hx - yb > 0) QuadAt(ref sink, p, cz, j - 1, cz, j, col, j, col, j - 1);
                        if (p != h - (j + 1) * s > 0) QuadAt(ref sink, p, cxz, j, cx, j, col, j, cz, j);
                        if (p != hz - yb > 0) QuadAt(ref sink, p, cx, j - 1, col, j - 1, col, j, cx, j);
                    }
                }
        }

        static bool AllInside(float a, float b, float c, float d, int j, float s)
        {
            float y = (j + 1) * s;
            return a - y > 0 && b - y > 0 && c - y > 0 && d - y > 0;
        }

        static bool AllOutside(float a, float b, float c, float d, int j, float s)
        {
            float y = j * s;
            return !(a - y > 0) && !(b - y > 0) && !(c - y > 0) && !(d - y > 0);
        }

        static void QuadAt<TSink>(ref TSink sink, bool solidLow, int c0, int j0, int c1, int j1, int c2, int j2, int c3, int j3) where TSink : struct, INetsSink
        {
            int a = Vertex(ref sink, c0, j0), b = Vertex(ref sink, c1, j1), c = Vertex(ref sink, c2, j2), d = Vertex(ref sink, c3, j3);
            if (solidLow) { sink.AddTriangle(a, b, d); sink.AddTriangle(d, b, c); }
            else { sink.AddTriangle(a, d, b); sink.AddTriangle(d, c, b); }
        }

        static int Vertex<TSink>(ref TSink sink, int col, int j) where TSink : struct, INetsSink =>
            sink.GetInt(col + 2) + j - sink.GetInt(col);

        static int Cell<TSink>(ref TSink sink, int ci, int j, int ck, float s, float d0, float d1, float d2, float d3, float d4, float d5, float d6, float d7)
            where TSink : struct, INetsSink
        {
            bool a0 = d0 > 0, any = a0 | d1 > 0 | d2 > 0 | d3 > 0 | d4 > 0 | d5 > 0 | d6 > 0 | d7 > 0;
            bool all = a0 & d1 > 0 & d2 > 0 & d3 > 0 & d4 > 0 & d5 > 0 & d6 > 0 & d7 > 0;
            if (!any || all) return -1;
            float sx = 0, sy = 0, sz = 0;
            int n = 0;
            Edge(d0, d1, 0, 0, 0, 0, ref sx, ref sy, ref sz, ref n);
            Edge(d2, d3, 0, 1, 0, 0, ref sx, ref sy, ref sz, ref n);
            Edge(d4, d5, 0, 0, 1, 0, ref sx, ref sy, ref sz, ref n);
            Edge(d6, d7, 0, 1, 1, 0, ref sx, ref sy, ref sz, ref n);
            Edge(d0, d2, 0, 0, 0, 1, ref sx, ref sy, ref sz, ref n);
            Edge(d1, d3, 1, 0, 0, 1, ref sx, ref sy, ref sz, ref n);
            Edge(d4, d6, 0, 0, 1, 1, ref sx, ref sy, ref sz, ref n);
            Edge(d5, d7, 1, 0, 1, 1, ref sx, ref sy, ref sz, ref n);
            Edge(d0, d4, 0, 0, 0, 2, ref sx, ref sy, ref sz, ref n);
            Edge(d1, d5, 1, 0, 0, 2, ref sx, ref sy, ref sz, ref n);
            Edge(d2, d6, 0, 1, 0, 2, ref sx, ref sy, ref sz, ref n);
            Edge(d3, d7, 1, 1, 0, 2, ref sx, ref sy, ref sz, ref n);
            float fx = sx / n, fy = sy / n, fz = sz / n, ux = 1 - fx, uy = 1 - fy, uz = 1 - fz;
            float gx = uy * uz * (d1 - d0) + fy * uz * (d3 - d2) + uy * fz * (d5 - d4) + fy * fz * (d7 - d6);
            float gy = ux * uz * (d2 - d0) + fx * uz * (d3 - d1) + ux * fz * (d6 - d4) + fx * fz * (d7 - d5);
            float gz = ux * uy * (d4 - d0) + fx * uy * (d5 - d1) + ux * fy * (d6 - d2) + fx * fy * (d7 - d3);
            float len = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
            float inv = len > 0 ? -1f / len : 0;
            return sink.AddVertex((ci + fx) * s, (j + fy) * s, (ck + fz) * s, gx * inv, gy * inv, gz * inv);
        }

        static void Edge(float da, float db, int ax, int ay, int az, int axis, ref float sx, ref float sy, ref float sz, ref int n)
        {
            if (da > 0 == db > 0) return;
            float t = da / (da - db);
            sx += axis == 0 ? ax + t : ax;
            sy += axis == 1 ? ay + t : ay;
            sz += axis == 2 ? az + t : az;
            n++;
        }

        static void Quad<TSink>(ref TSink sink, bool solidLow, int q0, int q1, int q2, int q3) where TSink : struct, INetsSink
        {
            int a = sink.GetInt(q0), b = sink.GetInt(q1), c = sink.GetInt(q2), d = sink.GetInt(q3);
            if (solidLow) { sink.AddTriangle(a, b, d); sink.AddTriangle(d, b, c); }
            else { sink.AddTriangle(a, d, b); sink.AddTriangle(d, c, b); }
        }
    }
}
