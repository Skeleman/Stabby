using System;
using System.Collections.Generic;
using System.Threading;

namespace Stabby.World
{
    public struct RoadSegment
    {
        public const int Dirt = 0, Ford = 1, Lane = 2;
        public float X0, Z0, X1, Z1, HalfWidth;
        public int Kind;

        public float Distance(float x, float z) => (float)Math.Sqrt(ObjectField.PointSegment2(x, z, X0, Z0, X1, Z1));
    }

    public sealed partial class ObjectField
    {
        public const int RoadLinks = 2, RoadMarginCells = 12, RoadSpanCells = 6;
        public const float RoadHalfM = 2f, RoadReachM = 9000f, RoadGrade = 0.25f, RoadClimb = 8f, FordHalfM = 10f, FordCost = 4f, RoadEditY = 6f, RoadClearM = 0.5f, RoadMarginM = 600f, RoadOutCells = 1.5f, RoadSiteReachM = 1500f, RoadSideReachM = 120f;

        readonly object roadLock = new object();
        RoadSegment[] roads, roadDraft;
        int[] roadStart, roadItems;
        ObjectInstance[][] roadSites;
        Dictionary<int, List<int>> siteCells;
        List<ObjectInstance> siteList;

        public RoadSegment[] Roads()
        {
            var found = roads;
            if (found != null) return found;
            lock (roadLock)
            {
                if (roads != null) return roads;
                if (roadDraft != null) return roadDraft;
                var list = new List<RoadSegment>();
                var plans = TownPlans();
                foreach (var p in plans)
                    list.Add(new RoadSegment { X0 = p.X - p.Ux * p.LaneM, Z0 = p.Z - p.Uz * p.LaneM, X1 = p.X + p.Ux * p.LaneM, Z1 = p.Z + p.Uz * p.LaneM, HalfWidth = Math.Min(RoadHalfM, 0.5f * p.LaneWidthM), Kind = RoadSegment.Lane });
                var linked = new HashSet<int>();
                var order = new List<int>();
                for (int a = 0; a < plans.Length; a++)
                {
                    order.Clear();
                    for (int b = 0; b < plans.Length; b++)
                    {
                        if (b == a) continue;
                        float dx = plans[b].X - plans[a].X, dz = plans[b].Z - plans[a].Z;
                        if (dx * dx + dz * dz < RoadReachM * RoadReachM) order.Add(b);
                    }
                    var pa = plans[a];
                    order.Sort((u, v) =>
                    {
                        float du = (plans[u].X - pa.X) * (plans[u].X - pa.X) + (plans[u].Z - pa.Z) * (plans[u].Z - pa.Z);
                        float dv = (plans[v].X - pa.X) * (plans[v].X - pa.X) + (plans[v].Z - pa.Z) * (plans[v].Z - pa.Z);
                        return du != dv ? du.CompareTo(dv) : u.CompareTo(v);
                    });
                    for (int k = 0; k < Math.Min(RoadLinks, order.Count); k++)
                    {
                        int b = order[k];
                        if (linked.Add(Math.Min(a, b) * plans.Length + Math.Max(a, b))) Link(plans, a, b, list);
                    }
                }
                roadDraft = list.ToArray();
                IndexRoads(roadDraft);
                PlaceRoadSites();
                foreach (var o in siteList) LinkSite(o, plans, list);
                var segs = list.ToArray();
                IndexRoads(segs);
                Volatile.Write(ref roads, segs);
                roadDraft = null;
                return segs;
            }
        }

        public bool RoadSite(int d) => IsTownMember(d) && seats[d] != null && lib.Objects[d].Placement.PerHectare > 0;

