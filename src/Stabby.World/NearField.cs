using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct RiverPiece
    {
        public float Ax, Az, Bx, Bz, Sa, Sb, Wa, Wb, Da, Db;
    }

    public interface INearCells
    {
        int N { get; }
        float CellMeters { get; }
        uint Seed { get; }
        float Height(int i);
        float Amp(int i);
        float Offset(int i);
        float Period { get; }
        int Bands { get; }
        float Band(int k);
        int PieceStart(int cell);
        int PieceAt(int k);
        RiverPiece Piece(int p);
    }

    public static class NearField
    {
        public const float MacroM = 180f, GullyAlongM = 40f, GullyAcrossM = 8f, GullyDepthM = 2.5f, RockM = 6f;
        public const float BankMinM = 3f, BankPerWidth = 0.75f, BankRiseM = 1f, BankRisePerWidth = 0.05f, BankFadeMinM = 30f, BankFadeCells = 0.75f;
        public const int MacroOctaves = 5, Chaikin = 2;
        const uint GullySalt = 0x9E3779B9u, RockSalt = 0x85EBCA6Bu;

        public static float Bank(float halfWidth) => BankMinM + BankPerWidth * halfWidth;
        public static float Rise(float halfWidth) => BankRiseM + BankRisePerWidth * 2 * halfWidth;
        public static float Fade(float cm) => Math.Max(BankFadeMinM, BankFadeCells * cm);
        public static float Reach(float halfWidth, float cm) => halfWidth + Bank(halfWidth) + Fade(cm);

        public static List<RiverPiece> Pieces(GeneratedWorld w)
        {
            var list = new List<RiverPiece>();
            if (w.Water == null) return list;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            foreach (var r in w.Water.Rivers)
            {
                int count = r.Cells.Count;
                if (count < 2) continue;
                var px = new List<float>(); var pz = new List<float>(); var ps = new List<float>(); var pw = new List<float>(); var pd = new List<float>();
                for (int k = 0; k < count; k++)
                {
                    px.Add(r.X[k] * cm); pz.Add(r.Y[k] * cm); ps.Add(Hydrology.Stage(w.Config, r, k)); pw.Add(r.Width[k] * 0.5f); pd.Add(r.Depth[k]);
                }
                for (int it = 0; it < Chaikin; it++) { Smooth(px); Smooth(pz); Smooth(ps); Smooth(pw); Smooth(pd); }
                for (int k = 0; k + 1 < px.Count; k++)
                {
                    if (Wet(w, n, cm, px[k], pz[k]) || Wet(w, n, cm, px[k + 1], pz[k + 1])) continue;
                    list.Add(new RiverPiece
                    {
                        Ax = px[k], Az = pz[k], Bx = px[k + 1], Bz = pz[k + 1],
                        Sa = ps[k], Sb = ps[k + 1], Wa = pw[k], Wb = pw[k + 1], Da = pd[k], Db = pd[k + 1]
                    });
                }
            }
            return list;
        }

        static bool Wet(GeneratedWorld w, int n, float cm, float x, float z)
        {
            int i = CellOf(x, z, cm, n);
            return w.Sea[i] || w.Water.LakeId[i] >= 0;
        }

        static void Smooth(List<float> p)
        {
            var o = new List<float>(p.Count * 2) { p[0] };
            for (int k = 0; k + 1 < p.Count; k++)
            {
                o.Add(p[k] + (p[k + 1] - p[k]) * 0.25f);
                o.Add(p[k] + (p[k + 1] - p[k]) * 0.75f);
            }
            o.Add(p[p.Count - 1]);
            p.Clear();
            p.AddRange(o);
        }

        public static int CellOf(float x, float z, float cm, int n)
        {
            int i = Clamp((int)Math.Floor(x / cm + 0.5f), n), k = Clamp((int)Math.Floor(z / cm + 0.5f), n);
            return k * n + i;
        }

        public static void Index(List<RiverPiece> pieces, int n, float cm, out int[] start, out int[] items)
        {
            var counts = new int[n * n + 1];
            var cells = new List<int>[pieces.Count];
            float half = 0.75f * cm;
            for (int p = 0; p < pieces.Count; p++)
            {
                var q = pieces[p];
                float r = Reach(Math.Max(q.Wa, q.Wb), cm) + half;
                int x0 = Clamp((int)Math.Floor((Math.Min(q.Ax, q.Bx) - r) / cm), n), x1 = Clamp((int)Math.Ceiling((Math.Max(q.Ax, q.Bx) + r) / cm), n);
                int z0 = Clamp((int)Math.Floor((Math.Min(q.Az, q.Bz) - r) / cm), n), z1 = Clamp((int)Math.Ceiling((Math.Max(q.Az, q.Bz) + r) / cm), n);
                var l = new List<int>();
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Project(q, x * cm, z * cm, out float d, out _);
                        if (d <= r) { l.Add(z * n + x); counts[z * n + x + 1]++; }
                    }
                cells[p] = l;
            }
            for (int i = 0; i < n * n; i++) counts[i + 1] += counts[i];
            start = counts;
            items = new int[counts[n * n]];
            var fill = new int[n * n];
            for (int p = 0; p < pieces.Count; p++)
                foreach (int c in cells[p]) items[start[c] + fill[c]++] = p;
        }

        public static void Project(RiverPiece q, float x, float z, out float d, out float t)
        {
            float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, l2 = ex * ex + ez * ez;
            t = l2 > 0 ? ((x - q.Ax) * ex + (z - q.Az) * ez) / l2 : 0;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            float dx = x - q.Ax - ex * t, dz = z - q.Az - ez * t;
            d = (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static float Smooth<T>(T c, float x, float z) where T : struct, INearCells
        {
            float cm = c.CellMeters;
            return Base(c, x / cm, z / cm);
        }

        public static float Sample<T>(T c, float x, float z) where T : struct, INearCells
        {
            int n = c.N;
            float cm = c.CellMeters, gx = x / cm, gz = z / cm;
            float b = Base(c, gx, gz);
            int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
            float tx = gx - x0, tz = gz - z0;
            int ax = Clamp(x0, n), az = Clamp(z0, n), bx = Clamp(x0 + 1, n), bz = Clamp(z0 + 1, n);
            int i00 = az * n + ax, i10 = az * n + bx, i01 = bz * n + ax, i11 = bz * n + bx;
            float a = Lerp2(c.Amp(i00), c.Amp(i10), c.Amp(i01), c.Amp(i11), tx, tz);

            int cell = CellOf(x, z, cm, n), s0 = c.PieceStart(cell), s1 = c.PieceStart(cell + 1);
            float fade = 1f, emin = float.MaxValue, f = Fade(cm);
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d >= wb + f) continue;
                fade = Math.Min(fade, Grid.Smoothstep(wb, wb + f, d));
                emin = Math.Min(emin, d - w);
            }

            float h = b;
            if (a > 0 && fade > 0)
            {
                float m = Noise.Fbm(c.Seed, x / MacroM, z / MacroM, MacroOctaves);
                h += fade * (a * Math.Max(-1f, Math.Min(1f, m)) + Fine(c, x, z, b, a, tx, tz, ax, az, bx, bz));
            }
            if (emin == float.MaxValue) return h;

            float sum = 0, acc = 0;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d >= wb + f) continue;
                float s = q.Sa + (q.Sb - q.Sa) * t, depth = q.Da + (q.Db - q.Da) * t;
                float top = SmoothMax(h, s + Rise(w), 1f), p;
                if (d < w) p = Math.Max(s - depth * (1 - d * d / (w * w)), 0.5f * Math.Min(s, Erosion.MinLandHeight));
                else if (d < wb) p = s + (top - s) * Grid.Smoothstep(w, wb, d);
                else p = h + (top - h) * (1 - Grid.Smoothstep(wb, wb + f, d));
                float e = d - w - emin, wt = (1 - Grid.Smoothstep(wb, wb + f, d)) / ((0.25f + e * e) * (0.25f + e * e));
                sum += wt; acc += wt * p;
            }
            return sum > 0 ? acc / sum : h;
        }

        static float Fine<T>(T c, float x, float z, float b, float a, float tx, float tz, int ax, int az, int bx, int bz) where T : struct, INearCells
        {
            int n = c.N;
            float cm = c.CellMeters;
            float gx = Lerp2(Dx(c, ax, az), Dx(c, bx, az), Dx(c, ax, bz), Dx(c, bx, bz), tx, tz) / cm;
            float gz = Lerp2(Dz(c, ax, az), Dz(c, bx, az), Dz(c, ax, bz), Dz(c, bx, bz), tx, tz) / cm;
            float slope2 = gx * gx + gz * gz, slope = (float)Math.Sqrt(slope2), inv = 1f / (float)Math.Sqrt(slope2 + 1e-6f);
            float dx = -gx * inv, dz = -gz * inv;
            float off = Lerp2(c.Offset(az * n + ax), c.Offset(az * n + bx), c.Offset(bz * n + ax), c.Offset(bz * n + bx), tx, tz);
            float hard = Hardness(c, b + off), sf = Grid.Smoothstep(0.05f, 0.6f, slope);
            float ga = Math.Min(GullyDepthM * (0.4f + 1 - hard) * sf, 0.25f * a);
            float ra = Math.Min(0.25f + 0.5f * hard * sf, 0.1f * a);
            float fine = 0;
            if (ga > 0)
            {
                float wsum = 1e-4f, g = 0;
                for (int i = 0; i < 4; i++)
                {
                    float ux = i == 0 ? 1f : i == 2 ? 0f : i == 1 ? 0.70710678f : -0.70710678f;
                    float uz = i == 0 ? 0f : i == 2 ? 1f : 0.70710678f;
                    float dot = ux * dx + uz * dz, w = dot * dot * dot * dot;
                    if (w < 1e-3f) continue;
                    float u = x * ux + z * uz, v = z * ux - x * uz;
                    uint s = c.Seed ^ GullySalt ^ (uint)i * 0x27D4EB2Fu;
                    float n1 = 1 - Math.Abs(Noise.Gradient(s, u / GullyAlongM, v / GullyAcrossM));
                    float n2 = 1 - Math.Abs(Noise.Gradient(s + 7u, 2 * u / GullyAlongM, 2 * v / GullyAcrossM));
                    float ch = n1 * n1 + 0.5f * n2 * n2;
                    g += w * (1 - ch * (2f / 1.5f));
                    wsum += w;
                }
                fine += ga * Math.Max(-1f, Math.Min(1f, g / wsum));
            }
            if (ra > 0) fine += ra * Math.Max(-1f, Math.Min(1f, Noise.Fbm(c.Seed ^ RockSalt, x / RockM, z / RockM, 2)));
            return fine;
        }

        static float Dx<T>(T c, int x, int z) where T : struct, INearCells
        {
            int n = c.N;
            return 0.5f * (c.Height(z * n + Clamp(x + 1, n)) - c.Height(z * n + Clamp(x - 1, n)));
        }

        static float Dz<T>(T c, int x, int z) where T : struct, INearCells
        {
            int n = c.N;
            return 0.5f * (c.Height(Clamp(z + 1, n) * n + x) - c.Height(Clamp(z - 1, n) * n + x));
        }

        static float Hardness<T>(T c, float h) where T : struct, INearCells
        {
            int bands = c.Bands;
            float u = h / c.Period;
            u = (u - (float)Math.Floor(u)) * bands;
            int k = Math.Min(bands - 1, (int)u);
            float t = u - k;
            return c.Band(k) + (c.Band((k + 1) % bands) - c.Band(k)) * t;
        }

        static float SmoothMax(float a, float b, float k)
        {
            float d = a - b;
            return 0.5f * (a + b + (float)Math.Sqrt(d * d + k * k));
        }

        static float Lerp2(float v00, float v10, float v01, float v11, float tx, float tz)
        {
            float a = v00 + (v10 - v00) * tx, b = v01 + (v11 - v01) * tx;
            return a + (b - a) * tz;
        }

        public static float Base<T>(T c, float gx, float gz) where T : struct, INearCells
        {
            int n = c.N, x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
            float tx = gx - x0, tz = gz - z0;
            float r0 = Row(c, n, z0 - 1, x0, tx), r1 = Row(c, n, z0, x0, tx), r2 = Row(c, n, z0 + 1, x0, tx), r3 = Row(c, n, z0 + 2, x0, tx);
            return Hermite(r0, r1, r2, r3, tz);
        }

        static float Row<T>(T c, int n, int z, int x0, float t) where T : struct, INearCells
        {
            int r = Clamp(z, n) * n;
            return Hermite(c.Height(r + Clamp(x0 - 1, n)), c.Height(r + Clamp(x0, n)), c.Height(r + Clamp(x0 + 1, n)), c.Height(r + Clamp(x0 + 2, n)), t);
        }

        static float Tangent(float d0, float d1) => d0 * d1 > 0 ? 2 * d0 * d1 / (d0 + d1) : 0;

        static float Hermite(float p0, float p1, float p2, float p3, float t)
        {
            float d = p2 - p1, m1 = Tangent(p1 - p0, d), m2 = Tangent(d, p3 - p2);
            float t2 = t * t, t3 = t2 * t;
            return p1 + (t3 - 2 * t2 + t) * m1 + (3 * t2 - 2 * t3) * d + (t3 - t2) * m2;
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;
    }
}
