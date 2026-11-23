using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct RiverPiece
    {
        public float Ax, Az, Bx, Bz, Sa, Sb, Wa, Wb, Da, Db, Ka, Kb, Pa, Pb, Ra, Rb;
        public float Lx, Lz, Ux, Uz, Fy, Fr, Fd, Fc, Ft, Fh, Fs, Fb, Fw, Ia, Ib, Ja, Jb, Iy;
    }

    public sealed class RiverLine
    {
        public float[] X, Z, S, W, D, K, P, R;
        public RiverFall[] Falls;
        public int[] Lip;
        public int Liquid;
        public WaterMix[] Mix;
    }

    public interface INearCells
    {
        int N { get; }
        float CellMeters { get; }
        uint Seed { get; }
        uint GullySeed { get; }
        uint RockSeed { get; }
        uint CoastSeed { get; }
        uint CragSeed { get; }
        uint RiverSeed { get; }
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
        public const float MacroM = 180f, GullyAlongM = 40f, GullyAcrossM = 14f, GullyDepthM = 2.5f, RockM = 10f, CreaseSoft = 0.25f, WallSlope = 1f, WallLift = 1e4f, CutSoft = 1.5f, WallRampM = 4f;
        public const float BankMinM = 3f, BankPerWidth = 0.75f, BankRiseM = 1f, BankRisePerWidth = 0.05f, BankFadeMinM = 30f, BankFadeCells = 0.75f;
        public const float CoastWarpCells = 0.5f, CoastWaveCells = 2.5f, CoastBandM = 3f, CoastPin = 0.35f;
        public const float CragFrac = 0.5f, CragMaxM = 10f, CragSlopeLo = 0.35f, CragSlopeHi = 1.2f, CragGridLo = 0.1f, CragGridHi = 0.25f, CragAlongM = 50f, CragAcrossM = 18f, CragJutM = 24f, CragFloorM = 0.25f, CragWeightFloor = 0.25f;
        public const float CragWarpM = 9f, CragWarpWaveM = 40f, LedgePeriodM = 6.5f, LedgeWarpM = 1.5f, LedgeWarpWaveM = 35f, LedgeRagM = 0.15f, LedgeRagWaveM = 4f, LedgeStrikeM = 70f;
        public const float BendFull = 0.05f, BarSkew = 0.6f, BarLiftFrac = 0.5f, CutShrink = 0.6f, BankNoise = 0.4f, BankNoiseM = 9f, BedNoise = 0.15f, BedNoiseM = 7f;
        public const float StepGradient = 0.025f, StepDropMin = 1.2f, StepDropMax = 3.5f, RapidBase = 0.35f, BoulderFrac = 0.45f, BoulderM = 5f, PoolDropPerDepth = 2.5f, MaxBankSlope = 2f, HoldStageM = 0.5f;
        public const int MacroOctaves = 5, Chaikin = 2, StepReach = 4, StepMinPieces = 3;
        static readonly float[] Plunge = { 0.55f, 0.5f, 0.25f };
        public const int NodeQuads = 64, NodeLevels = 9;

        public static float NodeStep(int level) => 1 << level;
        public static float NodeSize(int level) => NodeQuads * NodeStep(level);

        public static float PoolDrop(float drop, float depth) => Math.Min(drop, PoolDropPerDepth * depth);

        public static float Bank(float halfWidth) => BankMinM + BankPerWidth * halfWidth;
        public static float Rise(float halfWidth) => BankRiseM + BankRisePerWidth * 2 * halfWidth;
        public static float Fade(float cm) => Math.Max(BankFadeMinM, BankFadeCells * cm);
        public static float Reach(float halfWidth, float cm) => halfWidth + Bank(halfWidth) + Fade(cm);

        public static List<RiverLine> Lines(GeneratedWorld w)
        {
            var lines = new List<RiverLine>();
            if (w.Water == null) return lines;
            float cm = w.Config.CellMeters;
            var rng = Rng.Stream(w.Config.Seed, "river-steps");
            foreach (var r in w.Water.Rivers)
            {
                int count = r.Cells.Count;
                if (count < 2) continue;
                var px = new List<float>(); var pz = new List<float>(); var ps = new List<float>(); var pw = new List<float>(); var pd = new List<float>();
                var tint = new[] { new List<float>(), new List<float>(), new List<float>() };
                for (int k = 0; k < count; k++)
                {
                    px.Add(r.X[k] * cm); pz.Add(r.Y[k] * cm); ps.Add(Hydrology.Stage(w.Config, r, k)); pw.Add(r.Width[k] * 0.5f); pd.Add(r.Depth[k]);
                    var m = k < r.Mix.Count ? r.Mix[k] : default;
                    for (int t = 0; t < tint.Length; t++) tint[t].Add(m[t]);
                }
                for (int it = 0; it < Chaikin; it++) { Smooth(px); Smooth(pz); Smooth(ps); Smooth(pw); Smooth(pd); foreach (var t in tint) Smooth(t); }
                var falls = new List<RiverFall>(); var lips = new List<int>();
                int from = 0;
                foreach (var f in r.Falls)
                {
                    int at = InsertFall(f, from, px, pz, ps, pw, pd, tint);
                    if (at < 0) continue;
                    falls.Add(f); lips.Add(at); from = at + 2;
                }
                var line = new RiverLine { X = px.ToArray(), Z = pz.ToArray(), S = ps.ToArray(), W = pw.ToArray(), D = pd.ToArray(), Falls = falls.ToArray(), Lip = lips.ToArray(), Liquid = r.Liquid, Mix = new WaterMix[px.Count] };
                for (int k = 0; k < px.Count; k++) line.Mix[k] = new WaterMix { A = tint[0][k], B = tint[1][k], C = tint[2][k] };
                Bends(line);
                Steps(line, cm, ref rng);
                lines.Add(line);
            }
            return lines;
        }

        public const float FallDepthFrac = 0.5f, FallDepthBaseM = 0.8f, FallDepthPerDrop = 0.15f;
        public static float PoolReach(float c, float r) => c + (1 + FallRimFrac) * r + Math.Max(FallRingMinM, FallRingFrac * r) + 1f;
        public const float FallRadiusNoise = 0.25f, FallBowlNoise = 0.2f, FallBowlWaveM = 1.7f, FallRimWaveFrac = 0.7f, FallRimWaveM = 3f, FallRimHeight = 0.35f, FallRimCap = 0.15f, FallTalusWaveM = 2.5f, FallLowFrac = 1.3f;
        public const float FallTrimM = 0.1f, FallJagM = 0.6f, FallJagWaveM = 2.5f, FallRimFrac = 0.35f, FallRingMinM = 3f, FallRingFrac = 0.5f, FallLiftRise = 4f, FallBlendM = 1f, FallTalusFrac = 0.35f, FallSlumpFrac = 0.4f, FallClearM = 15f, FallOutletSlope = 1.2f, FallOutletFrac = 0.3f, FallOutletRise = 0.1f, FallFloorM = 0.02f, ShelfPadM = 2f;

        static float AlongLip(RiverFall f, float x, float z) => (x - f.LipX) * f.DirX + (z - f.LipZ) * f.DirZ;

        static int InsertFall(RiverFall f, int from, List<float> px, List<float> pz, List<float> ps, List<float> pw, List<float> pd, List<float>[] tint)
        {
            int m = px.Count, j = -1;
            float best = float.MaxValue, bt = 0;
            for (int k = from; k + 1 < m; k++)
            {
                float ex = px[k + 1] - px[k], ez = pz[k + 1] - pz[k], l2 = ex * ex + ez * ez;
                float t = l2 > 0 ? Math.Max(0, Math.Min(1, ((f.LipX - px[k]) * ex + (f.LipZ - pz[k]) * ez) / l2)) : 0;
                float dx = px[k] + ex * t - f.LipX, dz = pz[k] + ez * t - f.LipZ, d = dx * dx + dz * dz;
                if (d < best) { best = d; j = k; bt = t; }
            }
            if (j < 0) return -1;
            float w = pw[j] + (pw[j + 1] - pw[j]) * bt, depth = pd[j] + (pd[j + 1] - pd[j]) * bt;
            int lo = j, hi = j + 1;
            while (lo >= from && AlongLip(f, px[lo], pz[lo]) > -FallTrimM - Hydrology.ShelfFlare * f.ShelfHalf) lo--;
            while (hi < m && AlongLip(f, px[hi], pz[hi]) < Hydrology.FallBaseOffsetM + FallTrimM) hi++;
            if (lo < from) return -1;
            int cut = hi - lo - 1;
            foreach (var t in tint)
            {
                float v = t[j] + (t[j + 1] - t[j]) * bt;
                t.RemoveRange(lo + 1, cut);
                t.InsertRange(lo + 1, new[] { v, v });
            }
            px.RemoveRange(lo + 1, cut); pz.RemoveRange(lo + 1, cut); ps.RemoveRange(lo + 1, cut); pw.RemoveRange(lo + 1, cut); pd.RemoveRange(lo + 1, cut);
            px.InsertRange(lo + 1, new[] { f.LipX, f.BaseX }); pz.InsertRange(lo + 1, new[] { f.LipZ, f.BaseZ });
            ps.InsertRange(lo + 1, new[] { f.LipY, f.BaseY }); pw.InsertRange(lo + 1, new[] { f.LipPts == null ? w : Math.Max(w, f.ShelfHalf + ShelfPadM), w }); pd.InsertRange(lo + 1, new[] { depth, depth });
            return lo + 1;
        }

        static void Bends(RiverLine l)
        {
            int m = l.X.Length;
            var raw = new float[m];
            for (int k = 1; k + 1 < m; k++)
            {
                int lo = Math.Max(0, k - 2), hi = Math.Min(m - 1, k + 2);
                float ax = l.X[k] - l.X[lo], az = l.Z[k] - l.Z[lo], bx = l.X[hi] - l.X[k], bz = l.Z[hi] - l.Z[k], cx = ax + bx, cz = az + bz;
                float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz), lc = (float)Math.Sqrt(cx * cx + cz * cz);
                if (la < 1e-3f || lb < 1e-3f || lc < 1e-3f) continue;
                float cross = ax * bz - az * bx;
                raw[k] = Math.Max(-1f, Math.Min(1f, l.W[k] * 2 * cross / (la * lb * lc) / BendFull));
            }
            l.K = new float[m];
            for (int k = 0; k < m; k++)
                l.K[k] = 0.25f * raw[Math.Max(0, k - 1)] + 0.5f * raw[k] + 0.25f * raw[Math.Min(m - 1, k + 1)];
        }

        static float[] Along(RiverLine l)
        {
            int m = l.X.Length;
            var along = new float[m];
            for (int k = 1; k < m; k++)
            {
                float dx = l.X[k] - l.X[k - 1], dz = l.Z[k] - l.Z[k - 1];
                along[k] = along[k - 1] + (float)Math.Sqrt(dx * dx + dz * dz);
            }
            return along;
        }

        static void Steps(RiverLine l, float cm, ref Rng rng)
        {
            int m = l.X.Length;
            var along = Along(l);
            var mask = new bool[m];
            for (int f = 0; f < l.Lip.Length; f++)
            {
                int lip = l.Lip[f];
                float a0 = along[lip] - 0.5f * cm, a1 = along[lip] + Math.Max(0.5f * cm, 2 * l.Falls[f].PoolRadius) + l.Falls[f].ShelfBow;
                for (int k = 0; k < m; k++) if (along[k] >= a0 && along[k] <= a1) mask[k] = true;
            }
            var ps = l.S;
            var g = new float[m];
            for (int k = 0; k < m; k++)
            {
                int lo = Math.Max(0, k - StepReach), hi = Math.Min(m - 1, k + StepReach);
                float run = along[hi] - along[lo];
                g[k] = run > 1e-3f ? Math.Max(0, ps[lo] - ps[hi]) / run : 0;
            }
            var st = (float[])ps.Clone();
            l.P = new float[m];
            l.R = new float[m];
            int i = 1;
            while (i < m)
            {
                if (g[i] < StepGradient || mask[i]) { i++; continue; }
                int a = i;
                while (i < m && g[i] >= StepGradient && !mask[i]) i++;
                int end = i - 1;
                for (int start = a; start <= end;)
                {
                    float target = rng.Range(StepDropMin, StepDropMax);
                    int b = start;
                    while (b < end && (ps[start] - ps[b] < target || b - start + 1 < StepMinPieces)) b++;
                    for (int j = start; j <= b; j++) { st[j] = ps[b]; l.R[j] = Math.Max(l.R[j], RapidBase); }
                    float drop = PoolDrop(st[start - 1] - st[start], l.D[start]);
                    for (int j = Math.Max(0, start - 2); j < start; j++) l.R[j] = 1f;
                    for (int j = 0; j < Plunge.Length && start + j < m; j++) l.P[start + j] = Math.Max(l.P[start + j], Plunge[j] * drop);
                    start = b + 1;
                }
            }
            for (int f = 0; f < l.Lip.Length; f++)
            {
                int lip = l.Lip[f];
                for (int k = 0; k <= lip; k++) st[k] = Math.Max(st[k], l.Falls[f].LipY);
                for (int k = lip + 1; k < m; k++) st[k] = Math.Min(st[k], l.Falls[f].BaseY);
            }
            for (int k = 0; k < m; k++) if (mask[k]) { l.P[k] = 0; l.R[k] = 0; }
            for (int k = 0; k < m; k++) if (l.P[k] > 0) l.R[k] = 0;
            l.S = st;
        }

        public static List<RiverPiece> Pieces(GeneratedWorld w, List<int> starts = null)
        {
            var list = new List<RiverPiece>();
            if (w.Water == null) return list;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            foreach (var l in Lines(w))
            {
                starts?.Add(list.Count);
                var along = Along(l);
                for (int k = 0; k + 1 < l.X.Length; k++)
                {
                    if (Wet(w, n, cm, l.X[k], l.Z[k]) || Wet(w, n, cm, l.X[k + 1], l.Z[k + 1])) continue;
                    var q = new RiverPiece
                    {
                        Ax = l.X[k], Az = l.Z[k], Bx = l.X[k + 1], Bz = l.Z[k + 1],
                        Sa = l.S[k], Sb = l.S[k + 1], Wa = l.W[k], Wb = l.W[k + 1], Da = l.D[k], Db = l.D[k + 1],
                        Ka = l.K[k], Kb = l.K[k + 1], Pa = l.P[k], Pb = l.P[k + 1], Ra = l.R[k], Rb = l.R[k + 1]
                    };
                    for (int f = 0; f < l.Lip.Length; f++)
                    {
                        int lip = l.Lip[f];
                        var fall = l.Falls[f];
                        bool pool = k + 1 == lip, clip = k >= lip && along[k] - along[lip] <= Reach(Math.Max(q.Wa, q.Wb), cm) + PoolReach(fall.PoolCentre, fall.PoolRadius) + fall.ShelfHalf + fall.ShelfBow;
                        if (!pool && !clip) continue;
                        q.Lx = fall.LipX; q.Lz = fall.LipZ; q.Ux = fall.DirX; q.Uz = fall.DirZ; q.Fs = fall.ShelfHalf; q.Fb = fall.ShelfBow;
                        if (pool) { q.Fy = fall.BaseY; q.Fr = fall.PoolRadius; q.Fc = fall.PoolCentre; q.Fd = Math.Min(FallDepthFrac * fall.PoolRadius, FallDepthBaseM + FallDepthPerDrop * fall.Drop); q.Ft = Hydrology.FallThrow(fall.Drop); q.Fh = fall.LipPts == null ? fall.SheetHalf : 0; q.Fw = fall.LipPts == null ? q.Wb : l.W[Math.Min(lip + 1, l.W.Length - 1)]; Isles(ref q, fall); break; }
                    }
                    list.Add(q);
                }
            }
            return list;
        }

        static void Isles(ref RiverPiece q, in RiverFall f)
        {
            var runs = Hydrology.LipRuns(f);
            q.Iy = f.LipY;
            if (runs.Count >= 4) { q.Ia = 0.5f * (runs[1] + runs[2]); q.Ja = 0.5f * (runs[2] - runs[1]); }
            if (runs.Count >= 6) { q.Ib = 0.5f * (runs[3] + runs[4]); q.Jb = 0.5f * (runs[4] - runs[3]); }
        }

        public const float IsleRadiusFrac = 0.6f, IsleStretch = 1.4f, IsleRiseM = 1f, IsleRisePerHalf = 0.6f, IsleSlope = 40f, IsleBack = 1f, IsleWaveM = 1.7f, IsleNoise = 0.25f, IsleCap = 0.4f;

        public static float Isle(RiverPiece q, uint seed, float x, float z)
        {
            float best = float.MinValue;
            for (int j = 0; j < 2; j++)
            {
                float half = j == 0 ? q.Ja : q.Jb, at = j == 0 ? q.Ia : q.Ib;
                if (half <= 0) continue;
                float lx = x - q.Lx, lz = z - q.Lz, along = lx * q.Ux + lz * q.Uz, side = lx * q.Uz - lz * q.Ux;
                float u = q.Fs > 0 ? Math.Max(-1f, Math.Min(1f, at / q.Fs)) : 0;
                float n = Math.Max(-1f, Math.Min(1f, Noise.Gradient(seed, x / IsleWaveM + 7.1f * j, z / IsleWaveM - 3.9f * j)));
                float r = IsleRadiusFrac * half * (1 + IsleNoise * n), rise = (IsleRiseM + IsleRisePerHalf * half) * (1 + IsleNoise * n);
                float a = (along - q.Fb * u * u) / IsleStretch + IsleBack * IsleRadiusFrac * half, sd = side - at, e = (float)Math.Sqrt(a * a + sd * sd);
                float top = q.Iy + rise - IsleCap * rise * Sq(Math.Min(1f, e / r));
                best = Math.Max(best, e <= r ? top : top - IsleSlope * (e - r));
            }
            return best;
        }

        static float Isles<T>(T c, int s0, int s1, float x, float z, float h) where T : struct, INearCells
        {
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                if (q.Ja > 0) h = Math.Max(h, Isle(q, c.RiverSeed + 6u, x, z));
            }
            return h;
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
                float r = Reach(Math.Max(q.Wa, q.Wb), cm) + half + (q.Fr > 0 ? PoolReach(q.Fc, q.Fr) + q.Fs + q.Fb : 0);
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
            for (int p = 0; p < pieces.Count; p++)
                foreach (int c in cells[p]) items[start[c]++] = p;
            for (int i = n * n; i > 0; i--) start[i] = start[i - 1];
            start[0] = 0;
        }

        static float StageGradient(RiverPiece q)
        {
            if (q.Ax == q.Lx && q.Az == q.Lz && (q.Ux != 0 || q.Uz != 0)) return 0f;
            float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, l = (float)Math.Sqrt(ex * ex + ez * ez);
            return l > 1e-3f ? Math.Abs(q.Sb - q.Sa) / l : 0f;
        }

        public static void Project(RiverPiece q, float x, float z, out float d, out float t)
        {
            float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, l2 = ex * ex + ez * ez;
            t = l2 > 0 ? ((x - q.Ax) * ex + (z - q.Az) * ez) / l2 : 0;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            if (q.Fs > 0 && q.Fr > 0)
            {
                float u = Math.Max(-1f, Math.Min(1f, ((x - q.Lx) * q.Uz - (z - q.Lz) * q.Ux) / q.Fs)), shift = q.Fb * u * u * t;
                x -= q.Ux * shift;
                z -= q.Uz * shift;
                t = l2 > 0 ? ((x - q.Ax) * ex + (z - q.Az) * ez) / l2 : 0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
            }
            float dx = x - q.Ax - ex * t, dz = z - q.Az - ez * t;
            d = (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static void Frame(RiverPiece q, float x, float z, out float along, out float side)
        {
            float lx = x - q.Lx, lz = z - q.Lz;
            along = lx * q.Ux + lz * q.Uz;
            side = lx * q.Uz - lz * q.Ux;
            if (q.Fs <= 0) { side = Math.Abs(side); return; }
            float u = Math.Max(-1f, Math.Min(1f, side / q.Fs));
            along -= q.Fb * u * u;
            side = Math.Max(0, Math.Abs(side) - q.Fs);
        }

        static float PoolDist(RiverPiece q, float along, float side) => (float)Math.Sqrt((along - q.Fc) * (along - q.Fc) + side * side);

        public const float DetailReach = 1.35f + CragFrac;
        const float Deepest = (1 + BarSkew) * (1 + BedNoise);

        public static void Range<T>(T c, float x0, float z0, float x1, float z1, out float lo, out float hi) where T : struct, INearCells
        {
            int n = c.N;
            float cm = c.CellMeters, amp = 0, wlo = float.MaxValue, whi = float.MinValue;
            lo = float.MaxValue; hi = float.MinValue;
            int i0 = Clamp((int)Math.Floor(x0 / cm) - 1, n), i1 = Clamp((int)Math.Ceiling(x1 / cm) + 1, n);
            int k0 = Clamp((int)Math.Floor(z0 / cm) - 1, n), k1 = Clamp((int)Math.Ceiling(z1 / cm) + 1, n);
            for (int k = k0; k <= k1; k++)
                for (int i = i0; i <= i1; i++)
                {
                    int cell = k * n + i;
                    float h = c.Height(cell);
                    lo = Math.Min(lo, h); hi = Math.Max(hi, h); amp = Math.Max(amp, c.Amp(cell));
                    for (int q = c.PieceStart(cell), e = c.PieceStart(cell + 1); q < e; q++)
                    {
                        var p = c.Piece(c.PieceAt(q));
                        wlo = Math.Min(wlo, Math.Min(Math.Min(p.Sa - p.Da * Deepest - p.Pa, p.Sb - p.Db * Deepest - p.Pb), 0.5f * Math.Min(Math.Min(p.Sa, p.Sb), Erosion.MinLandHeight)));
                        whi = Math.Max(whi, Math.Max(p.Sa + Rise(p.Wa), p.Sb + Rise(p.Wb)) + 1f);
                        if (p.Fr > 0) wlo = Math.Min(wlo, p.Fy - FallLowFrac * p.Fd);
                        if (p.Ja > 0) whi = Math.Max(whi, p.Iy + (IsleRiseM + IsleRisePerHalf * Math.Max(p.Ja, p.Jb)) * (1 + IsleNoise) + 1f);
                    }
                }
            lo -= DetailReach * amp; hi += DetailReach * amp;
            lo = Math.Min(lo, wlo); hi = Math.Max(hi, whi);
        }

        public static float Smooth<T>(T c, float x, float z) where T : struct, INearCells
        {
            float cm = c.CellMeters;
            return Coast(c, x / cm, z / cm);
        }

        public static float Coast<T>(T c, float gx, float gz) where T : struct, INearCells
        {
            float b = Base(c, gx, gz), e = Math.Abs(b);
            if (e >= CoastBandM) return b;
            float px = gx - (float)Math.Round(gx), pz = gz - (float)Math.Round(gz);
            float k = CoastWarpCells * (1 - Grid.Smoothstep(0, CoastBandM, e)) * Grid.Smoothstep(0, CoastPin * CoastPin, px * px + pz * pz);
            if (k <= 0) return b;
            float u = gx / CoastWaveCells, v = gz / CoastWaveCells;
            float dx = Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.CoastSeed, u, v)));
            float dz = Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.CoastSeed, u + 57.31f, v - 23.17f)));
            return Base(c, gx + k * dx, gz + k * dz);
        }

        public static float Sample<T>(T c, float x, float z) where T : struct, INearCells
        {
            int n = c.N;
            float cm = c.CellMeters, gx = x / cm, gz = z / cm;
            float b = Coast(c, gx, gz);
            int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
            float tx = gx - x0, tz = gz - z0;
            int ax = Clamp(x0, n), az = Clamp(z0, n), bx = Clamp(x0 + 1, n), bz = Clamp(z0 + 1, n);
            int i00 = az * n + ax, i10 = az * n + bx, i01 = bz * n + ax, i11 = bz * n + bx;
            float a = Lerp2(c.Amp(i00), c.Amp(i10), c.Amp(i01), c.Amp(i11), tx, tz);

            int cell = CellOf(x, z, cm, n), s0 = c.PieceStart(cell), s1 = c.PieceStart(cell + 1);
            float fade = 1f, f = Fade(cm);
            bool near = false;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                if (q.Fr > 0 && PoolZone(q, x, z, f)) near = true;
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d >= wb + f) continue;
                fade = Math.Min(fade, Grid.Smoothstep(wb, wb + f, d));
                near = true;
            }

            float h = b;
            if (a > 0 && fade > 0)
            {
                float m = Noise.Fbm(c.Seed, x / MacroM, z / MacroM, MacroOctaves);
                h += fade * (a * Math.Max(-1f, Math.Min(1f, m)) + Fine(c, x, z, b, a, tx, tz, ax, az, bx, bz));
            }
            if (!near) return Isles(c, s0, s1, x, z, h);
            h = Carve(c, s0, s1, x, z, h, f);

            float raised = h;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d >= wb + f) continue;
                float s = q.Sa + (q.Sb - q.Sa) * t, top = SmoothMax(h, s + Rise(w), 1f);
                raised = Math.Max(raised, h + (top - h) * (1 - Grid.Smoothstep(wb, wb + f, d)) * (1 - Gorge(c, s0, s1, x, z, s, f)));
            }
            float cut = float.MaxValue, pool = float.MaxValue, low = Math.Min(h, 0.5f * Erosion.MinLandHeight);
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                if (q.Fr > 0) { pool = Math.Min(pool, Pool(c, q, x, z, raised)); if (PoolZone(q, x, z, f)) low = Math.Min(low, Math.Max(FallFloorM, 0.5f * Math.Min(q.Fy, Erosion.MinLandHeight))); }
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d >= wb + f) continue;
                float gate = q.Fr > 0 || (q.Ux == 0 && q.Uz == 0) ? 1 : Grid.Smoothstep(-FallTrimM - Math.Max(0, d - w), 0, ShearAlong(q, x, z) - Jag(c, x, z));
                float s = q.Sa + (q.Sb - q.Sa) * t, depth = q.Da + (q.Db - q.Da) * t, top = SmoothMax(h, s + Rise(w), 1f), p;
                float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, el = (float)Math.Sqrt(ex * ex + ez * ez);
                float lat = el > 1e-3f ? ((z - q.Az) * ex - (x - q.Ax) * ez) / el : 0;
                float bend = q.Ka + (q.Kb - q.Ka) * t, inner = Math.Max(-1f, Math.Min(1f, bend * lat / w));
                float bar = Math.Min(BarLiftFrac * depth, 0.5f * Rise(w)) * Grid.Smoothstep(0.3f, 1f, inner);
                if (d < w)
                {
                    float bed = 1 - d * d / (w * w), plunge = q.Pa + (q.Pb - q.Pa) * t, rapid = q.Ra + (q.Rb - q.Ra) * t;
                    float rough = 1 + BedNoise * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 2u, x / BedNoiseM, z / BedNoiseM)));
                    float boulder = rapid > 0 ? rapid * BoulderFrac * depth * Grid.Smoothstep(-0.2f, 0.8f, Noise.Gradient(c.RiverSeed + 3u, x / BoulderM, z / BoulderM)) : 0;
                    p = Math.Max(s - bed * (depth * (1 - BarSkew * inner) * rough + plunge - boulder) + bar * (1 - bed), 0.5f * Math.Min(s, Erosion.MinLandHeight));
                }
                else
                {
                    top = Math.Max(top, Hold(c, s0, s1, x, z, h, s, f));
                    float nb = 0.5f + 0.5f * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed, x / BankNoiseM, z / BankNoiseM) + 0.5f * Noise.Gradient(c.RiverSeed + 1u, 2.3f * x / BankNoiseM, 2.3f * z / BankNoiseM)));
                    wb = w + Math.Max(Bank(w) * (1 - CutShrink * Math.Max(0, -inner)) * (1 - BankNoise * nb), Math.Min(Bank(w), (top - s - bar) / MaxBankSlope));
                    float r = Math.Min(WallRampM, 2f * (wb - w));
                    float u = d < wb ? (d - w) / (wb - w) : 1, ss = Grid.Smoothstep(0, 1, u), shape = ss + (u * u - ss) * Math.Max(0, inner);
                    p = (d < wb ? s + bar + (top - s - bar) * shape : top) + (WallSlope + 2f * StageGradient(q)) * Ramp(d - wb + 0.5f * r, r);
                    if (d >= wb) p += WallLift * Grid.Smoothstep(wb + 0.5f * f, wb + f, d);
                }
                cut = Math.Min(cut, p + (1 - gate) * Math.Max(0, raised + FallClearM - p));
            }
            return Isles(c, s0, s1, x, z, Math.Max(PolyMin(SmoothMin(raised, cut, CutSoft), pool, FallBlendM), low));
        }

        static float Hold<T>(T c, int s0, int s1, float x, float z, float h, float s, float f) where T : struct, INearCells
        {
            float top = h;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w), reach = Math.Min(Bank(w), f);
                if (d >= wb + reach) continue;
                float sq = q.Sa + (q.Sb - q.Sa) * t;
                if (sq <= s) continue;
                top = Math.Max(top, h + Math.Max(0, sq + Rise(w) - h) * (1 - Grid.Smoothstep(wb, wb + reach, d)) * Grid.Smoothstep(0, HoldStageM, sq - s));
            }
            return top;
        }

        static float Gorge<T>(T c, int s0, int s1, float x, float z, float s, float f) where T : struct, INearCells
        {
            float g = 0;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                if (q.Fr <= 0 || s <= q.Fy) continue;
                float band = Math.Min(f, 2 * (s - q.Fy) / FallOutletSlope);
                float lx = x - q.Lx, lz = z - q.Lz;
                g = Math.Max(g, Outlet(q, x, z, q.Fr * (1 + FallRadiusNoise), band) * Grid.Smoothstep(q.Fy, q.Fy + FallBlendM, s) * (1 - Grid.Smoothstep(0.5f * f, f, (float)Math.Sqrt(lx * lx + lz * lz))));
            }
            return g;
        }

        static float Carve<T>(T c, int s0, int s1, float x, float z, float h, float f) where T : struct, INearCells
        {
            float g = Below(c, s0, s1, x, z, f), cut = 0;
            if (g <= 0) return h;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                if (q.Fr > 0 || (q.Ux == 0 && q.Uz == 0)) continue;
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, e = Math.Max(0, d - w);
                if (e >= f) continue;
                cut = Math.Max(cut, (1 - Grid.Smoothstep(0.5f * f, f, e)) * Math.Max(0, h - (q.Sa + (q.Sb - q.Sa) * t) - Rise(w) - FallOutletSlope * e));
            }
            return h - g * cut;
        }

        static float Below<T>(T c, int s0, int s1, float x, float z, float f) where T : struct, INearCells
        {
            float g = 0;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                if (q.Fr <= 0) continue;
                Frame(q, x, z, out float along, out float side);
                g = Math.Max(g, Grid.Smoothstep(0, FallJagM, along) * (1 - Grid.Smoothstep(0.5f * f, f, (float)Math.Sqrt(along * along + side * side))));
            }
            return g;
        }

        static float Outlet(RiverPiece q, float x, float z, float half, float band)
        {
            float along = (x - q.Lx) * q.Ux + (z - q.Lz) * q.Uz, side = Math.Abs((x - q.Lx) * q.Uz - (z - q.Lz) * q.Ux);
            if (along <= FallJagM) return 0;
            float flare = Math.Min(along - FallJagM, band);
            return Grid.Smoothstep(FallJagM, FallJagM + FallBlendM, along) * (1 - Grid.Smoothstep(0.5f * flare, flare + FallBlendM, side - half));
        }

        static float Jag<T>(T c, float x, float z) where T : struct, INearCells
            => FallJagM * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 4u, x / FallJagWaveM + 0.37f, z / FallJagWaveM - 0.61f)));

        static float ShearAlong(RiverPiece q, float x, float z)
        {
            Frame(q, x, z, out float along, out _);
            return along;
        }

        public static bool PoolNear(RiverPiece q, float x, float z)
        {
            Frame(q, x, z, out float along, out float side);
            return PoolDist(q, along, side) < PoolReach(q.Fc, q.Fr) - q.Fc;
        }

        public static bool PoolZone(RiverPiece q, float x, float z, float f)
        {
            float along = (x - q.Lx) * q.Ux + (z - q.Lz) * q.Uz, side = Math.Abs((x - q.Lx) * q.Uz - (z - q.Lz) * q.Ux);
            return PoolNear(q, x, z) || (along > FallJagM && along < PoolReach(q.Fc, q.Fr) + f && side < q.Fw + OutletRing(q.Fr) + FallBlendM);
        }

        static float PoolMargin(RiverPiece q, float x, float z, float f)
        {
            float along = (x - q.Lx) * q.Ux + (z - q.Lz) * q.Uz, side = Math.Abs((x - q.Lx) * q.Uz - (z - q.Lz) * q.Ux);
            Frame(q, x, z, out float sa, out float ss);
            float circle = PoolReach(q.Fc, q.Fr) - q.Fc - PoolDist(q, sa, ss);
            return Math.Max(circle, Math.Min(Math.Min(along - FallJagM, PoolReach(q.Fc, q.Fr) + f - along), q.Fw + OutletRing(q.Fr) + FallBlendM - side));
        }

        static float OutletRing(float r) => Math.Max(FallRingMinM, FallRingFrac * r);

        public static bool InPoolWater(RiverPiece q, float x, float z)
        {
            Frame(q, x, z, out float along, out float side);
            return q.Fr > 0 && PoolDist(q, along, side) < q.Fr * (1 + FallRadiusNoise);
        }

        static float Pool<T>(T c, RiverPiece q, float x, float z, float top) where T : struct, INearCells
        {
            Frame(q, x, z, out float sa, out float ss);
            float along = (x - q.Lx) * q.Ux + (z - q.Lz) * q.Uz;
            float f = Fade(c.CellMeters), reach = PoolReach(q.Fc, q.Fr);
            if (sa < Jag(c, x, z) || !PoolZone(q, x, z, f)) return float.MaxValue;
            float dist = PoolDist(q, sa, ss);
            float wave = 1f + 0.6f * q.Fr;
            float rn = q.Fr * (1 + FallRadiusNoise * Math.Max(-0.6f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 4u, x / wave + 17.3f, z / wave - 5.1f))));
            float u = dist / rn, v;
            float slump = Grid.Smoothstep(0.55f, 1f, u) * (1 - Grid.Smoothstep(1f, 1f + FallSlumpFrac, u)) + (1 - Grid.Smoothstep(0, FallSlumpFrac * q.Fr, sa)) * Grid.Smoothstep(0.3f, 0.7f, u) * (1 - Grid.Smoothstep(1f, 1f + FallSlumpFrac, u));
            float side = Math.Abs((x - q.Lx) * q.Uz - (z - q.Lz) * q.Ux), sheet = 1 - (1 - Grid.Smoothstep(q.Fh, q.Fh + FallBlendM, ss)) * (1 - Grid.Smoothstep(q.Ft, q.Ft + FallBlendM, sa));
            float talus = FallTalusFrac * q.Fd * sheet * Math.Min(1f, slump) * (0.5f + 0.5f * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 5u, x / FallTalusWaveM + 3.3f, z / FallTalusWaveM - 12.1f))));
            if (u < 1)
                v = q.Fy - q.Fd * (1 - u * u) * (1 + FallBowlNoise * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 4u, x / FallBowlWaveM - 31.7f, z / FallBowlWaveM + 8.9f))));
            else
            {
                float rw = FallRimWaveM + FallRimWaveFrac * q.Fr;
                float broken = 0.5f + 0.5f * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 5u, x / rw, z / rw)));
                float ring = Math.Max(FallRingMinM, FallRingFrac * q.Fr), lift = Math.Min(FallRimHeight * q.Fd, FallRimCap * q.Fr);
                float rim = q.Fy + lift * (0.2f + 0.8f * broken) * Grid.Smoothstep(1f, 1f + FallRimFrac, u);
                v = rim + Math.Max(0, top - rim) * Grid.Smoothstep(rn, rn + ring, dist) + FallLiftRise * Sq(Math.Max(0, dist - rn - ring));
            }
            float deep = 1 - Grid.Smoothstep(0.5f * q.Fw, q.Fw, side);
            float mouth = OutletRing(q.Fr), open = side - q.Fw - Math.Max(0, Math.Min(along - FallJagM, mouth)), late = along - reach - 0.5f * f;
            float floor = q.Fy + 0.5f * FallBlendM - (FallOutletFrac * q.Fd + 0.5f * FallBlendM) * deep * (1 - Grid.Smoothstep(reach, reach + 0.5f * f, along)) + FallOutletRise * Math.Max(0, along - reach);
            float notch = floor + FallOutletSlope * (Math.Max(0, side - q.Fw) + Math.Max(0, FallJagM + FallBlendM - along) + Math.Max(0, late)) + FallLiftRise * (Sq(Math.Max(0, open)) + Sq(Math.Max(0, late)));
            float cut = along <= FallJagM ? 0 : v - PolyMin(v, notch, FallBlendM), keep = 1 - Grid.Smoothstep(0, FallBlendM, cut);
            v = v - cut + talus * keep * (1 - Grid.Smoothstep(q.Fy - FallBlendM, q.Fy + FallTalusFrac * q.Fd + FallBlendM, v));
            return v + (1 - Grid.Smoothstep(0, 2 * FallBlendM, PoolMargin(q, x, z, f))) * Math.Max(0, top + FallBlendM - v);
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
                    uint s = c.GullySeed + (uint)i * 0x27D4EB2Fu;
                    float n1 = Ridge(Noise.Gradient(s, u / GullyAlongM, v / GullyAcrossM));
                    float n2 = Ridge(Noise.Gradient(s + 7u, 2 * u / GullyAlongM, 1.5f * v / GullyAcrossM));
                    float ch = n1 * n1 + 0.5f * n2 * n2;
                    g += w * (1 - ch * (2f / 1.5f));
                    wsum += w;
                }
                fine += ga * Math.Max(-1f, Math.Min(1f, g / wsum));
            }
            if (ra > 0) fine += ra * Math.Max(-1f, Math.Min(1f, Noise.Fbm(c.RockSeed, x / RockM, z / RockM, 2)));
            float ca = Math.Min(CragMaxM, CragFrac * a) * Grid.Smoothstep(CragGridLo, CragGridHi, slope) * (0.4f + 0.6f * hard);
            if (ca > CragFloorM)
            {
                float local = -BaseSlope(c, x / cm, z / cm, dx, dz) / cm;
                ca = ca * Grid.Smoothstep(CragSlopeLo, CragSlopeHi, local) - CragFloorM;
                if (ca > 0) fine += Crag(c, x, z, b + off, dx, dz, MaterialLibrary.Sedimentary(off), ca);
            }
            return fine;
        }

        static float Crag<T>(T c, float x, float z, float strat, float dx, float dz, float sed, float ca) where T : struct, INearCells
        {
            uint s = c.CragSeed;
            float r = 0;
            if (sed < 1)
            {
                float wu = x / CragWarpWaveM, wv = z / CragWarpWaveM;
                float wx = x + CragWarpM * Noise.Gradient(s + 1u, wu, wv), wz = z + CragWarpM * Noise.Gradient(s + 2u, wu + 31.7f, wv - 17.3f);
                float wsum = 1e-4f, m = 0;
                for (int i = 0; i < 4; i++)
                {
                    float ux = i == 0 ? 1f : i == 2 ? 0f : i == 1 ? 0.70710678f : -0.70710678f;
                    float uz = i == 0 ? 0f : i == 2 ? 1f : 0.70710678f;
                    float dot = ux * dx + uz * dz, w = dot * dot * dot * dot - CragWeightFloor;
                    if (w <= 0) continue;
                    float u = (wx * ux + wz * uz) / CragAlongM, v = (wz * ux - wx * uz) / CragAcrossM;
                    uint k = s + 16u + (uint)i * 0x9E3779B9u;
                    float g = Noise.Gradient(k, u, v) + 0.35f * Noise.Gradient(k + 3u, 2.3f * u, 2.3f * v) + 0.1f * Noise.Gradient(k + 5u, 4.1f * u, 4.1f * v);
                    float p = Grid.Smoothstep(-0.3f, 0.4f, g) * 2f - 1f;
                    float jut = Math.Max(-1f, Math.Min(1f, Noise.Gradient(k + 7u, (wx * ux + wz * uz) / CragJutM, v * (CragAcrossM / CragJutM))));
                    m += w * p * (0.6f + 0.4f * jut);
                    wsum += w;
                }
                r += (1 - sed) * m / wsum;
            }
            if (sed > 0)
            {
                float t = (strat + LedgeWarpM * Noise.Gradient(s + 41u, x / LedgeWarpWaveM, z / LedgeWarpWaveM) + LedgeRagM * Noise.Gradient(s + 43u, x / LedgeRagWaveM, z / LedgeRagWaveM)) / LedgePeriodM;
                float f = t - (float)Math.Floor(t);
                float sharp = 0.7f + 0.1f * Math.Max(-1f, Math.Min(1f, Noise.Gradient(s + 45u, x / LedgeWarpWaveM + 9.1f, z / LedgeWarpWaveM - 4.3f)));
                float g = Grid.Smoothstep(1 - sharp, 1, f);
                float on = Grid.Smoothstep(-0.3f, 0.4f, Noise.Gradient(s + 47u, x / LedgeStrikeM, z / LedgeStrikeM));
                r += sed * on * Math.Min(1f, LedgePeriodM / ca) * (g - f + 0.5f * (1 - sharp));
            }
            return ca * Math.Max(-1f, Math.Min(1f, r));
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

        static float Ridge(float g) => 1 + CreaseSoft - (float)Math.Sqrt(g * g + CreaseSoft * CreaseSoft);

        static float SmoothMax(float a, float b, float k)
        {
            float d = a - b;
            return 0.5f * (a + b + (float)Math.Sqrt(d * d + k * k));
        }

        static float Ramp(float e, float r) => e <= 0 ? 0 : e < r ? e * e / (2f * r) : e - 0.5f * r;

        static float Sq(float a) => a * a;

        static float PolyMin(float a, float b, float k)
        {
            float m = Math.Max(k - Math.Abs(a - b), 0) / k;
            return Math.Min(a, b) - m * m * k * 0.25f;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float d = a - b;
            return 0.5f * (a + b - (float)Math.Sqrt(d * d + k * k));
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

        static float BaseSlope<T>(T c, float gx, float gz, float dx, float dz) where T : struct, INearCells
        {
            int n = c.N, x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz);
            float tx = gx - x0, tz = gz - z0;
            float r0 = RowSlope(c, n, z0 - 1, x0, tx, out float d0), r1 = RowSlope(c, n, z0, x0, tx, out float d1);
            float r2 = RowSlope(c, n, z0 + 1, x0, tx, out float d2), r3 = RowSlope(c, n, z0 + 2, x0, tx, out float d3);
            float a = r1 - r0, d = r2 - r1, e = r3 - r2, da = d1 - d0, dd = d2 - d1, de = d3 - d2;
            float m1 = Tangent(a, d), m2 = Tangent(d, e);
            float dm1 = a * d > 0 ? 2 * (d * d * da + a * a * dd) / ((a + d) * (a + d)) : 0;
            float dm2 = d * e > 0 ? 2 * (e * e * dd + d * d * de) / ((d + e) * (d + e)) : 0;
            float t2 = tz * tz, t3 = t2 * tz;
            float sx = d1 + (t3 - 2 * t2 + tz) * dm1 + (3 * t2 - 2 * t3) * dd + (t3 - t2) * dm2;
            float sz = (3 * t2 - 4 * tz + 1) * m1 + (6 * tz - 6 * t2) * d + (3 * t2 - 2 * tz) * m2;
            return sx * dx + sz * dz;
        }

        static float RowSlope<T>(T c, int n, int z, int x0, float t, out float slope) where T : struct, INearCells
        {
            int r = Clamp(z, n) * n;
            float p0 = c.Height(r + Clamp(x0 - 1, n)), p1 = c.Height(r + Clamp(x0, n)), p2 = c.Height(r + Clamp(x0 + 1, n)), p3 = c.Height(r + Clamp(x0 + 2, n));
            float d = p2 - p1, m1 = Tangent(p1 - p0, d), m2 = Tangent(d, p3 - p2);
            float t2 = t * t, t3 = t2 * t;
            slope = (3 * t2 - 4 * t + 1) * m1 + (6 * t - 6 * t2) * d + (3 * t2 - 2 * t) * m2;
            return p1 + (t3 - 2 * t2 + t) * m1 + (3 * t2 - 2 * t3) * d + (t3 - t2) * m2;
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
