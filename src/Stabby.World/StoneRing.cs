using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float RingU = 1 / 16f, RingRadius = 0.35f;
        static readonly float[] RingSeat = { 0, 0, 0.37f, 0, -0.37f, 0, 0, 0.37f, 0, -0.37f, 0.26f, 0.26f, -0.26f, 0.26f, 0.26f, -0.26f, -0.26f, -0.26f };

        static void StoneRing(StructureLayout l, ulong key)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x2545F4914F6CDD1DUL));
            int slots = rng.Range(8, 13), fallen = rng.Range(1, 4), off = rng.Range(0, 16);
            float radius = RingRadius * rng.Range(0.94f, 1.06f);
            var down = new bool[slots];
            for (int k = 0; k < fallen; k++) down[rng.Range(0, slots)] = true;
            down[rng.Range(0, slots)] = true;
            for (int i = 0; i < slots; i++)
            {
                float t = off + 16f * i / slots + rng.Range(-0.25f, 0.25f);
                int k = (int)Math.Floor(t);
                float f = t - k, dx = Cos16(k) + f * (Cos16(k + 1) - Cos16(k)), dz = Sin16(k) + f * (Sin16(k + 1) - Sin16(k));
                float inv = 1 / (float)Math.Sqrt(dx * dx + dz * dz), rr = radius * rng.Range(0.95f, 1.05f);
                dx *= inv; dz *= inv;
                StandingStone(l, rng.NextU64(), dx * rr, dz * rr, dx, dz, down[i]);
            }
            if (rng.NextFloat() < 0.4f)
            {
                float yd = rng.Range(-1f, 1f), c = (float)Math.Sqrt(1 - 0.25f * yd * yd);
                float sx = RingU * rng.Range(2.0f, 2.8f), sz = RingU * rng.Range(0.9f, 1.3f), sy = RingU * rng.Range(0.5f, 0.8f);
                Add(l, key, 0, -0.2f * sy, 0, sx, sy, sz, c, 0.5f * yd, 0, 1);
                l.Columns.Add(new ObjectColumn { X = 0, Z = 0, R = 0.45f * sz, Bottom = 0, Top = 0.8f * sy });
            }
        }

        static void StandingStone(StructureLayout l, ulong key, float x, float z, float dx, float dz, bool fallen)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x9E3779B97F4A7C15UL));
            float h = RingU * rng.Range(1.4f, 2.6f), wd = RingU * rng.Range(0.6f, 1.0f), dp = RingU * rng.Range(0.4f, 0.7f);
            float tw = rng.Range(-0.35f, 0.35f), c = dx - tw * dz, s = dz + tw * dx, n = 1 / (float)Math.Sqrt(c * c + s * s);
            c *= n; s *= n;
            if (fallen)
            {
                float out1 = rng.Range(0.2f, 0.6f) * h, lx = x + dx * out1, lz = z + dz * out1;
                Add(l, key, lx, -0.25f * dp, lz, h, dp, wd, c, s, 0, 1, Squareness, 0, 0, 0.1f);
                for (int e = -1; e <= 1; e += 2)
                    l.Columns.Add(new ObjectColumn { X = lx + e * 0.3f * h * c, Z = lz + e * 0.3f * h * s, R = 0.45f * Math.Min(wd, dp + wd), Bottom = 0, Top = 0.75f * dp });
                int bits = rng.Range(1, 4);
                for (int b = 0; b < bits; b++)
                {
                    float bx = x + rng.Range(-0.6f, 0.6f) * wd, bz = z + rng.Range(-0.6f, 0.6f) * wd, bs = wd * rng.Range(0.2f, 0.4f);
                    Add(l, key, bx, -0.3f * bs, bz, bs * 1.3f, bs * 0.7f, bs, s, -c, 0, 0);
                }
                return;
            }
            float lean = rng.NextFloat() < 0.25f ? rng.Range(-0.3f, 0.3f) : rng.Range(-0.12f, 0.12f);
            Add(l, key, x, -0.15f * RingU, z, dp, h + 0.15f * RingU, wd, c, s, 0, 2, Squareness, lean, rng.Range(-0.05f, 0.05f), rng.Range(0.08f, 0.22f));
            l.Columns.Add(new ObjectColumn { X = x + 0.3f * lean * h * c, Z = z + 0.3f * lean * h * s, R = 0.4f * (wd + dp), Bottom = 0, Top = h });
            if (rng.NextFloat() < 0.5f)
            {
                float bs = wd * rng.Range(0.25f, 0.45f), side = rng.NextFloat() < 0.5f ? -1 : 1;
                Add(l, key, x - side * s * 0.8f * wd, -0.3f * bs, z + side * c * 0.8f * wd, bs * 1.4f, bs * 0.6f, bs, c, s, 0, 0);
            }
        }
    }
}
