using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public static readonly string[] DiscoveryNames = { "", "hilltop", "clearing", "camp", "hermit", "shrine", "ruin", "spring", "fairy-ring", "geyser", "mud-pot", "wolf-den", "dungeon", "overlook", "bones", "salt-tower", "fallen-tree", "lone-inn", "monument", "shipwreck", "watchtower", "sunken-temple", "ancient-tree", "fumarole-field", "great-mushroom" };
        static readonly string[] DiscoveryStreams = { "", "discoveries", "discoveries-clearing", "discoveries-camp", "discoveries-hermit", "discoveries-shrine", "discoveries-ruin", "discoveries-spring", "discoveries-fairy-ring", "discoveries-geyser", "discoveries-mud-pot", "discoveries-wolf-den", "discoveries-dungeon", "discoveries-overlook", "discoveries-bones", "discoveries-salt-tower", "discoveries-fallen-tree", "discoveries-lone-inn", "discoveries-monument", "discoveries-shipwreck", "discoveries-watchtower", "discoveries-sunken-temple", "discoveries-ancient-tree", "discoveries-fumarole-field", "discoveries-great-mushroom" };
        public const int Hilltop = 1, Clearing = 2, Camp = 3, Hermit = 4, Shrine = 5, Ruin = 6, Spring = 7, FairyRing = 8, Geyser = 9, MudPot = 10, WolfDen = 11, Dungeon = 12, Overlook = 13, Bones = 14, SaltTower = 15, FallenTree = 16, LoneInn = 17, Monument = 18, Shipwreck = 19, Watchtower = 20, SunkenTemple = 21, AncientTree = 22, FumaroleField = 23, GreatMushroom = 24;
        public const int VentsMin = 2, VentsMax = 4, FieldTries = 12, VentSitesMin = 4, VentBatch = 64;
        public const float VentSiteStepM = 30f;
        public const float VentFieldM = 120f, VentFieldRadiusM = 60f, VentGapM = 40f, SpringRiverM = 40f, SpringReliefM = 8f, VentReliefM = 6f, VentSteady = 0.15f;
        public const float DiscoveryInset = 0.2f, TownClearM = 400f, DiscoveryProbeM = 40f, HilltopRiseM = 1f, ClearingReliefM = 1.5f;
        public const float DiscoveryGapM = 250f, CampReliefM = 3f, CampRiverM = 40f, ForestPerHectare = 30f, HermitWoods = 0.6f, RuinReliefM = 4f, FairyReliefM = 2f, FairyWoods = 0.35f, DenReliefM = 4f, DenProbeM = 6f, DenSeekM = 50f, DenRiverM = 40f, DenWoods = 0.25f;
        public const float ShrineRoadMinM = 9f, ShrineRoadM = 30f, ShrineRoadAimM = 13f, ShrineProbeM = 12f, ShrineReliefM = 6f, ShrineSeatUp = 0.16f, ShrineSeatDown = 0.08f;
        public const int DenSeekRings = 3, DiscoveryTries = 16, ClimbSteps = 12, FitBatch = 4;
        public const float ClimbStartM = 16f, ClimbEndM = 2f;
        readonly object discoveryLock = new object();
        ObjectInstance[] discoveries;
        ObjectInstance[][] builtSites;
        float[] treeCover;
        List<LandmarkSketch> towns;
        WorldSketch sketch;
        Geothermal hotspots;

        public List<LandmarkSketch> Towns => towns ?? (towns = (w.Sketch ?? (sketch ?? (sketch = WorldSketch.Generate(w.Config)))).Landmarks);

        public static float DiscoveryStep(float worldM, float spacingM) => worldM / Math.Max(1, (int)Math.Floor(worldM / spacingM));

        public ObjectInstance[] Discoveries()
        {
            var found = discoveries;
            if (found != null) return found;
            if (roads == null) Roads();
            if (discoveries != null) return discoveries;
            lock (discoveryLock)
            {
                if (discoveries != null) return discoveries;
                builtSites = new ObjectInstance[across * across][];
                var list = new List<ObjectInstance>();
                PlaceFields(list);
                _ = Towns;
                Woods(0, 0);
                for (int pass = 0; pass < 2; pass++)
                    for (int d = 0; d < lib.Objects.Count; d++)
                    {
                        int kind = lib.Objects[d].Placement.Discovery;
                        if (kind > 0 && !Pooled(kind) && (kind == LoneInn) == (pass == 0)) PlaceDiscoveries(d, Rng.Stream(w.Config.Seed, DiscoveryStreams[kind]).NextU64(), list);
                    }
                builtSites = null;
                return discoveries = list.ToArray();
            }
        }

        public ObjectField Fresh() => Fresh(edits, Discoveries());

        public ObjectField Fresh(FeatureEdit[] placed, ObjectInstance[] found)
        {
            var f = new ObjectField(w, detail, placed, lib);
            f.discoveries = found;
            f.bogPools = BogPools();
            f.oasisPools = OasisPools();
            f.quicksandPools = QuicksandPools();
            f.roads = Roads(); f.roadStart = roadStart; f.roadItems = roadItems; f.roadSites = roadSites; f.siteCells = siteCells; f.siteList = siteList; f.tunnelEdits = tunnelEdits; f.tunnelGraphs = tunnelGraphs;
            f.sketch = sketch; f.towns = towns; f.townPlans = townPlans; f.townChunks = townChunks; f.hotspots = hotspots; f.treeCover = treeCover;
            return f;
        }

        struct DiscoveryTry
        {
            public float Fit, X, Z, Size, Qa, Qb;
            public int Variant, Cell;
            public bool Pass;
            public ObjectInstance O;
        }

        void PlaceDiscoveries(int d, ulong root, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            if (pl.Discovery == Shrine || pl.Discovery == LoneInn) { PlaceBesideRoads(d, root, list); return; }
            float step = DiscoveryStep(worldM, pl.SpacingM), inset = DiscoveryInset * step;
            int cells = (int)Math.Round(worldM / step);
            var drawn = new DiscoveryTry[cells * cells][];
            Parallel.For(0, cells * cells, c => drawn[c] = CellDraws(d, root, c, cells, step, inset));
            var all = new List<DiscoveryTry[]>();
            foreach (var tries in drawn) if (tries != null) all.Add(tries);
            Parallel.For(0, all.Count * DiscoveryTries, q => EvalTry(d, cells, step, inset, ref all[q / DiscoveryTries][q % DiscoveryTries]));
            foreach (var tries in all)
            {
                bool any = false;
                float best = float.MinValue;
                ObjectInstance pick = default;
                foreach (var t in tries)
                {
                    if (t.Fit <= best || !t.Pass || !ApartFromOthers(d, t.X, t.Z, list)) continue;
                    best = t.Fit; pick = t.O; any = true;
                }
                if (any) list.Add(pick);
            }
        }

        DiscoveryTry[] CellDraws(int d, ulong root, int c, int cells, float step, float inset)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            int gx = c % cells, gz = c / cells;
            var rng = new Rng(Rng.Mix(root ^ key[d] ^ ((ulong)(gz * cells + gx + 1) * 0x9E3779B97F4A7C15UL)));
            if (!(rng.NextFloat() < pl.Chance)) return null;
            var tries = new DiscoveryTry[DiscoveryTries];
            for (int t = 0; t < DiscoveryTries; t++)
            {
                ref var r = ref tries[t];
                r.Cell = c;
                r.X = gx * step + inset + rng.NextFloat() * (step - 2 * inset); r.Z = gz * step + inset + rng.NextFloat() * (step - 2 * inset);
                r.Size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * rng.NextFloat(); r.Qa = rng.Range(-1f, 1f); r.Qb = rng.Range(-1f, 1f);
                r.Variant = (int)(rng.NextU32() % (uint)def.Variants);
                r.Fit = float.MinValue;
            }
            return tries;
        }

        void EvalTry(int d, int cells, float step, float inset, ref DiscoveryTry r)
        {
            var pl = lib.Objects[d].Placement;
            int gx = r.Cell % cells, gz = r.Cell / cells;
            float px = r.X, pz = r.Z, qa = r.Qa, qb = r.Qb;
            float x0 = gx * step + inset, z0 = gz * step + inset, x1 = (gx + 1) * step - inset, z1 = (gz + 1) * step - inset;
            if (pl.Discovery == Hilltop || pl.Discovery == Watchtower) Climb(ref px, ref pz, x0, z0, x1, z1);
            if (pl.Discovery == Watchtower) WatchFace(px, pz, ref qa, ref qb);
            if (pl.Discovery == WolfDen)
            {
                if (!SeekDen(ref px, ref pz, x0, z0, x1, z1) || !ToFace(ref px, ref pz, DenRear(d, r.Variant, r.Size), x0, z0, x1, z1, out qa, out qb)) return;
            }
            if (pl.Discovery == SaltTower && !SeekSalt(ref px, ref pz, x0, z0, x1, z1)) return;
            if (pl.Discovery == FumaroleField && !SeekFumarole(ref px, ref pz, x0, z0, x1, z1)) return;
            if (pl.Discovery == Shipwreck && !SeekShore(ref px, ref pz, x0, z0, x1, z1, out qa, out qb)) return;
            if (pl.Discovery == Overlook && !SeekOverlook(d, r.Variant, r.Size, ref px, ref pz, x0, z0, x1, z1, out qa, out qb)) return;
            if (pl.Discovery == GreatMushroom && !SeekMound(ref px, ref pz, x0, z0, x1, z1)) return;
            if (pl.Discovery == SunkenTemple && !SeekTemple(d, r.Variant, r.Size, ref px, ref pz, x0, z0, x1, z1, out qa, out qb)) return;
            if (Pooled(pl.Discovery)) FaceDownhill(px, pz, ref qa, ref qb);
            if (pl.Discovery == FallenTree) FaceDownhill(px, pz, ref qa, ref qb);
            if (pl.Discovery == Dungeon)
            {
                FaceDownhill(px, pz, ref qa, ref qb);
                (qa, qb) = (-qb, qa);
            }
            float fit = Fitness(pl.Discovery, px, pz);
            if (fit == float.MinValue) return;
            r.Fit = fit; r.X = px; r.Z = pz;
            r.Pass = SiteFits(d, r.Variant, px, pz, r.Size, qa, qb, out r.O);
        }

        void PlaceFields(List<ObjectInstance> list)
        {
            var vents = VentDefs;
            var hot = Hotspots;
            var present = new List<int>();
            var best = new List<ObjectInstance>();
            var sites = new List<VentSite>();
            var usable = new List<VentSite>();
            var near = new List<VentSite>();
            var dense = new List<VentSite>();
            var counts = new int[vents.Length];
            var cluster = new int[vents.Length];
            int gk = Array.FindIndex(vents, d => lib.Objects[d].Placement.Discovery == Geyser);
            ulong root = Rng.Stream(w.Config.Seed, "geothermal-fields").NextU64(), siteRoot = Rng.Stream(w.Config.Seed, "geothermal-field-sites").NextU64();
            for (int h = 0; h < hot.Count; h++)
            {
                var rng = new Rng(Rng.Mix(root ^ ((ulong)(h + 1) * 0x9E3779B97F4A7C15UL)));
                var pick = new Rng(Rng.Mix(siteRoot ^ ((ulong)(h + 1) * 0x9E3779B97F4A7C15UL)));
                sites.Clear();
                VentSites(hot.X[h], hot.Z[h], hot.R[h], sites, int.MaxValue, cluster);
                Array.Clear(counts, 0, counts.Length);
                foreach (var s in sites) for (int k = 0; k < vents.Length; k++) if ((s.Mask & (1 << k)) != 0) counts[k]++;
                int mask = 0, most = -1;
                for (int k = 0; k < vents.Length; k++)
                {
                    if (rng.NextFloat() < lib.Objects[vents[k]].Placement.Chance && counts[k] >= VentsMin) mask |= 1 << k;
                    if (cluster[k] >= VentSitesMin && (most < 0 || cluster[k] > cluster[most])) most = k;
                }
                int target = VentsMin + (int)(rng.NextU32() % (uint)(VentsMax - VentsMin + 1)), first = (int)(rng.NextU32() % 64);
                if (most < 0) continue;
                bool want = gk >= 0 && cluster[gk] >= VentSitesMin;
                if (want) mask |= 1 << gk;
                if (mask == 0) mask = 1 << most;
                best.Clear();
                int bestScore = 0;
                for (int pass = 0; pass < 2 && best.Count < VentsMin; pass++)
                {
                    if (pass == 1) { if (mask == 1 << most) break; mask = 1 << most; want = false; }
                    present.Clear();
                    usable.Clear();
                    for (int k = 0; k < vents.Length; k++) if ((mask & (1 << k)) != 0) present.Add(vents[k]);
                    dense.Clear();
                    foreach (var s in sites) if ((s.Mask & mask) != 0) usable.Add(s);
                    foreach (var s in usable)
                    {
                        int c = 0;
                        foreach (var o in usable)
                        {
                            float dx = o.X - s.X, dz = o.Z - s.Z;
                            if (dx * dx + dz * dz <= VentFieldRadiusM * VentFieldRadiusM) c++;
                        }
                        if (c >= VentSitesMin) dense.Add(s);
                    }
                    var centres = dense.Count > 0 ? dense : usable;
                    if (want && centres.Exists(s => (s.Mask & (1 << gk)) != 0)) centres = centres.FindAll(s => (s.Mask & (1 << gk)) != 0);
                    int full = target + (want ? VentsMax + 1 : 0), lead = want ? present.IndexOf(vents[gk]) : 0;
                    for (int c = 0; c < FieldTries && bestScore < full; c++)
                    {
                        var site = centres[(int)(pick.NextU32() % (uint)centres.Count)];
                        near.Clear();
                        foreach (var s in usable)
                        {
                            float dx = s.X - site.X, dz = s.Z - site.Z;
                            if (dx * dx + dz * dz <= VentFieldRadiusM * VentFieldRadiusM) near.Add(s);
                        }
                        int start = list.Count;
                        for (int v = 0; v < target; v++)
                            for (int k = 0; k < present.Count; k++)
                                if (PlaceVent(present[want ? VentOrder(v, k, first, lead, present.Count) : (first + v + k) % present.Count], site, near, ref rng, list)) break;
                        int got = list.Count - start, score = got;
                        if (want && got >= VentsMin) for (int i = start; i < list.Count; i++) if (list[i].Def == vents[gk]) { score += VentsMax + 1; break; }
                        if (score > bestScore) { bestScore = score; best.Clear(); for (int i = start; i < list.Count; i++) best.Add(list[i]); }
                        list.RemoveRange(start, list.Count - start);
                    }
                }
                if (best.Count >= VentsMin) list.AddRange(best);
            }
        }

        static int VentOrder(int v, int k, int first, int lead, int count)
        {
            if (v == 0) return (lead + k) % count;
            if (k == count - 1) return lead;
            return (lead + 1 + (first + v + k) % (count - 1)) % count;
        }

        bool PlaceVent(int d, VentSite centre, List<VentSite> near, ref Rng rng, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            bool any = false;
            float best = float.MinValue;
            ObjectInstance pick = default;
            int bit = 1 << Array.IndexOf(VentDefs, d);
            var tries = new DiscoveryTry[DiscoveryTries];
            int count = 0;
            for (int t = 0; t < DiscoveryTries; t++)
            {
                var site = near[(int)(rng.NextU32() % (uint)near.Count)];
                float jitter = (t & 1) * VentSiteStepM, px = site.X + rng.Range(-0.5f, 0.5f) * jitter, pz = site.Z + rng.Range(-0.5f, 0.5f) * jitter;
                float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                int variant = (int)(rng.NextU32() % (uint)def.Variants);
                float cx = px - centre.X, cz = pz - centre.Z;
                if ((site.Mask & bit) == 0 || cx * cx + cz * cz > VentFieldRadiusM * VentFieldRadiusM || !ApartFromKin(d, px, pz, VentGapM, list)) continue;
                tries[count++] = new DiscoveryTry { X = px, Z = pz, Size = size, Qa = qa, Qb = qb, Variant = variant };
            }
            Parallel.For(0, count, q =>
            {
                ref var r = ref tries[q];
                FaceDownhill(r.X, r.Z, ref r.Qa, ref r.Qb);
                r.Fit = Fitness(pl.Discovery, r.X, r.Z);
            });
            var order = new List<int>(count);
            bool nan = false;
            for (int q = 0; q < count; q++)
            {
                nan |= float.IsNaN(tries[q].Fit);
                if (tries[q].Fit > best) order.Add(q);
            }
            if (nan)
            {
                Parallel.For(0, count, q =>
                {
                    ref var r = ref tries[q];
                    r.Pass = ApartFromOthers(d, r.X, r.Z, list) && SiteFits(d, r.Variant, r.X, r.Z, r.Size, r.Qa, r.Qb, out r.O);
                });
                for (int q = 0; q < count; q++)
                {
                    if (tries[q].Fit <= best || !tries[q].Pass) continue;
                    best = tries[q].Fit; pick = tries[q].O; any = true;
                }
                if (any) list.Add(pick);
                return any;
            }
            order.Sort((a, b) => tries[a].Fit != tries[b].Fit ? tries[b].Fit.CompareTo(tries[a].Fit) : a.CompareTo(b));
            for (int start = 0; start < order.Count && !any; start += FitBatch)
            {
                int end = Math.Min(order.Count, start + FitBatch);
                Parallel.For(start, end, k =>
                {
                    ref var r = ref tries[order[k]];
                    r.Pass = ApartFromOthers(d, r.X, r.Z, list) && SiteFits(d, r.Variant, r.X, r.Z, r.Size, r.Qa, r.Qb, out r.O);
                });
                for (int k = start; k < end; k++)
                {
                    if (!tries[order[k]].Pass) continue;
                    pick = tries[order[k]].O; any = true;
                    break;
                }
            }
            if (any) list.Add(pick);
            return any;
        }

        void PlaceBesideRoads(int d, ulong root, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            bool inn = pl.Discovery == LoneInn;
            float near = inn ? LoneInnRoadMinM : ShrineRoadMinM, far = inn ? LoneInnRoadM : ShrineRoadM;
            int tries = inn ? LoneInnTries : DiscoveryTries;
            var segs = Roads();
            float walked = 0, lx = float.NaN, lz = float.NaN;
            int station = 0;
            foreach (var s in segs)
            {
                if (s.Kind == RoadSegment.Lane || s.Kind == RoadSegment.Tunnel || s.Kind == RoadSegment.Bridge) { lx = float.NaN; continue; }
                if (s.X0 != lx || s.Z0 != lz) walked = 0;
                lx = s.X1; lz = s.Z1;
                float dx = s.X1 - s.X0, dz = s.Z1 - s.Z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-3f) continue;
                float ux = dx / len, uz = dz / len, next = pl.SpacingM - walked;
                for (; next <= len; next += pl.SpacingM)
                {
                    var rng = new Rng(Rng.Mix(root ^ key[d] ^ ((ulong)(++station) * 0x9E3779B97F4A7C15UL)));
                    if (rng.NextFloat() >= pl.Chance) continue;
                    float cx = s.X0 + ux * next, cz = s.Z0 + uz * next;
                    bool any = false;
                    float best = float.MinValue;
                    ObjectInstance pick = default;
                    for (int t = 0; t < tries; t++)
                    {
                        float along = (DiscoveryInset - 0.5f + rng.NextFloat() * (1 - 2 * DiscoveryInset)) * pl.SpacingM, off = (rng.NextFloat() < 0.5f ? -1 : 1) * rng.Range(near, far);
                        float px = cx + ux * along - uz * off, pz = cz + uz * along + ux * off;
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * rng.NextFloat(), qa = 1f, qb = 0f;
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        if (!ApartFromKin(d, px, pz, 0.5f * pl.SpacingM, list)) continue;
                        FaceRoad(px, pz, ref qa, ref qb);
                        any |= TrySite(d, variant, px, pz, size, qa, qb, list, ref best, ref pick);
                    }
                    if (any) list.Add(pick);
                }
                walked = len - (next - pl.SpacingM);
            }
        }

        bool TrySite(int d, int variant, float px, float pz, float size, float qa, float qb, List<ObjectInstance> list, ref float best, ref ObjectInstance pick)
        {
            float fit = Fitness(lib.Objects[d].Placement.Discovery, px, pz);
            if (fit <= best || !ApartFromOthers(d, px, pz, list) || !SiteFits(d, variant, px, pz, size, qa, qb, out var o)) return false;
            best = fit; pick = o;
            return true;
        }

        bool SiteFits(int d, int variant, float px, float pz, float size, float qa, float qb, out ObjectInstance o)
        {
            if (!Open(d, variant, size, px, pz) || !Accept(d, px, pz, size, qa, qb, out o, site: true)) { o = default; return false; }
            int kind = lib.Objects[d].Placement.Discovery;
            bool shrine = kind == Shrine, spring = Pooled(kind);
            int tries = shrine || spring || kind == Ruin || kind == FairyRing || kind == WolfDen || kind == Dungeon || kind == Overlook || kind == Bones || kind == SaltTower || kind == FallenTree || kind == LoneInn || kind == Monument || kind == Shipwreck || kind == Watchtower || kind == SunkenTemple || kind == AncientTree || kind == FumaroleField || kind == GreatMushroom ? lib.Objects[d].Variants : 1, k = 0;
            for (; k < tries; k++)
            {
                o.Variant = (variant + k) % lib.Objects[d].Variants;
                if (ClearOfBuilt(o) && !OnRoad(o.X, o.Z, SiteRadius(o)) && (!shrine || SeatGrounded(o)) && (!spring || VentFault(o) == null) && (kind != WolfDen || (DenFault(o) == null && DenBackFault(o) == null && DenCoverFault(o) == null)) && (kind != Dungeon || DungeonFault(o) == null) && (kind != Overlook || OverlookFault(o) == null) && (kind != Bones || BonesFault(o) == null) && (kind != SaltTower || SaltFault(o) == null) && (kind != FallenTree || TreeFault(o) == null) && (kind != LoneInn || LoneInnFault(o) == null) && (kind != Monument || MonumentFault(o) == null) && (kind != Shipwreck || ShipwreckFault(o) == null) && (kind != Watchtower || WatchFault(o) == null) && (kind != SunkenTemple || TempleFault(o) == null) && (kind != AncientTree || AncientFault(o) == null) && (kind != FumaroleField || FumaroleFault(o) == null) && (kind != GreatMushroom || GreatMushroomFault(o) == null)) break;
            }
            return k < tries;
        }

        public bool SeatGrounded(ObjectInstance o)
        {
            var def = lib.Objects[o.Def];
            Structures.WaysideSeat(ObjectMeshes.Key(def.Id, 0, o.Variant), out float x, out float z, out float c, out float s);
            float u = Structures.WaysideU * o.Size, foot = o.Y + def.Placement.Sink * o.Size;
            for (int e = -1; e <= 1; e++)
            {
                Rotate(o, (x + e * Structures.SeatFootM * c) * u, 0, (z + e * Structures.SeatFootM * s) * u, out float rx, out _, out float rz);
                float g = GroundAt(detail, cm, o.X + rx, o.Z + rz) - foot;
                if (g > ShrineSeatUp * u || g < -ShrineSeatDown * u) return false;
            }
            return true;
        }

        static bool ApartFromKin(int d, float x, float z, float gapM, List<ObjectInstance> list)
        {
            foreach (var o in list)
            {
                if (o.Def != d) continue;
                float dx = o.X - x, dz = o.Z - z;
                if (dx * dx + dz * dz < gapM * gapM) return false;
            }
            return true;
        }

        bool Open(int d, int variant, float size, float x, float z)
        {
            float r = SiteRadius(new ObjectInstance { Def = d, Variant = variant, Size = size });
            int n = w.Config.Size;
            bool shore = lib.Objects[d].Placement.Discovery == SunkenTemple;
            for (int k = 0; k < 9; k++)
            {
                float ux = k % 3 - 1, uz = k / 3 - 1, len = ux != 0 && uz != 0 ? 0.70710678f : 1f;
                float px = x + r * ux * len, pz = z + r * uz * len;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return false;
                int i = Math.Min(n - 1, (int)(pz / cm)) * n + Math.Min(n - 1, (int)(px / cm));
                if (w.Sea[i] || ((!shore || k == 4) && w.Water.LakeId[i] >= 0)) return false;
            }
            foreach (var l in Towns)
            {
                float dx = l.X * cm - x, dz = l.Y * cm - z;
                if (dx * dx + dz * dz < TownClearM * TownClearM) return false;
            }
            return !InTownGround(x, z, r, true);
        }

        bool ApartFromOthers(int d, float x, float z, List<ObjectInstance> list)
        {
            bool vent = Pooled(lib.Objects[d].Placement.Discovery);
            foreach (var o in list)
            {
                if (o.Def == d) continue;
                float dx = o.X - x, dz = o.Z - z, g = vent && IsVent(o) ? VentGapM : DiscoveryGapM;
                if (dx * dx + dz * dz < g * g) return false;
            }
            return true;
        }

        void FaceRoad(float x, float z, ref float qa, ref float qb)
        {
            float d = RoadSide(x, z, out float rx, out float rz);
            if (d == float.MaxValue || d < 1e-3f) return;
            float vx = (rx - x) / d, vz = (rz - z) / d;
            qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + vz)));
            qb = (vx < 0 ? -1 : 1) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - vz)));
        }

        public float CampDraw(float x, float z) => ForestEdge(x, z) + Riverside(x, z);

        float ForestEdge(float x, float z) => 1 - Math.Abs(2 * Woods(x, z) - 1);

        public float Woods(float x, float z)
        {
            if (treeCover == null)
            {
                int biomes = w.BiomeIds == null ? 0 : w.BiomeIds.Length;
                var cover = new float[biomes];
                for (int d = 0; d < lib.Objects.Count; d++)
                {
                    var def = lib.Objects[d];
                    bool tree = false;
                    foreach (var p in def.Parts) tree |= p.Shape == ObjectPart.Broadleaf || p.Shape == ObjectPart.Conifer;
                    if (!tree) continue;
                    for (int b = 0; b < biomes; b++) cover[b] += def.Placement.PerHectare * density[d][b];
                }
                treeCover = cover;
            }
            int forested = 0, probes = 0;
            for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < 8; k++)
                {
                    FanDir(k, 0.70710678f, out float ux, out float uz);
                    float px = x + ux * ring * DiscoveryProbeM, pz = z + uz * ring * DiscoveryProbeM;
                    probes++;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) continue;
                    int b = BiomeBorder.At(w, px, pz);
                    if (b >= 0 && treeCover[b] >= ForestPerHectare) forested++;
                }
            return forested / (float)probes;
        }

        float Riverside(float x, float z)
        {
            int gx = Cell(x), gz = Cell(z);
            float best = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int i = az * n + ax;
                    for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
                    {
                        int item = blockItems[k];
                        if (item < 0) continue;
                        float near = 1 - (Distance(item, x, z) - phw[item]) / CampRiverM;
                        best = Math.Max(best, Math.Min(1, near));
                    }
                }
            return best;
        }

        public float RiverGap(float x, float z)
        {
            int gx = Cell(x), gz = Cell(z);
            float best = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int i = az * n + ax;
                    for (int k = blockStart[i]; k < blockStart[i + 1]; k++)
                    {
                        int item = blockItems[k];
                        if (item < 0) continue;
                        best = Math.Min(best, Distance(item, x, z) - phw[item]);
                    }
                }
            return best;
        }

        public Geothermal Hotspots
        {
            get
            {
                if (hotspots != null) return hotspots;
                _ = Towns;
                _ = VentDefs;
                return hotspots = Geothermal.Build(w, w.Sketch ?? (sketch ?? (sketch = WorldSketch.Generate(w.Config))), (x, z, r) => VentSites(x, z, r, null, VentSitesMin) >= VentSitesMin);
            }
        }

        void FaceDownhill(float x, float z, ref float qa, ref float qb)
        {
            float s = SlopeStepM;
            float gx = (GroundAt(detail, cm, x + s, z) - GroundAt(detail, cm, x - s, z)) / (2 * s);
            float gz = (GroundAt(detail, cm, x, z + s) - GroundAt(detail, cm, x, z - s)) / (2 * s);
            float len = (float)Math.Sqrt(gx * gx + gz * gz);
            if (len < 1e-4f) return;
            float vx = -gx / len, vz = -gz / len;
            qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + vz)));
            qb = (vx < 0 ? -1 : 1) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - vz)));
        }

        public bool IsSpring(ObjectInstance o) => lib.Objects[o.Def].Placement.Discovery == Spring;
        public bool InVentField(ObjectInstance s, ObjectInstance[] found)
        {
            int h = Hotspots.Index(s.X, s.Z);
            foreach (var o in found)
            {
                if (!IsVent(o) || (o.X == s.X && o.Z == s.Z) || Hotspots.Index(o.X, o.Z) != h) continue;
                float dx = o.X - s.X, dz = o.Z - s.Z;
                if (dx * dx + dz * dz <= VentFieldM * VentFieldM) return true;
            }
            return false;
        }

        public int VentSites(float hx, float hz, float radius, List<VentSite> sites = null, int stop = int.MaxValue, int[] cluster = null)
        {
            var vents = VentDefs;
            float reach = radius - VentFieldRadiusM;
            int steps = (int)(reach / VentSiteStepM), found = 0, start = (sites ??= new List<VentSite>()).Count;
            var nb = new List<int>();
            if (cluster != null) Array.Clear(cluster, 0, cluster.Length);
            _ = Towns;
            var grid = new List<float>();
            for (int a = -steps; a <= steps; a++)
                for (int b = -steps; b <= steps; b++)
                {
                    float dx = b * VentSiteStepM, dz = a * VentSiteStepM;
                    if (dx * dx + dz * dz > reach * reach) continue;
                    grid.Add(hx + dx); grid.Add(hz + dz);
                }
            int points = grid.Count / 2;
            var masks = new int[Math.Min(points, VentBatch)];
            for (int at = 0; at < points && found < stop; at += VentBatch)
            {
                int from = at, count = Math.Min(VentBatch, points - at);
                Parallel.For(0, count, q => masks[q] = VentGround(grid[2 * (from + q)], grid[2 * (from + q) + 1], vents));
                for (int p = 0; p < count && found < stop; p++)
                {
                    int mask = masks[p];
                    if (mask == 0) continue;
                    var site = new VentSite { X = grid[2 * (from + p)], Z = grid[2 * (from + p) + 1], Mask = mask };
                    int i = sites.Count - start;
                    for (int k = 0; k < vents.Length; k++) nb.Add((mask & (1 << k)) != 0 ? 1 : 0);
                    for (int j = 0; j < i; j++)
                    {
                        var o = sites[start + j];
                        int shared = o.Mask & mask;
                        float ox = o.X - site.X, oz = o.Z - site.Z;
                        if (shared == 0 || ox * ox + oz * oz > VentFieldRadiusM * VentFieldRadiusM) continue;
                        for (int k = 0; k < vents.Length; k++)
                            if ((shared & (1 << k)) != 0) { Tally(nb, j * vents.Length + k, k, cluster, ref found); Tally(nb, i * vents.Length + k, k, cluster, ref found); }
                    }
                    sites.Add(site);
                    for (int k = 0; k < vents.Length; k++) if ((mask & (1 << k)) != 0) Tally(nb, i * vents.Length + k, k, cluster, ref found, 0);
                }
            }
            return found;
        }

        static void Tally(List<int> nb, int at, int k, int[] cluster, ref int found, int add = 1)
        {
            int c = nb[at] += add;
            found = Math.Max(found, c);
            if (cluster != null && c > cluster[k]) cluster[k] = c;
        }

        public int HotspotSites(int h) => VentSites(Hotspots.X[h], Hotspots.Z[h], Hotspots.R[h]);

        public int KindCluster(int h, int discovery)
        {
            int k = Array.FindIndex(VentDefs, d => lib.Objects[d].Placement.Discovery == discovery);
            if (k < 0) return 0;
            var cluster = new int[VentDefs.Length];
            VentSites(Hotspots.X[h], Hotspots.Z[h], Hotspots.R[h], null, int.MaxValue, cluster);
            return cluster[k];
        }

        int VentGround(float x, float z, int[] vents)
        {
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return 0;
            int b = BiomeBorder.At(w, x, z), mask = 0;
            if (b < 0) return 0;
            for (int k = 0; k < vents.Length; k++) if (density[vents[k]][b] > 0) mask |= 1 << k;
            if (mask == 0) return 0;
            float y = GroundAt(detail, cm, x, z), relief = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * DiscoveryProbeM, pz = z + uz * DiscoveryProbeM;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return 0;
                relief = Math.Max(relief, Math.Abs(y - GroundAt(detail, cm, px, pz)));
            }
            if (relief > Math.Max(SpringReliefM, VentReliefM) || RiverGap(x, z) < SpringRiverM || UnderMesa(x, z, 0)) return 0;
            float s = SlopeStepM, gxs = (GroundAt(detail, cm, x + s, z) - GroundAt(detail, cm, x - s, z)) / (2 * s), gzs = (GroundAt(detail, cm, x, z + s) - GroundAt(detail, cm, x, z - s)) / (2 * s);
            float slope = (float)Math.Sqrt(gxs * gxs + gzs * gzs), hard = strata.Hardness(Cell(z) * n + Cell(x), y) / contrast;
            for (int k = 0; k < vents.Length; k++)
            {
                if ((mask & (1 << k)) == 0) continue;
                var pl = lib.Objects[vents[k]].Placement;
                if (relief > (pl.Discovery == Spring ? SpringReliefM : VentReliefM) || slope < pl.SlopeMin || slope > pl.SlopeMax || hard < pl.HardnessMin || hard > pl.HardnessMax || !Open(vents[k], 0, pl.SizeMinM, x, z)) mask &= ~(1 << k);
            }
            return mask;
        }

        bool UnderMesa(float x, float z, float r)
        {
            if (w.Sketch == null) return false;
            foreach (var m in w.Sketch.Mesas) if (Mesas.Dist(m, x, z) - r < Mesas.Toe(m, BogSteadyM)) return true;
            return Volcanoes.Near(w.Sketch, x, z) < r;
        }

        int[] ventDefs;
        int[] VentDefs
        {
            get
            {
                if (ventDefs != null) return ventDefs;
                var v = new List<int>();
                for (int d = 0; d < lib.Objects.Count; d++) if (Pooled(lib.Objects[d].Placement.Discovery)) v.Add(d);
                return ventDefs = v.ToArray();
            }
        }

        public bool IsVent(ObjectInstance o) => Pooled(lib.Objects[o.Def].Placement.Discovery);
        public static bool Pooled(int kind) => kind == Spring || kind == Geyser || kind == MudPot;
        public int VentKind(ObjectInstance o) => lib.Objects[o.Def].Placement.Discovery;

        public MaterialLibrary Materials => w.Materials;

        public int LiquidOf(ObjectInstance o)
        {
            string id = lib.Objects[o.Def].Placement.Liquid;
            return id == null || w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid(id));
        }

        public static void LocalPools(int kind, ulong key, List<float> local)
        {
            if (kind == Geyser) Structures.GeyserPools(key, local);
            else if (kind == MudPot) Structures.MudPools(key, local);
            else Structures.SpringPools(key, local);
        }

        public ulong VentSeed(ObjectInstance o) => Rng.Mix(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant) ^ ((ulong)(uint)(int)(o.X * 16f) << 32 | (uint)(int)(o.Z * 16f)));

        public const int SpringStride = 9;
        public const float SpringStepU = 0.2f, SpringFloorU = 3f, SpringTopU = 2f, GeyserLipU = 0.15f, GeyserVentU = 2.5f, GeyserTaper = 0.9f, GeyserFootU = 0.5f, MudSinkU = 0.15f, MudFillU = 0.3f, MudCrestU = 0.45f, MudDepthU = 0.5f, MudBankU = 0.3f, MudBank = 1.1f, MudShelf = 2.5f, MudShelfU = 0.08f, MudShelfTopU = 0.2f, MudRimU = 0.7f, MudRimSlope = 0.7f, MudLobe = 0.3f, GeyserLobe = 0.18f;

        public void SpringPoolsAt(ObjectInstance o, List<float> world)
        {
            world.Clear();
            var local = new List<float>();
            int kind = VentKind(o);
            ulong key = ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant);
            LocalPools(kind, key, local);
            float u = Structures.SpringU * o.Size, last = float.MaxValue, lift = 0;
            if (kind == Geyser) { Structures.GeyserShape(key, out float mound, out _, out float cone, out _); lift = (mound + cone) * u; }
            for (int k = 0; k < local.Count; k += Structures.PoolStride)
            {
                Rotate(o, local[k] * u, 0, local[k + 1] * u, out float rx, out _, out float rz);
                float x = o.X + rx, z = o.Z + rz, r = local[k + 2] * u, rout = local[k + 3] * u;
                float sum = 0, low = float.MaxValue, high = GroundAt(detail, cm, x, z);
                for (int p = 0; p < 8; p++)
                {
                    FanDir(p, 0.70710678f, out float ux, out float uz);
                    float g = GroundAt(detail, cm, x + ux * r, z + uz * r);
                    sum += g; high = Math.Max(high, g);
                    low = Math.Min(low, GroundAt(detail, cm, x + ux * rout, z + uz * rout));
                }
                float level = Math.Min(sum / 8, last - SpringStepU * u), crest = level + Structures.SpringCrest * u, floor = level - Structures.SpringDepth * u;
                if (kind == Geyser)
                {
                    crest = (sum + high) / 9 + lift; level = crest - GeyserLipU * u; floor = level - GeyserVentU * u;
                    for (int p = 0; p < 8; p++)
                    {
                        FanDir(p, 0.70710678f, out float ux, out float uz);
                        high = Math.Max(high, GroundAt(detail, cm, x + ux * rout, z + uz * rout));
                    }
                }
                else if (kind == MudPot) { level = sum / 8 + (MudFillU - MudSinkU) * u; crest = level + (MudCrestU - MudFillU) * u; floor = level - (MudDepthU + MudFillU) * u; }
                last = level;
                world.Add(x); world.Add(z); world.Add(r); world.Add(rout); world.Add(level); world.Add(crest); world.Add(floor);
                world.Add(Math.Min(low, floor) - SpringFloorU * u); world.Add(Math.Max(high, crest) + SpringTopU * u);
            }
        }

        public string PoolFault(ObjectInstance o)
        {
            var pools = new List<float>();
            SpringPoolsAt(o, pools);
            for (int k = 0; k < pools.Count; k += SpringStride)
            {
                float x = pools[k], z = pools[k + 1], rout = pools[k + 3];
                for (int p = 0; p < 9; p++)
                {
                    float ux = 0, uz = 0;
                    if (p > 0) FanDir(p - 1, 0.70710678f, out ux, out uz);
                    float px = x + ux * rout, pz = z + uz * rout;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return "pool reaches past the world edge";
                    int i = Cell(pz) * n + Cell(px);
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || RiverGap(px, pz) <= 0) return "pool stands in water";
                }
                foreach (var f in edits)
                    if (f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring && x + rout >= f.MinX && x - rout <= f.MaxX && z + rout >= f.MinZ && z - rout <= f.MaxZ) return "pool overlaps a terrain feature";
            }
            return null;
        }

        public string VentFault(ObjectInstance o)
        {
            string fault = PoolFault(o);
            if (fault != null) return fault;
            if (UnderMesa(o.X, o.Z, SiteRadius(o))) return "vent sits under a mesa";
            int kind = VentKind(o), liquid = LiquidOf(o);
            var pools = new List<float>();
            var own = new List<FeatureEdit>();
            var carves = new List<FeatureEdit>();
            VentEdits(o, pools, own, carves);
            own.AddRange(carves);
            var shape = own.ToArray();
            for (int k = 0; k < pools.Count; k += SpringStride)
            {
                var basin = Pool(kind, liquid, pools[k], pools[k + 1], pools[k + 3], pools[k + 4], pools[k + 7], pools[k + 8], shape);
                if (basin.Leaks || basin.WetCount == 0 || !(basin.Level - basin.Floor >= PoolMinDepthM)) return "pool holds no water";
            }
            if (kind == Spring) return null;
            for (int k = 0; k < pools.Count; k += SpringStride)
            {
                float x = pools[k], z = pools[k + 1], rout = pools[k + 3], low = GroundAt(detail, cm, x, z), high = low;
                for (int p = 0; p < 8; p++)
                {
                    FanDir(p, 0.70710678f, out float ux, out float uz);
                    float g = GroundAt(detail, cm, x + ux * rout, z + uz * rout);
                    low = Math.Min(low, g); high = Math.Max(high, g);
                }
                if (high - low > VentSteady * 2 * rout) return "vent sits on unsteady ground";
            }
            return null;
        }

        internal static FeatureEdit Disc(int kind, float x, float z, float top, float bottom, float r, float round, float taper, float p, float n, float seed, float mud, float d)
        {
            float y0 = bottom + round, y1 = top - round, w = r + (taper != 0f ? taper * (y1 - y0) - round * ((float)Math.Sqrt(1f + taper * taper) - 1f) : 0f) - round;
            return new FeatureEdit
            {
                Kind = kind, Shape = FeatureEdit.Spring, R = round, Ax = x, Bx = x, Az = z, Bz = z, Ay = 0.5f * (y0 + y1), By = 0.5f * (y0 + y1),
                W = w, H = Math.Max(0f, 0.5f * (y1 - y0)), T = taper, P = p, N = n, S = seed, F = mud, D = d,
            };
        }

        public FeatureEdit[] SpringEdits()
        {
            var list = new List<FeatureEdit>();
            var carves = new List<FeatureEdit>();
            var pools = new List<float>();
            foreach (var o in Discoveries())
                if (IsVent(o)) VentEdits(o, pools, list, carves);
            list.AddRange(carves);
            return list.ToArray();
        }

        void VentEdits(ObjectInstance o, List<float> pools, List<FeatureEdit> list, List<FeatureEdit> carves)
        {
            SpringPoolsAt(o, pools);
            int kind = VentKind(o);
            float u = Structures.SpringU * o.Size, mud = kind == MudPot ? 1f : 0f;
            for (int k = 0; k < pools.Count; k += SpringStride)
            {
                float x = pools[k], z = pools[k + 1], r = pools[k + 2], rout = pools[k + 3], crest = pools[k + 5], floor = pools[k + 6], bottom = pools[k + 7], top = pools[k + 8];
                float ra = 0.6f * u, rc = 0.8f * u, hole = (1f - 1.25f * FeatureEdit.Blend) * rc, seed = (float)((Rng.Mix(((ulong)(uint)(int)(x * 64f) << 32 | (uint)(int)(z * 64f)) ^ (ulong)k * 0x9E3779B97F4A7C15UL) >> 40) / 16777216.0);
                if (kind == Geyser)
                {
                    Structures.GeyserShape(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant), out float mound, out float mtop, out float cone, out float ctop);
                    float low = GroundAt(detail, cm, x, z);
                    for (int p = 0; p < 8; p++)
                    {
                        FanDir(p, 0.70710678f, out float ux, out float uz);
                        low = Math.Min(low, GroundAt(detail, cm, x + ux * rout, z + uz * rout));
                    }
                    float baseY = crest - (mound + cone) * u, moundY = baseY + mound * u, foot = Math.Min(low, baseY) - GeyserFootU * u;
                    float taper = (rout - mtop * u) / (moundY - foot);
                    list.Add(Disc(FeatureEdit.Add, x, z, moundY, foot, mtop * u, 0.4f * u, taper, -GeyserLobe, 0, seed, 0, 0).Finer());
                    list.Add(Disc(FeatureEdit.Add, x, z, crest, Math.Max(foot, crest - 0.8f * (rout - ctop * u) / GeyserTaper), ctop * u, 0.4f * u, GeyserTaper, -GeyserLobe, 0, seed, 0, 0).Finer());
                    carves.Add(Disc(FeatureEdit.Carve, x, z, top + rc - hole, floor - rc + hole, r + rc - hole, rc, 0, 0, 0.06f * u, seed, 0, 0));
                    continue;
                }
                if (kind == MudPot)
                {
                    float level = pools[k + 4] - MudFillU * u, bank = MudBankU * u;
                    list.Add(Disc(FeatureEdit.Add, x, z, crest, bottom, r + MudRimU * u, ra, MudRimSlope, -MudLobe, 0.1f * u, seed, mud, 0));
                    carves.Add(Disc(FeatureEdit.Carve, x, z, top, floor, r + MudBank * (top - bank - level), bank, -MudBank, -MudLobe, 0.06f * u, seed, mud, 0));
                    float shelf = MudShelfU * u, lip = level + MudShelfTopU * u;
                    carves.Add(Disc(FeatureEdit.Carve, x, z, lip, level - 2f * shelf, r + MudShelf * (lip - shelf - level) + shelf * (float)Math.Sqrt(1f + MudShelf * MudShelf), shelf, -MudShelf, -MudLobe, 0.04f * u, seed, mud, 0));
                    continue;
                }
                list.Add(new FeatureEdit
                {
                    Kind = FeatureEdit.Add, Shape = FeatureEdit.Spring, R = ra,
                    Ax = x, Bx = x, Az = z, Bz = z, Ay = 0.5f * (crest + bottom), By = 0.5f * (crest + bottom),
                    W = rout - ra, H = Math.Max(0f, 0.5f * (crest - bottom) - ra), P = 0.15f, N = 0.1f * u, S = seed,
                }.Finer());
                carves.Add(new FeatureEdit
                {
                    Kind = FeatureEdit.Carve, Shape = FeatureEdit.Spring, R = rc,
                    Ax = x, Bx = x, Az = z, Bz = z, Ay = 0.5f * (top + floor), By = 0.5f * (top + floor),
                    W = r - hole, H = Math.Max(0f, 0.5f * (top - floor) - hole), N = 0.08f * u, S = seed,
                }.Finer());
            }
        }

        bool ClearOfBuilt(ObjectInstance o)
        {
            float r = SiteRadius(o), span = WorldChunks.Size * cm;
            int reach = (int)Math.Ceiling((r + siteReach) / span);
            int cx = (int)(o.X / span), cz = (int)(o.Z / span);
            for (int z = Math.Max(0, cz - reach); z <= Math.Min(across - 1, cz + reach); z++)
                for (int x = Math.Max(0, cx - reach); x <= Math.Min(across - 1, cx + reach); x++)
                    foreach (var s in Built(x, z))
                        if (Math.Min(SiteGap(s, o.X, o.Z), SiteGap(o, s.X, s.Z)) < r + SiteMarginM + SiteBandM) return false;
            return true;
        }

        ObjectInstance[] Built(int x, int z)
        {
            var cache = builtSites;
            int c = z * across + x;
            var built = cache == null ? null : Volatile.Read(ref cache[c]);
            if (built != null) return built;
            var list = new List<ObjectInstance>();
            PlaceInto(x, z, false, list, null, true, true);
            var made = list.ToArray();
            return cache == null ? made : Interlocked.CompareExchange(ref cache[c], made, null) ?? made;
        }

        void Climb(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float y = GroundAt(detail, cm, x, z);
            for (float s = ClimbStartM; s >= ClimbEndM; s *= 0.5f)
                for (int n = 0; n < ClimbSteps; n++)
                {
                    float bx = x, bz = z, by = y;
                    for (int k = 0; k < 8; k++)
                    {
                        FanDir(k, 0.70710678f, out float ux, out float uz);
                        float px = Math.Min(x1, Math.Max(x0, x + ux * s)), pz = Math.Min(z1, Math.Max(z0, z + uz * s));
                        float py = GroundAt(detail, cm, px, pz);
                        if (py > by) { bx = px; bz = pz; by = py; }
                    }
                    if (by <= y) break;
                    x = bx; z = bz; y = by;
                }
        }

        bool SeekDen(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = Fitness(WolfDen, x, z), bx = x, bz = z;
            for (int ring = 1; ring <= DenSeekRings; ring++)
                for (int k = 0; k < 8; k++)
                {
                    FanDir(k, 0.70710678f, out float ux, out float uz);
                    float px = Math.Min(x1, Math.Max(x0, x + ux * ring * DenSeekM)), pz = Math.Min(z1, Math.Max(z0, z + uz * ring * DenSeekM));
                    float fit = Fitness(WolfDen, px, pz);
                    if (fit > best) { best = fit; bx = px; bz = pz; }
                }
            x = bx; z = bz;
            return best > float.MinValue;
        }

        public const float DungeonReliefM = 8f, DungeonReliefAimM = 4f, DungeonRiverM = 40f;

        public float Fitness(int kind, float x, float z)
        {
            if (kind == Overlook) return OverlookFit(x, z, out _, out _);
            if (kind == FallenTree)
            {
                float wd = Woods(x, z);
                if (!(wd >= TreeWoods)) return float.MinValue;
            }
            if (kind == AncientTree && !(Woods(x, z) >= AncientWoods)) return float.MinValue;
            if (kind == Bones) return BonesFit(x, z);
            if (kind == SaltTower) return SaltFit(x, z);
            if (kind == FumaroleField) return FumaroleFit(x, z);
            if (kind == Monument) return MonumentFit(x, z);
            if (kind == Shipwreck) return ShipwreckFit(x, z);
            if (kind == Watchtower) return WatchFit(x, z);
            if (kind == SunkenTemple) return TempleFit(x, z, out _, out _);
            if (kind == GreatMushroom) return GreatMushroomFit(x, z);
            float denWoods = kind == WolfDen ? Woods(x, z) : 0;
            if (kind == WolfDen && denWoods < DenWoods) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), r = kind == Shrine ? ShrineProbeM : kind == LoneInn ? LoneInnProbeM : kind == WolfDen ? DenProbeM : DiscoveryProbeM, rise = 0, relief = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * r, pz = z + uz * r;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
                float dy = y - GroundAt(detail, cm, px, pz);
                rise += dy;
                relief = Math.Max(relief, Math.Abs(dy));
                if (kind == WolfDen && relief > DenReliefM) return float.MinValue;
            }
            rise *= 0.125f;
            if (kind == FallenTree) return relief <= TreeReliefM ? Woods(x, z) - 0.1f * relief : float.MinValue;
            if (kind == AncientTree) return relief <= AncientReliefM ? Woods(x, z) - 0.1f * relief : float.MinValue;
            if (kind == Camp)
            {
                if (relief > CampReliefM) return float.MinValue;
                float draw = CampDraw(x, z);
                return draw > 0 ? draw - 0.1f * relief : float.MinValue;
            }
            if (kind == Hermit)
            {
                if (relief > CampReliefM) return float.MinValue;
                float woods = Woods(x, z);
                return woods >= HermitWoods ? woods - 0.1f * relief : float.MinValue;
            }
            if (kind == Shrine)
            {
                float d = RoadSide(x, z, out _, out _);
                if (relief > ShrineReliefM) return float.MinValue;
                return d >= ShrineRoadMinM && d <= ShrineRoadM ? -Math.Abs(d - ShrineRoadAimM) / ShrineRoadM - 0.1f * relief : float.MinValue;
            }
            if (kind == LoneInn)
            {
                float d = RoadSide(x, z, out _, out _);
                if (relief > LoneInnReliefM) return float.MinValue;
                return d >= LoneInnRoadMinM && d <= LoneInnRoadM ? -Math.Abs(d - LoneInnRoadAimM) / LoneInnRoadM - 0.1f * relief : float.MinValue;
            }
            if (Pooled(kind))
            {
                float draw = Hotspots.Heat(x, z);
                if (!(draw > 0) || relief > (kind == Spring ? SpringReliefM : VentReliefM) || RiverGap(x, z) < SpringRiverM) return float.MinValue;
                return draw - 0.05f * relief;
            }
            if (kind == Dungeon) return relief >= 1f && relief <= DungeonReliefM && RiverGap(x, z) >= DungeonRiverM ? -0.2f * rise - 0.1f * Math.Abs(relief - DungeonReliefAimM) : float.MinValue;
            if (kind == Ruin) return relief <= RuinReliefM ? rise - 0.2f * relief : float.MinValue;
            if (kind == FairyRing) return relief <= FairyReliefM ? -0.2f * relief - Math.Abs(Woods(x, z) - FairyWoods) : float.MinValue;
            if (kind == WolfDen)
            {
                return RiverGap(x, z) >= DenRiverM && BesideRock(x, z) ? 0.1f * rise - 0.1f * relief + denWoods : float.MinValue;
            }
            if (kind == Hilltop) return rise >= HilltopRiseM ? rise : float.MinValue;
            return relief <= ClearingReliefM ? -relief : float.MinValue;
        }

        void EmitDiscoveries(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list)
        {
            int ordinal = 0;
            foreach (var o in Discoveries())
            {
                if (o.Def != d || o.X < x0 || o.Z < z0 || o.X >= x0 + sx || o.Z >= z0 + sz) continue;
                var s = o;
                s.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(s);
            }
        }

        public float SiteRadius(ObjectInstance s)
        {
            var box = siteBox[s.Def];
            int v = s.Variant * 4;
            float ex = Math.Max(-box[v], box[v + 1]), ez = Math.Max(-box[v + 2], box[v + 3]);
            return (float)Math.Sqrt(ex * ex + ez * ez) * s.Size;
        }
    }
}
