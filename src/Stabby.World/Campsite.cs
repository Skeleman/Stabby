using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const int Ash = 7;
        public const float CampU = 1 / 10f;

        static void Rock(StructureLayout l, ulong key, in Frame f, float x, float y, float z, float sx, float sy, float sz, float c, float s, int lo, int hi, float lean = 0)
        {
            int a = Math.Max(f.Lo, lo), b = Math.Min(f.Hi, hi);
            if (a > b) return;
            Add(l, key, f.X + f.U * (f.C * x - f.S * z), f.Y + f.U * y, f.Z + f.U * (f.S * x + f.C * z), f.U * sx, f.U * sy, f.U * sz, f.C * c - f.S * s, f.S * c + f.C * s, a, b, Squareness, lean);
        }

        static void LogBar(StructureLayout l, ulong key, in Frame f, float x, float y, float z, float len, float d, float c, float s, int m, int lo, int hi)
        {
            float a = 0.293f * d, w = 0.414f * d;
            int top = Math.Min(hi, 1);
            Box(l, key, f, x, y, z, len, a, 0.707f * d, m, GX, lo, top, c, s, 0, 0, -1);
            Box(l, key, f, x, y + a, z, len, w, d, m, GX, lo, hi, c, s);
            Box(l, key, f, x, y + a + w, z, len, a, 0.707f * d, m, GX, lo, top, c, s, 0, 0, 1);
        }

        static void Tent(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "tent");
            float len = rng.Range(2.2f, 2.6f), hw = rng.Range(1.0f, 1.2f), h = rng.Range(1.2f, 1.45f), sd = rng.NextFloat() < 0.5f ? -1 : 1;
            float pz = 0.5f * len + 0.08f, rope = rng.Range(0.55f, 0.7f);
            int first = l.Pieces.Count;
            for (int side = -1; side <= 1; side += 2)
                Box(l, key, f, side * 0.5f * hw, 0, 0, 0.03f, h, len, Cloth, GV, 0, 2, 1, 0, -side * hw / h);
            Box(l, key, f, 0, 0, -0.5f * len, 0.03f, h, hw, Cloth, GV, 0, 2, 0, 1, 0, 0, hw / h);
            Box(l, key, f, 0.25f * sd * hw, 0, 0.5f * len, 0.03f, h, 0.5f * hw, Cloth, GV, 0, 2, 0, 1, 0, sd * hw / (2 * h), hw / (2 * h));
            Box(l, key, f, -0.92f * sd * hw, 0, 0.5f * len + 0.04f, 0.13f, 0.4f * h, 0.13f, Cloth, GV, 0, 1, 1, 0, sd * 0.1f);
            int last = l.Pieces.Count;
            ulong dye = Rng.Mix(key ^ 0x7F4A7C159E3779B9UL);
            for (int i = first; i < last; i++)
            {
                var p = l.Pieces[i];
                p.Key = dye;
                l.Pieces[i] = p;
            }
            Box(l, key, f, 0, h - 0.03f, 0, 0.05f, 0.05f, len + 0.3f, Timber, GZ, 0, 1);
            for (int e = -1; e <= 1; e += 2)
            {
                Box(l, key, f, 0, 0, e * pz, 0.05f, h, 0.05f, Timber, GV, 0, 1);
                Box(l, key, f, 0, 0, e * (pz + 0.5f * rope), 0.015f, h, 0.015f, Timber, GV, 0, 0, 1, 0, 0, -e * rope / h);
                Box(l, key, f, 0, -0.05f, e * (pz + rope), 0.035f, 0.2f, 0.035f, Timber, GV, 0, 0, 1, 0, 0, e * 0.2f);
                for (int side = -1; side <= 1; side += 2)
                    Box(l, key, f, side * (hw + 0.06f), -0.04f, e * (0.5f * len - 0.1f), 0.03f, 0.14f, 0.03f, Timber, GV, 0, 0, 1, 0, side * 0.25f);
                Col(l, f, 0, e * 0.25f * len, 0.8f * hw, 0, h);
            }
        }

        static void FirePit(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "fire-pit");
            float r = rng.Range(0.55f, 0.7f), sq = 0.62f * r;
            int stones = rng.Range(9, 14), off = rng.Range(0, 16);
            for (int i = 0; i < stones; i++)
            {
                float t = off + 16f * i / stones + rng.Range(-0.2f, 0.2f);
                int k = (int)Math.Floor(t);
                float u = t - k, dx = Cos16(k) + u * (Cos16(k + 1) - Cos16(k)), dz = Sin16(k) + u * (Sin16(k + 1) - Sin16(k));
                float inv = 1 / (float)Math.Sqrt(dx * dx + dz * dz), rr = r * rng.Range(0.93f, 1.07f);
                dx *= inv; dz *= inv;
                float sx = rng.Range(0.24f, 0.34f), sy = rng.Range(0.17f, 0.25f), sz = rng.Range(0.18f, 0.26f), tw = rng.Range(-0.3f, 0.3f);
                float c = -dz - tw * dx, s = dx - tw * dz, n = 1 / (float)Math.Sqrt(c * c + s * s);
                Rock(l, rng.NextU64(), f, dx * rr, -0.12f * sy, dz * rr, sx, sy, sz, c * n, s * n, 0, 2, rng.Range(-0.1f, 0.1f));
            }
            Box(l, key, f, 0, -0.08f, 0, 2 * sq, 0.19f, 2 * sq * 0.98f, Ash, GX, 0, 2);
            Box(l, key, f, 0, -0.08f, 0, 2 * sq * 0.96f, 0.17f, 2 * sq, Ash, GX, 0, 0, Cos16(2), Sin16(2));
            int logs = rng.Range(2, 5), lk = rng.Range(0, 16);
            for (int i = 0; i < logs; i++)
            {
                int k = lk + i * 16 / logs + rng.Range(0, 2);
                float dx = Cos16(k), dz = Sin16(k), len = rng.Range(0.35f, 0.55f), at = rng.Range(0.05f, 0.25f) + 0.5f * len;
                LogBar(l, key, f, dx * at, 0.12f, dz * at, len, rng.Range(0.07f, 0.11f), dx, dz, Ash, 0, 1);
            }
            if (rng.NextFloat() < 0.5f)
            {
                float top = rng.Range(0.95f, 1.1f), lr = r + 0.12f;
                int k0 = rng.Range(0, 16);
                for (int i = 0; i < 3; i++)
                {
                    int k = k0 + (i * 16 + 1) / 3;
                    float bx = Cos16(k) * lr, bz = Sin16(k) * lr;
                    Box(l, key, f, 0.5f * bx, 0, 0.5f * bz, 0.04f, top, 0.04f, Timber, GV, 0, 1, 1, 0, -bx / top, -bz / top);
                }
                float py = rng.Range(0.3f, 0.4f);
                Box(l, key, f, 0, py + 0.2f, 0, 0.012f, top - py - 0.2f, 0.012f, Iron, GV, 0, 0);
                Box(l, key, f, 0, py, 0, 0.24f, 0.2f, 0.24f, Iron, GV, 0, 1, 1, 0, 0, 0, 0.12f);
                Box(l, key, f, 0, py, 0, 0.24f, 0.2f, 0.24f, Iron, GV, 0, 0, Cos16(2), Sin16(2), 0, 0, 0.12f);
            }
            Col(l, f, 0, 0, r + 0.15f, 0, 0.25f);
        }

        static void LogSeat(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "log-seat");
            float len = rng.Range(1.3f, 1.65f), d = rng.Range(0.32f, 0.4f);
            LogBar(l, key, f, 0, -0.01f, 0, len, d, 1, 0, Timber, 0, 2);
            if (rng.NextFloat() < 0.5f)
            {
                float e = rng.NextFloat() < 0.5f ? -1 : 1, bl = rng.Range(0.18f, 0.3f);
                Box(l, key, f, e * rng.Range(0.1f, 0.4f) * len, d * 0.55f, 0, 0.06f, bl, 0.06f, Timber, GV, 0, 0, 1, 0, e * 0.6f, rng.Range(-0.3f, 0.3f));
            }
            else
                Box(l, key, f, rng.Range(-0.35f, 0.35f) * len, 0.25f * d, 0.45f * d, 0.09f, 0.08f, 0.06f, Timber, GX, 0, 0);
            for (int e = -1; e <= 1; e += 2) Col(l, f, e * 0.25f * len, 0, 0.6f * d, 0, d);
        }

        static void Campsite(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "campsite");
            FirePit(l, rng.NextU64(), f.Child(0, 0, 0));
            int k0 = rng.Range(0, 16);
            float dx = Cos16(k0), dz = Sin16(k0), td = rng.Range(2.7f, 3.0f);
            var tent = f.Child(dx * td, 0, dz * td, -dz, dx);
            Tent(l, rng.NextU64(), tent);
            int seats = rng.Range(2, 4);
            for (int i = 0; i < seats; i++)
            {
                int k = seats == 2 ? k0 + 6 + 4 * i + rng.Range(-1, 2) : k0 + 5 + 3 * i;
                float sx = Cos16(k), sz = Sin16(k), sr = rng.Range(1.5f, 1.7f);
                LogSeat(l, rng.NextU64(), f.Child(sx * sr, 0, sz * sr, -sz, sx));
            }
            float side = rng.NextFloat() < 0.5f ? -1 : 1;
            if (rng.NextFloat() < 0.8f)
            {
                Yaw(ref rng, 0.4f, out float c, out float s);
                Crate(l, rng.NextU64(), tent.Child(side * rng.Range(1.7f, 1.9f), 0, rng.Range(-0.6f, 0.2f), c, s));
            }
            if (rng.NextFloat() < 0.5f)
                Barrel(l, rng.NextU64(), tent.Child(side * rng.Range(1.75f, 1.95f), 0, rng.Range(0.7f, 1.0f)));
            if (rng.NextFloat() < 0.85f)
            {
                var pile = tent.Child(-side * rng.Range(1.6f, 1.8f), 0, rng.Range(-0.4f, 0.4f), 0, 1);
                int n = rng.Range(4, 8);
                for (int i = 0; i < n; i++)
                {
                    int row = i < 4 ? 0 : 1;
                    float z = (row == 0 ? -0.18f + 0.12f * i : -0.12f + 0.12f * (i - 4)) + rng.Range(-0.01f, 0.01f);
                    LogBar(l, rng.NextU64(), pile, rng.Range(-0.05f, 0.05f), row * 0.1f, z, rng.Range(0.6f, 0.75f), rng.Range(0.1f, 0.13f), 1, 0, Timber, 0, 1);
                }
                Col(l, pile, 0, 0, 0.4f, 0, 0.25f);
            }
            if (rng.NextFloat() < 0.5f)
            {
                int k = k0 + (rng.NextFloat() < 0.5f ? 3 : -3);
                float bx = Cos16(k) * 2.2f, bz = Sin16(k) * 2.2f;
                LogBar(l, rng.NextU64(), f, bx, 0, bz, rng.Range(0.6f, 0.75f), 0.2f, -Sin16(k), Cos16(k), Cloth, 0, 1);
            }
        }
    }
}
