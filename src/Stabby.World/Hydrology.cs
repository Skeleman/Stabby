using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public enum RiverEnd { Sea, Lake, Junction }

    public sealed class Lake
    {
        public int Id;
        public float Level;
        public List<int> Cells = new List<int>();
        public int Outlet = -1;
    }

    public sealed class RiverSegment
    {
        public int Id;
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
    }

    public sealed class Hydrology
    {
        public int[] Receiver;
        public float[] WaterSurface;
        public float[] DischargeM3s;
        public int[] LakeId;
        public List<Lake> Lakes = new List<Lake>();
        public List<RiverSegment> Rivers = new List<RiverSegment>();
        public bool[] IsRiver;

        const float SecondsPerYear = 31557600f;

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
            foreach (var lake in hy.Lakes) RouteToOutlet(n, lake, hy, surface);
            var stack = Drainage.Stack(hy.Receiver, n);
            float cellArea = c.CellMeters * c.CellMeters;
            var runoff = new float[n * n];
            for (int i = 0; i < n * n; i++) runoff[i] = sea[i] ? 0 : cellArea * precipitationMm[i] / 1000f / SecondsPerYear;
            hy.DischargeM3s = Drainage.Accumulate(stack, hy.Receiver, runoff);

            hy.IsRiver = new bool[n * n];
            for (int i = 0; i < n * n; i++)
                hy.IsRiver[i] = !sea[i] && hy.LakeId[i] < 0 && hy.DischargeM3s[i] >= c.RiverMinDischarge;
            BuildSegments(c, sea, hy);
            CarveBeds(c, h, sea, hy);
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

        public static void Relink(WorldConfig c, bool[] sea, Hydrology hy)
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
                if (hy.LakeId[hy.Receiver[i]] != id) lake.Outlet = i;
            }
            BuildSegments(c, sea, hy);
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
            foreach (var seg in hy.Rivers)
            {
                if (seg.End == RiverEnd.Junction) seg.Downstream = startOf[seg.Downstream];
                Shape(cfg, seg, meander);
            }
        }

        static void Add(WorldConfig c, RiverSegment seg, Hydrology hy, int i)
        {
            seg.Cells.Add(i);
            seg.Surface.Add(hy.WaterSurface[i]);
            seg.Discharge.Add(hy.DischargeM3s[i]);
        }

        public static float ReachCells(WorldConfig c, float width) => Math.Max(c.MeanderMaxCells, c.MeanderReachHalfWidths * 0.5f * width / c.CellMeters);

        static void Shape(WorldConfig c, RiverSegment seg, uint seed)
        {
            int n = c.Size, count = seg.Cells.Count;
            var along = new float[count];
            for (int k = 1; k < count; k++)
            {
                int a = seg.Cells[k - 1], b = seg.Cells[k];
                along[k] = along[k - 1] + (a % n != b % n && a / n != b / n ? 1.41421356f : 1f);
            }
            float total = along[count - 1];
            var flats = new float[count];
            for (int k = 0; k < count; k++)
            {
                int lo = Math.Max(0, k - SlopeReach), hi = Math.Min(count - 1, k + SlopeReach);
                float run = (along[hi] - along[lo]) * c.CellMeters;
                float slope = run > 0 ? Math.Max(0, seg.Surface[lo] - seg.Surface[hi]) / run : 0;
                flats[k] = 1f - Grid.Smoothstep(0, c.RiverFlatSlope, slope);
                float width = c.RiverWidthAtUnitFlow * (float)FMath.Pow(seg.Discharge[k], c.RiverWidthExponent) * (1f + c.RiverLowlandWiden * flats[k]);
                seg.Width.Add(width);
                seg.Depth.Add(0.3f + width / 25f);
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
            int head = seg.Cells[0];
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
                float o = len > 0 ? amp * Math.Max(-1f, Math.Min(1f, Noise.Gradient(seed, phase, head * 0.618034f))) / len : 0;
                int cell = seg.Cells[k];
                float ox = sx[k] - dy * o - cell % n, oy = sy[k] + dx * o - cell / n, off = (float)Math.Sqrt(ox * ox + oy * oy);
                if (off > reach) { ox *= reach / off; oy *= reach / off; }
                seg.X.Add(cell % n + ox);
                seg.Y.Add(cell / n + oy);
            }
        }

        static void CarveBeds(WorldConfig c, float[] h, bool[] sea, Hydrology hy)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            foreach (var seg in hy.Rivers)
            {
                for (int k = 0; k + 1 < seg.Cells.Count; k++)
                {
                    float ax = seg.X[k], ay = seg.Y[k], bx = seg.X[k + 1], by = seg.Y[k + 1];
                    float half = Math.Max(seg.Width[k], seg.Width[k + 1]) * 0.5f / cm;
                    float wet = half, reach = half * (1 + c.RiverBankWidths);
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
                            float target;
                            if (d < wet) { float u = d / wet; target = surface - depth * (1 - u * u); }
                            else
                            {
                                float rise = c.RiverBankRise + seg.Width[k] * 0.02f;
                                target = Math.Min(h[i], surface + rise) + (h[i] - Math.Min(h[i], surface + rise)) * Grid.Smoothstep(wet, reach, d);
                            }
                            h[i] = Math.Min(h[i], Math.Max(Erosion.MinLandHeight, target));
                        }
                }
                for (int k = 0; k < seg.Cells.Count; k++)
                {
                    int i = seg.Cells[k];
                    if (sea[i] || hy.LakeId[i] >= 0) continue;
                    h[i] = Math.Min(h[i], Math.Max(Erosion.MinLandHeight, seg.Surface[k] - 0.5f * seg.Depth[k]));
                }
            }
        }

        const int SlopeReach = 3;
        const float MeanderTaperCells = 3f;
        const int SmoothMinCells = 2, SmoothMaxCells = 6;
    }
}
