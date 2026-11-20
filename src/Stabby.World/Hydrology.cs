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

    // One river reach between a source or confluence and the next confluence,
    // lake or sea. Points are cell indices; the last point is shared with the
    // downstream reach (or is the lake/sea cell it flows into).
    public sealed class RiverSegment
    {
        public int Id;
        public List<int> Cells = new List<int>();
        public List<float> Surface = new List<float>();
        public List<float> Discharge = new List<float>();
        public List<float> Width = new List<float>();
        public List<float> Depth = new List<float>();
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

        // Fills pits too small to be lakes into the terrain itself, so terrain
        // and water agree before anything is extracted.
        public static Hydrology Build(WorldConfig c, float[] h, bool[] sea, float[] precipitationMm)
        {
            int n = c.Size;
            var hy = new Hydrology();
            var level = Drainage.Fill(h, sea, n, 0f);
            hy.LakeId = new int[n * n];
            for (int i = 0; i < n * n; i++) hy.LakeId[i] = -1;
            FindLakes(c, h, sea, level, hy);

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
            BuildSegments(n, sea, hy);
            return hy;
        }

        // Picks the single spill cell, then routes every other lake cell through
        // the lake to it, so a lake always has exactly one outflow.
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

        static void FindLakes(WorldConfig c, float[] h, bool[] sea, float[] level, Hydrology hy)
        {
            int n = c.Size;
            var seen = new bool[n * n];
            var queue = new List<int>();
            for (int s = 0; s < n * n; s++)
            {
                if (seen[s] || sea[s] || level[s] - h[s] <= 1e-4f) continue;
                queue.Clear(); queue.Add(s); seen[s] = true;
                float lv = level[s], maxDepth = 0;
                for (int q = 0; q < queue.Count; q++)
                {
                    int i = queue[q];
                    maxDepth = Math.Max(maxDepth, lv - h[i]);
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
                if (maxDepth >= c.LakeMinDepth && queue.Count >= c.LakeMinCells)
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

        static void BuildSegments(int n, bool[] sea, Hydrology hy)
        {
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
                    Add(seg, hy, c);
                    int r = hy.Receiver[c];
                    if (sea[r]) { Add(seg, hy, r); seg.End = RiverEnd.Sea; break; }
                    if (hy.LakeId[r] >= 0) { Add(seg, hy, r); seg.End = RiverEnd.Lake; seg.LakeId = hy.LakeId[r]; break; }
                    if (riverDonors[r] >= 2) { Add(seg, hy, r); seg.End = RiverEnd.Junction; seg.Downstream = r; break; }
                    c = r;
                }
            }
            foreach (var seg in hy.Rivers)
                if (seg.End == RiverEnd.Junction) seg.Downstream = startOf[seg.Downstream];
        }

        static void Add(RiverSegment seg, Hydrology hy, int i)
        {
            float q = hy.DischargeM3s[i];
            seg.Cells.Add(i);
            seg.Surface.Add(hy.WaterSurface[i]);
            seg.Discharge.Add(q);
            // Leopold hydraulic geometry: width and depth grow as powers of discharge.
            seg.Width.Add(4f * (float)Math.Pow(q, 0.5));
            seg.Depth.Add(0.4f * (float)Math.Pow(q, 0.4));
        }
    }
}
