using System;
using System.Collections.Generic;
using System.Threading;

namespace Stabby.World
{
    public struct RoadSegment
    {
        public const int Dirt = 0, Ford = 1, Lane = 2, Tunnel = 3, Bridge = 4;
        public float X0, Z0, X1, Z1, HalfWidth;
        public int Kind;

        public float Distance(float x, float z) => (float)Math.Sqrt(ObjectField.PointSegment2(x, z, X0, Z0, X1, Z1));
    }

    public sealed partial class ObjectField
    {
        public const int RoadLinks = 2, RoadMarginCells = 12, RoadSpanCells = 6;
        public const float RoadHalfM = 2f, RoadReachM = 9000f, RoadGrade = 0.25f, RoadClimb = 8f, FordHalfM = 10f, FordCost = 4f, RoadEditY = 6f, RoadClearM = 0.5f, RoadMarginM = 600f, RoadOutCells = 1.5f, RoadSiteReachM = 1500f, RoadSideReachM = 120f, BridgeCost = 6f;
        public const string TunnelClass = "road-tunnel";
        public const int TunnelTries = 4;
        public const float TunnelMinM = 70f, TunnelMostM = 250f, TunnelGrade = 0.12f, TunnelCost = 1f, TunnelRise = 0.5f, TunnelSpanM = 150f, TunnelSocketM = 100f, TunnelCampMin = 14.5f, TunnelCampMax = 18f, TunnelWarp = 0.04f;
        public const float TunnelHalfM = DungeonLinks.RadiusMax, TunnelRoofM = Mines.RoofM, TunnelStubM = DungeonLinks.RadiusMax + Mines.RoofM + Underground.MouthM, TunnelCoverM = DungeonLinks.RadiusMax * (1f + Features.CaveHollow) + Mines.RoofM + 1f;

        readonly object roadLock = new object();
        RoadSegment[] roads, roadDraft;
        int[] roadStart, roadItems;
        ObjectInstance[][] roadSites;
        Dictionary<int, List<int>> siteCells;
        List<ObjectInstance> siteList;
        int[] siteChunkOf;
        HashSet<long> tunnelBan;
        HashSet<int> tunnelTiles, tunnelBlocked;
        List<FeatureEdit> tunnelEdits = new List<FeatureEdit>();
        List<CaveGraph> tunnelGraphs = new List<CaveGraph>();
        CaveClass tunnelShape;
        ulong tunnelSeed;

        public static bool IsTunnel(CaveGraph g) => g.Class == TunnelClass;

        public FeatureEdit[] TunnelEdits(out CaveGraph[] graphs)
        {
            Roads();
            graphs = tunnelGraphs.ToArray();
            return tunnelEdits.ToArray();
        }

        sealed class Bored
        {
            public int Tile;
            public List<FeatureEdit> Edits;
            public CaveGraph Graph;
        }

        void TunnelStart()
        {
            tunnelBan = new HashSet<long>();
            tunnelTiles = new HashSet<int>();
            tunnelEdits.Clear();
            tunnelGraphs.Clear();
            tunnelSeed = Rng.Stream(w.Config.Seed, "road-tunnels").NextU64();
            tunnelShape = new CaveClass { Id = TunnelClass, Entrances = 2, RadiusMin = DungeonLinks.RadiusMin, RadiusMax = DungeonLinks.RadiusMax, ChamberMin = TunnelCampMin, ChamberMax = TunnelCampMax, Warp = TunnelWarp };
        }

        static int TileOfCell(int cx, int cz) => cz / Features.TileCells * 65536 + cx / Features.TileCells;

        bool TunnelEnd(int cx, int cz)
        {
            int m = Features.MarginCells + 1, ix = cx % Features.TileCells, iz = cz % Features.TileCells;
            if (ix < m || iz < m || ix >= Features.TileCells - m || iz >= Features.TileCells - m) return false;
            int i = cz * n + cx;
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return false;
            int tile = TileOfCell(cx, cz);
            return !tunnelTiles.Contains(tile) && !TunnelBlocked().Contains(tile);
        }

