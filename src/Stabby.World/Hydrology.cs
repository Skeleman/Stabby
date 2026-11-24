using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public enum RiverEnd { Sea, Lake, Junction }

    public sealed class Lake
    {
        public int Id;
        public int Liquid;
        public float Level;
        public List<int> Cells = new List<int>();
        public int Outlet = -1;
        public int Below = -1;
        public float Outflow;
        public WaterMix Mix;
        public List<LakeInflow> Inflows = new List<LakeInflow>();
    }

    public sealed class RiverSegment
    {
        public int Id;
        public int Liquid;
        public List<int> Cells = new List<int>();
        public List<float> Surface = new List<float>();
        public List<float> Discharge = new List<float>();
        public List<float> Width = new List<float>();
        public List<float> Depth = new List<float>();
        public List<float> X = new List<float>();
        public List<float> Y = new List<float>();
        public RiverEnd End;
        public int Downstream = -1;
        public int LakeId = -1;
        public List<RiverFall> Falls = new List<RiverFall>();
        public List<WaterMix> Mix = new List<WaterMix>();
    }

    public struct RiverFall
    {
        public int K;
        public float LipX, LipZ, LipY;
        public float BaseX, BaseZ, BaseY;
        public float Width;
        public float DirX, DirZ;
        public float PoolRadius, PoolCentre;
        public float Discharge;
        public float[] LipPts;
        public float Drop => LipY - BaseY;
        public float ShelfHalf => LipPts == null ? 0 : (LipPts[LipPts.Length - 2] - LipX) * DirZ - (LipPts[LipPts.Length - 1] - LipZ) * DirX;
        public float ShelfBow => LipPts == null ? 0 : (LipPts[LipPts.Length - 2] - LipX) * DirX + (LipPts[LipPts.Length - 1] - LipZ) * DirZ;
        public float SheetHalf => LipPts == null ? 0.5f * Hydrology.SheetWidth(Width) : ShelfHalf;
    }

    public sealed class Hydrology
    {
        public int[] Receiver;
        public float[] WaterSurface;
        public float[] DischargeM3s;
        public int[] LakeId;
        public int[] LakeCover;
        public List<Lake> Lakes = new List<Lake>();
        public List<RiverSegment> Rivers = new List<RiverSegment>();
        public bool[] IsRiver;

        const float SecondsPerYear = 31557600f;

        public static float Runoff(WorldConfig c, bool sea, float precipitationMm) => sea ? 0 : c.CellMeters * c.CellMeters * precipitationMm / 1000f / SecondsPerYear;

        public static Hydrology Build(WorldConfig c, float[] h, bool[] sea, float[] precipitationMm, bool[] keepDry)
        {
            int n = c.Size;
            var hy = new Hydrology();
            var level = Drainage.Fill(h, sea, n, 0f);
            hy.LakeId = new int[n * n];
            for (int i = 0; i < n * n; i++) hy.LakeId[i] = -1;
            FindLakes(c, h, sea, level, keepDry, hy);

            var surface = Drainage.Fill(h, sea, n, 1e-3f);
            hy.WaterSurface = (float[])surface.Clone();
            for (int i = 0; i < n * n; i++)
            {
                if (sea[i]) hy.WaterSurface[i] = 0;
                else if (hy.LakeId[i] >= 0) hy.WaterSurface[i] = hy.Lakes[hy.LakeId[i]].Level;
            }
            hy.Receiver = Drainage.Receivers(surface, sea, n, c.CellMeters);
            foreach (var lake in hy.Lakes)
            {
                RouteToOutlet(n, lake, hy, surface);
                if (lake.Outlet < 0) continue;
                int below = hy.Receiver[lake.Outlet];
                if (sea[below] || surface[below] <= lake.Level) continue;
                lake.Level = surface[below];
                foreach (int i in lake.Cells) hy.WaterSurface[i] = lake.Level;
            }
            var stack = Drainage.Stack(hy.Receiver, n);
            var runoff = new float[n * n];
            for (int i = 0; i < n * n; i++) runoff[i] = Runoff(c, sea[i], precipitationMm[i]);
            hy.DischargeM3s = Drainage.Accumulate(stack, hy.Receiver, runoff);
            foreach (var lake in hy.Lakes) if (lake.Outlet >= 0) { lake.Below = hy.Receiver[lake.Outlet]; lake.Outflow = hy.DischargeM3s[lake.Outlet]; }

            hy.IsRiver = new bool[n * n];
            for (int i = 0; i < n * n; i++)
                hy.IsRiver[i] = !sea[i] && hy.LakeId[i] < 0 && hy.DischargeM3s[i] >= c.RiverMinDischarge;
            BuildSegments(c, sea, hy);
            CarveBeds(c, h, sea, hy);
            Shelves(c, h, sea, hy);
            Cover(c, h, sea, hy);
            return hy;
        }

        static void RouteToOutlet(int n, Lake lake, Hydrology hy, float[] surface)
        {
            float best = float.MaxValue;
            foreach (int i in lake.Cells)
            {
                int r = hy.Receiver[i];
                if (hy.LakeId[r] == lake.Id || r == i) continue;
                if (surface[r] < best || (surface[r] == best && i < lake.Outlet)) { best = surface[r]; lake.Outlet = i; }
            }
            if (lake.Outlet < 0) return;
            var queue = new List<int> { lake.Outlet };
            var reached = new HashSet<int> { lake.Outlet };
            for (int q = 0; q < queue.Count; q++)
            {
                int i = queue[q], x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int j = ny * n + nx;
                    if (hy.LakeId[j] != lake.Id || !reached.Add(j)) continue;
                    hy.Receiver[j] = i;
                    queue.Add(j);
                }
            }
        }

        static void FindLakes(WorldConfig c, float[] h, bool[] sea, float[] level, bool[] keepDry, Hydrology hy)
        {
            int n = c.Size;
            var seen = new bool[n * n];
            var queue = new List<int>();
            for (int s = 0; s < n * n; s++)
            {
                if (seen[s] || sea[s] || level[s] - h[s] <= 1e-4f) continue;
                queue.Clear(); queue.Add(s); seen[s] = true;
                float lv = level[s], maxDepth = 0;
                bool dry = false;
                for (int q = 0; q < queue.Count; q++)
                {
                    int i = queue[q];
                    maxDepth = Math.Max(maxDepth, lv - h[i]);
                    dry |= keepDry[i];
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int j = ny * n + nx;
                        if (seen[j] || sea[j] || level[j] - h[j] <= 1e-4f || level[j] != lv) continue;
                        seen[j] = true; queue.Add(j);
                    }
                }
                if (!dry && maxDepth >= c.LakeMinDepth && queue.Count * c.CellMeters * c.CellMeters >= c.LakeMinAreaM2)
                {
                    var lake = new Lake { Id = hy.Lakes.Count, Level = lv };
                    foreach (int i in queue) { lake.Cells.Add(i); hy.LakeId[i] = lake.Id; }
                    hy.Lakes.Add(lake);
                }
                else
                {
                    foreach (int i in queue) h[i] = lv;
                }
            }
        }

        public static void Relink(WorldConfig c, float[] h, bool[] sea, Hydrology hy)
        {
            int n = c.Size;
            hy.Lakes.Clear();
            hy.Rivers.Clear();
            for (int i = 0; i < n * n; i++)
            {
                int id = hy.LakeId[i];
                if (id < 0) continue;
                while (hy.Lakes.Count <= id) hy.Lakes.Add(new Lake { Id = hy.Lakes.Count });
                var lake = hy.Lakes[id];
                lake.Cells.Add(i);
                lake.Level = hy.WaterSurface[i];
                if (hy.LakeId[hy.Receiver[i]] != id) { lake.Outlet = i; lake.Below = hy.Receiver[i]; lake.Outflow = hy.DischargeM3s[i]; }
            }
            BuildSegments(c, sea, hy);
            Shelves(c, h, sea, hy);
            Cover(c, h, sea, hy);
        }

        static void BuildSegments(WorldConfig cfg, bool[] sea, Hydrology hy)
        {
            int n = cfg.Size;
            var riverDonors = new int[n * n];
            for (int i = 0; i < n * n; i++)
                if (hy.IsRiver[i] && hy.Receiver[i] != i) riverDonors[hy.Receiver[i]]++;

            var startOf = new Dictionary<int, int>();
            var heads = new List<int>();
            for (int i = 0; i < n * n; i++)
                if (hy.IsRiver[i] && riverDonors[i] != 1) heads.Add(i);

            foreach (int head in heads)
            {
                var seg = new RiverSegment { Id = hy.Rivers.Count };
                startOf[head] = seg.Id;
                hy.Rivers.Add(seg);
                int c = head;
                while (true)
                {
                    Add(cfg, seg, hy, c);
                    int r = hy.Receiver[c];
                    if (sea[r]) { Add(cfg, seg, hy, r); seg.End = RiverEnd.Sea; break; }
                    if (hy.LakeId[r] >= 0) { Add(cfg, seg, hy, r); seg.End = RiverEnd.Lake; seg.LakeId = hy.LakeId[r]; break; }
                    if (riverDonors[r] >= 2) { Add(cfg, seg, hy, r); seg.End = RiverEnd.Junction; seg.Downstream = r; break; }
                    c = r;
                }
            }
            uint meander = Rng.Stream(cfg.Seed, "meander").NextU32();
            uint wobble = Rng.Stream(cfg.Seed, "riverwobble").NextU32();
            foreach (var seg in hy.Rivers)
            {
                if (seg.End == RiverEnd.Junction) seg.Downstream = startOf[seg.Downstream];
                Shape(cfg, seg, meander, wobble);
            }
            foreach (var seg in hy.Rivers) FindFalls(cfg, seg, sea, hy);
        }

        static void Add(WorldConfig c, RiverSegment seg, Hydrology hy, int i)
        {
            seg.Cells.Add(i);
            seg.Surface.Add(hy.WaterSurface[i]);
            seg.Discharge.Add(hy.DischargeM3s[i]);
        }

        public const int CoverWingCells = 3;

        static void Cover(WorldConfig c, float[] h, bool[] sea, Hydrology hy)
        {
            int n = c.Size;
            hy.LakeCover = new int[n * n];
            var into = new int[n * n];
            var wing = new int[n * n];
            for (int i = 0; i < n * n; i++) { hy.LakeCover[i] = -1; into[i] = -1; }
            foreach (var s in hy.Rivers)
            {
                int id = DrainsTo(hy, s);
                if (id >= 0) foreach (int i in s.Cells) into[i] = id;
            }
            var queue = new List<int>();
            foreach (var lake in hy.Lakes)
            {
                queue.Clear();
                queue.AddRange(lake.Cells);
                for (int q = 0; q < queue.Count; q++)
                {
                    int i = queue[q], x = i % n, y = i / n;
                    bool open = hy.LakeId[i] < 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int o = ny * n + nx;
                        if (sea[o] || hy.LakeId[o] >= 0 || hy.LakeCover[o] >= 0 || h[o] >= lake.Level) continue;
                        if (into[o] == lake.Id) wing[o] = 0;
                        else if (into[o] < 0 && !hy.IsRiver[o] && open && wing[i] < CoverWingCells) wing[o] = wing[i] + 1;
                        else continue;
                        hy.LakeCover[o] = lake.Id;
                        queue.Add(o);
                    }
                }
            }
        }

        public static int DrainsTo(Hydrology hy, RiverSegment s)
        {
            for (int hop = 0; s.End == RiverEnd.Junction && hop < hy.Rivers.Count; hop++) s = hy.Rivers[s.Downstream];
            return s.End == RiverEnd.Lake ? s.LakeId : -1;
        }

        public static float Stage(WorldConfig c, RiverSegment s, int k)
        {
            float bed = Math.Max(Erosion.MinLandHeight, s.Surface[k] - s.Depth[k]);
            return s.Surface[k] - c.RiverStageFraction * Math.Max(0, s.Surface[k] - bed);
        }

        public static float ReachCells(WorldConfig c, float width) => Math.Max(c.MeanderMaxCells, c.MeanderReachHalfWidths * 0.5f * width / c.CellMeters);

        static void Shape(WorldConfig c, RiverSegment seg, uint seed, uint wobble)
        {
            int n = c.Size, count = seg.Cells.Count;
            var along = new float[count];
            for (int k = 1; k < count; k++)
            {
                int a = seg.Cells[k - 1], b = seg.Cells[k];
                along[k] = along[k - 1] + (a % n != b % n && a / n != b / n ? 1.41421356f : 1f);
            }
            float total = along[count - 1];
            int head = seg.Cells[0];
            float lane = head * 0.618034f;
            var flats = new float[count];
            for (int k = 0; k < count; k++)
            {
                int lo = Math.Max(0, k - SlopeReach), hi = Math.Min(count - 1, k + SlopeReach);
                float run = (along[hi] - along[lo]) * c.CellMeters;
                float slope = run > 0 ? Math.Max(0, seg.Surface[lo] - seg.Surface[hi]) / run : 0;
                flats[k] = 1f - Grid.Smoothstep(0, c.RiverFlatSlope, slope);
                float width = c.RiverWidthAtUnitFlow * (float)FMath.Pow(seg.Discharge[k], c.RiverWidthExponent) * (1f + c.RiverLowlandWiden * flats[k]);
                float swell = along[k] * c.CellMeters / Math.Max(c.CellMeters, c.RiverWobbleWavelengthWidths * width);
                width *= 1f + c.RiverWidthWobble * Math.Max(-1f, Math.Min(1f, Noise.Gradient(wobble, swell, lane)));
                seg.Width.Add(width);
                float depth = Math.Min(c.RiverDepthMin + c.RiverDepthAtUnitFlow * (float)FMath.Pow(seg.Discharge[k], c.RiverDepthExponent), c.RiverDepthPerWidth * width);
                seg.Depth.Add(k > 0 ? Math.Max(depth, seg.Depth[k - 1]) : depth);
            }
            var sx = new float[count];
            var sy = new float[count];
            for (int k = 0; k < count; k++)
            {
                int r = Math.Min(Math.Min(k, count - 1 - k), Math.Max(SmoothMinCells, Math.Min(SmoothMaxCells, (int)(c.RiverSmoothWidths * seg.Width[k] / c.CellMeters))));
                float wx = 0, wy = 0, ws = 0;
                for (int d = -r; d <= r; d++)
                {
                    int cell = seg.Cells[k + d];
                    float wt = r + 1 - Math.Abs(d);
                    wx += wt * (cell % n); wy += wt * (cell / n); ws += wt;
                }
                sx[k] = wx / ws; sy[k] = wy / ws;
            }
            float phase = 0;
            for (int k = 0; k < count; k++)
            {
                float width = seg.Width[k];
                int p = Math.Max(0, k - 2), q = Math.Min(count - 1, k + 2);
                float dx = sx[q] - sx[p], dy = sy[q] - sy[p], len = (float)Math.Sqrt(dx * dx + dy * dy);
                float wave = Math.Max(c.MeanderMinWavelengthCells, c.MeanderWavelengthWidths * width / c.CellMeters);
                if (k > 0) phase += (along[k] - along[k - 1]) / wave;
                float taper = Grid.Smoothstep(0, MeanderTaperCells, along[k]) * Grid.Smoothstep(0, MeanderTaperCells, total - along[k]);
                float reach = ReachCells(c, width);
                float amp = Math.Min(Math.Min(reach, wave * 0.25f), c.MeanderWidths * width / c.CellMeters) * flats[k] * taper;
                float o = len > 0 ? amp * Math.Max(-1f, Math.Min(1f, Noise.Gradient(seed, phase, lane))) / len : 0;
                float jit = along[k] * c.CellMeters / Math.Max(c.CellMeters, c.RiverJitterWavelengthWidths * width);
                o += len > 0 ? c.RiverJitterWidths * width / c.CellMeters * taper * Math.Max(-1f, Math.Min(1f, Noise.Gradient(wobble, jit, lane + 37.1f))) / len : 0;
                int cell = seg.Cells[k];
                float ox = sx[k] - dy * o - cell % n, oy = sy[k] + dx * o - cell / n, off = (float)Math.Sqrt(ox * ox + oy * oy);
                if (off > reach) { ox *= reach / off; oy *= reach / off; }
                seg.X.Add(cell % n + ox);
                seg.Y.Add(cell / n + oy);
            }
        }

        public const float FallMinDrop = 4f, FallPoolCentre = 0.8f, FallBaseOffsetM = 0.3f;
        public const float FallSheetMinM = 12f, FallThrowK = 1.5f;
        public static float SheetWidth(float width) => Math.Max(width, FallSheetMinM);
        public static float FallThrow(float drop) => FallThrowK * (float)Math.Sqrt(2 * Math.Max(drop, 0) / 9.81f);
        const float FallSheetMarginM = 1f;

        static void SheetPool(float half, float drop, ref float r, out float centre)
        {
            float t = FallThrow(drop), h = half + FallSheetMarginM, low = 1 - 0.6f * NearField.FallRadiusNoise;
            r = Math.Max(r, h / low);
            centre = Math.Min(FallPoolCentre * r, t + (float)Math.Sqrt(Math.Max(0, low * low * r * r - h * h)));
        }
        const float FallStepFraction = 0.5f, FallPoolBaseM = 1.5f, FallPoolScale = 0.9f, FallPoolWidths = 0.75f, FallPoolMaxM = 15f, FallLipT = 0.5f, FallLipStepT = 0.125f;
        const int FallSpanCells = 2;

        static void FindFalls(WorldConfig c, RiverSegment seg, bool[] sea, Hydrology hy)
        {
            int count = seg.Cells.Count;
            float cm = c.CellMeters, step = FallStepFraction * FallMinDrop;
            var stage = new float[count];
            for (int k = 0; k < count; k++) stage[k] = Stage(c, seg, k);
            for (int k = 0; k + 1 < count; k++)
            {
                if (stage[k] - stage[k + 1] < step) continue;
                int e = k + 1, big = k;
                while (e + 1 < count && e - k < FallSpanCells && stage[e] - stage[e + 1] >= step)
                {
                    if (stage[e] - stage[e + 1] > stage[big] - stage[big + 1]) big = e;
                    e++;
                }
                float drop = stage[k] - stage[e];
                if (drop >= FallMinDrop)
                {
                    float dx = seg.X[big + 1] - seg.X[big], dz = seg.Y[big + 1] - seg.Y[big], len = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (len > 0) { dx /= len; dz /= len; } else { dx = 1; dz = 0; }
                    float lx = 0, lz = 0, t = FallLipT;
                    for (; t >= 0; t -= FallLipStepT)
                    {
                        lx = (seg.X[big] + t * (seg.X[big + 1] - seg.X[big])) * cm; lz = (seg.Y[big] + t * (seg.Y[big + 1] - seg.Y[big])) * cm;
                        if (!Wet(c, sea, hy, lx, lz)) break;
                    }
                    float baseY = stage[e], bx = lx + dx * FallBaseOffsetM, bz = lz + dz * FallBaseOffsetM;
                    if (Wet(c, sea, hy, bx, bz)) baseY = Math.Max(baseY, hy.WaterSurface[CellAt(c, bx, bz)]);
                    if (t < 0 || stage[k] - baseY < FallMinDrop || InJunction(c, seg, hy, e, lx, lz)) { k = e - 1; continue; }
                    drop = stage[k] - baseY;
                    float q = seg.Discharge[big], w = seg.Width[big];
                    float pool = Math.Min(FallPoolMaxM, Math.Max(FallPoolWidths * w, FallPoolBaseM + FallPoolScale * (float)FMath.Pow(Math.Max(q, 0.01f), 0.25) * (float)Math.Sqrt(drop)));
                    SheetPool(0.5f * SheetWidth(w), drop, ref pool, out float centre);
                    seg.Falls.Add(new RiverFall
                    {
                        K = big,
                        LipX = lx, LipZ = lz, LipY = stage[k],
                        BaseX = bx, BaseZ = bz, BaseY = baseY,
                        Width = w, DirX = dx, DirZ = dz,
                        PoolRadius = pool, PoolCentre = centre, Discharge = q,
                    });
                }
                k = e - 1;
            }
        }

        public const float ShelfMinDrop = 12f, ShelfChance = 0.35f, ShelfHalfPerDrop = 0.3f, ShelfHalfMinM = 10f, ShelfHalfMaxM = 22f, ShelfBowMin = 0.15f, ShelfBowMax = 0.4f, ShelfStepM = 2f, ShelfFlare = 1.5f, ShelfSureDropM = 10f;

        public static void LipAt(in RiverFall f, float s, out float x, out float z)
        {
            x = f.LipX + f.DirZ * s;
            z = f.LipZ - f.DirX * s;
            var p = f.LipPts;
            if (p == null) return;
            float prev = Side(f, p[0], p[1]);
            for (int k = 2; k < p.Length; k += 2)
            {
                float next = Side(f, p[k], p[k + 1]);
                if (s <= next || k + 2 == p.Length)
                {
                    float t = Math.Max(0, Math.Min(1, (s - prev) / (next - prev)));
                    x = p[k - 2] + (p[k] - p[k - 2]) * t;
                    z = p[k - 1] + (p[k + 1] - p[k - 1]) * t;
                    return;
                }
                prev = next;
            }
        }

        static float Side(in RiverFall f, float x, float z) => (x - f.LipX) * f.DirZ - (z - f.LipZ) * f.DirX;

        static void Shelves(WorldConfig c, float[] h, bool[] sea, Hydrology hy)
        {
            var rng = Rng.Stream(c.Seed, "fall-shelf");
            foreach (var seg in hy.Rivers)
                for (int i = 0; i < seg.Falls.Count; i++)
                {
                    var f = seg.Falls[i];
                    if (f.Drop < ShelfMinDrop) continue;
                    float roll = rng.NextFloat(), half = Math.Max(ShelfHalfMinM, Math.Min(ShelfHalfMaxM, ShelfHalfPerDrop * f.Drop)), bow = half * rng.Range(ShelfBowMin, ShelfBowMax);
                    if (roll >= ShelfChance + (1 - ShelfChance) * Math.Min(1f, (f.Drop - ShelfMinDrop) / ShelfSureDropM)) continue;
                    float r = f.PoolRadius;
                    SheetPool(0, f.Drop, ref r, out float centre);
                    float reach = NearField.PoolReach(centre, r);
                    if (Crowded(c, hy, seg, i, half + reach) || Upstream(c, seg, i) < ShelfFlare * half + 2 * c.CellMeters) continue;
                    int steps = (int)Math.Ceiling(half / ShelfStepM), count = 2 * steps + 1;
                    var pts = new float[2 * count];
                    bool ok = true;
                    float t = FallThrow(f.Drop);
                    for (int k = 0; k < count && ok; k++)
                    {
                        float s = half * (k - steps) / steps, a = bow * (s / half) * (s / half);
                        float x = f.LipX + f.DirZ * s + f.DirX * a, z = f.LipZ - f.DirX * s + f.DirZ * a;
                        pts[2 * k] = x;
                        pts[2 * k + 1] = z;
                        float fx = x + f.DirX * t, fz = z + f.DirZ * t, cx = x + f.DirX * centre, cz = z + f.DirZ * centre;
                        ok = !Wet(c, sea, hy, x, z) && !Wet(c, sea, hy, fx, fz) && !Wet(c, sea, hy, cx, cz) && h[CellAt(c, cx, cz)] <= f.LipY - 0.5f * f.Drop;
                    }
                    if (!ok) continue;
                    f.LipPts = pts;
                    f.PoolRadius = r;
                    f.PoolCentre = centre;
                    seg.Falls[i] = f;
                }
            Islands(c, hy);
        }

        public const float IslandChance = 0.95f, IslandTwoChance = 0.45f, IslandSpread = 0.3f, IslandPair = 0.5f;
        public const int IslandGapMin = 2, IslandGapMax = 3, IslandPairGap = 1, IslandRunMin = 3, IslandTwoPts = 11;

        static void Islands(WorldConfig c, Hydrology hy)
        {
            var rng = Rng.Stream(c.Seed, "fall-island");
            foreach (var seg in hy.Rivers)
                for (int i = 0; i < seg.Falls.Count; i++)
                {
                    var f = seg.Falls[i];
                    if (f.LipPts == null) continue;
                    float roll = rng.NextFloat(), two = rng.NextFloat(), p0 = rng.NextFloat(), p1 = rng.NextFloat();
                    int k0 = rng.Range(IslandGapMin, IslandGapMax + 1);
                    if (roll >= IslandChance) continue;
                    int count = f.LipPts.Length / 2, mid = count / 2;
                    var drop = new bool[count];
                    if (count >= IslandTwoPts && two < IslandTwoChance)
                    {
                        int off = Math.Max((IslandRunMin + 1) / 2, Math.Min(mid - IslandRunMin, (int)Math.Round(mid * (IslandPair + IslandSpread * (p0 - 0.5f)))));
                        Gap(drop, mid - off, IslandPairGap);
                        Gap(drop, mid + off, IslandPairGap);
                    }
                    else
                    {
                        int lo = IslandRunMin + k0 / 2, hi = count - IslandRunMin - k0 + k0 / 2;
                        Gap(drop, Math.Max(lo, Math.Min(hi, mid + (int)Math.Round(mid * IslandSpread * (2 * p1 - 1)))), k0);
                    }
                    if (!Runs(drop, IslandRunMin)) continue;
                    var pts = new List<float>();
                    for (int k = 0; k < count; k++)
                        if (!drop[k]) { pts.Add(f.LipPts[2 * k]); pts.Add(f.LipPts[2 * k + 1]); }
                    f.LipPts = pts.ToArray();
                    seg.Falls[i] = f;
                }
        }

        static void Gap(bool[] drop, int centre, int k)
        {
            for (int j = 0; j < k; j++)
            {
                int at = centre - k / 2 + j;
                if (at >= 0 && at < drop.Length) drop[at] = true;
            }
        }

        static bool Runs(bool[] drop, int min)
        {
            int run = 0;
            for (int k = 0; k <= drop.Length; k++)
            {
                if (k < drop.Length && !drop[k]) { run++; continue; }
                if (run < min) return false;
                run = 0;
                while (k + 1 < drop.Length && drop[k + 1]) k++;
            }
            return true;
        }

        public static List<float> LipRuns(in RiverFall f)
        {
            var runs = new List<float>();
            var p = f.LipPts;
            if (p == null) { runs.Add(-f.SheetHalf); runs.Add(f.SheetHalf); return runs; }
            float step = float.MaxValue;
            for (int k = 2; k < p.Length; k += 2) step = Math.Min(step, Side(f, p[k], p[k + 1]) - Side(f, p[k - 2], p[k - 1]));
            float start = Side(f, p[0], p[1]), prev = start;
            for (int k = 2; k < p.Length; k += 2)
            {
                float s = Side(f, p[k], p[k + 1]);
                if (s - prev > IslandGapStep * step) { runs.Add(start); runs.Add(prev); start = s; }
                prev = s;
            }
            runs.Add(start);
            runs.Add(prev);
            return runs;
        }

        public const float IslandGapStep = 1.5f;

        static float Upstream(WorldConfig c, RiverSegment seg, int fall)
        {
            int from = fall > 0 ? seg.Falls[fall - 1].K + 1 : 0;
            float run = 0;
            for (int k = from; k < seg.Falls[fall].K; k++)
            {
                float dx = seg.X[k + 1] - seg.X[k], dz = seg.Y[k + 1] - seg.Y[k];
                run += (float)Math.Sqrt(dx * dx + dz * dz) * c.CellMeters;
            }
            return run;
        }

        static bool Crowded(WorldConfig c, Hydrology hy, RiverSegment own, int fall, float r)
        {
            var f = own.Falls[fall];
            float cm = c.CellMeters, r2 = r * r;
            foreach (var seg in hy.Rivers)
            {
                if (seg != own)
                    for (int k = 0; k < seg.X.Count; k++)
                    {
                        float dx = seg.X[k] * cm - f.LipX, dz = seg.Y[k] * cm - f.LipZ;
                        if (dx * dx + dz * dz < r2) return true;
                    }
            }
            for (int j = 0; j < own.Falls.Count; j++)
            {
                if (j == fall) continue;
                float dx = own.Falls[j].LipX - f.LipX, dz = own.Falls[j].LipZ - f.LipZ;
                if (dx * dx + dz * dz < 4 * r2) return true;
            }
            return false;
        }

        static bool InJunction(WorldConfig c, RiverSegment seg, Hydrology hy, int e, float x, float z)
        {
            int last = seg.Cells.Count - 1;
            if (seg.End != RiverEnd.Junction || e != last) return false;
            float hw = 0.5f * hy.Rivers[seg.Downstream].Width[0], dx = seg.X[last] * c.CellMeters - x, dz = seg.Y[last] * c.CellMeters - z, r = hw + NearField.Bank(hw);
            return dx * dx + dz * dz < r * r;
        }

        static int CellAt(WorldConfig c, float x, float z)
        {
            int n = c.Size;
            int cx = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(x / c.CellMeters + 0.5f))), cz = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(z / c.CellMeters + 0.5f)));
            return cz * n + cx;
        }

        static bool Wet(WorldConfig c, bool[] sea, Hydrology hy, float x, float z)
        {
            int i = CellAt(c, x, z);
            return sea[i] || hy.LakeId[i] >= 0;
        }

        static void CarveBeds(WorldConfig c, float[] h, bool[] sea, Hydrology hy)
        {
            int n = c.Size;
            var cut = (float[])h.Clone();
            var fill = new float[n * n];
            var wetBed = new float[n * n];
            for (int i = 0; i < fill.Length; i++) { fill[i] = float.MinValue; wetBed[i] = float.MaxValue; }
            foreach (var seg in hy.Rivers)
                for (int k = 0; k + 1 < seg.Cells.Count; k++)
                {
                    float ax = seg.X[k], ay = seg.Y[k], bx = seg.X[k + 1], by = seg.Y[k + 1];
                    float width = Math.Max(seg.Width[k], seg.Width[k + 1]);
                    float wet = width * 0.5f / c.CellMeters, reach = wet * (1 + c.RiverBankWidths), peak = wet + (reach - wet) * c.RiverLeveePeak;
                    float rise = c.RiverBankRise + width * c.RiverBankRisePerWidth;
                    int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - reach)), x1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(ax, bx) + reach));
                    int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - reach)), y1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(ay, by) + reach));
                    float ex = bx - ax, ey = by - ay, ee = ex * ex + ey * ey;
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = y * n + x;
                            if (sea[i] || hy.LakeId[i] >= 0) continue;
                            float t = ee > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * ex + (y - ay) * ey) / ee)) : 0;
                            float px = ax + ex * t - x, py = ay + ey * t - y, d = (float)Math.Sqrt(px * px + py * py);
                            if (d >= reach) continue;
                            float surface = seg.Surface[k] + (seg.Surface[k + 1] - seg.Surface[k]) * t;
                            float depth = seg.Depth[k] + (seg.Depth[k + 1] - seg.Depth[k]) * t;
                            float top = surface + rise;
                            if (d < wet) { float u = d / wet, u2 = u * u; wetBed[i] = Math.Min(wetBed[i], surface - depth * (1 - u2 * u2)); }
                            else if (d < peak)
                            {
                                float face = surface + rise * Grid.Smoothstep(wet, peak, d);
                                cut[i] = Math.Min(cut[i], face);
                                fill[i] = Math.Max(fill[i], face);
                            }
                            else
                            {
                                float s = Grid.Smoothstep(peak, reach, d);
                                cut[i] = Math.Min(cut[i], Math.Min(h[i], top) + (h[i] - Math.Min(h[i], top)) * s);
                                fill[i] = Math.Max(fill[i], top - (top - Math.Min(h[i], top)) * s);
                            }
                        }
                }
            for (int i = 0; i < h.Length; i++)
            {
                if (sea[i] || hy.LakeId[i] >= 0) continue;
                float v = wetBed[i] != float.MaxValue ? Math.Min(h[i], wetBed[i]) : Math.Max(cut[i], fill[i]);
                h[i] = Math.Max(Erosion.MinLandHeight, v);
            }
            foreach (var seg in hy.Rivers)
                for (int k = 0; k < seg.Cells.Count; k++)
                {
                    int i = seg.Cells[k];
                    if (sea[i] || hy.LakeId[i] >= 0) continue;
                    h[i] = Math.Min(h[i], Math.Max(Erosion.MinLandHeight, seg.Surface[k] - 0.5f * seg.Depth[k]));
                }
        }

        const int SlopeReach = 3;
        const float MeanderTaperCells = 3f;
        const int SmoothMinCells = 3, SmoothMaxCells = 12;
    }
}
