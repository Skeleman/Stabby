using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public static readonly string[] DiscoveryNames = { "", "hilltop", "clearing", "camp", "hermit", "shrine", "ruin" };
        static readonly string[] DiscoveryStreams = { "", "discoveries", "discoveries-clearing", "discoveries-camp", "discoveries-hermit", "discoveries-shrine", "discoveries-ruin" };
        public const int Hilltop = 1, Clearing = 2, Camp = 3, Hermit = 4, Shrine = 5, Ruin = 6;
        public const float DiscoveryInset = 0.2f, TownClearM = 400f, DiscoveryProbeM = 40f, HilltopRiseM = 1f, ClearingReliefM = 1.5f;
        public const float DiscoveryGapM = 250f, CampReliefM = 3f, CampRiverM = 40f, ForestPerHectare = 30f, HermitWoods = 0.6f, RuinReliefM = 4f;
        public const float ShrineRoadMinM = 9f, ShrineRoadM = 30f, ShrineRoadAimM = 13f, RouteStepM = 24f, ShrineProbeM = 12f, ShrineReliefM = 6f, ShrineSeatUp = 0.16f, ShrineSeatDown = 0.08f;
        public const int DiscoveryTries = 16, ClimbSteps = 12, RouteLinks = 2;
        public const float ClimbStartM = 16f, ClimbEndM = 2f;
        readonly object discoveryLock = new object();
        ObjectInstance[] discoveries;
        ObjectInstance[][] builtSites;
        float[] treeCover;
        List<LandmarkSketch> towns;
        float[] routes;

        public List<LandmarkSketch> Towns => towns ?? (towns = (w.Sketch ?? WorldSketch.Generate(w.Config)).Landmarks);

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
            bool shrine = kind == Shrine;
            int tries = shrine || kind == Ruin ? lib.Objects[d].Variants : 1, k = 0;
            for (; k < tries; k++)
            {
                o.Variant = (variant + k) % lib.Objects[d].Variants;
                if (ClearOfBuilt(o) && (!shrine || SeatGrounded(o))) break;
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
            if (kind == Ruin) return relief <= RuinReliefM ? rise - 0.2f * relief : float.MinValue;
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