        HashSet<int> TunnelBlocked()
        {
            if (tunnelBlocked != null) return tunnelBlocked;
            var set = new HashSet<int>();
            float span = Features.TileCells * cm;
            foreach (var e in edits)
            {
                if (!e.CaveCarve) continue;
                int a = (int)Math.Floor(e.MinX / span), b = (int)Math.Floor(e.MaxX / span), c = (int)Math.Floor(e.MinZ / span), d = (int)Math.Floor(e.MaxZ / span);
                for (int z = c; z <= d; z++)
                    for (int x = a; x <= b; x++) set.Add(z * 65536 + x);
            }
            return tunnelBlocked = set;
        }

        List<int> Route(int sx, int sz, int tx, int tz, out List<byte> jumps, out List<Bored> bored)
        {
            bored = new List<Bored>();
            for (int t = 0; ; t++)
            {
                var path = RoadPath(sx, sz, tx, tz, t < TunnelTries, out jumps);
                if (path == null) return null;
                bored.Clear();
                bool ok = true;
                for (int k = 1; k < path.Count && ok; k++)
                {
                    if (jumps[k] != 1) continue;
                    long key = (long)path[k - 1] * n * n + path[k];
                    var b = Bore(path[k - 1], path[k], key);
                    if (b == null || bored.Exists(o => o.Tile == b.Tile)) { tunnelBan.Add(key); ok = false; }
                    else bored.Add(b);
                }
                if (ok) return path;
            }
        }

        Bored Bore(int a, int b, long key)
        {
            float ax = (a % n) * cm, az = (a / n) * cm, bx = (b % n) * cm, bz = (b / n) * cm;
            var r = new Rng(Rng.Mix(tunnelSeed ^ (ulong)key));
            var list = new List<FeatureEdit>();
            if (!Underground.Bore(w, detail, tunnelShape, ax, az, bx, bz, TunnelRoofM, TunnelSocketM, TunnelSpanM, ref r, list, out var g)) return null;
            int tile = Features.TileOf(list[0], cm);
            if (tile < 0 || tunnelTiles.Contains(tile) || TunnelBlocked().Contains(tile)) return null;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (Features.TileOf(e, cm) != tile) return null;
                e.Link = true;
                list[i] = e;
                x0 = Math.Min(x0, e.MinX); x1 = Math.Max(x1, e.MaxX); z0 = Math.Min(z0, e.MinZ); z1 = Math.Max(z1, e.MaxZ);
            }
            var near = new List<FeatureEdit>();
            foreach (var e in edits)
                if (e.Shape != FeatureEdit.Boulder && e.Shape != FeatureEdit.Spring && e.Shape != FeatureEdit.Vault && e.MinX < x1 && e.MaxX > x0 && e.MinZ < z1 && e.MaxZ > z0) near.Add(e);
            near.AddRange(list);
            var all = near.ToArray();
            foreach (var e in list) if (e.Shape == FeatureEdit.Cave && (detail.HeightAt(e.Bx / cm, e.Bz / cm) < e.By + e.R || Underground.PathFault(detail, all, e) != null)) return null;
            foreach (var edge in g.Edges) if (Underground.Cramped(detail, all, g, edge) != null) return null;
            if (Underground.Through(detail, all, g) != null || !DungeonLinks.Fits(DungeonLinks.Width(detail, all, g))) return null;
            g.Tile = tile;
            return new Bored { Tile = tile, Edits = list, Graph = g };
        }

        void Commit(List<Bored> bored)
        {
            foreach (var b in bored)
            {
                tunnelTiles.Add(b.Tile);
                tunnelEdits.AddRange(b.Edits);
                tunnelGraphs.Add(b.Graph);
            }
        }

