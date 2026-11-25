using System;

namespace Stabby.World
{
    public interface IGroundHeight
    {
        float HeightAt(float x, float z);
    }

    public struct DetailGround : IGroundHeight
    {
        public TerrainDetail Detail;

        public float HeightAt(float x, float z) => Detail.Sample(x, z);
    }

    public static class Walk
    {
        public const float StepUp = 1.2f, Height = 1.8f, Radius = 0.4f, FallSpeed = 25f, Probe = 0.2f, Reach = 64f;

        internal struct Column
        {
            public float X, Z, H, Slope;
            public bool Edited;
        }

        internal static Column At<TG, TE>(TG g, TE e, float x, float z, float s) where TG : struct, IGroundHeight where TE : struct, IEditList
        {
            var c = new Column { X = x, Z = z, H = g.HeightAt(x, z) };
            c.Slope = Features.Slope(g.HeightAt(x + s, z), g.HeightAt(x - s, z), g.HeightAt(x, z + s), g.HeightAt(x, z - s), s);
            for (int q = 0, n = e.Count; q < n && !c.Edited; q++)
            {
                var f = e.At(q);
                c.Edited = x >= f.MinX && x <= f.MaxX && z >= f.MinZ && z <= f.MaxZ;
            }
            return c;
        }

        internal static float Density<TE>(TE e, Column c, float y) where TE : struct, IEditList
        {
            float d = (c.H - y) / c.Slope;
            return c.Edited ? Features.Edit(e, d, c.X, y, c.Z) : d;
        }

        public static float Solid<TG, TE>(TG g, TE e, float s, float x, float y, float z) where TG : struct, IGroundHeight where TE : struct, IEditList
            => Density(e, At(g, e, x, z, s), y);

        static float Floor<TE>(TE e, Column c, float top, float bottom) where TE : struct, IEditList
        {
            if (!c.Edited) return c.H >= bottom ? c.H : float.NegativeInfinity;
            float yp = top, dp = Density(e, c, top);
            if (dp >= 0) return float.NaN;
            for (float y = top - Probe; y >= bottom; y -= Probe)
            {
                float d = Density(e, c, y);
                if (d >= 0) return yp + (y - yp) * dp / (dp - d);
                yp = y; dp = d;
            }
            return float.NegativeInfinity;
        }

        public static float Surface<TG, TE>(TG g, TE e, float s, float x, float z, float top, float bottom) where TG : struct, IGroundHeight where TE : struct, IEditList
        {
            var c = At(g, e, x, z, s);
            if (!c.Edited) return c.H >= bottom ? c.H : bottom;
            bool adds = false;
            for (int q = 0, n = e.Count; q < n && !adds; q++)
            {
                var f = e.At(q);
                adds = f.Kind == FeatureEdit.Add && x >= f.MinX && x <= f.MaxX && z >= f.MinZ && z <= f.MaxZ;
            }
            float yp = top, dp = Density(e, c, top);
            if (dp >= 0) return top;
            float y = top - Probe;
            if (!adds)
            {
                while (y >= bottom && y > c.H) { yp = y; y -= Probe; }
                if (yp != top) dp = Density(e, c, yp);
            }
            for (; y >= bottom; y -= Probe)
            {
                float d = Density(e, c, y);
                if (d >= 0) return yp + (y - yp) * dp / (dp - d);
                yp = y; dp = d;
            }
            return bottom;
        }

        static bool Try<TG, TE, TO>(TG g, TE e, TO o, float s, float y, float nx, float nz, float ux, float uz, float fall, out float ny) where TG : struct, IGroundHeight where TE : struct, IEditList where TO : struct, IColumnTable
        {
            ny = y;
            var c = At(g, e, nx, nz, s);
            float top = y + StepUp;
            if (Density(e, c, top) >= 0) return false;
            float f = Floor(e, c, top, y - Reach);
            float feet = f >= y - fall ? f : y - fall;
            float rock = Obstacles.Top(o, nx, nz, Radius, feet, top);
            if (rock > top) return false;
            if (rock > feet) feet = rock;
            if (Density(e, c, feet + Height) >= 0 || Density(e, c, feet + 0.5f * Height) >= 0) return false;
            var r = At(g, e, nx + ux * Radius, nz + uz * Radius, s);
            if (Density(e, r, top) >= 0 || Density(e, r, feet + Height) >= 0) return false;
            ny = feet;
            return true;
        }

        public static bool Step<TG, TE>(TG g, TE e, float s, ref float x, ref float y, ref float z, float dx, float dz, float fall) where TG : struct, IGroundHeight where TE : struct, IEditList
            => Step(g, e, default(NoColumns), s, ref x, ref y, ref z, dx, dz, fall);

        public static bool Step<TG, TE, TO>(TG g, TE e, TO o, float s, ref float x, ref float y, ref float z, float dx, float dz, float fall) where TG : struct, IGroundHeight where TE : struct, IEditList where TO : struct, IColumnTable
        {
            float l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l > 1e-6f)
            {
                float ux = dx / l, uz = dz / l;
                if (Try(g, e, o, s, y, x + dx, z + dz, ux, uz, fall, out float ny)) { x += dx; z += dz; y = ny; return true; }
                if (dx != 0 && Try(g, e, o, s, y, x + dx, z, Math.Sign(dx), 0, fall, out ny)) { x += dx; y = ny; return true; }
                if (dz != 0 && Try(g, e, o, s, y, x, z + dz, 0, Math.Sign(dz), fall, out ny)) { z += dz; y = ny; return true; }
            }
            var c = At(g, e, x, z, s);
            float rest = Obstacles.Top(o, x, z, Radius, y - Reach, y + StepUp);
            float f = Math.Max(Floor(e, c, y + StepUp, y - Reach), rest <= y + StepUp ? rest : float.NegativeInfinity);
            if (!float.IsNaN(f)) y = f >= y - fall ? f : y - fall;
            return false;
        }
    }
}
