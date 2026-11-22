using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float RuinU = 1 / 12f;
        const float RuinBase = -0.45f, RuinT = 0.55f;

        static float RuinHeight(float[] knots, float len, float x)
        {
            float t = Math.Max(0, Math.Min(0.9999f, (x + 0.5f * len) / len)) * (knots.Length - 1);
            int i = (int)t;
            return knots[i] + (knots[i + 1] - knots[i]) * (t - i);
        }

        static int RuinCourses(float[] knots, float len, float ch, float x, float gap, float gw) =>
            Math.Max((int)((RuinHeight(knots, len, x) - RuinBase) / ch), Math.Abs(x - gap) < 0.5f * gw + 0.9f ? 4 : 1);

        static void RuinWall(StructureLayout l, ref Rng rng, in Frame w, float len, float ch, float[] knots, float gap, float gw)
        {
            for (int j = 0; ; j++)
            {
                float y = RuinBase + j * ch, x = -0.5f * len - (j & 1) * rng.Range(0.2f, 0.35f);
                bool any = false;
                while (x < 0.5f * len - 0.05f)
                {
                    float bl = rng.Range(0.42f, 0.78f), a = Math.Max(x, -0.5f * len), b = Math.Min(x + bl, 0.5f * len), xc = 0.5f * (a + b);
                    x += bl;
                    if (b - a < 0.18f || Math.Abs(xc - gap) < 0.5f * gw + 0.5f * (b - a)) continue;
                    int n = RuinCourses(knots, len, ch, xc, gap, gw);
                    if (j >= n) continue;
                    any = true;
                    if (j == n - 1 && j > 0 && rng.NextFloat() < 0.22f) continue;
                    Yaw(ref rng, 0.04f, out float c, out float s);
                    Hewn(l, rng.NextU64(), w, xc + rng.Range(-0.015f, 0.015f), y, rng.Range(-0.03f, 0.03f), b - a - 0.03f, ch - 0.015f, RuinT * rng.Range(0.86f, 1f), c, s, 0, 0, rng.Range(0.9f, 0.95f), rng.Range(-0.02f, 0.02f), 0, 0.04f);
                }
                if (!any) break;
            }
            for (float x = -0.5f * len; x < 0.5f * len - 0.1f; x += 1f)
            {
                float a = x, b = Math.Min(x + 1f, 0.5f * len);
                if (b > gap - 0.5f * gw && a < gap + 0.5f * gw)
                {
                    if (a < gap - 0.5f * gw) b = gap - 0.5f * gw;
                    else if (b > gap + 0.5f * gw) a = gap + 0.5f * gw;
                    else continue;
                }
                float xc = 0.5f * (a + b);
                int n = RuinCourses(knots, len, ch, xc, gap, gw);
                Hewn(l, rng.NextU64(), w, xc, RuinBase, 0, b - a, n * ch, RuinT, 1, 0, 1, 2, 0.76f, 0, 0, 0.03f);
                for (float cx = a + 0.25f; cx < b; cx += 0.5f)
                    Col(l, w, cx, 0, 0.3f, RuinBase, RuinBase + RuinCourses(knots, len, ch, cx, gap, gw) * ch);
            }
        }

        static void Fallen(StructureLayout l, ref Rng rng, in Frame w, float x, float z, float ch, int lo)
        {
            Yaw(ref rng, 0.7f, out float c, out float s);
            float bl = rng.Range(0.45f, 0.75f);
            Hewn(l, rng.NextU64(), w, x, rng.Range(-0.14f, -0.06f), z, bl, ch * rng.Range(0.9f, 1f), RuinT * rng.Range(0.8f, 0.95f), c, s, lo, 1, rng.Range(0.84f, 0.94f), rng.Range(-0.12f, 0.12f), 0, 0.06f);
        }

        static void Rubble(StructureLayout l, ref Rng rng, in Frame f, float px, float pz, float ch)
        {
            float r = rng.Range(0.55f, 1.0f);
            int n = rng.Range(5, 10);
            for (int i = 0; i < n; i++)
            {
                int k = rng.Range(0, 16), d = rng.Range(0, 16);
                float rd = r * rng.NextFloat(), fall = 1 - rd / r, sz = rng.Range(0.18f, 0.4f) * (0.6f + 0.4f * fall);
                float x = px + Cos16(k) * rd, z = pz + Sin16(k) * rd, y = -0.1f + 0.35f * r * fall * rng.Range(0.6f, 1f);
                if (rng.NextFloat() < 0.3f) Hewn(l, rng.NextU64(), f, x, y - 0.04f, z, sz * 1.5f, ch * 0.9f, sz * 1.1f, Cos16(d), Sin16(d), 0, 1, rng.Range(0.88f, 0.94f), rng.Range(-0.15f, 0.15f));
                else Rock(l, rng.NextU64(), f, x, y, z, sz * 1.2f, sz * 0.7f, sz, Cos16(d), Sin16(d), 0, 1, rng.Range(-0.1f, 0.1f));
            }
            Rock(l, rng.NextU64(), f, px, -0.12f, pz, 1.7f * r, 0.45f * r, 1.5f * r, 1, 0, 2, 2);
            Col(l, f, px, pz, 0.7f * r, -0.1f, 0.3f * r);
        }

        static void Tuft(StructureLayout l, ulong key, ref Rng rng, in Frame f, float x, float y, float z)
        {
            float sz = rng.Range(0.12f, 0.26f);
            int d = rng.Range(0, 16);
            Box(l, key, f, x, y - 0.02f, z, sz, sz * rng.Range(0.5f, 0.85f), sz, Thatch, GV, 0, 0, Cos16(d), Sin16(d), 0, 0, 0.3f);
        }

        static void Ruin(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "ruin");
            int start = l.Pieces.Count;
            Yaw(ref rng, 0.1f, out float c0, out float s0);
            var f = root.Child(rng.Range(-0.3f, 0.3f), 0, rng.Range(-0.3f, 0.3f), c0, s0);
            float hx = rng.Range(3.0f, 3.8f), hz = rng.Range(2.2f, 2.9f), ch = rng.Range(0.27f, 0.33f), gw = rng.Range(1.0f, 1.2f);
            var walls = new[] { f.Child(0, 0, hz, 1, 0), f.Child(0, 0, -hz, -1, 0), f.Child(hx, 0, 0, 0, -1), f.Child(-hx, 0, 0, 0, 1) };
            var lens = new[] { 2 * hx + RuinT, 2 * hx + RuinT, 2 * hz - RuinT, 2 * hz - RuinT };
            var knots = new float[4][];
            int low = rng.Range(0, 4);
            for (int wl = 0; wl < 4; wl++)
            {
                float peak = wl == low ? rng.Range(0.1f, 0.5f) : rng.Range(0.9f, 2.4f);
                knots[wl] = new float[5];
                for (int k = 0; k < 5; k++) knots[wl][k] = peak * rng.Range(0.15f, 1f);
                knots[wl][rng.Range(0, 5)] = rng.Range(-0.2f, 0.15f);
            }
            float gap = rng.Range(-0.35f, 0.35f) * hx;
            const float none = 1e4f;

            for (int wl = 0; wl < 4; wl++) RuinWall(l, ref rng, walls[wl], lens[wl], ch, knots[wl], wl == 0 ? gap : none, wl == 0 ? gw : 0);

            var front = walls[0];
            float jh = rng.Range(1.9f, 2.2f);
            bool fallen = rng.NextFloat() < 0.35f;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float h = fallen && sd > 0 ? jh * rng.Range(0.55f, 0.8f) : jh;
                Yaw(ref rng, 0.04f, out float c, out float s);
                Hewn(l, rng.NextU64(), front, gap + sd * (0.5f * gw + 0.17f), RuinBase, 0, 0.36f, h - RuinBase, RuinT + 0.08f, c, s, 0, 2, rng.Range(0.91f, 0.95f), rng.Range(-0.015f, 0.015f), 0, 0.05f);
                Col(l, front, gap + sd * (0.5f * gw + 0.17f), 0, 0.3f, RuinBase, h);
            }
            if (fallen)
            {
                Yaw(ref rng, 0.45f, out float c, out float s);
                Hewn(l, rng.NextU64(), front, gap + rng.Range(-0.4f, 0.4f), -0.12f, rng.Range(0.9f, 1.3f), gw + 0.7f, 0.32f, 0.4f, c, s, 0, 2, 0.92f, rng.Range(-0.08f, 0.08f), 0, 0.06f);
            }
            else
            {
                Yaw(ref rng, 0.03f, out float c, out float s);
                Hewn(l, rng.NextU64(), front, gap + rng.Range(-0.03f, 0.03f), jh, 0, gw + 0.8f, rng.Range(0.3f, 0.36f), RuinT + 0.06f, c, s, 0, 2, 0.93f, rng.Range(-0.02f, 0.02f), 0, 0.04f);
            }

            for (int wl = 0; wl < 4; wl++)
            {
                float len = lens[wl], g = wl == 0 ? gap : none, gg = wl == 0 ? gw : 0;
                int n = rng.Range(2, 6);
                for (int i = 0; i < n; i++)
                {
                    float x = rng.Range(-0.45f, 0.45f) * len;
                    if (Math.Abs(x - g) < 0.5f * gg + 0.5f) continue;
                    Fallen(l, ref rng, walls[wl], x, (rng.NextFloat() < 0.75f ? 1 : -1) * rng.Range(0.6f, 1.7f), ch, 0);
                }
                if (rng.NextFloat() < 0.45f)
                {
                    float x = rng.Range(-0.3f, 0.1f) * len, z = 0.5f * RuinT + rng.Range(0.5f, 0.9f), lean = rng.Range(0.1f, 0.2f);
                    int row = rng.Range(3, 6);
                    for (int i = 0; i < row && x < 0.45f * len; i++)
                    {
                        float bl = rng.Range(0.48f, 0.72f);
                        if (Math.Abs(x - g) >= 0.5f * gg + 0.4f)
                        {
                            Yaw(ref rng, 0.12f, out float c, out float s);
                            Hewn(l, rng.NextU64(), walls[wl], x + 0.5f * bl, rng.Range(-0.12f, -0.07f), z + rng.Range(-0.08f, 0.08f), bl - 0.04f, ch * 0.95f, RuinT * 0.9f, c, s, 0, 1, rng.Range(0.86f, 0.94f), lean + rng.Range(-0.05f, 0.05f), 0, 0.05f);
                        }
                        x += bl + rng.Range(0.02f, 0.12f);
                    }
                }
            }

            int piles = rng.Range(2, 5);
            for (int i = 0; i < piles; i++)
            {
                int wl = rng.Range(0, 4);
                float len = lens[wl], x = rng.Range(-0.45f, 0.45f) * len;
                if (wl == 0 && Math.Abs(x - gap) < 0.5f * gw + 0.9f) x = gap + (x < gap ? -1 : 1) * (0.5f * gw + 0.9f);
                float z = (rng.NextFloat() < 0.6f ? 1 : -1) * rng.Range(0.7f, 1.3f);
                Rubble(l, ref rng, walls[wl], x, z, ch);
            }

            int flags = rng.Range(5, 10), before = l.Pieces.Count;
            for (int i = 0; i < flags; i++)
            {
                float sx = rng.Range(0.45f, 0.8f), sz = rng.Range(0.4f, 0.7f);
                Yaw(ref rng, 0.15f, out float c, out float s);
                Hewn(l, rng.NextU64(), f, rng.Range(-hx + 0.8f, hx - 0.8f), -0.06f, rng.Range(-hz + 0.8f, hz - 0.8f), sx, rng.Range(0.07f, 0.1f), sz, c, s, 0, 0, rng.Range(0.7f, 0.85f), rng.Range(-0.04f, 0.04f));
            }
            Flat(l, before);
            Mossy(l, start);

            int tufts = rng.Range(14, 24);
            for (int i = 0; i < tufts; i++)
            {
                int wl = rng.Range(0, 4);
                float len = lens[wl], x = rng.Range(-0.48f, 0.48f) * len, z = (rng.NextFloat() < 0.5f ? 1 : -1) * (0.5f * RuinT + rng.Range(0.05f, 0.3f));
                Tuft(l, key, ref rng, walls[wl], x, 0, z);
            }
            int tops = rng.Range(3, 7);
            for (int i = 0; i < tops; i++)
            {
                int wl = rng.Range(0, 4);
                float len = lens[wl], x = rng.Range(-0.45f, 0.45f) * len, g = wl == 0 ? gap : none, gg = wl == 0 ? gw : 0;
                if (Math.Abs(x - g) < 0.5f * gg + 0.3f) continue;
                Tuft(l, key, ref rng, walls[wl], x, RuinBase + RuinCourses(knots[wl], len, ch, x, g, gg) * ch, rng.Range(-0.12f, 0.12f));
            }
        }
    }
}