        List<(List<float> x, List<float> z)> Runs(List<int> path, List<byte> jumps, out List<int> joins)
        {
            var runs = new List<(List<float> x, List<float> z)>();
            joins = new List<int>();
            var px = new List<float>(); var pz = new List<float>();
            for (int k = 0; k < path.Count; k++)
            {
                if (jumps[k] != 0) { runs.Add((px, pz)); px = new List<float>(); pz = new List<float>(); joins.Add(jumps[k] == 1 ? RoadSegment.Tunnel : RoadSegment.Bridge); }
                px.Add((path[k] % n) * cm); pz.Add((path[k] / n) * cm);
            }
            runs.Add((px, pz));
            foreach (var r in runs) { Pull(r.x, r.z); Smooth(r.x, r.z); }
            return runs;
        }

        void EmitRuns(List<(List<float> x, List<float> z)> runs, List<int> joins, List<RoadSegment> list, bool site)
        {
            for (int r = 0; r < runs.Count; r++)
            {
                var (px, pz) = runs[r];
                if (r > 0)
                {
                    var (qx, qz) = runs[r - 1];
                    int kind = joins[r - 1];
                    list.Add(new RoadSegment { X0 = qx[qx.Count - 1], Z0 = qz[qz.Count - 1], X1 = px[0], Z1 = pz[0], HalfWidth = kind == RoadSegment.Tunnel ? TunnelHalfM : RoadHalfM, Kind = kind });
                }
                for (int k = 0; k + 1 < px.Count; k++)
                {
                    if (site) { AddRun(px[k], pz[k], px[k + 1], pz[k + 1], list); continue; }
                    RoadFault(px[k], pz[k], px[k + 1], pz[k + 1], out bool ford);
                    list.Add(new RoadSegment { X0 = px[k], Z0 = pz[k], X1 = px[k + 1], Z1 = pz[k + 1], HalfWidth = RoadHalfM, Kind = ford ? RoadSegment.Ford : RoadSegment.Dirt });
                }
            }
        }

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
                TunnelStart();
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
            var all = new List<ObjectInstance>();
            var chunks = new List<int>();
            var scratch = new List<ObjectInstance>();
            for (int d = 0; d < lib.Objects.Count; d++)
            {
                if (!RoadSite(d)) continue;
                for (int c = 0; c < across * across; c++)
                {
                    scratch.Clear();
                    PlaceInto(c % across, c / across, false, scratch, null, false, false, d);
                    all.AddRange(scratch);
                    for (int k = 0; k < scratch.Count; k++) chunks.Add(c);
                }
            }
            IndexSites(all, chunks.ToArray());
        }

        void IndexSites(List<ObjectInstance> all, int[] chunks)
        {
            var bins = new ObjectInstance[across * across][];
            var count = new int[across * across];
            foreach (int c in chunks) count[c]++;
            var fill = new int[across * across];
            for (int k = 0; k < all.Count; k++)
            {
                int c = chunks[k];
                var bin = bins[c] ?? (bins[c] = new ObjectInstance[count[c]]);
                bin[fill[c]++] = all[k];
            }
            var cells = new Dictionary<int, List<int>>();
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
            siteChunkOf = chunks;
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
                if (s.Kind == RoadSegment.Lane || s.Kind == RoadSegment.Tunnel || s.Kind == RoadSegment.Bridge) continue;
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
            var path = Route(Cell(ax), Cell(az), Cell(tx), Cell(tz), out var jumps, out var bored);
            if (path == null) return;
            var runs = Runs(path, jumps, out var joins);
            var (fx, fz) = runs[0];
            var (lx, lz) = runs[runs.Count - 1];
            int end = lx.Count - 1;
            if (RoadFault(sx, sz, fx[0], fz[0], out _) != null) return;
            if (!town && RoadFault(lx[end], lz[end], tx, tz, out _) != null) return;
            AddRun(sx, sz, fx[0], fz[0], list);
            EmitRuns(runs, joins, list, true);
            if (town) list.Add(new RoadSegment { X0 = lx[end], Z0 = lz[end], X1 = ex, Z1 = ez, HalfWidth = RoadHalfM, Kind = RoadSegment.Lane });
            else AddRun(lx[end], lz[end], tx, tz, list);
            Commit(bored);
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
                        if (s.Kind == RoadSegment.Tunnel) continue;
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
            float reach = r + Math.Max(RoadHalfM, TunnelHalfM) + RoadClearM;
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
                        if (s.Kind == RoadSegment.Tunnel)
                        {
                            float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, l = (float)Math.Sqrt(dx * dx + dz * dz), f = l > TunnelStubM ? TunnelStubM / l : 1f;
                            if (PointSegment2(x, z, s.X0, s.Z0, s.X0 + dx * f, s.Z0 + dz * f) < g * g || PointSegment2(x, z, s.X1, s.Z1, s.X1 - dx * f, s.Z1 - dz * f) < g * g) return true;
                            continue;
                        }
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

        string KnightFault(float x0, float z0, float x1, float z1, float h0, float h1, out bool ford)
        {
            ford = false;
            float dx = x1 - x0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
            int pieces = Math.Max(1, (int)Math.Ceiling(len / cm));
            float xa = x0, za = z0, ha = h0;
            for (int k = 0; k < pieces; k++)
            {
                float tb = (float)(k + 1) / pieces, xb = x0 + dx * tb, zb = z0 + dz * tb;
                float hb = xb == x1 && zb == z1 ? h1 : GroundAt(detail, cm, xb, zb);
                string f = StepFault(xa, za, xb, zb, ha, hb, out bool wet);
                if (f != null) return f;
                ford |= wet;
                xa = xb; za = zb; ha = hb;
            }
            return null;
        }

        string StepFault(float x0, float z0, float x1, float z1, out bool ford) => StepFault(x0, z0, x1, z1, GroundAt(detail, cm, x0, z0), GroundAt(detail, cm, x1, z1), out ford);

        string StepFault(float x0, float z0, float x1, float z1, float h0, float h1, out bool ford) => StepFault(x0, z0, x1, z1, h0, h1, h0, h1, out ford, false);

        string StepFault(float x0, float z0, float x1, float z1, float h0, float h1, float u0, float u1, out bool ford, bool bridge)
        {
            ford = false;
            float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            if (!bridge && Math.Abs(h1 - h0) > RoadGrade * len) return "is too steep";
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
                        if (f.MinY < Math.Max(h0, h1) + RoadEditY && f.MaxY > Math.Min(u0, u1) - RoadEditY) return $"meets a feature edit of shape {f.Shape}";
                        continue;
                    }
                    if (bridge) continue;
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
            float sa = Side(pa, pb), sb = Side(pb, pa);
            stepSeen = new Dictionary<long, byte>();
            groundSeen = new Dictionary<long, float>();
            for (int k = 0; k < 4; k++)
            {
                float ua = k < 2 ? sa : -sa, ub = k % 2 == 0 ? sb : -sb;
                LaneEnd(pa, ua, out float ax, out float az, out float aox, out float aoz);
                LaneEnd(pb, ub, out float bx, out float bz, out float box, out float boz);
                var path = Route(Cell(aox), Cell(aoz), Cell(box), Cell(boz), out var jumps, out var bored);
                if (path == null) continue;
                stepSeen = null; groundSeen = null;
                var runs = Runs(path, jumps, out var joins);
                var (fx, fz) = runs[0];
                var (lx, lz) = runs[runs.Count - 1];
                list.Add(new RoadSegment { X0 = ax, Z0 = az, X1 = fx[0], Z1 = fz[0], HalfWidth = RoadHalfM, Kind = RoadSegment.Lane });
                EmitRuns(runs, joins, list, false);
                list.Add(new RoadSegment { X0 = lx[lx.Count - 1], Z0 = lz[lz.Count - 1], X1 = bx, Z1 = bz, HalfWidth = RoadHalfM, Kind = RoadSegment.Lane });
                Commit(bored);
                return;
            }
            stepSeen = null; groundSeen = null;
        }

        Dictionary<long, byte> stepSeen;
        Dictionary<long, float> groundSeen;

        static float Side(TownPlan p, TownPlan to) => (to.X - p.X) * p.Ux + (to.Z - p.Z) * p.Uz >= 0 ? 1f : -1f;

        void LaneEnd(TownPlan p, float s, out float x, out float z, out float ox, out float oz)
        {
            float o = p.Radius + RoadHalfM + RoadOutCells * cm;
            x = p.X + s * p.Ux * p.LaneM; z = p.Z + s * p.Uz * p.LaneM;
            ox = p.X + s * p.Ux * o; oz = p.Z + s * p.Uz * o;
        }

        List<int> RoadPath(int sx, int sz, int tx, int tz, bool tunnels, out List<byte> jumps)
        {
            jumps = null;
            if (sx == tx && sz == tz) return null;
            int margin = Math.Max(RoadMarginCells, (int)(RoadMarginM / cm));
            int x0 = Math.Max(0, Math.Min(sx, tx) - margin), x1 = Math.Min(n - 1, Math.Max(sx, tx) + margin);
            int z0 = Math.Max(0, Math.Min(sz, tz) - margin), z1 = Math.Min(n - 1, Math.Max(sz, tz) + margin);
            int bw = x1 - x0 + 1, bh = z1 - z0 + 1, size = bw * bh;
            var cost = new float[size];
            var from = new int[size];
            var done = new bool[size];
            var tun = new byte[size];
            bool bridges = BridgeDef() >= 0;
            int hw = 2 * bw;
            var height = new float[4 * size];
            for (int i = 0; i < size; i++) { cost[i] = float.MaxValue; from[i] = -1; }
            for (int i = 0; i < 4 * size; i++) height[i] = float.NaN;
            float Lattice(int hx, int hz)
            {
                int k = (hz - 2 * z0) * hw + hx - 2 * x0;
                if (float.IsNaN(height[k]))
                {
                    long g = (long)hz * (2 * n + 1) + hx;
                    if (groundSeen == null || !groundSeen.TryGetValue(g, out height[k]))
                    {
                        height[k] = GroundAt(detail, cm, 0.5f * hx * cm, 0.5f * hz * cm);
                        if (groundSeen != null) groundSeen[g] = height[k];
                    }
                }
                return height[k];
            }
            var heap = new List<(float f, int i)>();
            void Jump(int cur, int cx, int cz, int dx, int dz, float ha)
            {
                float step = (dx != 0 && dz != 0 ? 1.41421356f : 1f) * cm;
                int most = (int)(TunnelMostM / step);
                if (!TunnelEnd(cx, cz)) return;
                for (int k = Math.Max(2, (int)Math.Ceiling(TunnelMinM / step)); k <= most; k++)
                {
                    int nx = cx + k * dx, nz = cz + k * dz;
                    if (nx < x0 || nz < z0 || nx > x1 || nz > z1) return;
                    int j = (nz - z0) * bw + (nx - x0);
                    float dist = k * step, hb = Lattice(2 * nx, 2 * nz);
                    if (done[j] || Math.Abs(hb - ha) > TunnelGrade * dist || TileOfCell(nx, nz) != TileOfCell(cx, cz) || !TunnelEnd(nx, nz)) continue;
                    bool deep = true;
                    for (int m = 1; m < 2 * k && deep; m++)
                    {
                        float t = m / (2f * k), d0 = t * dist, gr = Lattice(2 * cx + m * dx, 2 * cz + m * dz);
                        if (d0 < TunnelStubM) deep = gr >= ha;
                        else if (dist - d0 < TunnelStubM) deep = gr >= hb;
                        else deep = gr >= ha + (hb - ha) * t + TunnelCoverM;
                    }
                    if (!deep || tunnelBan.Contains((long)(cz * n + cx) * n * n + nz * n + nx)) continue;
                    float c = cost[cur] + dist * TunnelCost;
                    if (c < cost[j])
                    {
                        cost[j] = c; from[j] = cur; tun[j] = 1;
                        HeapPush(heap, (c + Heuristic(nx, nz, tx, tz), j));
                    }
                    return;
                }
            }
            void Span(int cur, int cx, int cz, int dx, int dz)
            {
                float step = (dx != 0 && dz != 0 ? 1.41421356f : 1f) * cm, ax = cx * cm, az = cz * cm;
                if (Footing(ax, az) != null) return;
                int most = (int)(lib.Roads.SpanM / step), tries = 0;
                bool wet = false;
                for (int k = 1; k <= most; k++)
                {
                    int nx = cx + k * dx, nz = cz + k * dz;
                    if (nx < x0 || nz < z0 || nx > x1 || nz > z1) return;
                    if (RiverUnder(nx * cm, nz * cm, 0, out _, out _)) { wet = true; continue; }
                    if (!wet) { if (k >= BridgeLookCells) return; continue; }
                    int j = (nz - z0) * bw + (nx - x0);
                    if (k < 2 || done[j]) continue;
                    if (!BridgeOk(cz * n + cx, nz * n + nx)) { if (++tries >= BridgeTries) return; continue; }
                    float c = cost[cur] + k * step * BridgeCost;
                    if (c < cost[j])
                    {
                        cost[j] = c; from[j] = cur; tun[j] = 2;
                        HeapPush(heap, (c + Heuristic(nx, nz, tx, tz), j));
                    }
                    return;
                }
            }
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
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int ad = Math.Abs(dx) + Math.Abs(dz);
                        if (ad == 0 || (ad == 2 && dx != 0 && dz == 0) || (ad == 2 && dz != 0 && dx == 0) || ad == 4) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < x0 || nz < z0 || nx > x1 || nz > z1) continue;
                        int j = (nz - z0) * bw + (nx - x0);
                        if (done[j]) continue;
                        float ax = cx * cm, az = cz * cm, bx = nx * cm, bz = nz * cm;
                        float len = (ad == 3 ? 2.23606798f : ad == 2 ? 1.41421356f : 1f) * cm;
                        float ha = Lattice(2 * cx, 2 * cz), hb = Lattice(2 * nx, 2 * nz), rise = Math.Abs(hb - ha);
                        if (tunnels && ad < 3 && hb > ha && rise > TunnelRise * RoadGrade * len) Jump(cur, cx, cz, dx, dz, ha);
                        bool span = bridges && ad < 3 && WideAhead(cx, cz, dx, dz);
                        if (rise > RoadGrade * len) { if (span) Span(cur, cx, cz, dx, dz); continue; }
                        bool ford;
                        string why;
                        long sk = ((long)cz * n + cx) * 25 + (dz + 2) * 5 + dx + 2;
                        if (stepSeen != null && stepSeen.TryGetValue(sk, out byte seen)) { why = seen == 2 ? "" : null; ford = seen == 1; }
                        else if (ad == 3) why = KnightFault(ax, az, bx, bz, ha, hb, out ford);
                        else if (dx != 0 && dz != 0)
                        {
                            float hm = Lattice(2 * cx + dx, 2 * cz + dz), mx = (cx + 0.5f * dx) * cm, mz = (cz + 0.5f * dz) * cm;
                            why = StepFault(ax, az, mx, mz, ha, hm, out bool f0);
                            bool f1 = false;
                            if (why == null) why = StepFault(mx, mz, bx, bz, hm, hb, out f1);
                            ford = why == null && (f0 || f1);
                        }
                        else why = StepFault(ax, az, bx, bz, ha, hb, out ford);
                        if (stepSeen != null) stepSeen[sk] = why != null ? (byte)2 : ford ? (byte)1 : (byte)0;
                        if (why != null) { if (span) Span(cur, cx, cz, dx, dz); continue; }
                        float grade = rise / len;
                        float c = cost[cur] + len * (1 + RoadClimb * grade) * (ford ? FordCost : 1f);
                        if (c >= cost[j]) continue;
                        cost[j] = c; from[j] = cur; tun[j] = 0;
                        HeapPush(heap, (c + Heuristic(nx, nz, tx, tz), j));
                    }
            }
            if (!done[goal]) return null;
            var path = new List<int>();
            jumps = new List<byte>();
            for (int k = goal; k >= 0; k = from[k]) { path.Add((k / bw + z0) * n + k % bw + x0); jumps.Add(tun[k]); }
            path.Reverse();
            jumps.Reverse();
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
                var fill = pass == 1 ? start : null;
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
                else { for (int i = n * n; i > 0; i--) start[i] = start[i - 1]; start[0] = 0; }
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