        void PlaceRoadSites()
        {
            var bins = new ObjectInstance[across * across][];
            var all = new List<ObjectInstance>();
            var scratch = new List<ObjectInstance>();
            var cells = new Dictionary<int, List<int>>();
            for (int d = 0; d < lib.Objects.Count; d++)
            {
                if (!RoadSite(d)) continue;
                for (int c = 0; c < across * across; c++)
                {
                    scratch.Clear();
                    PlaceInto(c % across, c / across, false, scratch, null, false, false, d);
                    if (scratch.Count == 0) continue;
                    var bin = bins[c];
                    int at = bin == null ? 0 : bin.Length;
                    Array.Resize(ref bin, at + scratch.Count);
                    scratch.CopyTo(bin, at);
                    bins[c] = bin;
                    all.AddRange(scratch);
                }
            }
            for (int k = 0; k < all.Count; k++)
            {
                var o = all[k];
                float g = SiteDisc(o) + cm;
                int a = Math.Max(0, (int)Math.Floor((o.X - g) / cm)), b = Math.Min(n - 1, (int)Math.Ceiling((o.X + g) / cm));
                int c0 = Math.Max(0, (int)Math.Floor((o.Z - g) / cm)), c1 = Math.Min(n - 1, (int)Math.Ceiling((o.Z + g) / cm));
                for (int gz = c0; gz <= c1; gz++)
                    for (int gx = a; gx <= b; gx++)
                    {
                        if (!cells.TryGetValue(gz * n + gx, out var l)) cells[gz * n + gx] = l = new List<int>();
                        l.Add(k);
                    }
            }
            roadSites = bins;
            siteList = all;
            siteCells = cells;
        }

        float SiteDisc(ObjectInstance o) => footprint[o.Def] * o.Size + RoadHalfM;

        void EmitRoadSites(int d, int chunk, List<ObjectInstance> list, ref int ordinal)
        {
            Roads();
            var bin = roadSites[chunk];
            if (bin == null) return;
            foreach (var m in bin)
            {
                if (m.Def != d) continue;
                var o = m;
                o.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(o);
            }
        }

