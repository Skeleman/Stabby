using System;

namespace Stabby.World
{
    public interface ISurfaceField : IColumnField
    {
        float At(float x, float z);
    }

    public struct StitchPoint
    {
        public float X, Y, Z, NX, NY, NZ;
    }

    public static class LodStitch
    {
        public static bool Target<TCols, TEdits>(TCols coarse, TEdits near, float s2, int x0, int z0, int m, int i, int j, int k, out StitchPoint p)
            where TCols : struct, ISurfaceField where TEdits : struct, IEditList
        {
            Axis(x0, i, m, s2, out int cx, out bool sx, out bool lx, out float ax);
            Axis(z0, k, m, s2, out int cz, out bool sz, out bool lz, out float az);
            bool hit = Row(coarse, near, s2, cx, sx, lx, ax, cz, sz && !lz, az, j >> 1, out p);
            if (lz)
            {
                hit &= Row(coarse, near, s2, cx, sx, lx, ax, cz + 1, false, az, j >> 1, out var p1);
                p = Lerp(p, p1, Fraction(p.Z, p1.Z, az));
            }
            if (sz) p.Z = az;
            return hit;
        }

        public static bool Open<TEdits>(TEdits edits, float x, float z) where TEdits : struct, IEditList
        {
            for (int q = 0; q < edits.Count; q++)
            {
                var e = edits.At(q);
                if (x >= e.MinX && x <= e.MaxX && z >= e.MinZ && z <= e.MaxZ) return false;
            }
            return true;
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

        static bool Row<TCols, TEdits>(TCols coarse, TEdits near, float s2, int cx, bool sx, bool lx, float ax, int cz, bool sz, float az, int j, out StitchPoint p)
            where TCols : struct, ISurfaceField where TEdits : struct, IEditList
        {
            bool hit = Coarse(coarse, near, s2, cx, j, cz, sx && !lx, ax, sz, az, out p);
            if (lx)
            {
                hit &= Coarse(coarse, near, s2, cx + 1, j, cz, false, ax, sz, az, out var q);
                p = Lerp(p, q, Fraction(p.X, q.X, ax));
            }
            if (sx) p.X = ax;
            return hit;
        }

        static bool Coarse<TCols, TEdits>(TCols coarse, TEdits near, float s2, int cx, int j, int cz, bool sx, float ax, bool sz, float az, out StitchPoint p)
            where TCols : struct, ISurfaceField where TEdits : struct, IEditList
        {
            SurfaceNets.CoarseVertex(coarse, near, s2, cx, j, cz, out p.X, out p.Y, out p.Z, out p.NX, out p.NY, out p.NZ);
            if (sx) p.X = ax;
            if (sz) p.Z = az;
            if ((sx || sz) && Open(near, p.X, p.Z)) p.Y = coarse.At(p.X, p.Z);
            return true;
        }

        static float Fraction(float a, float b, float t) => b != a ? Math.Max(0f, Math.Min(1f, (t - a) / (b - a))) : 0.5f;

        static StitchPoint Lerp(StitchPoint a, StitchPoint b, float f) => new StitchPoint
        {
            X = a.X + (b.X - a.X) * f, Y = a.Y + (b.Y - a.Y) * f, Z = a.Z + (b.Z - a.Z) * f,
            NX = a.NX + (b.NX - a.NX) * f, NY = a.NY + (b.NY - a.NY) * f, NZ = a.NZ + (b.NZ - a.NZ) * f,
        };
    }
}
