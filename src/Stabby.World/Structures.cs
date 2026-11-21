using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct StructurePiece
    {
        public ulong Key;
        public float X, Y, Z, Sx, Sy, Sz, C, S;
        public int Lo, Hi;
    }

    public sealed class StructureLayout
    {
        public readonly List<StructurePiece> Pieces = new List<StructurePiece>();
        public readonly List<ObjectColumn> Columns = new List<ObjectColumn>();
    }

    public static class Structures
    {
        public const int Tiers = 3;
        public const float SeatM = 0.5f;
        public const float WallHeight = 0.2f, WallBase = 0.12f, WallBatter = 0.3f, WallColumnStep = 0.05f, Squareness = 0.45f;
        static readonly int[] TierRock = { 1, 0, 0 };
        static readonly float[] Dir =
        {
            1f, 0.9238795f, 0.70710678f, 0.3826834f, 0f, -0.3826834f, -0.70710678f, -0.9238795f,
            -1f, -0.9238795f, -0.70710678f, -0.3826834f, 0f, 0.3826834f, 0.70710678f, 0.9238795f,
        };
        static readonly float[] WallSeat = { -0.45f, 0, -0.225f, 0, 0, 0, 0.225f, 0, 0.45f, 0 };
        static readonly float[] CairnSeat = { 0, 0, 0.4f, 0, -0.4f, 0, 0, 0.4f, 0, -0.4f };

        static float Cos16(int k) => Dir[k & 15];
        static float Sin16(int k) => Dir[(k + 12) & 15];

        public static float[] Seat(int shape) => shape == ObjectPart.Wall ? WallSeat : CairnSeat;

        public static StructureLayout Layout(int shape, ulong key)
        {
            var l = new StructureLayout();
            if (shape == ObjectPart.Wall) Wall(key, l);
            else Cairn(key, l);
            return l;
        }

        public static ObjectMesh Build(int shape, ulong key, int lod)
        {
            int tier = Math.Max(0, Math.Min(Tiers - 1, lod));
            var b = ObjectMeshes.Builder.New();
            foreach (var p in Layout(shape, key).Pieces)
            {
                if (tier < p.Lo || tier > p.Hi) continue;
                var m = ObjectMeshes.Rock(p.Key, TierRock[tier]);
                var u = Bounds(m.Positions);
                for (int v = 0; v < u.Length; v += 3)
                {
                    float inf = Math.Max(Math.Abs(u[v]), Math.Max(Math.Abs(u[v + 1]), Math.Abs(u[v + 2])));
                    if (inf < 1e-4f) continue;
                    float k = 1 + Squareness * ((float)Math.Sqrt(u[v] * u[v] + u[v + 1] * u[v + 1] + u[v + 2] * u[v + 2]) / inf - 1);
                    u[v] *= k; u[v + 1] *= k; u[v + 2] *= k;
                }
                u = Bounds(u);
                int first = b.P.Count / 3;
                for (int v = 0; v < u.Length; v += 3)
                {
                    float x = 0.5f * u[v] * p.Sx, y = 0.5f * (u[v + 1] + 1) * p.Sy, z = 0.5f * u[v + 2] * p.Sz;
                    b.Add(p.X + p.C * x - p.S * z, p.Y + y, p.Z + p.S * x + p.C * z);
                }
                foreach (int i in m.Indices) b.I.Add(first + i);
            }
            return b.Finish();
        }

        static float[] Bounds(float[] pos)
        {
            var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int v = 0; v < pos.Length; v++) { min[v % 3] = Math.Min(min[v % 3], pos[v]); max[v % 3] = Math.Max(max[v % 3], pos[v]); }
            var u = new float[pos.Length];
            for (int v = 0; v < pos.Length; v++) u[v] = (2 * pos[v] - min[v % 3] - max[v % 3]) / Math.Max(1e-4f, max[v % 3] - min[v % 3]);
            return u;
        }

        static void Add(StructureLayout l, ulong key, float x, float y, float z, float sx, float sy, float sz, float c, float s, int lo, int hi)
        {
            ulong k = Rng.Mix(key ^ ((ulong)(l.Pieces.Count + 1) * 0x9E3779B97F4A7C15UL));
            l.Pieces.Add(new StructurePiece { Key = k, X = x, Y = y, Z = z, Sx = sx, Sy = sy, Sz = sz, C = c, S = s, Lo = lo, Hi = hi });
        }

        static void Yaw(ref Rng rng, float range, out float c, out float s)
        {
            s = rng.Range(-range, range);
            c = (float)Math.Sqrt(1 - s * s);
        }

        static float WallTop(StructureLayout l, float x)
        {
            float top = 0;
            foreach (var p in l.Pieces)
                if (p.Lo == 0 && p.Hi >= 1 && Math.Abs(x - p.X) <= 0.5f * p.Sx) top = Math.Max(top, p.Y + p.Sy);
            return top;
        }

        static void Wall(ulong key, StructureLayout l)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x3C6EF372FE94F82BUL));
            float h = WallHeight * rng.Range(0.8f, 1.15f);
            var courses = new List<float>();
            float y = 0;
            while (h - y > 0.012f)
            {
                float u = y / h, ch = (0.04f + (0.026f - 0.04f) * u) * rng.Range(0.85f, 1.15f);
                if (h - y - ch < 0.6f * ch) ch = h - y;
                courses.Add(y);
                y += ch;
            }
            courses.Add(h);
            int count = courses.Count - 1;
            float stepL = rng.Range(0.004f, 0.035f), stepR = rng.Range(0.004f, 0.035f);
            bool breach = count > 2 && rng.NextFloat() < 0.5f;
            float bx = rng.Range(-0.25f, 0.25f), bw = rng.Range(0.03f, 0.07f);
            int bBase = rng.Range(1, Math.Max(2, count - 1));
            for (int j = 0; j < count; j++)
            {
                float yb = courses[j], ch = courses[j + 1] - yb;
                float a = -0.48f + stepL * j, e = 0.48f - stepR * j;
                if (e - a < 0.05f) continue;
                float w = WallBase * (1 - WallBatter * (yb + 0.5f * ch) / h);
                float ga = 1, gb = -1;
                if (breach && j >= bBase)
                {
                    float half = bw * (1 + 0.8f * (j - bBase));
                    ga = bx - half;
                    gb = bx + half;
                    Course(l, key, ref rng, j, a, Math.Min(e, ga), yb, ch, w);
                    Course(l, key, ref rng, j, Math.Max(a, gb), e, yb, ch, w);
                }
                else Course(l, key, ref rng, j, a, e, yb, ch, w);
                if (j == count - 1) Cope(l, key, ref rng, ga, gb, a, e, h, WallBase * (1 - WallBatter));
            }
            int tumble = rng.Range(2, 7) + (breach ? 4 : 0);
            for (int k = 0; k < tumble; k++)
            {
                float tx = breach && k < 4 ? bx + rng.Range(-0.08f, 0.08f) : rng.Range(-0.45f, 0.45f);
                float side = rng.NextFloat() < 0.5f ? -1f : 1f, s = rng.Range(0.025f, 0.045f);
                float tz = side * (0.5f * WallBase + rng.Range(0.01f, 0.06f));
                float sy = s * rng.Range(0.6f, 0.9f);
                int dir = rng.Range(0, 16);
                Add(l, key, tx, -0.2f * sy, tz, s * rng.Range(1.2f, 1.8f), sy, s, Cos16(dir), Sin16(dir), 0, 0);
            }
            for (float x = -0.48f + 0.04f; x < 0.48f; x += 0.08f)
            {
                float top = WallTop(l, x);
                if (top > 0.01f) Add(l, key, x, 0, 0, 0.1f, top, WallBase * 0.95f, 1, 0, 2, 2);
            }
            for (float x = -0.5f; x <= 0.5f + 1e-4f; x += WallColumnStep)
            {
                float top = WallTop(l, x);
                if (top > 0.01f) l.Columns.Add(new ObjectColumn { X = x, Z = 0, R = 0.5f * WallBase, Bottom = 0, Top = top });
            }
        }

        static void Course(StructureLayout l, ulong key, ref Rng rng, int j, float a, float e, float yb, float ch, float w)
        {
            float t = a;
            bool first = true;
            while (e - t > 0.004f)
            {
                float len = ch * rng.Range(1.4f, 2.6f);
                if (first && (j & 1) == 1) len *= 0.5f;
                first = false;
                len = Math.Min(len, e - t);
                if (e - t - len < 0.6f * ch) len = e - t;
                float xc = t + 0.5f * len;
                if (rng.NextFloat() < 0.12f)
                {
                    Yaw(ref rng, 0.06f, out float c, out float s);
                    Add(l, key, xc, yb - 0.08f * ch, rng.Range(-0.003f, 0.003f), len * rng.Range(1f, 1.12f), ch * rng.Range(1.05f, 1.2f), w * 1.04f, c, s, 0, 1);
                }
                else
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float d = w * rng.Range(0.5f, 0.62f);
                        Yaw(ref rng, 0.06f, out float c, out float s);
                        Add(l, key, xc, yb - 0.08f * ch, side * (0.5f * w - 0.5f * d) + rng.Range(-0.003f, 0.003f), len * rng.Range(1f, 1.12f), ch * rng.Range(1.05f, 1.2f), d, c, s, 0, 1);
                    }
                t += len;
            }
        }

        static void Cope(StructureLayout l, ulong key, ref Rng rng, float gapA, float gapB, float a, float e, float h, float w)
        {
            float t = a;
            while (e - t > 0.015f)
            {
                float th = rng.Range(0.02f, 0.032f), sy = rng.Range(0.026f, 0.038f);
                float xc = t + 0.5f * th;
                if (xc < gapA - 0.5f * th || xc > gapB + 0.5f * th)
                {
                    Yaw(ref rng, 0.1f, out float c, out float s);
                    Add(l, key, xc, h - 0.004f, rng.Range(-0.003f, 0.003f), th, sy, w * rng.Range(1f, 1.15f), c, s, 0, 1);
                }
                t += th * rng.Range(0.95f, 1.05f);
            }
        }

        static void Cairn(ulong key, StructureLayout l)
        {
            var rng = new Rng(Rng.Mix(key ^ 0x7F4A7C159E3779B9UL));
            int layers = rng.Range(4, 7);
            float r = rng.Range(0.3f, 0.34f), s = rng.Range(0.18f, 0.22f), y = 0;
            for (int j = 0; j < layers; j++)
            {
                float sy = s * rng.Range(0.55f, 0.75f);
                int count = Math.Max(3, Math.Min(16, (int)(6.2831853f * r / (s * 0.95f) + 0.5f)));
                int off = rng.Range(0, 16);
                for (int i = 0; i < count; i++)
                {
                    int k = off + i * 16 / count;
                    float dx = Cos16(k), dz = Sin16(k), rr = r * rng.Range(0.92f, 1.06f);
                    Add(l, key, dx * rr, y, dz * rr, s * rng.Range(1f, 1.35f), sy * rng.Range(0.9f, 1.1f), s * rng.Range(0.75f, 0.95f), -dz, dx, 0, 1);
                }
                Add(l, key, 0, y, 0, 1.8f * r, 0.9f * sy, 1.8f * r, 1, 0, 0, 1);
                int pd = rng.Range(0, 16);
                Add(l, key, 0, y, 0, 2 * r + 0.9f * s, sy, 2 * r + 0.9f * s, Cos16(pd), Sin16(pd), 2, 2);
                l.Columns.Add(new ObjectColumn { X = 0, Z = 0, R = r + 0.4f * s, Bottom = 0, Top = y + sy });
                y += 0.82f * sy;
                r *= 0.72f;
                s *= 0.86f;
            }
            bool standing = rng.NextFloat() < 0.5f;
            float tx = s * (standing ? 0.7f : 1.5f), tz = s * (standing ? 0.5f : 1.3f), ty = s * (standing ? 1.8f : 0.5f);
            int td = rng.Range(0, 16);
            Add(l, key, 0, y, 0, tx, ty, tz, Cos16(td), Sin16(td), 0, 2);
            l.Columns.Add(new ObjectColumn { X = 0, Z = 0, R = 0.5f * Math.Min(tx, tz), Bottom = 0, Top = y + ty });
            int loose = rng.Range(3, 7);
            for (int k = 0; k < loose; k++)
            {
                int dir = rng.Range(0, 16), yd = rng.Range(0, 16);
                float rr = rng.Range(0.38f, 0.43f), ss = rng.Range(0.05f, 0.09f), sy = 0.7f * ss;
                Add(l, key, Cos16(dir) * rr, -0.2f * sy, Sin16(dir) * rr, 1.4f * ss, sy, ss, Cos16(yd), Sin16(yd), 0, 0);
            }
        }
    }
}