        void LinkSite(ObjectInstance o, TownPlan[] plans, List<RoadSegment> list)
        {
            float best = RoadSiteReachM * RoadSiteReachM, tx = 0, tz = 0, ex = 0, ez = 0;
            bool any = false, town = false;
            foreach (var p in plans)
                for (int s = -1; s <= 1; s += 2)
                {
                    float o2 = p.Radius + RoadHalfM + RoadOutCells * cm, ox = p.X + s * p.Ux * o2, oz = p.Z + s * p.Uz * o2;
                    float d2 = (ox - o.X) * (ox - o.X) + (oz - o.Z) * (oz - o.Z);
                    if (d2 >= best) continue;
                    best = d2; tx = ox; tz = oz; ex = p.X + s * p.Ux * p.LaneM; ez = p.Z + s * p.Uz * p.LaneM; any = town = true;
                }
            foreach (var s in list)
            {
                if (s.Kind == RoadSegment.Lane) continue;
                float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, l2 = dx * dx + dz * dz;
                float t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, ((o.X - s.X0) * dx + (o.Z - s.Z0) * dz) / l2)) : 0f;
                float px = s.X0 + t * dx, pz = s.Z0 + t * dz, d2 = (px - o.X) * (px - o.X) + (pz - o.Z) * (pz - o.Z);
                if (d2 >= best) continue;
                best = d2; tx = px; tz = pz; any = true; town = false;
            }
            if (!any) return;
            float dist = (float)Math.Sqrt(best), ux = (tx - o.X) / dist, uz = (tz - o.Z) / dist, r = SiteDisc(o);
            float sx = o.X + ux * (r + RoadClearM), sz = o.Z + uz * (r + RoadClearM), ax = o.X + ux * (r + 2 * cm), az = o.Z + uz * (r + 2 * cm);
            if (ax < 0 || az < 0 || ax >= worldM || az >= worldM) return;
            var path = RoadPath(Cell(ax), Cell(az), Cell(tx), Cell(tz));
            if (path == null) return;
            var px2 = new List<float>(); var pz2 = new List<float>();
            foreach (int node in path) { px2.Add((node % n) * cm); pz2.Add((node / n) * cm); }
            Pull(px2, pz2);
            Smooth(px2, pz2);
            if (RoadFault(sx, sz, px2[0], pz2[0], out _) != null) return;
            int end = px2.Count - 1;
            if (!town && RoadFault(px2[end], pz2[end], tx, tz, out _) != null) return;
            AddRun(sx, sz, px2[0], pz2[0], list);
            for (int k = 0; k < end; k++) AddRun(px2[k], pz2[k], px2[k + 1], pz2[k + 1], list);
            if (town) list.Add(new RoadSegment { X0 = px2[end], Z0 = pz2[end], X1 = ex, Z1 = ez, HalfWidth = RoadHalfM, Kind = RoadSegment.Lane });
            else AddRun(px2[end], pz2[end], tx, tz, list);
        }

        void AddRun(float x0, float z0, float x1, float z1, List<RoadSegment> list)
        {
            if ((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0) < 1e-6f) return;
            RoadFault(x0, z0, x1, z1, out bool ford);
            list.Add(new RoadSegment { X0 = x0, Z0 = z0, X1 = x1, Z1 = z1, HalfWidth = RoadHalfM, Kind = ford ? RoadSegment.Ford : RoadSegment.Dirt });
        }

        public float RoadSide(float x, float z, out float rx, out float rz)
        {
            var segs = Roads();
            float best = RoadSideReachM * RoadSideReachM;
            rx = x; rz = z;
            bool any = false;
            int a = Math.Max(0, (int)Math.Floor((x - RoadSideReachM) / cm)), b = Math.Min(n - 1, (int)Math.Ceiling((x + RoadSideReachM) / cm));
            int c = Math.Max(0, (int)Math.Floor((z - RoadSideReachM) / cm)), d = Math.Min(n - 1, (int)Math.Ceiling((z + RoadSideReachM) / cm));
            for (int gz = c; gz <= d; gz++)
                for (int gx = a; gx <= b; gx++)
                {
                    int i = gz * n + gx;
                    for (int k = roadStart[i]; k < roadStart[i + 1]; k++)
                    {
                        var s = segs[roadItems[k]];
                        float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, l2 = dx * dx + dz * dz;
                        float t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, ((x - s.X0) * dx + (z - s.Z0) * dz) / l2)) : 0f;
                        float px = s.X0 + t * dx, pz = s.Z0 + t * dz, d2 = (px - x) * (px - x) + (pz - z) * (pz - z);
                        if (d2 < best) { best = d2; rx = px; rz = pz; any = true; }
                    }
                }
            return any ? (float)Math.Sqrt(best) : float.MaxValue;
        }

        public bool OnRoad(float x, float z, float r)
        {
            var segs = Roads();
            float reach = r + RoadHalfM + RoadClearM;
            int a = Math.Max(0, (int)Math.Floor((x - reach) / cm)), b = Math.Min(n - 1, (int)Math.Ceiling((x + reach) / cm));
            int c = Math.Max(0, (int)Math.Floor((z - reach) / cm)), d = Math.Min(n - 1, (int)Math.Ceiling((z + reach) / cm));
            for (int gz = c; gz <= d; gz++)
                for (int gx = a; gx <= b; gx++)
                {
                    int i = gz * n + gx;
                    for (int k = roadStart[i]; k < roadStart[i + 1]; k++)
                    {
                        var s = segs[roadItems[k]];
                        float g = s.HalfWidth + r + RoadClearM;
                        if (PointSegment2(x, z, s.X0, s.Z0, s.X1, s.Z1) < g * g) return true;
                    }
                }
            return false;
        }

        public string RoadFault(float x0, float z0, float x1, float z1, out bool ford)
        {
            ford = false;
            float dx = x1 - x0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
            int pieces = Math.Max(1, (int)Math.Ceiling(len / cm));
            for (int k = 0; k < pieces; k++)
            {
                float ta = (float)k / pieces, tb = (float)(k + 1) / pieces;
                string f = StepFault(x0 + dx * ta, z0 + dz * ta, x0 + dx * tb, z0 + dz * tb, out bool wet);
                if (f != null) return f;
                ford |= wet;
            }
            return null;
        }

        string StepFault(float x0, float z0, float x1, float z1, out bool ford)
        {
            ford = false;
            float h0 = GroundAt(detail, cm, x0, z0), h1 = GroundAt(detail, cm, x1, z1);
            float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            if (Math.Abs(h1 - h0) > RoadGrade * len) return "is too steep";
            int last = -1;
            for (int t = 0; t <= 2; t++)
            {
                float x = x0 + (x1 - x0) * 0.5f * t, z = z0 + (z1 - z0) * 0.5f * t;
                if (x < 0 || z < 0 || x >= worldM || z >= worldM) return "leaves the map";
                int i = Cell(z) * n + Cell(x);
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) return "crosses standing water";
                if (i == last) continue;
                last = i;
                for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
                {
                    int item = blockItems[k];
                    if (item < 0)
                    {
                        var f = solid[~item];
                        if (f.Shape == FeatureEdit.Boulder || f.Shape == FeatureEdit.Spring || f.Shape == FeatureEdit.Vault || f.Link) continue;
                        if (Math.Max(x0, x1) + RoadHalfM < f.MinX || Math.Min(x0, x1) - RoadHalfM > f.MaxX || Math.Max(z0, z1) + RoadHalfM < f.MinZ || Math.Min(z0, z1) - RoadHalfM > f.MaxZ) continue;
                        if (f.MinY < Math.Max(h0, h1) + RoadEditY && f.MaxY > Math.Min(h0, h1) - RoadEditY) return $"meets a feature edit of shape {f.Shape}";
                        continue;
                    }
                    float g = phw[item] + RoadHalfM;
                    if (SegmentSegment2(x0, z0, x1, z1, pax[item], paz[item], pbx[item], pbz[item]) >= g * g) continue;
                    if (phw[item] > FordHalfM) return "crosses a river too wide to ford";
                    ford = true;
                }
            }
            foreach (var p in TownPlans())
            {
                float g = p.Radius + RoadHalfM;
                if (PointSegment2(p.X, p.Z, x0, z0, x1, z1) < g * g) return "crosses a town";
            }
            if (siteCells == null) return null;
            last = -1;
            for (int t = 0; t <= 2; t++)
            {
                int i = Cell(z0 + (z1 - z0) * 0.5f * t) * n + Cell(x0 + (x1 - x0) * 0.5f * t);
                if (i == last || !siteCells.TryGetValue(i, out var near)) continue;
                last = i;
                foreach (int k in near)
                {
                    var o = siteList[k];
                    float g = SiteDisc(o);
                    if (PointSegment2(o.X, o.Z, x0, z0, x1, z1) < g * g) return "crosses a building";
                }
            }
            return null;
        }

        void Link(TownPlan[] plans, int a, int b, List<RoadSegment> list)
        {
            var pa = plans[a]; var pb = plans[b];
            LaneEnd(pa, pb, out float ax, out float az, out float aox, out float aoz);
            LaneEnd(pb, pa, out float bx, out float bz, out float box, out float boz);
            var path = RoadPath(Cell(aox), Cell(aoz), Cell(box), Cell(boz));
            if (path == null) return;
            var px = new List<float>(); var pz = new List<float>();
            foreach (int node in path) { px.Add((node % n) * cm); pz.Add((node / n) * cm); }
            Pull(px, pz);
            Smooth(px, pz);
            list.Add(new RoadSegment { X0 = ax, Z0 = az, X1 = px[0], Z1 = pz[0], HalfWidth = RoadHalfM, Kind = RoadSegment.Lane });
            for (int k = 0; k + 1 < px.Count; k++)
            {
                RoadFault(px[k], pz[k], px[k + 1], pz[k + 1], out bool ford);
                list.Add(new RoadSegment { X0 = px[k], Z0 = pz[k], X1 = px[k + 1], Z1 = pz[k + 1], HalfWidth = RoadHalfM, Kind = ford ? RoadSegment.Ford : RoadSegment.Dirt });
            }
            list.Add(new RoadSegment { X0 = px[px.Count - 1], Z0 = pz[pz.Count - 1], X1 = bx, Z1 = bz, HalfWidth = RoadHalfM, Kind = RoadSegment.Lane });
        }

        void LaneEnd(TownPlan p, TownPlan to, out float x, out float z, out float ox, out float oz)
        {
            float s = (to.X - p.X) * p.Ux + (to.Z - p.Z) * p.Uz >= 0 ? 1f : -1f, o = p.Radius + RoadHalfM + RoadOutCells * cm;
            x = p.X + s * p.Ux * p.LaneM; z = p.Z + s * p.Uz * p.LaneM;
            ox = p.X + s * p.Ux * o; oz = p.Z + s * p.Uz * o;
        }

        List<int> RoadPath(int sx, int sz, int tx, int tz)
        {
            if (sx == tx && sz == tz) return null;
            int margin = Math.Max(RoadMarginCells, (int)(RoadMarginM / cm));
            int x0 = Math.Max(0, Math.Min(sx, tx) - margin), x1 = Math.Min(n - 1, Math.Max(sx, tx) + margin);
            int z0 = Math.Max(0, Math.Min(sz, tz) - margin), z1 = Math.Min(n - 1, Math.Max(sz, tz) + margin);
            int bw = x1 - x0 + 1, bh = z1 - z0 + 1, size = bw * bh;
            var cost = new float[size];
            var from = new int[size];
            var done = new bool[size];
            for (int i = 0; i < size; i++) { cost[i] = float.MaxValue; from[i] = -1; }
            var heap = new List<(float f, int i)>();
            int start = (sz - z0) * bw + (sx - x0), goal = (tz - z0) * bw + (tx - x0);
            cost[start] = 0;
            HeapPush(heap, (Heuristic(sx, sz, tx, tz), start));
            while (heap.Count > 0)
            {
                int cur = HeapPop(heap).i;
                if (done[cur]) continue;
                done[cur] = true;
                if (cur == goal) break;
                int cx = cur % bw + x0, cz = cur / bw + z0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < x0 || nz < z0 || nx > x1 || nz > z1) continue;
                        int j = (nz - z0) * bw + (nx - x0);
                        if (done[j]) continue;
                        float ax = cx * cm, az = cz * cm, bx = nx * cm, bz = nz * cm;
                        if (RoadFault(ax, az, bx, bz, out bool ford) != null) continue;
                        float len = (dx != 0 && dz != 0 ? 1.41421356f : 1f) * cm;
                        float grade = Math.Abs(GroundAt(detail, cm, bx, bz) - GroundAt(detail, cm, ax, az)) / len;
                        float c = cost[cur] + len * (1 + RoadClimb * grade) * (ford ? FordCost : 1f);
                        if (c >= cost[j]) continue;
                        cost[j] = c; from[j] = cur;
                        HeapPush(heap, (c + Heuristic(nx, nz, tx, tz), j));
                    }
            }
            if (!done[goal]) return null;
            var path = new List<int>();
            for (int k = goal; k >= 0; k = from[k]) path.Add((k / bw + z0) * n + k % bw + x0);
            path.Reverse();
            return path;
        }

        float Heuristic(int ax, int az, int bx, int bz) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)) * cm;

        static void HeapPush(List<(float f, int i)> h, (float f, int i) v)
        {
            h.Add(v);
            int k = h.Count - 1;
            while (k > 0)
            {
                int p = (k - 1) / 2;
                if (!Before(h[k], h[p])) break;
                (h[k], h[p]) = (h[p], h[k]);
                k = p;
            }
        }

        static (float f, int i) HeapPop(List<(float f, int i)> h)
        {
            var top = h[0];
            h[0] = h[h.Count - 1];
            h.RemoveAt(h.Count - 1);
            int k = 0;
            while (true)
            {
                int l = 2 * k + 1, r = l + 1, m = k;
                if (l < h.Count && Before(h[l], h[m])) m = l;
                if (r < h.Count && Before(h[r], h[m])) m = r;
                if (m == k) break;
                (h[k], h[m]) = (h[m], h[k]);
                k = m;
            }
            return top;
        }

        static bool Before((float f, int i) a, (float f, int i) b) => a.f < b.f || (a.f == b.f && a.i < b.i);

        void Pull(List<float> px, List<float> pz)
        {
            var ox = new List<float> { px[0] }; var oz = new List<float> { pz[0] };
            int at = 0;
            while (at < px.Count - 1)
            {
                int next = at + 1;
                for (int j = Math.Min(px.Count - 1, at + RoadSpanCells); j > at + 1; j--)
                    if (RoadFault(px[at], pz[at], px[j], pz[j], out _) == null) { next = j; break; }
                ox.Add(px[next]); oz.Add(pz[next]);
                at = next;
            }
            px.Clear(); px.AddRange(ox); pz.Clear(); pz.AddRange(oz);
        }

        void Smooth(List<float> px, List<float> pz)
        {
            if (px.Count < 3) return;
            var sx = new List<float> { px[0] }; var sz = new List<float> { pz[0] };
            for (int k = 0; k + 1 < px.Count; k++)
            {
                sx.Add(0.75f * px[k] + 0.25f * px[k + 1]); sz.Add(0.75f * pz[k] + 0.25f * pz[k + 1]);
                sx.Add(0.25f * px[k] + 0.75f * px[k + 1]); sz.Add(0.25f * pz[k] + 0.75f * pz[k + 1]);
            }
            sx.Add(px[px.Count - 1]); sz.Add(pz[pz.Count - 1]);
            for (int k = 0; k + 1 < sx.Count; k++)
                if (RoadFault(sx[k], sz[k], sx[k + 1], sz[k + 1], out _) != null) return;
            px.Clear(); px.AddRange(sx); pz.Clear(); pz.AddRange(sz);
        }

        void IndexRoads(RoadSegment[] segs)
        {
            var start = new int[n * n + 1];
            int[] items = null;
            for (int pass = 0; pass < 2; pass++)
            {
                var fill = pass == 1 ? (int[])start.Clone() : null;
                if (pass == 1) items = new int[start[n * n]];
                for (int k = 0; k < segs.Length; k++)
                {
                    var s = segs[k];
                    int a = Math.Max(0, (int)Math.Floor((Math.Min(s.X0, s.X1) - s.HalfWidth) / cm)), b = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(s.X0, s.X1) + s.HalfWidth) / cm));
                    int c = Math.Max(0, (int)Math.Floor((Math.Min(s.Z0, s.Z1) - s.HalfWidth) / cm)), d = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(s.Z0, s.Z1) + s.HalfWidth) / cm));
                    for (int gz = c; gz <= d; gz++)
                        for (int gx = a; gx <= b; gx++)
                        {
                            int i = gz * n + gx;
                            if (fill == null) start[i + 1]++;
                            else items[fill[i]++] = k;
                        }
                }
                if (pass == 0) for (int i = 1; i <= n * n; i++) start[i] += start[i - 1];
            }
            roadStart = start;
            roadItems = items;
        }

        internal static float PointSegment2(float x, float z, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            float t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (z - az) * dz) / l2)) : 0f;
            float ex = ax + t * dx - x, ez = az + t * dz - z;
            return ex * ex + ez * ez;
        }

        static float SegmentSegment2(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
        {
            float o1 = (bx - ax) * (cz - az) - (bz - az) * (cx - ax), o2 = (bx - ax) * (dz - az) - (bz - az) * (dx - ax);
            float o3 = (dx - cx) * (az - cz) - (dz - cz) * (ax - cx), o4 = (dx - cx) * (bz - cz) - (dz - cz) * (bx - cx);
            if (o1 * o2 < 0 && o3 * o4 < 0) return 0f;
            return Math.Min(Math.Min(PointSegment2(ax, az, cx, cz, dx, dz), PointSegment2(bx, bz, cx, cz, dx, dz)), Math.Min(PointSegment2(cx, cz, ax, az, bx, bz), PointSegment2(dx, dz, ax, az, bx, bz)));
        }
    }
}
