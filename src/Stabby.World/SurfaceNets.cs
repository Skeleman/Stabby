using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public interface ISolidField
    {
        float Density(int i, int j, int k);
        bool Sharp(int i, int j, int k);
        float Base(int i, int j, int k);
        float Edit(float d, float x, float y, float z);
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

        public float Density(int i, int j, int k) => Features.Level(Columns, Columns.Height(i, k) - j * StepM, i, k, StepM);
        public bool Sharp(int i, int j, int k) => false;
        public float Base(int i, int j, int k) => Density(i, j, k);
        public float Edit(float d, float x, float y, float z) => d;
    }

    struct CellField<TField> where TField : struct, ISolidField
    {
        public TField F;
        public float B0, B1, B2, B3, B4, B5, B6, B7, S;
        public int I, J, K;

        public float At(float u, float v, float w)
        {
            float a = B0 + (B1 - B0) * u, b = B2 + (B3 - B2) * u, c = B4 + (B5 - B4) * u, d = B6 + (B7 - B6) * u;
            float lo = a + (b - a) * v, hi = c + (d - c) * v;
            return F.Edit(lo + (hi - lo) * w, (I + u) * S, (J + v) * S, (K + w) * S);
        }
    }

    struct Sym
    {
        public float A00, A01, A02, A11, A12, A22;
        public float V00, V01, V02, V10, V11, V12, V20, V21, V22;

        public void Rotate(int p, int r)
        {
            float apq = Get(p, r);
            if (!(Math.Abs(apq) > 1e-12f)) return;
            float theta = (Get(r, r) - Get(p, p)) / (2 * apq);
            float t = (theta >= 0 ? 1f : -1f) / (Math.Abs(theta) + (float)Math.Sqrt(theta * theta + 1));
            float c = 1f / (float)Math.Sqrt(t * t + 1), s = t * c;
            int o = 3 - p - r;
            float app = Get(p, p), arr = Get(r, r), aop = Get(o, p), aor = Get(o, r);
            Set(p, p, app - t * apq);
            Set(r, r, arr + t * apq);
            Set(p, r, 0);
            Set(o, p, c * aop - s * aor);
            Set(o, r, s * aop + c * aor);
            for (int k = 0; k < 3; k++)
            {
                float vp = GetV(k, p), vr = GetV(k, r);
                SetV(k, p, c * vp - s * vr);
                SetV(k, r, s * vp + c * vr);
            }
        }

        float Get(int a, int b)
        {
            int k = a < b ? a * 3 + b : b * 3 + a;
            return k == 0 ? A00 : k == 1 ? A01 : k == 2 ? A02 : k == 4 ? A11 : k == 5 ? A12 : A22;
        }

        void Set(int a, int b, float v)
        {
            int k = a < b ? a * 3 + b : b * 3 + a;
            if (k == 0) A00 = v; else if (k == 1) A01 = v; else if (k == 2) A02 = v; else if (k == 4) A11 = v; else if (k == 5) A12 = v; else A22 = v;
        }

        float GetV(int a, int b)
        {
            int k = a * 3 + b;
            return k == 0 ? V00 : k == 1 ? V01 : k == 2 ? V02 : k == 3 ? V10 : k == 4 ? V11 : k == 5 ? V12 : k == 6 ? V20 : k == 7 ? V21 : V22;
        }

        void SetV(int a, int b, float v)
        {
            int k = a * 3 + b;
            if (k == 0) V00 = v; else if (k == 1) V01 = v; else if (k == 2) V02 = v; else if (k == 3) V10 = v; else if (k == 4) V11 = v; else if (k == 5) V12 = v; else if (k == 6) V20 = v; else if (k == 7) V21 = v; else V22 = v;
        }
    }

    struct Qef
    {
        public float A00, A01, A02, A11, A12, A22, B0, B1, B2;
        public int N;
    }

    public struct DetailColumns : IColumnField
    {
        public TerrainDetail Detail;
        public float StepM;

        public float Height(int i, int k) => Detail.Sample(i * StepM, k * StepM);
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

        public bool SameSurfaceAs(NetsBuffers o)
        {
            var mine = Faces(this);
            var theirs = Faces(o);
            if (mine.Count != theirs.Count) return false;
            for (int k = 0; k < mine.Count; k++)
                if (mine[k] != theirs[k]) return false;
            return true;
        }

        public bool SameRimAs(NetsBuffers o, int cells)
        {
            int w = cells + 1;
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    if (i != 0 && k != 0 && i != cells && k != cells) continue;
                    int col = 3 * (k * w + i), count = ints[col + 1] - ints[col];
                    if (count != o.ints[col + 1] - o.ints[col] || ints[col] != o.ints[col]) return false;
                    for (int q = 0; q < count * 6; q++)
                        if (BitConverter.SingleToInt32Bits(Verts[ints[col + 2] * 6 + q]) != BitConverter.SingleToInt32Bits(o.Verts[o.ints[col + 2] * 6 + q])) return false;
                }
            return true;
        }

        static List<string> Faces(NetsBuffers b)
        {
            var list = new List<string>(b.Tris.Count / 3);
            var v = new string[3];
            for (int t = 0; t < b.Tris.Count; t += 3)
            {
                for (int q = 0; q < 3; q++)
                {
                    int o = b.Tris[t + q] * 6;
                    v[q] = BitConverter.SingleToInt32Bits(b.Verts[o]).ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + BitConverter.SingleToInt32Bits(b.Verts[o + 1]).ToString("x8", System.Globalization.CultureInfo.InvariantCulture) + BitConverter.SingleToInt32Bits(b.Verts[o + 2]).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
                }
                int m = string.CompareOrdinal(v[0], v[1]) <= 0 ? (string.CompareOrdinal(v[0], v[2]) <= 0 ? 0 : 2) : (string.CompareOrdinal(v[1], v[2]) <= 0 ? 1 : 2);
                list.Add(v[m] + v[(m + 1) % 3] + v[(m + 2) % 3]);
            }
            list.Sort(string.CompareOrdinal);
            return list;
        }

        public int ColumnStart(int col) => ints[3 * col];
        public int ColumnEnd(int col) => ints[3 * col + 1];
        public int ColumnBase(int col) => ints[3 * col + 2];

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
                        sink.SetInt((k * w + i) * layers + j - jMin, Cell(ref sink, field, ci, j, ck, c.StepM, d0, d1, d2, d3, d4, d5, d6, d7));
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
            where TCols : struct, IColumnField where TSink : struct, INetsSink =>
            Edited(columns, default(NoEdits), c, ref sink);

        public static void Edited<TCols, TEdits, TSink>(TCols columns, TEdits edits, NetsChunk c, ref TSink sink)
            where TCols : struct, IColumnField where TEdits : struct, IEditList where TSink : struct, INetsSink =>
            Edited(columns, edits, c, ref sink, true);

        public static void Edited<TCols, TEdits, TSink>(TCols columns, TEdits edits, NetsChunk c, ref TSink sink, bool skip)
            where TCols : struct, IColumnField where TEdits : struct, IEditList where TSink : struct, INetsSink
        {
            int w = c.Cells + 1;
            Layout(columns, edits, c, ref sink);
            Vertices(columns, edits, c, ref sink, skip, 0, w);
            Quads(edits, c, ref sink, skip, 1, w);
        }

        public static int LayoutInts(NetsChunk c) => 3 * (c.Cells + 1) * (c.Cells + 1);

        public static int LayoutFloats(NetsChunk c)
        {
            int hw = c.Cells + 2, rw = hw + 2;
            return rw * rw + hw * hw;
        }

        public static int Layout<TCols, TEdits, TSink>(TCols columns, TEdits edits, NetsChunk c, ref TSink sink)
            where TCols : struct, IColumnField where TEdits : struct, IEditList where TSink : struct, INetsSink
        {
            int w = c.Cells + 1, hw = w + 1, rw = hw + 2, bx = c.X0 - 1, bz = c.Z0 - 1, sl = rw * rw;
            float s = c.StepM;
            sink.Begin(LayoutInts(c), LayoutFloats(c));
            for (int k = 0; k < rw; k++)
                for (int i = 0; i < rw; i++)
                    sink.SetFloat(k * rw + i, columns.Height(bx - 1 + i, bz - 1 + k));
            for (int k = 0; k < hw; k++)
                for (int i = 0; i < hw; i++)
                {
                    int r = (k + 1) * rw + i + 1;
                    sink.SetFloat(sl + k * hw + i, Features.Slope(sink.GetFloat(r + 1), sink.GetFloat(r - 1), sink.GetFloat(r + rw), sink.GetFloat(r - rw), s));
                }
            int next = 0;
            for (int k = 0; k < w; k++)
                for (int i = 0; i < w; i++)
                {
                    int col = 3 * (k * w + i);
                    Span(edits, H(ref sink, rw, i, k), H(ref sink, rw, i + 1, k), H(ref sink, rw, i, k + 1), H(ref sink, rw, i + 1, k + 1), s, bx + i, bz + k, out int j, out int end);
                    sink.SetInt(col, j);
                    sink.SetInt(col + 2, next);
                    sink.SetInt(col + 1, end);
                    next += end - j;
                }
            return next;
        }

        public static void Vertices<TCols, TEdits, TSink>(TCols columns, TEdits edits, NetsChunk c, ref TSink sink, bool skip, int k0, int k1)
            where TCols : struct, IColumnField where TEdits : struct, IEditList where TSink : struct, INetsSink
        {
            int w = c.Cells + 1, hw = w + 1, rw = hw + 2, bx = c.X0 - 1, bz = c.Z0 - 1, sl = rw * rw;
            float s = c.StepM;
            var solid = new EditedSolid<TCols, TEdits> { Columns = columns, Edits = edits, StepM = s };
            for (int k = k0; k < k1; k++)
                for (int i = 0; i < w; i++)
                {
                    int ci = bx + i, ck = bz + k, col = 3 * (k * w + i);
                    float h00 = H(ref sink, rw, i, k), h10 = H(ref sink, rw, i + 1, k);
                    float h01 = H(ref sink, rw, i, k + 1), h11 = H(ref sink, rw, i + 1, k + 1);
                    float l00 = sink.GetFloat(sl + k * hw + i), l10 = sink.GetFloat(sl + k * hw + i + 1);
                    float l01 = sink.GetFloat(sl + (k + 1) * hw + i), l11 = sink.GetFloat(sl + (k + 1) * hw + i + 1);
                    for (int j = sink.GetInt(col), end = sink.GetInt(col + 1); j < end; j++)
                    {
                        if (skip && Plain(edits, h00, h10, h01, h11, l00, l10, l01, l11, s, ci, j, ck)) sink.AddVertex((ci + 0.5f) * s, (j + 0.5f) * s, (ck + 0.5f) * s, 0, 1, 0);
                        else Emit(ref sink, solid, edits, ci, j, ck, s, h00, h10, h01, h11, l00, l10, l01, l11, true);
                    }
                }
        }

        public static void Quads<TEdits, TSink>(TEdits edits, NetsChunk c, ref TSink sink, bool skip, int k0, int k1)
            where TEdits : struct, IEditList where TSink : struct, INetsSink
        {
            int w = c.Cells + 1, hw = w + 1, rw = hw + 2, bx = c.X0 - 1, bz = c.Z0 - 1, sl = rw * rw;
            float s = c.StepM;
            for (int k = Math.Max(k0, 1); k < k1; k++)
                for (int i = 1; i < w; i++)
                {
                    int ci = bx + i, ck = bz + k;
                    int col = 3 * (k * w + i), cx = col - 3, cz = col - 3 * w, cxz = cz - 3;
                    int jLo = Math.Min(Math.Min(sink.GetInt(col), sink.GetInt(cx)), Math.Min(sink.GetInt(cz), sink.GetInt(cxz)));
                    int jHi = Math.Max(Math.Max(sink.GetInt(col + 1), sink.GetInt(cx + 1)), Math.Max(sink.GetInt(cz + 1), sink.GetInt(cxz + 1)));
                    float h = H(ref sink, rw, i, k), hx = H(ref sink, rw, i + 1, k), hz = H(ref sink, rw, i, k + 1);
                    float l = sink.GetFloat(sl + k * hw + i), lx = sink.GetFloat(sl + k * hw + i + 1), lz = sink.GetFloat(sl + (k + 1) * hw + i);
                    float xa = ci * s, xb = (ci + 1) * s, za = ck * s, zb = (ck + 1) * s;
                    float hxz = H(ref sink, rw, i + 1, k + 1), lxz = sink.GetFloat(sl + (k + 1) * hw + i + 1);
                    for (int j = jLo; j < jHi; j++)
                    {
                        if (skip && Plain(edits, h, hx, hz, hxz, l, lx, lz, lxz, s, ci, j, ck)) continue;
                        float yb = j * s, yt = (j + 1) * s;
                        bool p = Features.Edit(edits, (h - yb) / l, xa, yb, za) > 0;
                        if (p != Features.Edit(edits, (hx - yb) / lx, xb, yb, za) > 0) QuadAt(ref sink, p, cz, j - 1, cz, j, col, j, col, j - 1);
                        if (p != Features.Edit(edits, (h - yt) / l, xa, yt, za) > 0) QuadAt(ref sink, p, cxz, j, cx, j, col, j, cz, j);
                        if (p != Features.Edit(edits, (hz - yb) / lz, xa, yb, zb) > 0) QuadAt(ref sink, p, cx, j - 1, col, j - 1, col, j, cx, j);
                    }
                }
        }

        public static int CoarseVertex<TCols, TEdits>(TCols columns, TEdits edits, float s, int ci, int j, int ck, out float x, out float y, out float z)
            where TCols : struct, IColumnField where TEdits : struct, IEditList
            => CoarseVertex(columns, edits, s, ci, j, ck, out x, out y, out z, out _, out _, out _);

        public static int CoarseVertex<TCols, TEdits>(TCols columns, TEdits edits, float s, int ci, int j, int ck, out float x, out float y, out float z, out float nx, out float ny, out float nz)
            where TCols : struct, IColumnField where TEdits : struct, IEditList
        {
            float h00 = columns.Height(ci, ck), h10 = columns.Height(ci + 1, ck), h01 = columns.Height(ci, ck + 1), h11 = columns.Height(ci + 1, ck + 1);
            float l00 = Features.Slope(h10, columns.Height(ci - 1, ck), h01, columns.Height(ci, ck - 1), s);
            float l10 = Features.Slope(columns.Height(ci + 2, ck), h00, h11, columns.Height(ci + 1, ck - 1), s);
            float l01 = Features.Slope(h11, columns.Height(ci - 1, ck + 1), columns.Height(ci, ck + 2), h00, s);
            float l11 = Features.Slope(columns.Height(ci + 2, ck + 1), h01, columns.Height(ci + 1, ck + 2), h10, s);
            Span(edits, h00, h10, h01, h11, s, ci, ck, out int start, out int end);
            j = Math.Max(start, Math.Min(end - 1, j));
            var sink = new PointSink();
            var solid = new EditedSolid<TCols, TEdits> { Columns = columns, Edits = edits, StepM = s };
            int cell = j;
            for (int d = 0; d < end - start; d++)
            {
                int a = j - d, b = j + d;
                if (a >= start && Emit(ref sink, solid, edits, ci, a, ck, s, h00, h10, h01, h11, l00, l10, l01, l11, false)) { cell = a; break; }
                if (d > 0 && b < end && Emit(ref sink, solid, edits, ci, b, ck, s, h00, h10, h01, h11, l00, l10, l01, l11, false)) { cell = b; break; }
                if (d == end - start - 1) Emit(ref sink, solid, edits, ci, j, ck, s, h00, h10, h01, h11, l00, l10, l01, l11, true);
            }
            x = sink.X; y = sink.Y; z = sink.Z;
            nx = sink.NX; ny = sink.NY; nz = sink.NZ;
            return cell;
        }

        public static bool CrossingVertex<TCols, TEdits>(TCols columns, TEdits edits, float s, int ci, int j, int ck, out float x, out float y, out float z, out float nx, out float ny, out float nz)
            where TCols : struct, IColumnField where TEdits : struct, IEditList
        {
            float h00 = columns.Height(ci, ck), h10 = columns.Height(ci + 1, ck), h01 = columns.Height(ci, ck + 1), h11 = columns.Height(ci + 1, ck + 1);
            float l00 = Features.Slope(h10, columns.Height(ci - 1, ck), h01, columns.Height(ci, ck - 1), s);
            float l10 = Features.Slope(columns.Height(ci + 2, ck), h00, h11, columns.Height(ci + 1, ck - 1), s);
            float l01 = Features.Slope(h11, columns.Height(ci - 1, ck + 1), columns.Height(ci, ck + 2), h00, s);
            float l11 = Features.Slope(columns.Height(ci + 2, ck + 1), h01, columns.Height(ci + 1, ck + 2), h10, s);
            var sink = new PointSink();
            bool hit = Emit(ref sink, new EditedSolid<TCols, TEdits> { Columns = columns, Edits = edits, StepM = s }, edits, ci, j, ck, s, h00, h10, h01, h11, l00, l10, l01, l11, false);
            x = sink.X; y = sink.Y; z = sink.Z;
            nx = sink.NX; ny = sink.NY; nz = sink.NZ;
            return hit;
        }

        struct PointSink : INetsSink
        {
            public float X, Y, Z, NX, NY, NZ;
            public void Begin(int ints, int floats) { }
            public int GetInt(int k) => 0;
            public void SetInt(int k, int v) { }
            public float GetFloat(int k) => 0;
            public void SetFloat(int k, float v) { }
            public int AddVertex(float x, float y, float z, float nx, float ny, float nz) { X = x; Y = y; Z = z; NX = nx; NY = ny; NZ = nz; return 0; }
            public void AddTriangle(int a, int b, int c) { }
        }

        static void Span<TEdits>(TEdits edits, float h00, float h10, float h01, float h11, float s, int ci, int ck, out int j, out int end) where TEdits : struct, IEditList
        {
            float lo = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
            j = (int)Math.Floor(lo / s) - 2;
            while (!AllInside(h00, h10, h01, h11, j - 1, s)) j--;
            while (AllInside(h00, h10, h01, h11, j, s)) j++;
            end = j;
            while (!AllOutside(h00, h10, h01, h11, end, s)) end++;
            for (int q = 0, count = edits.Count; q < count; q++)
            {
                var e = edits.At(q);
                if (e.MaxX < ci * s || e.MinX > (ci + 1) * s || e.MaxZ < ck * s || e.MinZ > (ck + 1) * s) continue;
                j = Math.Min(j, (int)Math.Floor(e.MinY / s) - 1);
                end = Math.Max(end, (int)Math.Floor(e.MaxY / s) + 2);
            }
        }

        static bool Plain<TEdits>(TEdits edits, float h00, float h10, float h01, float h11, float l00, float l10, float l01, float l11, float s, int ci, int j, int ck) where TEdits : struct, IEditList
        {
            bool inside = AllInside(h00, h10, h01, h11, j, s), outside = !inside && AllOutside(h00, h10, h01, h11, j, s);
            float x0 = ci * s, y0 = j * s, z0 = ck * s, x1 = x0 + s, y1 = y0 + s, z1 = z0 + s;
            float xb = (ci + 1) * s, yt = (j + 1) * s, zb = (ck + 1) * s;
            bool touched = false, hollow = false, bounded = true;
            double half = 0.8661 * s;
            float holeLo = float.MaxValue, kMax = 0f;
            for (int q = 0, count = edits.Count; q < count; q++)
            {
                var e = edits.At(q);
                if (e.MaxX < x0 || e.MinX > x1 || e.MaxY < y0 || e.MinY > y1 || e.MaxZ < z0 || e.MinZ > z1) continue;
                if (e.Shape == FeatureEdit.Spring || e.W > 0f)
                {
                    if (e.Kind == FeatureEdit.Add) return false;
                    touched = true;
                    bounded = false;
                    continue;
                }
                double ex = e.Bx - e.Ax, ey = e.By - e.Ay, ez = e.Bz - e.Az, px = x0 + 0.5 * s - e.Ax, py = y0 + 0.5 * s - e.Ay, pz = z0 + 0.5 * s - e.Az;
                double ll = ex * ex + ey * ey + ez * ez, t = ll > 0 ? Math.Max(0.0, Math.Min(1.0, (px * ex + py * ey + pz * ez) / ll)) : 0.0;
                double qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
                double cd = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                if (cd >= e.R + half + 0.01) continue;
                if (e.Kind == FeatureEdit.Add) return false;
                touched = true;
                double shell = e.R - 1.25 * e.R * FeatureEdit.Blend;
                e.RockSpan(out float rockLo, out float rockHi);
                float k = e.R * FeatureEdit.Blend, near = (float)(cd - half - 0.05 - shell - rockLo);
                if (!hollow && x0 >= e.MinX && xb <= e.MaxX && y0 >= e.MinY && yt <= e.MaxY && z0 >= e.MinZ && zb <= e.MaxZ)
                {
                    double far = cd + half + 0.05 + rockHi + (e.P > 0f ? e.R * (e.P + (e.D > 0f ? FeatureEdit.Flare + e.D : 0f)) : 0.0) + (e.N > 0f ? e.N : 0f);
                    hollow = far - shell < -0.05;
                }
                holeLo = holeLo < float.MaxValue ? FeatureEdit.Cut(holeLo, near, Features.JoinBlend * k) : near;
                kMax = Math.Max(kMax, k);
            }
            if (!touched) return inside || outside;
            if (hollow) return true;
            if (outside) return Math.Max(Math.Max((h00 - y0) / l00, (h10 - y0) / l10), Math.Max((h01 - y0) / l01, (h11 - y0) / l11)) < -0.05f;
            if (!inside || !bounded) return false;
            float dLo = Math.Min(Math.Min((h00 - yt) / l00, (h10 - yt) / l10), Math.Min((h01 - yt) / l01, (h11 - yt) / l11));
            return FeatureEdit.Cut(dLo, holeLo, kMax) > 0.05f;
        }

        static bool Emit<TSink, TCols, TEdits>(ref TSink sink, EditedSolid<TCols, TEdits> solid, TEdits edits, int ci, int j, int ck, float s,
            float h00, float h10, float h01, float h11, float l00, float l10, float l01, float l11, bool fill)
            where TSink : struct, INetsSink where TCols : struct, IColumnField where TEdits : struct, IEditList
        {
            float yb = j * s, yt = (j + 1) * s;
            float xa = ci * s, xb = (ci + 1) * s, za = ck * s, zb = (ck + 1) * s;
            int v = Cell(ref sink, solid, ci, j, ck, s,
                Features.Edit(edits, (h00 - yb) / l00, xa, yb, za), Features.Edit(edits, (h10 - yb) / l10, xb, yb, za),
                Features.Edit(edits, (h00 - yt) / l00, xa, yt, za), Features.Edit(edits, (h10 - yt) / l10, xb, yt, za),
                Features.Edit(edits, (h01 - yb) / l01, xa, yb, zb), Features.Edit(edits, (h11 - yb) / l11, xb, yb, zb),
                Features.Edit(edits, (h01 - yt) / l01, xa, yt, zb), Features.Edit(edits, (h11 - yt) / l11, xb, yt, zb));
            if (v < 0 && fill) sink.AddVertex((ci + 0.5f) * s, (j + 0.5f) * s, (ck + 0.5f) * s, 0, 1, 0);
            return v >= 0;
        }

        static float H<TSink>(ref TSink sink, int rw, int i, int k) where TSink : struct, INetsSink => sink.GetFloat((k + 1) * rw + i + 1);

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

        static int Cell<TSink, TField>(ref TSink sink, TField field, int ci, int j, int ck, float s, float d0, float d1, float d2, float d3, float d4, float d5, float d6, float d7)
            where TSink : struct, INetsSink where TField : struct, ISolidField
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
            float fx = sx / n, fy = sy / n, fz = sz / n;
            float gx, gy, gz;
            if (field.Sharp(ci, j, ck))
            {
                var f = Corners(field, ci, j, ck, s);
                Sharpen(ref f, d0, d1, d2, d3, d4, d5, d6, d7, ref fx, ref fy, ref fz);
                float e = QefStep;
                gx = f.At(fx + e, fy, fz) - f.At(fx - e, fy, fz);
                gy = f.At(fx, fy + e, fz) - f.At(fx, fy - e, fz);
                gz = f.At(fx, fy, fz + e) - f.At(fx, fy, fz - e);
            }
            else
            {
                float ux = 1 - fx, uy = 1 - fy, uz = 1 - fz;
                gx = uy * uz * (d1 - d0) + fy * uz * (d3 - d2) + uy * fz * (d5 - d4) + fy * fz * (d7 - d6);
                gy = ux * uz * (d2 - d0) + fx * uz * (d3 - d1) + ux * fz * (d6 - d4) + fx * fz * (d7 - d5);
                gz = ux * uy * (d4 - d0) + fx * uy * (d5 - d1) + ux * fy * (d6 - d2) + fx * fy * (d7 - d3);
            }
            float len = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
            float inv = len > 0 ? -1f / len : 0;
            return sink.AddVertex((ci + fx) * s, (j + fy) * s, (ck + fz) * s, gx * inv, gy * inv, gz * inv);
        }

        const float QefStep = 0.05f, QefDrop = 0.1f;

        static CellField<TField> Corners<TField>(TField field, int ci, int j, int ck, float s) where TField : struct, ISolidField
            => new CellField<TField>
            {
                F = field, S = s, I = ci, J = j, K = ck,
                B0 = field.Base(ci, j, ck), B1 = field.Base(ci + 1, j, ck), B2 = field.Base(ci, j + 1, ck), B3 = field.Base(ci + 1, j + 1, ck),
                B4 = field.Base(ci, j, ck + 1), B5 = field.Base(ci + 1, j, ck + 1), B6 = field.Base(ci, j + 1, ck + 1), B7 = field.Base(ci + 1, j + 1, ck + 1),
            };

        static void Sharpen<TField>(ref CellField<TField> f, float d0, float d1, float d2, float d3, float d4, float d5, float d6, float d7, ref float fx, ref float fy, ref float fz)
            where TField : struct, ISolidField
        {
            var q = new Qef();
            Hermite(ref f, ref q, d0, d1, 0, 0, 0, 0);
            Hermite(ref f, ref q, d2, d3, 0, 1, 0, 0);
            Hermite(ref f, ref q, d4, d5, 0, 0, 1, 0);
            Hermite(ref f, ref q, d6, d7, 0, 1, 1, 0);
            Hermite(ref f, ref q, d0, d2, 0, 0, 0, 1);
            Hermite(ref f, ref q, d1, d3, 1, 0, 0, 1);
            Hermite(ref f, ref q, d4, d6, 0, 0, 1, 1);
            Hermite(ref f, ref q, d5, d7, 1, 0, 1, 1);
            Hermite(ref f, ref q, d0, d4, 0, 0, 0, 2);
            Hermite(ref f, ref q, d1, d5, 1, 0, 0, 2);
            Hermite(ref f, ref q, d2, d6, 0, 1, 0, 2);
            Hermite(ref f, ref q, d3, d7, 1, 1, 0, 2);
            if (q.N == 0) return;
            float r0 = q.B0 - (q.A00 * fx + q.A01 * fy + q.A02 * fz);
            float r1 = q.B1 - (q.A01 * fx + q.A11 * fy + q.A12 * fz);
            float r2 = q.B2 - (q.A02 * fx + q.A12 * fy + q.A22 * fz);
            var m = new Sym { A00 = q.A00, A01 = q.A01, A02 = q.A02, A11 = q.A11, A12 = q.A12, A22 = q.A22, V00 = 1, V11 = 1, V22 = 1 };
            for (int sweep = 0; sweep < 6; sweep++)
            {
                m.Rotate(0, 1); m.Rotate(0, 2); m.Rotate(1, 2);
            }
            float top = Math.Max(Math.Abs(m.A00), Math.Max(Math.Abs(m.A11), Math.Abs(m.A22))), cut = QefDrop * top;
            float i0 = Math.Abs(m.A00) > cut ? 1f / m.A00 : 0, i1 = Math.Abs(m.A11) > cut ? 1f / m.A11 : 0, i2 = Math.Abs(m.A22) > cut ? 1f / m.A22 : 0;
            float p0 = (m.V00 * r0 + m.V10 * r1 + m.V20 * r2) * i0;
            float p1 = (m.V01 * r0 + m.V11 * r1 + m.V21 * r2) * i1;
            float p2 = (m.V02 * r0 + m.V12 * r1 + m.V22 * r2) * i2;
            fx = Clamp01(fx + m.V00 * p0 + m.V01 * p1 + m.V02 * p2);
            fy = Clamp01(fy + m.V10 * p0 + m.V11 * p1 + m.V12 * p2);
            fz = Clamp01(fz + m.V20 * p0 + m.V21 * p1 + m.V22 * p2);
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        static void Hermite<TField>(ref CellField<TField> f, ref Qef q, float da, float db, int ax, int ay, int az, int axis) where TField : struct, ISolidField
        {
            if (da > 0 == db > 0) return;
            float t = da / (da - db);
            float px = axis == 0 ? ax + t : ax, py = axis == 1 ? ay + t : ay, pz = axis == 2 ? az + t : az, e = QefStep;
            float gx = f.At(px + e, py, pz) - f.At(px - e, py, pz);
            float gy = f.At(px, py + e, pz) - f.At(px, py - e, pz);
            float gz = f.At(px, py, pz + e) - f.At(px, py, pz - e);
            float len = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
            if (!(len > 0)) return;
            gx /= len; gy /= len; gz /= len;
            float dot = gx * px + gy * py + gz * pz;
            q.A00 += gx * gx; q.A01 += gx * gy; q.A02 += gx * gz; q.A11 += gy * gy; q.A12 += gy * gz; q.A22 += gz * gz;
            q.B0 += gx * dot; q.B1 += gy * dot; q.B2 += gz * dot;
            q.N++;
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
