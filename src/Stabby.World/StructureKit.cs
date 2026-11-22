using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const int Stone = 0, Timber = 1, Plank = 2, Plaster = 3, Thatch = 4, Iron = 5, Cloth = 6, Fungus = 8, Turf = 9;
        const int GX = 0, GV = 1, GZ = 2;
        const int RoundLog = 1, RoundDisc = 2, RoundStem = 3, RoundDome = 4;
        static readonly float[] DomeR = { 1f, 0.93f, 0.74f, 0.43f }, DomeY = { -0.8f, -0.15f, 0.42f, 0.84f };
        static readonly float[] DomeLowR = { 1f, 0.8f }, DomeLowY = { -0.8f, 0.3f };

        static readonly float[] UnitBox = MakeUnitBox();
        static readonly int[] UnitBoxIdx = MakeUnitBoxIdx();

        static float[] MakeUnitBox()
        {
            var u = new float[72];
            float[] cu = { -1, 1, 1, -1 }, cv = { -1, -1, 1, 1 };
            for (int f = 0; f < 6; f++)
            {
                int ax = f >> 1, ua = (ax + 1) % 3, va = (ax + 2) % 3;
                float sg = (f & 1) == 0 ? 1 : -1;
                for (int k = 0; k < 4; k++)
                {
                    int c = sg > 0 ? k : 3 - k, o = (f * 4 + k) * 3;
                    u[o + ax] = sg; u[o + ua] = cu[c]; u[o + va] = cv[c];
                }
            }
            return u;
        }

        static void RoundDir(int i, int n, out float c, out float s)
        {
            int k = i * 32 / n, h = k >> 1;
            c = Cos16(h); s = Sin16(h);
            if ((k & 1) == 0) return;
            c += Cos16(h + 1); s += Sin16(h + 1);
            float inv = 1 / (float)Math.Sqrt(c * c + s * s);
            c *= inv; s *= inv;
        }

        static float[] RoundNoise(ref Rng rng, int n, float lo, float hi)
        {
            var r = new float[n];
            var t = new float[n];
            for (int i = 0; i < n; i++) r[i] = rng.NextFloat();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++) t[i] = 0.25f * r[(i + n - 1) % n] + 0.5f * r[i] + 0.25f * r[(i + 1) % n];
                Array.Copy(t, r, n);
            }
            float mn = 1, mx = 0;
            for (int i = 0; i < n; i++) { mn = Math.Min(mn, r[i]); mx = Math.Max(mx, r[i]); }
            float k = mx > mn ? 1 / (mx - mn) : 0;
            for (int i = 0; i < n; i++) r[i] = lo + (hi - lo) * (r[i] - mn) * k;
            return r;
        }

        static float[] RoundMesh(ulong key, int shape, int tier, out int[] idx, out float[] lift)
        {
            var rng = Rng.Stream(key, "round");
            int sides = shape == RoundDisc ? 32 : tier < (shape >= RoundStem ? 1 : 2) ? 16 : 8;
            var dc = new float[sides];
            var ds = new float[sides];
            for (int i = 0; i < sides; i++) RoundDir(i, sides, out dc[i], out ds[i]);
            if (shape == RoundDome) return DomeMesh(ref rng, sides, tier, dc, ds, out idx, out lift);
            if (shape == RoundDisc)
            {
                var r = RoundNoise(ref rng, sides, 0.66f, 1f);
                var u = new float[3 * (sides + 1)];
                lift = new float[sides + 1];
                idx = new int[3 * sides];
                u[1] = 1; lift[0] = 4;
                for (int i = 0; i < sides; i++)
                {
                    u[3 + 3 * i] = r[i] * dc[i]; u[4 + 3 * i] = 1; u[5 + 3 * i] = r[i] * ds[i];
                    lift[1 + i] = 7;
                    idx[3 * i] = 0; idx[3 * i + 1] = 1 + (i + 1) % sides; idx[3 * i + 2] = 1 + i;
                }
                return u;
            }
            int rings = tier < 2 ? 5 : 2, side = rings * sides, cap = sides + 1;
            var rad = new float[side];
            var cy = new float[rings];
            var cz = new float[rings];
            float bend = rng.Range(-0.03f, 0.03f), sway = rng.Range(-0.03f, 0.03f);
            for (int j = 0; j < rings; j++)
            {
                float t = rings > 1 ? 2f * j / (rings - 1) - 1 : 0, taper = 1 - 0.03f * t * t;
                var ring = RoundNoise(ref rng, sides, 0.86f, 0.97f);
                for (int i = 0; i < sides; i++) rad[j * sides + i] = ring[i] * taper;
                cy[j] = bend * (1 - t * t); cz[j] = sway * (1 - t * t);
            }
            var w = new float[3 * (side + 2 * cap)];
            lift = new float[side + 2 * cap];
            var id = new System.Collections.Generic.List<int>();
            for (int j = 0; j < rings; j++)
            {
                float x = rings > 1 ? 2f * j / (rings - 1) - 1 : 0;
                for (int i = 0; i < sides; i++)
                {
                    int v = j * sides + i;
                    float r = rad[v];
                    w[3 * v] = x; w[3 * v + 1] = cy[j] + r * dc[i]; w[3 * v + 2] = cz[j] + r * ds[i];
                    lift[v] = 8;
                }
            }
            for (int j = 0; j + 1 < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = j * sides + i, b = j * sides + (i + 1) % sides, c = b + sides, d = a + sides;
                    id.Add(a); id.Add(b); id.Add(c);
                    id.Add(a); id.Add(c); id.Add(d);
                }
            for (int e = 0; e < 2; e++)
            {
                int j = e == 0 ? 0 : rings - 1, o = side + e * cap;
                float x = e == 0 ? -1 : 1;
                w[3 * o] = x; w[3 * o + 1] = cy[j]; w[3 * o + 2] = cz[j];
                lift[o] = 4;
                for (int i = 0; i < sides; i++)
                {
                    int v = o + 1 + i, src = j * sides + i;
                    w[3 * v] = x; w[3 * v + 1] = w[3 * src + 1]; w[3 * v + 2] = w[3 * src + 2];
                    lift[v] = 7;
                    int n1 = o + 1 + (i + 1) % sides;
                    id.Add(o);
                    if (e == 0) { id.Add(n1); id.Add(v); }
                    else { id.Add(v); id.Add(n1); }
                }
            }
            idx = id.ToArray();
            if (shape == RoundStem)
                for (int v = 0; v < w.Length; v += 3)
                {
                    float y = w[v], k = 1.12f - 0.08f * (y + 1);
                    w[v] = k * w[v + 1]; w[v + 1] = y; w[v + 2] *= k;
                }
            if (shape == RoundStem)
                for (int t = 0; t < idx.Length; t += 3) { int a = idx[t + 1]; idx[t + 1] = idx[t + 2]; idx[t + 2] = a; }
            return w;
        }

        static float[] DomeMesh(ref Rng rng, int sides, int tier, float[] dc, float[] ds, out int[] idx, out float[] lift)
        {
            var pr = tier < 2 ? DomeR : DomeLowR;
            var py = tier < 2 ? DomeY : DomeLowY;
            int rings = pr.Length, top = rings * sides, apex = top, under = top + 1, centre = under + sides;
            var r = RoundNoise(ref rng, sides, 0.88f, 1f);
            float tx = rng.Range(-0.06f, 0.06f), tz = rng.Range(-0.06f, 0.06f), ay = rng.Range(0.94f, 1.04f);
            var w = new float[3 * (centre + 1)];
            lift = new float[centre + 1];
            var id = new System.Collections.Generic.List<int>();
            for (int j = 0; j < rings; j++)
            {
                float t = (py[j] + 0.8f) / 1.8f;
                for (int i = 0; i < sides; i++)
                {
                    int v = j * sides + i;
                    float rr = pr[j] * (r[i] + (1 - r[i]) * 0.5f * t);
                    w[3 * v] = rr * dc[i] + tx * t; w[3 * v + 1] = py[j]; w[3 * v + 2] = rr * ds[i] + tz * t;
                    lift[v] = 9 + (int)Math.Round(4f * j / rings);
                }
            }
            w[3 * apex] = tx; w[3 * apex + 1] = ay; w[3 * apex + 2] = tz;
            lift[apex] = 13;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = j * sides + i, b = j * sides + (i + 1) % sides;
                    if (j + 1 == rings) { id.Add(a); id.Add(apex); id.Add(b); continue; }
                    int c = b + sides, d = a + sides;
                    id.Add(a); id.Add(c); id.Add(b);
                    id.Add(a); id.Add(d); id.Add(c);
                }
            for (int i = 0; i < sides; i++)
            {
                int v = under + i;
                w[3 * v] = w[3 * i]; w[3 * v + 1] = w[3 * i + 1]; w[3 * v + 2] = w[3 * i + 2];
                lift[v] = 7;
                id.Add(centre); id.Add(v); id.Add(under + (i + 1) % sides);
            }
            w[3 * centre + 1] = -0.55f;
            lift[centre] = 4;
            idx = id.ToArray();
            return w;
        }

        static int[] MakeUnitBoxIdx()
        {
            var idx = new int[36];
            for (int f = 0; f < 6; f++)
            {
                int b = f * 4, o = f * 6;
                idx[o] = b; idx[o + 1] = b + 1; idx[o + 2] = b + 2;
                idx[o + 3] = b; idx[o + 4] = b + 2; idx[o + 5] = b + 3;
            }
            return idx;
        }

        struct Frame
        {
            public float X, Y, Z, C, S, U;
            public int Lo, Hi;

            public Frame(float x, float y, float z, float c, float s, float u, int lo, int hi)
            {
                X = x; Y = y; Z = z; C = c; S = s; U = u; Lo = lo; Hi = hi;
            }

            public static Frame Root(float u) => new Frame(0, 0, 0, 1, 0, u, 0, Tiers - 1);

            public Frame Child(float x, float y, float z, float c = 1, float s = 0, int lo = 0, int hi = Tiers - 1, float scale = 1) =>
                new Frame(X + U * (C * x - S * z), Y + U * y, Z + U * (S * x + C * z), C * c - S * s, S * c + C * s, U * scale, Math.Max(Lo, lo), Math.Min(Hi, hi));
        }

        static void Box(StructureLayout l, ulong key, in Frame f, float x, float y, float z, float sx, float sy, float sz, int m, int g, int lo = 0, int hi = Tiers - 1, float c = 1, float s = 0, float lean = 0, float batter = 0, float taper = 0)
        {
            int a = Math.Max(f.Lo, lo), b = Math.Min(f.Hi, hi);
            if (a > b || sx <= 0 || sy <= 0 || sz <= 0) return;
            float mid = Math.Max(Math.Min(sx, sy), Math.Min(Math.Max(sx, sy), sz));
            ulong k = Rng.Mix(key ^ ((ulong)(l.Pieces.Count + 1) * 0x9E3779B97F4A7C15UL));
            l.Pieces.Add(new StructurePiece
            {
                Key = k,
                X = f.X + f.U * (f.C * x - f.S * z), Y = f.Y + f.U * y, Z = f.Z + f.U * (f.S * x + f.C * z),
                Sx = f.U * sx, Sy = f.U * sy, Sz = f.U * sz,
                C = f.C * c - f.S * s, S = f.S * c + f.C * s,
                L = lean, B = batter, T = taper,
                Q = m == Stone ? StoneSquare : 1,
                A = f.U * Math.Min(0.5f * mid, 0.5f),
                Lo = a, Hi = b, M = m, G = g,
            });
        }

        static void Col(StructureLayout l, in Frame f, float x, float z, float r, float bottom, float top)
        {
            if (f.Lo > 0) return;
            l.Columns.Add(new ObjectColumn { X = f.X + f.U * (f.C * x - f.S * z), Z = f.Z + f.U * (f.S * x + f.C * z), R = f.U * r, Bottom = f.Y + f.U * bottom, Top = f.Y + f.U * top });
        }

        static void Grain(in StructurePiece p, out float gx, out float gz)
        {
            if (p.G == GX) { gx = p.C; gz = p.S; return; }
            if (p.G == GZ) { gx = -p.S; gz = p.C; return; }
            float x = p.C * p.L - p.S * p.B, z = p.S * p.L + p.C * p.B, len = (float)Math.Sqrt(x * x + 1 + z * z);
            gx = x / len; gz = z / len;
        }

        static void Table(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "table");
            for (int i = 0; i < 4; i++)
                Box(l, key, f, rng.Range(-0.01f, 0.01f), 0.7f + rng.Range(-0.004f, 0.004f), -0.3f + 0.2f * i, 1.6f + rng.Range(-0.02f, 0.02f), 0.05f, 0.19f, Plank, GX, 0, 0);
            Box(l, key, f, 0, 0.69f, 0, 1.6f, 0.06f, 0.8f, Plank, GX, 1, 2);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1;
                Box(l, key, f, sx * 0.68f, 0, sz * 0.3f, 0.08f, 0.7f, 0.08f, Timber, GV, 0, 1);
            }
            for (int i = -1; i <= 1; i += 2)
            {
                Box(l, key, f, 0, 0.58f, i * 0.3f, 1.28f, 0.11f, 0.04f, Timber, GX, 0, 0);
                Box(l, key, f, i * 0.68f, 0.58f, 0, 0.04f, 0.11f, 0.52f, Timber, GZ, 0, 0);
                Box(l, key, f, i * 0.62f, 0, 0, 0.1f, 0.69f, 0.6f, Timber, GV, 2, 2);
            }
            for (int i = -1; i <= 1; i += 2) Col(l, f, i * 0.4f, 0, 0.4f, 0, 0.75f);
        }

        static void Bench(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "bench");
            for (int i = -1; i <= 1; i += 2)
                Box(l, key, f, rng.Range(-0.01f, 0.01f), 0.4f, i * 0.076f, 1.4f, 0.05f, 0.148f, Plank, GX, 0, 0);
            Box(l, key, f, 0, 0.4f, 0, 1.4f, 0.05f, 0.3f, Plank, GX, 1, 1);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1;
                Box(l, key, f, sx * 0.55f, 0, sz * 0.08f, 0.06f, 0.41f, 0.06f, Timber, GV, 0, 0, 1, 0, -0.12f * sx);
            }
            Box(l, key, f, 0, 0.15f, 0, 1.0f, 0.05f, 0.04f, Timber, GX, 0, 0);
            for (int i = -1; i <= 1; i += 2) Box(l, key, f, i * 0.55f, 0, 0, 0.06f, 0.4f, 0.26f, Timber, GV, 1, 1);
            Box(l, key, f, 0, 0, 0, 1.4f, 0.45f, 0.3f, Plank, GX, 2, 2);
            for (int i = -1; i <= 1; i += 2) Col(l, f, i * 0.4f, 0, 0.3f, 0, 0.45f);
        }

        static void Barrel(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "barrel");
            for (int k = 0; k < 16; k += 2)
            {
                float dx = Cos16(k), dz = Sin16(k), c = dz, s = -dx, w = 0.22f + rng.Range(-0.01f, 0.01f);
                Box(l, key, f, dx * 0.275f, 0, dz * 0.275f, w, 0.425f, 0.026f, Plank, GV, 0, 0, c, s, 0, 0.12f);
                Box(l, key, f, dx * 0.275f, 0.425f, dz * 0.275f, w, 0.425f, 0.026f, Plank, GV, 0, 0, c, s, 0, -0.12f);
                Box(l, key, f, dx * 0.27f, 0, dz * 0.27f, 0.23f, 0.85f, 0.03f, Plank, GV, 1, 1, c, s);
                Box(l, key, f, dx * 0.285f, 0.13f, dz * 0.285f, 0.24f, 0.045f, 0.014f, Iron, GX, 0, 0, c, s);
                Box(l, key, f, dx * 0.292f, 0.6f, dz * 0.292f, 0.245f, 0.045f, 0.014f, Iron, GX, 0, 0, c, s);
            }
            Box(l, key, f, 0, 0.77f, 0, 0.35f, 0.04f, 0.35f, Plank, GX, 0, 1);
            Box(l, key, f, 0, 0, 0, 0.5f, 0.85f, 0.5f, Plank, GV, 2, 2);
            Box(l, key, f, 0, 0, 0, 0.5f, 0.85f, 0.5f, Plank, GV, 2, 2, Cos16(2), Sin16(2));
            Col(l, f, 0, 0, 0.3f, 0, 0.85f);
        }

        static void Crate(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "crate");
            float h = 0.7f * rng.Range(0.9f, 1.05f);
            Box(l, key, f, 0, 0.03f, 0, 0.64f, h - 0.06f, 0.64f, Plank, GX);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1;
                Box(l, key, f, sx * 0.315f, 0, sz * 0.315f, 0.07f, h, 0.07f, Timber, GV, 0, 1);
            }
            for (int j = 0; j < 2; j++)
                for (int i = -1; i <= 1; i += 2)
                {
                    float y = j * (h - 0.07f);
                    Box(l, key, f, 0, y, i * 0.315f, 0.56f, 0.07f, 0.07f, Timber, GX, 0, 0);
                    Box(l, key, f, i * 0.315f, y, 0, 0.07f, 0.07f, 0.56f, Timber, GZ, 0, 0);
                }
            Col(l, f, 0, 0, 0.35f, 0, h);
        }

        static void Stall(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "market-stall");
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1, h = sz > 0 ? 2.4f : 2.62f;
                Box(l, key, f, sx * 1.4f, 0, sz * 0.9f, 0.12f, h, 0.12f, Timber, GV, 0, sz > 0 ? 2 : 1);
                Col(l, f, sx * 1.4f, sz * 0.9f, 0.1f, 0, h);
            }
            for (int i = 0; i < 3; i++)
                Box(l, key, f, rng.Range(-0.01f, 0.01f), 0.9f, 0.4f + 0.2f * i, 2.84f, 0.05f, 0.19f, Plank, GX, 0, 0);
            for (int i = 0; i < 4; i++)
                Box(l, key, f, 0, 0.04f + 0.21f * i, 0.88f, 2.8f, 0.2f, 0.03f, Plank, GX, 0, 0);
            Box(l, key, f, 0, 0.85f, 0.6f, 2.8f, 0.06f, 0.6f, Timber, GX, 0, 0);
            Box(l, key, f, 0, 0, 0.6f, 2.84f, 0.95f, 0.6f, Plank, GX, 1, 2);
            float bb = -2.15f / 0.3f, zc = 1.15f - 0.1075f - 1.075f;
            for (int i = 0; i < 5; i++)
            {
                float x = -1.16f + 0.58f * i;
                Box(l, key, f, x, 2.3f + rng.Range(-0.01f, 0.01f), zc, 0.58f, 0.3f, 0.215f, Cloth, GZ, 0, 0, 1, 0, 0, bb);
                Box(l, key, f, x, 2.1f, 1.16f, 0.58f, 0.2f, 0.02f, Cloth, GX, 0, 0);
            }
            Box(l, key, f, 0, 2.3f, zc, 2.95f, 0.3f, 0.215f, Cloth, GZ, 1, 2, 1, 0, 0, bb);
            for (int i = -1; i <= 1; i += 2) Box(l, key, f, i * 1.4f, 2.22f, 0, 0.06f, 0.08f, 1.9f, Timber, GZ, 0, 0);
            int goods = rng.Range(4, 7);
            for (int i = 0; i < goods; i++)
            {
                float w = rng.Range(0.15f, 0.32f), x = -1.2f + 2.4f * (i + rng.Range(0.2f, 0.8f)) / goods;
                Box(l, key, f, x, 0.95f, rng.Range(0.45f, 0.75f), w, w * rng.Range(0.6f, 1.2f), w * rng.Range(0.8f, 1.1f), rng.NextFloat() < 0.5f ? Cloth : Plank, GX, 0, 0);
            }
            for (float x = -1.3f; x <= 1.31f; x += 0.52f) Col(l, f, x, 0.6f, 0.32f, 0, 0.95f);
        }

        static void Well(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "well");
            for (int j = 0; j < 3; j++)
                for (int k = j & 1; k < 16; k += 2)
                {
                    float dx = Cos16(k), dz = Sin16(k), r = 0.62f * rng.Range(0.98f, 1.03f);
                    Box(l, key, f, dx * r, 0.22f * j - (j == 0 ? 0.08f : 0), dz * r, 0.5f * rng.Range(0.95f, 1.05f), 0.22f * rng.Range(0.92f, 1.04f) + (j == 0 ? 0.08f : 0), 0.28f * rng.Range(0.9f, 1.05f), Stone, GX, 0, 0, dz, -dx);
                }
            for (int k = 0; k < 16; k += 2)
            {
                float dx = Cos16(k), dz = Sin16(k);
                Box(l, key, f, dx * 0.64f, 0.65f, dz * 0.64f, 0.52f, 0.11f, 0.36f, Stone, GX, 0, 0, dz, -dx);
                Box(l, key, f, dx * 0.62f, -0.05f, dz * 0.62f, 0.52f, 0.76f, 0.3f, Stone, GX, 1, 1, dz, -dx);
            }
            Box(l, key, f, 0, 0.3f, 0, 0.92f, 0.02f, 0.92f, Iron, GX, 0, 1);
            Box(l, key, f, 0, 0.3f, 0, 0.92f, 0.02f, 0.92f, Iron, GX, 0, 1, Cos16(2), Sin16(2));
            for (int k = 0; k < 2; k++)
                Box(l, key, f, 0, -0.05f, 0, 1.3f, 0.8f, 1.3f, Stone, GX, 2, 2, Cos16(2 * k), Sin16(2 * k));
            for (int i = -1; i <= 1; i += 2)
            {
                Box(l, key, f, i * 0.85f, 0, 0, 0.12f, 2.0f, 0.12f, Timber, GV, 0, 1);
                Col(l, f, i * 0.85f, 0, 0.1f, 0, 2.0f);
                float sz = 0.04f * 0.55f / 0.4f;
                Box(l, key, f, 0, 1.92f, i * 0.325f, 2.1f, 0.4f, sz + 0.02f, Plank, GV, 0, 2, 1, 0, 0, -i * 0.55f / 0.4f);
                Box(l, key, f, i * 0.9f, 1.25f, 0.12f, 0.04f, 0.04f, 0.24f, Iron, GZ, 0, 0);
            }
            Box(l, key, f, 0, 1.88f, 0, 1.9f, 0.1f, 0.1f, Timber, GX, 0, 1);
            Box(l, key, f, 0, 1.3f, 0, 1.7f, 0.12f, 0.12f, Timber, GX, 0, 0);
            Box(l, key, f, 0.95f, 1.22f, 0.25f, 0.04f, 0.2f, 0.04f, Iron, GV, 0, 0);
            Box(l, key, f, 0, 0.78f, 0, 0.015f, 0.52f, 0.015f, Cloth, GV, 0, 0);
            Box(l, key, f, 0, 0.5f, 0, 0.24f, 0.28f, 0.24f, Plank, GV, 0, 0);
            Col(l, f, 0, 0, 0.75f, 0, 0.76f);
        }
    }
}
