using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public static readonly string[] DiscoveryNames = { "", "hilltop", "clearing", "camp", "hermit", "shrine", "ruin", "spring", "fairy-ring", "geyser", "mud-pot" };
        static readonly string[] DiscoveryStreams = { "", "discoveries", "discoveries-clearing", "discoveries-camp", "discoveries-hermit", "discoveries-shrine", "discoveries-ruin", "discoveries-spring", "discoveries-fairy-ring", "discoveries-geyser", "discoveries-mud-pot" };
        public const int Hilltop = 1, Clearing = 2, Camp = 3, Hermit = 4, Shrine = 5, Ruin = 6, Spring = 7, FairyRing = 8, Geyser = 9, MudPot = 10;
        public const float SpringRiverM = 40f, SpringReliefM = 8f, VentReliefM = 4f, VentSteady = 0.15f;
        public const float DiscoveryInset = 0.2f, TownClearM = 400f, DiscoveryProbeM = 40f, HilltopRiseM = 1f, ClearingReliefM = 1.5f;
        public const float DiscoveryGapM = 250f, CampReliefM = 3f, CampRiverM = 40f, ForestPerHectare = 30f, HermitWoods = 0.6f, RuinReliefM = 4f, FairyReliefM = 2f, FairyWoods = 0.35f;
        public const float ShrineRoadMinM = 9f, ShrineRoadM = 30f, ShrineRoadAimM = 13f, RouteStepM = 24f, ShrineProbeM = 12f, ShrineReliefM = 6f, ShrineSeatUp = 0.16f, ShrineSeatDown = 0.08f;
        public const int DiscoveryTries = 16, ClimbSteps = 12, RouteLinks = 2;
        public const float ClimbStartM = 16f, ClimbEndM = 2f;
        readonly object discoveryLock = new object();
        ObjectInstance[] discoveries;
        ObjectInstance[][] builtSites;
        float[] treeCover;
        List<LandmarkSketch> towns;
        WorldSketch sketch;
        float[] routes;

        public List<LandmarkSketch> Towns => towns ?? (towns = (w.Sketch ?? (sketch ?? (sketch = WorldSketch.Generate(w.Config)))).Landmarks);

        public static float DiscoveryStep(float worldM, float spacingM) => worldM / Math.Max(1, (int)Math.Floor(worldM / spacingM));

        public ObjectInstance[] Discoveries()
        {
            var found = discoveries;
            if (found != null) return found;
            lock (discoveryLock)
            {
                if (discoveries != null) return discoveries;
                builtSites = new ObjectInstance[across * across][];
                var list = new List<ObjectInstance>();
                for (int d = 0; d < lib.Objects.Count; d++)
                {
                    int kind = lib.Objects[d].Placement.Discovery;
                    if (kind > 0) PlaceDiscoveries(d, Rng.Stream(w.Config.Seed, DiscoveryStreams[kind]).NextU64(), list);
                }
                builtSites = null;
                return discoveries = list.ToArray();
            }
        }

        void PlaceDiscoveries(int d, ulong root, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            if (pl.Discovery == Shrine) { PlaceShrines(d, root, list); return; }
            float step = DiscoveryStep(worldM, pl.SpacingM), inset = DiscoveryInset * step;
            int cells = (int)Math.Round(worldM / step);
            for (int gz = 0; gz < cells; gz++)
                for (int gx = 0; gx < cells; gx++)
                {
                    var rng = new Rng(Rng.Mix(root ^ key[d] ^ ((ulong)(gz * cells + gx + 1) * 0x9E3779B97F4A7C15UL)));
                    bool roll = rng.NextFloat() < pl.Chance, any = false;
                    float best = float.MinValue;
                    ObjectInstance pick = default;
                    for (int t = 0; t < DiscoveryTries; t++)
                    {
                        float px = gx * step + inset + rng.NextFloat() * (step - 2 * inset), pz = gz * step + inset + rng.NextFloat() * (step - 2 * inset);
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        if (!roll) continue;
                        if (pl.Discovery == Hilltop) Climb(ref px, ref pz, gx * step + inset, gz * step + inset, (gx + 1) * step - inset, (gz + 1) * step - inset);
                        if (Pooled(pl.Discovery)) FaceDownhill(px, pz, ref qa, ref qb);
                        any |= TrySite(d, variant, px, pz, size, qa, qb, list, ref best, ref pick);
                    }
                    if (any) list.Add(pick);
                }
        }

        void PlaceShrines(int d, ulong root, List<ObjectInstance> list)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            var r = Routes;
            for (int k = 0; k + 3 < r.Length; k += 4)
            {
                float ax = r[k], az = r[k + 1], dx = r[k + 2] - ax, dz = r[k + 3] - az, len = (float)Math.Sqrt(dx * dx + dz * dz);
                int stations = (int)(len / pl.SpacingM);
                if (stations < 1) continue;
                float ux = dx / len, uz = dz / len, step = len / stations;
                for (int st = 0; st < stations; st++)
                {
                    var rng = new Rng(Rng.Mix(root ^ key[d] ^ ((ulong)((k / 4) * 65536 + st + 1) * 0x9E3779B97F4A7C15UL)));
                    if (rng.NextFloat() >= pl.Chance) continue;
                    bool any = false;
                    float best = float.MinValue;
                    ObjectInstance pick = default;
                    for (int t = 0; t < DiscoveryTries; t++)
                    {
                        float along = (st + DiscoveryInset + rng.NextFloat() * (1 - 2 * DiscoveryInset)) * step, off = (rng.NextFloat() < 0.5f ? -1 : 1) * rng.Range(ShrineRoadMinM, ShrineRoadM);
                        float px = ax + ux * along - uz * off, pz = az + uz * along + ux * off;
                        float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * rng.NextFloat(), qa = 1f, qb = 0f;
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        if (!ApartFromKin(d, px, pz, 0.5f * pl.SpacingM, list)) continue;
                        FaceRoad(px, pz, ref qa, ref qb);
                        any |= TrySite(d, variant, px, pz, size, qa, qb, list, ref best, ref pick);
                    }
                    if (any) list.Add(pick);
                }
            }
        }

        bool TrySite(int d, int variant, float px, float pz, float size, float qa, float qb, List<ObjectInstance> list, ref float best, ref ObjectInstance pick)
        {
            float fit = Fitness(lib.Objects[d].Placement.Discovery, px, pz);
            if (fit <= best || !Open(d, variant, size, px, pz) || !ApartFromOthers(d, px, pz, list) || !Accept(d, px, pz, size, qa, qb, out var o)) return false;
            int kind = lib.Objects[d].Placement.Discovery;
            bool shrine = kind == Shrine, spring = Pooled(kind);
            int tries = shrine || spring || kind == Ruin || kind == FairyRing ? lib.Objects[d].Variants : 1, k = 0;
            for (; k < tries; k++)
            {
                o.Variant = (variant + k) % lib.Objects[d].Variants;
                if (ClearOfBuilt(o) && (!shrine || SeatGrounded(o)) && (!spring || VentFault(o) == null)) break;
            }
            if (k == tries) return false;
            best = fit; pick = o;
            return true;
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
            for (int k = 0; k < 9; k++)
            {
                float ux = k % 3 - 1, uz = k / 3 - 1, len = ux != 0 && uz != 0 ? 0.70710678f : 1f;
                float px = x + r * ux * len, pz = z + r * uz * len;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return false;
                int i = Math.Min(n - 1, (int)(pz / cm)) * n + Math.Min(n - 1, (int)(px / cm));
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) return false;
            }
            foreach (var l in Towns)
            {
                float dx = l.X * cm - x, dz = l.Y * cm - z;
                if (dx * dx + dz * dz < TownClearM * TownClearM) return false;
            }
            return true;
        }

        static bool ApartFromOthers(int d, float x, float z, List<ObjectInstance> list)
        {
            foreach (var o in list)
            {
                if (o.Def == d) continue;
                float dx = o.X - x, dz = o.Z - z;
                if (dx * dx + dz * dz < DiscoveryGapM * DiscoveryGapM) return false;
            }
            return true;
        }

        public float[] Routes
        {
            get
            {
                if (routes != null) return routes;
                var t = Towns;
                var seg = new List<float>();
                var linked = new HashSet<int>();
                var order = new List<int>();
                for (int a = 0; a < t.Count; a++)
                {
                    float ax = t[a].X * cm, az = t[a].Y * cm;
                    order.Clear();
                    for (int b = 0; b < t.Count; b++) if (b != a) order.Add(b);
                    order.Sort((p, q) =>
                    {
                        float dp = Far(t[p], ax, az), dq = Far(t[q], ax, az);
                        return dp != dq ? dp.CompareTo(dq) : p.CompareTo(q);
                    });
                    int links = 0;
                    foreach (int b in order)
                    {
                        if (links >= RouteLinks) break;
                        float bx = t[b].X * cm, bz = t[b].Y * cm;
                        if (!DryLine(ax, az, bx, bz)) continue;
                        links++;
                        if (!linked.Add(Math.Min(a, b) * t.Count + Math.Max(a, b))) continue;
                        seg.Add(ax); seg.Add(az); seg.Add(bx); seg.Add(bz);
                    }
                }
                return routes = seg.ToArray();
            }
        }

        float Far(LandmarkSketch l, float x, float z)
        {
            float dx = l.X * cm - x, dz = l.Y * cm - z;
            return dx * dx + dz * dz;
        }

        bool DryLine(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / RouteStepM));
            for (int k = 0; k <= steps; k++)
            {
                float u = k / (float)steps, px = x0 + u * dx, pz = z0 + u * dz;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return false;
                int i = Cell(pz) * n + Cell(px);
                if (w.Sea[i] || w.Water.LakeId[i] >= 0) return false;
            }
            return true;
        }

        public float RoadSide(float x, float z, out float rx, out float rz)
        {
            var r = Routes;
            float best = float.MaxValue;
            rx = x; rz = z;
            for (int k = 0; k + 3 < r.Length; k += 4)
            {
                float dx = r[k + 2] - r[k], dz = r[k + 3] - r[k + 1], len2 = dx * dx + dz * dz;
                float u = len2 > 0 ? Math.Max(0, Math.Min(1, ((x - r[k]) * dx + (z - r[k + 1]) * dz) / len2)) : 0;
                float px = r[k] + u * dx, pz = r[k + 1] + u * dz, d2 = (px - x) * (px - x) + (pz - z) * (pz - z);
                if (d2 < best) { best = d2; rx = px; rz = pz; }
            }
            return best == float.MaxValue ? best : (float)Math.Sqrt(best);
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
                    int b = w.Biome[Cell(pz) * n + Cell(px)];
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

        public float SpringDraw(float x, float z)
        {
            var sk = w.Sketch ?? (sketch ?? (sketch = WorldSketch.Generate(w.Config)));
            float px = x / cm, pz = z / cm, best = 0;
            foreach (var g in sk.Ranges)
                if (g.Width > 0) best = Math.Max(best, 1 - WorldSketch.DistanceToPolyline(g.X, g.Y, px, pz) / (3 * g.Width));
            foreach (var h in sk.Highlands)
            {
                if (h.Plateau || h.Radius <= 0) continue;
                float dx = h.X - px, dz = h.Y - pz;
                best = Math.Max(best, 1 - (float)Math.Sqrt(dx * dx + dz * dz) / (1.5f * h.Radius));
            }
            return best;
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
        public bool IsVent(ObjectInstance o) => Pooled(lib.Objects[o.Def].Placement.Discovery);
        public static bool Pooled(int kind) => kind == Spring || kind == Geyser || kind == MudPot;
        public int VentKind(ObjectInstance o) => lib.Objects[o.Def].Placement.Discovery;

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
        public const float SpringStepU = 0.2f, SpringFloorU = 3f, SpringTopU = 2f, GeyserLipU = 0.15f, GeyserVentU = 2.5f, GeyserTaper = 0.9f, GeyserFootU = 0.5f, MudSinkU = 0.15f, MudFillU = 0.3f, MudCrestU = 0.45f, MudDepthU = 0.5f, MudBankU = 0.3f, MudBank = 1.1f, MudShelf = 2.5f, MudShelfU = 0.08f, MudShelfTopU = 0.2f, MudRimU = 0.7f, MudRimSlope = 0.7f, MudLobe = 0.3f, GeyserLobe = 0.18f, GeyserLumpU = 0.3f, GeyserTerraceU = 0.7f;

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
            int kind = VentKind(o);
            if (fault != null || kind == Spring) return fault;
            var pools = new List<float>();
            SpringPoolsAt(o, pools);
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

        static FeatureEdit Disc(int kind, float x, float z, float top, float bottom, float r, float round, float taper, float p, float n, float seed, float mud, float d)
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
            {
                if (!IsVent(o)) continue;
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
                        list.Add(Disc(FeatureEdit.Add, x, z, moundY, foot, mtop * u, 0.4f * u, taper, -GeyserLobe, GeyserLumpU * u, seed, 0, GeyserTerraceU * u));
                        list.Add(Disc(FeatureEdit.Add, x, z, crest, Math.Max(foot, crest - 0.8f * (rout - ctop * u) / GeyserTaper), ctop * u, 0.4f * u, GeyserTaper, -GeyserLobe, GeyserLumpU * u, seed, 0, 0.8f * GeyserTerraceU * u));
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
                    });
                    carves.Add(new FeatureEdit
                    {
                        Kind = FeatureEdit.Carve, Shape = FeatureEdit.Spring, R = rc,
                        Ax = x, Bx = x, Az = z, Bz = z, Ay = 0.5f * (top + floor), By = 0.5f * (top + floor),
                        W = r - hole, H = Math.Max(0f, 0.5f * (top - floor) - hole), N = 0.08f * u, S = seed,
                    });
                }
            }
            list.AddRange(carves);
            return list.ToArray();
        }

        bool ClearOfBuilt(ObjectInstance o)
        {
            float r = SiteRadius(o), span = WorldChunks.Size * cm;
            int reach = (int)Math.Ceiling((r + siteReach) / span);
            int cx = (int)(o.X / span), cz = (int)(o.Z / span);
            for (int z = Math.Max(0, cz - reach); z <= Math.Min(across - 1, cz + reach); z++)
                for (int x = Math.Max(0, cx - reach); x <= Math.Min(across - 1, cx + reach); x++)
                {
                    int c = z * across + x;
                    var built = builtSites[c];
                    if (built == null)
                    {
                        var list = new List<ObjectInstance>();
                        PlaceInto(x, z, false, list, null, true, true);
                        builtSites[c] = built = list.ToArray();
                    }
                    foreach (var s in built)
                        if (Math.Min(SiteGap(s, o.X, o.Z), SiteGap(o, s.X, s.Z)) < r + SiteMarginM + SiteBandM) return false;
                }
            return true;
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

        public float Fitness(int kind, float x, float z)
        {
            float y = GroundAt(detail, cm, x, z), r = kind == Shrine ? ShrineProbeM : DiscoveryProbeM, rise = 0, relief = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * r, pz = z + uz * r;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
                float dy = y - GroundAt(detail, cm, px, pz);
                rise += dy;
                relief = Math.Max(relief, Math.Abs(dy));
            }
            rise *= 0.125f;
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
            if (Pooled(kind))
            {
                float draw = SpringDraw(x, z);
                if (!(draw > 0) || relief > (kind == Spring ? SpringReliefM : VentReliefM) || RiverGap(x, z) < SpringRiverM) return float.MinValue;
                return draw - 0.05f * relief;
            }
            if (kind == Ruin) return relief <= RuinReliefM ? rise - 0.2f * relief : float.MinValue;
            if (kind == FairyRing) return relief <= FairyReliefM ? -0.2f * relief - Math.Abs(Woods(x, z) - FairyWoods) : float.MinValue;
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
