using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct RiverPiece
    {
        public float Ax, Az, Bx, Bz, Sa, Sb, Wa, Wb, Da, Db, Ka, Kb, Pa, Pb, Ra, Rb;
        public float Lx, Lz, Ux, Uz, Fy, Fr, Fd, Fc, Ft, Fh, Fs, Fb, Fw, Ia, Ib, Ja, Jb, Iy;
    }

    public struct PieceMask
    {
        ulong a, b, c, d;
        bool all;

        public PieceMask(int count) { a = b = c = d = 0; all = count > 256; }

        public void Set(int i)
        {
            ulong bit = 1UL << (i & 63);
            if (i < 64) a |= bit; else if (i < 128) b |= bit; else if (i < 192) c |= bit; else if (i < 256) d |= bit;
        }

        public bool Has(int i) => all || ((i < 64 ? a : i < 128 ? b : i < 192 ? c : d) >> (i & 63) & 1UL) != 0;
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
        public const float MacroM = 180f, GullyAlongM = 40f, GullyAcrossM = 14f, GullyDepthM = 2.5f, RockM = 10f, ShoreBlendM = 0.25f, ShoreTangentCap = 8f, CreaseSoft = 0.25f, WallSlope = 1f, WallLift = 1e4f, CutSoft = 1.5f, WallRampM = 4f;
        public const float BankMinM = 3f, BankPerWidth = 0.75f, BankRiseM = 1f, BankRisePerWidth = 0.05f, BankFadeMinM = 30f, BankFadeCells = 0.75f, MaskPadM = 0.5f;
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
            int n = w.Config.Size;
            var spill = new Dictionary<int, Lake>();
            foreach (var lake in w.Water.Lakes) if (lake.Outlet >= 0) spill[lake.Below] = lake;
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
                if (spill.TryGetValue(r.Cells[0], out var from0))
                {
                    int o = from0.Outlet;
                    px.Insert(0, (r.X[0] + OutletReach * (o % n - r.X[0])) * cm); pz.Insert(0, (r.Y[0] + OutletReach * (o / n - r.Y[0])) * cm);
                    ps.Insert(0, Math.Max(ps[0], from0.Level)); pw.Insert(0, pw[0]); pd.Insert(0, pd[0]);
                    foreach (var t in tint) t.Insert(0, t[0]);
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
                var coast = new bool[px.Count];
                for (int k = 0; k < px.Count; k++) coast[k] = Hydrology.NearSea(w.Config, w.Sea, line.X[k], line.Z[k]);
                Steps(line, cm, coast, ref rng);
                for (int k = 0; k < px.Count; k++)
                    if (!w.Sea[CellOf(line.X[k], line.Z[k], cm, w.Config.Size)]) line.S[k] = Math.Max(line.S[k], 0.5f * Erosion.MinLandHeight);
                lines.Add(line);
            }
            return lines;
        }

        public const float FallDepthFrac = 0.5f, FallDepthBaseM = 0.8f, FallDepthPerDrop = 0.15f;
        public static float PoolReach(float c, float r) => c + (1 + FallRimFrac) * r + Math.Max(FallRingMinM, FallRingFrac * r) + 1f;
        public const float FallRadiusNoise = 0.25f, FallBowlNoise = 0.2f, FallBowlWaveM = 1.7f, FallRimWaveFrac = 0.7f, FallRimWaveM = 3f, FallRimHeight = 0.35f, FallRimCap = 0.15f, FallTalusWaveM = 2.5f, FallLowFrac = 1.3f;
        public const float FallTrimM = 0.1f, FallJagM = 0.6f, FallJagWaveM = 2.5f, FallRimFrac = 0.35f, FallRingMinM = 3f, FallRingFrac = 0.5f, FallLiftRise = 4f, FallBlendM = 1f, FallTalusFrac = 0.35f, FallSlumpFrac = 0.4f, FallClearM = 15f, FallOutletSlope = 1.2f, FallOutletFrac = 0.3f, FallOutletRise = 0.1f, FallFloorM = 0.02f, SurfaceFloorM = 0.02f, MouthDepthM = 0.8f, ShelfPadM = 2f, OutletReach = 1f, OutletWidth = 0.5f;

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

        static void Steps(RiverLine l, float cm, bool[] coast, ref Rng rng)
        {
            int m = l.X.Length;
            var along = Along(l);
            var mask = (bool[])coast.Clone();
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
            var lines = Lines(w);
            foreach (var l in lines)
            {
                starts?.Add(list.Count);
                var along = Along(l);
                int lead = 0;
                while (lead < l.X.Length && Lake(w, n, cm, l.X[lead], l.Z[lead])) lead++;
                for (int k = 0; k + 1 < l.X.Length; k++)
                {
                    if (k >= lead && (Lake(w, n, cm, l.X[k], l.Z[k]) || Lake(w, n, cm, l.X[k + 1], l.Z[k + 1]))) continue;
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
            var hy = w.Water;
            foreach (var lake in hy.Lakes)
            {
                if (lake.Outlet < 0 || lake.Outflow < w.Config.RiverMinDischarge) continue;
                int below = lake.Below;
                for (int r = 0, i = 0; r < hy.Rivers.Count; r++)
                {
                    var cells = hy.Rivers[r].Cells;
                    if (cells.Count < 2) continue;
                    var l = lines[i++];
                    if (cells[0] == below || !cells.Contains(below)) continue;
                    float ox = lake.Outlet % n * cm, oz = lake.Outlet / n * cm, bx = below % n * cm, bz = below / n * cm, near = float.MaxValue;
                    int j = 0;
                    for (int k = 0; k < l.X.Length; k++)
                    {
                        float dd = (l.X[k] - bx) * (l.X[k] - bx) + (l.Z[k] - bz) * (l.Z[k] - bz);
                        if (dd < near) { near = dd; j = k; }
                    }
                    starts?.Add(list.Count);
                    list.Add(new RiverPiece
                    {
                        Ax = ox, Az = oz, Bx = l.X[j], Bz = l.Z[j], Sa = Math.Max(lake.Level, l.S[j]), Sb = l.S[j],
                        Wa = OutletWidth * l.W[j], Wb = OutletWidth * l.W[j], Da = l.D[j], Db = l.D[j], Ka = l.K[j], Kb = l.K[j], Pa = l.P[j], Pb = l.P[j], Ra = l.R[j], Rb = l.R[j]
                    });
                    break;
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

        static bool Lake(GeneratedWorld w, int n, float cm, float x, float z)
        {
            int i = CellOf(x, z, cm, n);
            return w.Water.LakeId[i] >= 0;
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
            bool near = false, falls = false, isles = false;
            PieceMask mask = new PieceMask(s1 - s0), hold = mask;
            for (int k = s0; k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                falls |= q.Fr > 0;
                isles |= q.Ja > 0;
                if (q.Fr > 0 && PoolZone(q, x, z, f)) near = true;
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d < wb + f + MaskPadM) mask.Set(k - s0);
                if (d < wb + Math.Min(Bank(w), f) + MaskPadM) hold.Set(k - s0);
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
            if (!near) return isles ? Isles(c, s0, s1, x, z, h) : h;
            if (falls) h = Carve(c, s0, s1, x, z, h, f);

            float raised = h, sea = Grid.Smoothstep(0.75f, 1, Lerp2(SeaWeight(c.Height(i00)), SeaWeight(c.Height(i10)), SeaWeight(c.Height(i01)), SeaWeight(c.Height(i11)), tx, tz));
            for (int k = s0; k < s1; k++)
            {
                if (!mask.Has(k - s0)) continue;
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                if (d >= wb + f) continue;
                float s = q.Sa + (q.Sb - q.Sa) * t, top = SmoothMax(h, s + Rise(w), 1f);
                float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, el = (float)Math.Sqrt(ex * ex + ez * ez), along = el > 1e-3f ? ((x - q.Ax) * ex + (z - q.Az) * ez) / el : 0;
                float cap = Grid.Smoothstep(0, f, Math.Max(along - el, -along)) * sea;
                raised = Math.Max(raised, h + (top - h) * (1 - Grid.Smoothstep(wb, wb + f, d)) * (falls ? 1 - Gorge(c, s0, s1, x, z, s, f) : 1) * Grid.Smoothstep(0, Erosion.MinLandHeight, s) * (1 - cap) * (1 - sea * (1 - Grid.Smoothstep(2 * Erosion.MinLandHeight, 4 * Erosion.MinLandHeight, s))));
            }
            float cut = float.MaxValue, pool = float.MaxValue, bank = 1, off = 1, rim = 1, banked = 0, low = Math.Min(h, 0.5f * Erosion.MinLandHeight), land = low;
            Span<float> hs = stackalloc float[HoldCap], ha = stackalloc float[HoldCap], hm = stackalloc float[HoldCap], hp = stackalloc float[HoldCap];
            int hn = -1;
            bool hasJag = false, hasNb = false, hasRough = false, hasBoulder = false;
            float jag = 0, nb = 0, rough = 0, boulderNoise = 0;
            for (int k = s0; k < s1; k++)
            {
                if (!falls && !mask.Has(k - s0)) continue;
                var q = c.Piece(c.PieceAt(k));
                if (q.Fr > 0 && PoolZone(q, x, z, f)) low = Math.Min(low, Math.Max(FallFloorM, 0.5f * Math.Min(q.Fy, Erosion.MinLandHeight)));
                if (falls && q.Fr <= 0)
                {
                    Project(q, x, z, out float d0, out float t0);
                    float w0 = q.Wa + (q.Wb - q.Wa) * t0, b0 = Bank(w0), ex0 = q.Bx - q.Ax, ez0 = q.Bz - q.Az, l0 = (float)Math.Sqrt(ex0 * ex0 + ez0 * ez0);
                    float a0 = l0 > 1e-3f ? ((x - q.Ax) * ex0 + (z - q.Az) * ez0) / l0 : 0, inside = Grid.Smoothstep(0, FallBlendM, a0) * Grid.Smoothstep(0, FallBlendM, l0 - a0);
                    off = Math.Min(off, Grid.Smoothstep(w0, w0 + b0, d0));
                    rim = Math.Min(rim, 1 - inside * (1 - Grid.Smoothstep(w0 + b0, w0 + b0 + FallBlendM, d0)));
                }
                if (!mask.Has(k - s0)) continue;
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w);
                bank = Math.Min(bank, Grid.Smoothstep(w, wb, d));
                if (q.Fr <= 0 && d < wb + Bank(w)) banked = 1;
                if (d >= wb + f) continue;
                float gate = 1;
                if (!(q.Fr > 0 || (q.Ux == 0 && q.Uz == 0)))
                {
                    if (!hasJag) { jag = Jag(c, x, z); hasJag = true; }
                    gate = Grid.Smoothstep(-FallTrimM - Math.Max(0, d - w), 0, ShearAlong(q, x, z) - jag);
                }
                float s = q.Sa + (q.Sb - q.Sa) * t, depth = q.Da + (q.Db - q.Da) * t, top = SmoothMax(h, s + Rise(w), 1f), p;
                float ex = q.Bx - q.Ax, ez = q.Bz - q.Az, el = (float)Math.Sqrt(ex * ex + ez * ez);
                float lat = el > 1e-3f ? ((z - q.Az) * ex - (x - q.Ax) * ez) / el : 0;
                float bend = q.Ka + (q.Kb - q.Ka) * t, inner = Math.Max(-1f, Math.Min(1f, bend * lat / w));
                float bar = Math.Min(BarLiftFrac * depth, 0.5f * Rise(w)) * Grid.Smoothstep(0.3f, 1f, inner);
                if (d < w)
                {
                    float bed = 1 - d * d / (w * w), plunge = q.Pa + (q.Pb - q.Pa) * t, rapid = q.Ra + (q.Rb - q.Ra) * t;
                    if (!hasRough) { rough = 1 + BedNoise * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed + 2u, x / BedNoiseM, z / BedNoiseM))); hasRough = true; }
                    if (rapid > 0 && !hasBoulder) { boulderNoise = Grid.Smoothstep(-0.2f, 0.8f, Noise.Gradient(c.RiverSeed + 3u, x / BoulderM, z / BoulderM)); hasBoulder = true; }
                    float boulder = rapid > 0 ? rapid * BoulderFrac * depth * boulderNoise : 0;
                    float mouth = MouthDepthM * bed * (1 - Grid.Smoothstep(0.5f * Erosion.MinLandHeight, Erosion.MinLandHeight, s));
                    p = Math.Max(s - bed * (depth * (1 - BarSkew * inner) * rough + plunge - boulder) + bar * (1 - bed), 0.5f * Math.Min(s, Erosion.MinLandHeight) - mouth);
                    low = Math.Min(low, land + (Math.Max(SurfaceFloorM, 0.5f * Math.Min(s, Erosion.MinLandHeight)) - mouth - land) * (1 - Grid.Smoothstep(0.5f * w, w, d)));
                }
                else
                {
                    if (hn == -1) hn = Held(c, s0, s1, hold, x, z, h, f, hs, ha, hm, hp);
                    top = hn < 0 ? Math.Max(top, Hold(c, s0, s1, hold, x, z, h, s, f)) : Hold(hs, ha, hm, hp, hn, h, s, top);
                    if (!hasNb) { nb = 0.5f + 0.5f * Math.Max(-1f, Math.Min(1f, Noise.Gradient(c.RiverSeed, x / BankNoiseM, z / BankNoiseM) + 0.5f * Noise.Gradient(c.RiverSeed + 1u, 2.3f * x / BankNoiseM, 2.3f * z / BankNoiseM))); hasNb = true; }
                    wb = w + Math.Max(Bank(w) * (1 - CutShrink * Math.Max(0, -inner)) * (1 - BankNoise * nb), Math.Min(Bank(w), (top - s - bar) / MaxBankSlope));
                    float r = Math.Min(WallRampM, 2f * (wb - w));
                    float u = d < wb ? (d - w) / (wb - w) : 1, ss = Grid.Smoothstep(0, 1, u), shape = ss + (u * u - ss) * Math.Max(0, inner);
                    p = (d < wb ? s + bar + (top - s - bar) * shape : top) + (WallSlope + 2f * StageGradient(q)) * Ramp(d - wb + 0.5f * r, r);
                    if (d >= wb) p += WallLift * Grid.Smoothstep(wb + 0.5f * f, wb + f, d);
                }
                cut = Math.Min(cut, p + (1 - gate) * Math.Max(0, raised + FallClearM - p));
            }
            for (int k = s0; falls && k < s1; k++)
            {
                var q = c.Piece(c.PieceAt(k));
                if (q.Fr <= 0) continue;
                float v = Pool(c, q, x, z, raised, out float carve), reach = PoolReach(q.Fc, q.Fr), along = (x - q.Lx) * q.Ux + (z - q.Lz) * q.Uz;
                float past = Math.Max(Grid.Smoothstep(reach, reach + 0.5f * f, along), banked * bank * (1 - Grid.Smoothstep(-FallBlendM, 0, PoolCircle(q, x, z))));
                v += Math.Min(carve, Math.Max(0, raised + FallBlendM - v)) * Grid.Smoothstep(0.5f, 1f, Math.Min(bank, off)) * (1 - rim) * (1 - Grid.Smoothstep(-FallBlendM, 0, PoolEdge(q, x, z)));
                pool = Math.Min(pool, cut < float.MaxValue ? v + past * Math.Max(0, cut - v) : v);
            }
            float ground = Math.Max(PolyMin(SmoothMin(raised, cut, CutSoft), pool, FallBlendM), low);
            return isles ? Isles(c, s0, s1, x, z, ground) : ground;
        }

        static float Hold<T>(T c, int s0, int s1, PieceMask mask, float x, float z, float h, float s, float f) where T : struct, INearCells
        {
            float top = h;
            for (int k = s0; k < s1; k++)
            {
                if (!mask.Has(k - s0)) continue;
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

        const int HoldCap = 256;

        static int Held<T>(T c, int s0, int s1, PieceMask mask, float x, float z, float h, float f, Span<float> hs, Span<float> ha, Span<float> hm, Span<float> hp) where T : struct, INearCells
        {
            int n = Gather(c, s0, s1, mask, x, z, h, f, hs, ha);
            if (n <= 0) return n;
            for (int i = 1; i < n; i++)
            {
                float ks = hs[i], ka = ha[i];
                int j = i - 1;
                for (; j >= 0 && hs[j] > ks; j--) { hs[j + 1] = hs[j]; ha[j + 1] = ha[j]; }
                hs[j + 1] = ks; ha[j + 1] = ka;
            }
            hm[n - 1] = ha[n - 1];
            for (int i = n - 2; i >= 0; i--) hm[i] = Math.Max(hm[i + 1], ha[i]);
            hp[0] = ha[0];
            for (int i = 1; i < n; i++) hp[i] = Math.Max(hp[i - 1], ha[i]);
            return n;
        }

        static int Gather<T>(T c, int s0, int s1, PieceMask mask, float x, float z, float h, float f, Span<float> hs, Span<float> ha) where T : struct, INearCells
        {
            int n = 0;
            for (int k = s0; k < s1; k++)
            {
                if (!mask.Has(k - s0)) continue;
                var q = c.Piece(c.PieceAt(k));
                Project(q, x, z, out float d, out float t);
                float w = q.Wa + (q.Wb - q.Wa) * t, wb = w + Bank(w), reach = Math.Min(Bank(w), f);
                if (d >= wb + reach) continue;
                float sq = q.Sa + (q.Sb - q.Sa) * t, a = Math.Max(0, sq + Rise(w) - h) * (1 - Grid.Smoothstep(wb, wb + reach, d));
                if (a == 0) continue;
                if (n == HoldCap) return -2;
                hs[n] = sq;
                ha[n++] = a;
            }
            return n;
        }

        static float Hold(Span<float> hs, Span<float> ha, Span<float> hm, Span<float> hp, int n, float h, float s, float top)
        {
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (hs[mid] <= s) lo = mid + 1; else hi = mid;
            }
            int e = n;
            hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (hs[mid] - s >= HoldStageM) e = hi = mid; else lo = mid + 1;
            }
            top = Math.Max(top, h);
            if (e < n) top = Math.Max(top, h + hm[e]);
            for (int i = e - 1; i >= 0 && hs[i] > s; i--)
            {
                float g = Grid.Smoothstep(0, HoldStageM, hs[i] - s);
                if (h + hp[i] * g <= top) break;
                top = Math.Max(top, h + ha[i] * g);
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
            return Math.Max(PoolCircle(q, x, z), Math.Min(Math.Min(along - FallJagM, PoolReach(q.Fc, q.Fr) + f - along), q.Fw + OutletRing(q.Fr) + FallBlendM - side));
        }

        static float PoolCircle(RiverPiece q, float x, float z)
        {
            Frame(q, x, z, out float sa, out float ss);
            return PoolReach(q.Fc, q.Fr) - q.Fc - PoolDist(q, sa, ss);
        }

        static float PoolEdge(RiverPiece q, float x, float z)
        {
            Frame(q, x, z, out float sa, out float ss);
            return q.Fr * (1 + FallRadiusNoise) - PoolDist(q, sa, ss);
        }

        static float OutletRing(float r) => Math.Max(FallRingMinM, FallRingFrac * r);

        public static bool InPoolWater(RiverPiece q, float x, float z)
        {
            Frame(q, x, z, out float along, out float side);
            return q.Fr > 0 && PoolDist(q, along, side) < q.Fr * (1 + FallRadiusNoise);
        }

        static float Pool<T>(T c, RiverPiece q, float x, float z, float top, out float carve) where T : struct, INearCells
        {
            carve = 0;
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
            carve = cut;
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

        static float SeaWeight(float height) => 1 - Grid.Smoothstep(-1.8f * Erosion.MinLandHeight, -1.2f * Erosion.MinLandHeight, height);

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
            const float e = 1f / 64f;
            return (Base(c, gx + e * dx, gz + e * dz) - Base(c, gx - e * dx, gz - e * dz)) / (2 * e);
        }

        static float Row<T>(T c, int n, int z, int x0, float t) where T : struct, INearCells
        {
            int r = Clamp(z, n) * n;
            return Hermite(c.Height(r + Clamp(x0 - 1, n)), c.Height(r + Clamp(x0, n)), c.Height(r + Clamp(x0 + 1, n)), c.Height(r + Clamp(x0 + 2, n)), t);
        }

        static float Tangent(float p0, float p1, float p2)
        {
            float d0 = p1 - p0, d1 = p2 - p1;
            if (d0 * d1 <= 0) return 0;
            float harm = 2 * d0 * d1 / (d0 + d1);
            float cross = Math.Min(Math.Max(p0, Math.Max(p1, p2)), -Math.Min(p0, Math.Min(p1, p2)));
            if (cross <= 0) return harm;
            float step = Math.Min(Math.Abs(d0), Math.Abs(d1));
            float w = Math.Min(1f, cross / Math.Max(ShoreBlendM, step));
            float lim = ShoreTangentCap * step, mean = 0.5f * (d0 + d1);
            return harm + w * (Math.Max(-lim, Math.Min(lim, mean)) - harm);
        }

        static float Hermite(float p0, float p1, float p2, float p3, float t)
        {
            float d = p2 - p1, m1 = Tangent(p0, p1, p2), m2 = Tangent(p1, p2, p3);
            float t2 = t * t, t3 = t2 * t;
            return p1 + (t3 - 2 * t2 + t) * m1 + (3 * t2 - 2 * t3) * d + (t3 - t2) * m2;
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;
    }
}
