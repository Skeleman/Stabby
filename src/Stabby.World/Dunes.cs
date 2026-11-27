using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct DuneSpec
    {
        public float Wx, Wz, Spacing, Height, Ox, Oz, Side;
        public int Linear;
        public uint Seed;
    }

    public static class Dunes
    {
        public const string Biome = "desert";
        public const float EdgeM = 60f, FadeM = 1500f, SlipSlope = 0.58f, WindwardSlope = 0.25f, FlatHi = 0.06f, VeerMax = 0.35f;
        public const float SpacingMin = 100f, SpacingMax = 400f, HeightPerSpacingLo = 0.09f, HeightPerSpacingHi = 0.11f, HeightMin = 5f, HeightMax = 40f, LinearShare = 0.45f;
        public const float MaxSlope = 0.7002075f, ClearM = 0.01f, SlopeMargin = 0.012f;
        public const int MaxRegions = 255;
        const float BarchanRadius = 6f, BarchanLee = 0.3f, BarchanHollow = 0.5f, BarchanAlong = 1.5f, Jitter = 0.1f;
        const float RampShare = 0.5f;
        const float LinearJitter = 0.3f, LinearWander = 0.1f, LinearCrest = 0.1f;

        const float Room = MaxSlope - SlopeMargin - SlipSlope * 1.01f - FlatHi;

        public static void Build(GeneratedWorld w, int[] pieceStart, out ushort[] pack, out DuneSpec[] specs)
        {
            pack = Array.Empty<ushort>();
            specs = Array.Empty<DuneSpec>();
            if (w.Biome == null || w.BiomeIds == null) return;
            int desert = Array.IndexOf(w.BiomeIds, Biome);
            if (desert < 0) return;
            int n = w.Config.Size, nn = n * n;
            float cm = w.Config.CellMeters;
            var h = w.Height;
            var d = new float[nn];
            bool any = false;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    d[i] = Blocked(w, pieceStart, desert, n, cm, h, x, y) ? 0f : float.MaxValue;
                    any |= d[i] > 0;
                }
            if (!any) return;
            Chamfer(d, n, cm);
            float edge = Math.Max(EdgeM, 2.01f * cm);
            var label = new int[nn];
            var sizes = new List<int>();
            var deep = new List<float>();
            var stack = new Stack<int>();
            for (int i = 0; i < nn; i++)
            {
                if (d[i] <= edge || label[i] != 0) continue;
                int id = sizes.Count + 1, size = 0;
                float far = 0;
                label[i] = id;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop(), cx = c % n, cy = c / n;
                    size++;
                    far = Math.Max(far, d[c]);
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = cx + Grid.DX[k], ny = cy + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int j = ny * n + nx;
                        if (d[j] <= edge || label[j] != 0) continue;
                        label[j] = id;
                        stack.Push(j);
                    }
                }
                sizes.Add(size);
                deep.Add(far);
            }
            if (sizes.Count == 0) return;

            Wind(w, out float wx, out float wz);
            var rng = Rng.Stream(w.Config.Seed, "dunes");
            var all = new DuneSpec[sizes.Count];
            var fades = new float[sizes.Count];
            for (int r = 0; r < all.Length; r++)
            {
                float s = rng.Range(-VeerMax, VeerMax), c = (float)Math.Sqrt(1 - s * s);
                float spacing = rng.Range(SpacingMin, SpacingMax), ratio = rng.Range(HeightPerSpacingLo, HeightPerSpacingHi);
                float fade = Math.Min(FadeM, RampShare * (deep[r] - edge));
                float height = Math.Min(Math.Min(HeightMax, spacing * ratio), Room * fade);
                spacing = Math.Max(SpacingMin, Math.Min(spacing, height / ratio));
                int linear = rng.NextFloat() < LinearShare ? 1 : 0;
                float side = rng.NextFloat() < 0.5f ? -1f : 1f;
                float ox = rng.Range(0f, 1000f), oz = rng.Range(0f, 1000f);
                fades[r] = height >= HeightMin ? fade : 0f;
                all[r] = new DuneSpec { Wx = wx * c - wz * s, Wz = wx * s + wz * c, Spacing = spacing, Height = height, Ox = ox, Oz = oz, Side = side, Linear = linear, Seed = rng.NextU32() };
            }

            var order = new int[sizes.Count];
            int live = 0;
            for (int r = 0; r < order.Length; r++) if (fades[r] > 0) order[live++] = r;
            if (live == 0) return;
            Array.Sort(order, 0, live, Comparer<int>.Create((a, b) => sizes[a] != sizes[b] ? sizes[b].CompareTo(sizes[a]) : a.CompareTo(b)));
            int keep = Math.Min(MaxRegions, live);
            Array.Sort(order, 0, keep);
            var slot = new int[sizes.Count + 1];
            specs = new DuneSpec[keep];
            for (int k = 0; k < keep; k++) { slot[order[k] + 1] = k + 1; specs[k] = all[order[k]]; }
            pack = new ushort[nn];
            for (int i = 0; i < nn; i++)
            {
                int r = label[i] == 0 ? 0 : slot[label[i]];
                if (r == 0) continue;
                int wt = (int)Math.Round(255f * Math.Min(1f, (d[i] - edge) / fades[label[i] - 1]));
                if (wt > 0) pack[i] = (ushort)(r << 8 | wt);
            }
        }

        static bool Blocked(GeneratedWorld w, int[] pieceStart, int desert, int n, float cm, float[] h, int x, int y)
        {
            if (x == 0 || y == 0 || x == n - 1 || y == n - 1) return true;
            int i = y * n + x;
            if (w.Biome[i] != desert || w.Sea[i] || h[i] <= 1f) return true;
            if (w.Water != null && w.Water.LakeId[i] >= 0) return true;
            if (pieceStart != null && pieceStart[i + 1] > pieceStart[i]) return true;
            for (int k = 0; k < 8; k++)
            {
                int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                if (Math.Abs(h[j] - h[i]) > FlatHi * Grid.Dist[k] * cm) return true;
            }
            return false;
        }

        static void Chamfer(float[] d, int n, float cm)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? 0 : n * n - 1, step = pass == 0 ? 1 : -1;
                for (int i = start; i >= 0 && i < n * n; i += step)
                {
                    if (d[i] == 0) continue;
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        bool back = Grid.DY[k] < 0 || (Grid.DY[k] == 0 && Grid.DX[k] < 0);
                        if (back != (pass == 0)) continue;
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        float v = d[ny * n + nx];
                        if (v != float.MaxValue) d[i] = Math.Min(d[i], v + Grid.Dist[k] * cm);
                    }
                }
            }
        }

        static void Wind(GeneratedWorld w, out float wx, out float wz)
        {
            float deg = w.Materials != null && w.Materials.Surface != null ? w.Materials.Surface.WindDeg : 0f;
            float t = deg * ((float)Math.PI / 180f);
            wx = Sin(t);
            wz = Sin(t + 0.5f * (float)Math.PI);
            float l = (float)Math.Sqrt(wx * wx + wz * wz);
            if (l < 1e-6f) { wx = 0; wz = 1; return; }
            wx /= l; wz /= l;
        }

        static float Sin(float t)
        {
            float tau = 2f * (float)Math.PI;
            t -= tau * (float)Math.Floor(t / tau + 0.5f);
            float t2 = t * t, term = t, sum = t;
            for (int k = 1; k <= 5; k++)
            {
                term *= -t2 / ((2 * k) * (2 * k + 1));
                sum += term;
            }
            return sum;
        }

        public static float Weight<T>(T c, int cell) where T : struct, INearCells => (c.Dune(cell) & 255) / 255f;

        public static float Top<T>(T c, int cell) where T : struct, INearCells
        {
            int p = c.Dune(cell);
            return p == 0 ? 0 : c.DuneRegion((p >> 8) - 1).Height * (p & 255) / 255f;
        }

        public static float At<T>(T c, float x, float z) where T : struct, INearCells
        {
            int n = c.N;
            float cm = c.CellMeters, gx = x / cm, gz = z / cm;
            int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
            if (x0 < 0 || z0 < 0 || x0 >= n - 1 || z0 >= n - 1) return 0;
            float tx = gx - x0, tz = gz - z0;
            int i00 = z0 * n + x0;
            int p00 = c.Dune(i00), p10 = c.Dune(i00 + 1), p01 = c.Dune(i00 + n), p11 = c.Dune(i00 + n + 1);
            if ((p00 | p10 | p01 | p11) == 0) return 0;
            int top = Math.Max(Math.Max(p00 & 255, p10 & 255), Math.Max(p01 & 255, p11 & 255));
            int region = (p00 & 255) == top ? p00 >> 8 : (p10 & 255) == top ? p10 >> 8 : (p01 & 255) == top ? p01 >> 8 : p11 >> 8;
            float a = (p00 & 255) + ((p10 & 255) - (p00 & 255)) * tx, b = (p01 & 255) + ((p11 & 255) - (p01 & 255)) * tx;
            float wn = (a + (b - a) * tz) / 255f;
            if (wn <= 0) return 0;
            return wn * Shape(c.DuneRegion(region - 1), x, z);
        }

        public static float Shape(DuneSpec s, float x, float z)
        {
            float u = x * s.Wx + z * s.Wz + s.Ox, v = -x * s.Wz + z * s.Wx + s.Oz;
            return s.Linear != 0 ? Linear(s, u, v) : Barchan(s, u, v);
        }

        static float Barchan(DuneSpec s, float u, float v)
        {
            float lu = BarchanAlong * s.Spacing, lv = s.Spacing, best = 0;
            int iu = (int)Math.Floor(u / lu);
            for (int cu = iu - 1; cu <= iu + 1; cu++)
            {
                float stagger = (cu & 1) != 0 ? 0.5f * lv : 0f;
                int iv = (int)Math.Floor((v - stagger) / lv);
                for (int cv = iv - 1; cv <= iv + 1; cv++)
                {
                    uint hsh = Noise.Hash(s.Seed, cu, cv);
                    float ju = ((hsh & 1023) / 1023f - 0.5f) * 2f * Jitter, jv = (((hsh >> 10) & 1023) / 1023f - 0.5f) * 2f * Jitter, js = ((hsh >> 20) & 1023) / 1023f;
                    float hd = s.Height * (0.8f + 0.2f * js), r = BarchanRadius * hd;
                    float du = u - (cu + 0.5f + ju) * lu, dv = v - stagger - (cv + 0.5f + jv) * lv;
                    float q2 = (du * du + dv * dv) / (r * r);
                    if (q2 >= 1) continue;
                    float dome = hd * (1 - q2) * (1 - q2);
                    float lx = du - BarchanLee * r;
                    float cone = SlipSlope * ((float)Math.Sqrt(lx * lx + dv * dv) - BarchanHollow * r);
                    best = Math.Max(best, Math.Min(dome, cone));
                }
            }
            return best;
        }

        static float Linear(DuneSpec s, float u, float v)
        {
            float l = s.Spacing, best = 0;
            float hc = s.Height * (0.8f + 0.2f * Math.Max(-1f, Math.Min(1f, Noise.Gradient(s.Seed + 1u, u / (2f * l), 0.5f))));
            float wander = LinearWander * l * Noise.Gradient(s.Seed + 2u, u / (3f * l), 0.5f);
            int j = (int)Math.Floor((v - wander) / l);
            for (int k = j - 1; k <= j + 1; k++)
            {
                uint hsh = Noise.Hash(s.Seed, k, 0);
                float jit = ((hsh & 1023) / 1023f - 0.5f) * 2f * LinearJitter;
                float crest = (k + 0.5f + jit) * l + wander;
                float d = (v - crest) * s.Side;
                float p = PolyMin(hc - SlipSlope * d, hc + WindwardSlope * d, LinearCrest * hc);
                best = Math.Max(best, p);
            }
            return best;
        }

        static float PolyMin(float a, float b, float k)
        {
            float m = Math.Max(k - Math.Abs(a - b), 0) / k;
            return Math.Min(a, b) - m * m * k * 0.25f;
        }
    }
}
