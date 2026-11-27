using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class VolcanoSketch
    {
        public float X, Z, Ro, R, Rc, Rf, A, H, Base, RimLow, Lx, Lz, Drop, Fill;
        public uint Seed;
        public bool Breached, Active;
    }

    public static class Volcanoes
    {
        public static readonly string[] Biomes = { "tundra", "steppe", "shrubland", "desert", "cold-desert", "alpine" };
        public const float MinRoM = 235f, MaxRoM = 250f, MinCrater = 0.26f, MaxCrater = 0.31f, FloorShare = 0.62f, ApronShare = 0.3f, A0 = 0.8f, Slope = 0.62f, SlopeLimit = 0.675f;
        public const float OutlineShare = 0.04f, CraterNoise = 0.04f, RimNoiseM = 1.5f, GullyM = 1.2f, DirK = 1.5f, GullyK = 5f, ReliefShare = 1f, CoreShare = 0.4f, RimSlackM = 0.3f;
        public const float MinDrop = 0.12f, MaxDrop = 0.2f, MinBreachDrop = 0.3f, MaxBreachDrop = 0.4f, BreachChance = 0.35f, ActiveChance = 0.5f;
        public const float MinLavaFill = 0.5f, MaxLavaFill = 0.85f, MinWaterFill = 0.3f, MaxWaterFill = 0.7f;
        public const float GapM = 3000f, LandmarkGapM = 1200f, RoadGapM = 500f, CoastPadM = 300f, RiverPadM = 300f, MesaPadM = 200f, SampleM = 20f;
        public const float CarveShare = 0.85f, CarveRound = 1.5f, CarveTaper = -1.4f, CarveP = -0.15f, CarveN = 0.3f, MinDepthM = 4f, MaxDepthM = 10f, RingPadM = 2f, TopPadM = 2f;
        public const float NoneChance = 0.3f, OneChance = 0.75f, RainPadMm = 120f;
        public const float BigMinRoM = 600f, BigMaxRoM = 1300f, StratoSearchM = 4000f, SpineJitterM = 250f, IslandSearchM = 400f, IslandPadM = 150f, LiftShare = 1.5f, ToeShare = 0.5f, FadeSpan = 3f, MaxCarveM = 60f, CinderReachM = 6000f;
        public const int BigTries = 400, MaxClosed = 2, Tries = 1500, RimDirs = 256, RingDirs = 48, ReachPasses = 4;
        const ulong CraterTag = 0xC4A7E5D1B3F29A61UL;

        public static void RaiseBig(WorldConfig c, WorldSketch s, bool[] sea, float[] h)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            float[] flow = null;
            var v = new float[12];
            ulong bigSeed = Rng.Stream(c.Seed, "stratovolcanoes").NextU64();
            var spine = new List<float>();
            for (int k = 0; k < s.Hotspots.Count; k++)
            {
                var spot = s.Hotspots[k];
                if (spot.Kind == HotspotSketch.Plume) continue;
                bool island = spot.Kind == HotspotSketch.Ocean;
                float reach = island ? IslandSearchM : SpineJitterM;
                spine.Clear();
                if (!island) Spine(s, spot, cm, spine);
                for (int t = 0; t < BigTries; t++)
                {
                    var r = new Rng(Rng.Mix(bigSeed + (ulong)(k * BigTries + t)));
                    for (int q = 0; q < v.Length; q++) v[q] = r.NextFloat();
                    if (island) v[2] = 1f - (float)t / BigTries;
                    float px = spot.X * cm, pz = spot.Y * cm;
                    if (spine.Count > 0)
                    {
                        int j = Math.Min(spine.Count / 2 - 1, (int)(v[11] * (spine.Count / 2)));
                        px = spine[2 * j]; pz = spine[2 * j + 1];
                    }
                    var b = Shape(px + (2f * v[0] - 1f) * reach, pz + (2f * v[1] - 1f) * reach, v, BigMinRoM, BigMaxRoM);
                    if (!Site(c, s, sea, h, null, b, island ? IslandPadM : CoastPadM, out float drop)) continue;
                    flow ??= Mesas.Flow(c, sea, h);
                    if (Mesas.Wet(c, flow, b.X, b.Z, b.Rc + RiverPadM)) continue;
                    b.RimLow = Low(b);
                    Stamp(c, s, sea, h, b, drop);
                    s.Volcanoes.Add(b);
                    break;
                }
            }
        }

        public static void Raise(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate, IReadOnlyList<BiomeDef> biomes)
        {
            var root = Rng.Stream(c.Seed, "volcanoes");
            ulong baseSeed = root.NextU64();
            float roll = root.NextFloat();
            int count = roll < NoneChance ? 0 : roll < OneChance ? 1 : 2;
            if (count == 0 || biomes == null) return;
            int n = c.Size;
            float cm = c.CellMeters;
            float[] flow = null;
            var v = new float[12];
            var open = OpenCells(c, s, sea, h, climate, biomes);
            int edge = (int)Math.Ceiling((MaxRoM + CoastPadM) / cm) + 1;
            float near = CinderReachM / cm;
            var cells = new List<int>();
            for (int y = edge; y < n - edge; y++)
                for (int x = edge; x < n - edge; x++)
                {
                    int i = y * n + x;
                    if (sea[i] || h[i] <= 0f || !open[i]) continue;
                    foreach (var spot in s.Hotspots)
                        if (spot.Kind != HotspotSketch.Ocean && (spot.X - x) * (spot.X - x) + (spot.Y - y) * (spot.Y - y) <= near * near) { cells.Add(i); break; }
                }
            if (cells.Count == 0) return;
            for (int k = 0; k < count; k++)
                for (int t = 0; t < Tries; t++)
                {
                    var r = new Rng(Rng.Mix(baseSeed + (ulong)(k * Tries + t)));
                    for (int q = 0; q < v.Length; q++) v[q] = r.NextFloat();
                    int cell = cells[Math.Min(cells.Count - 1, (int)(v[0] * cells.Count))];
                    var b = Shape((cell % n + v[1] - 0.5f) * cm, (cell / n + v[11] - 0.5f) * cm, v, MinRoM, MaxRoM);
                    if (!Site(c, s, sea, h, open, b, CoastPadM, out _)) continue;
                    flow ??= Mesas.Flow(c, sea, h);
                    if (Mesas.Wet(c, flow, b.X, b.Z, b.Ro + RiverPadM)) continue;
                    b.RimLow = Low(b);
                    Stamp(c, s, sea, h, b, 0f);
                    s.Volcanoes.Add(b);
                    break;
                }
        }

        public static bool Big(VolcanoSketch v) => v.Ro > MaxRoM;

        static void Spine(WorldSketch s, HotspotSketch spot, float cm, List<float> spine)
        {
            float reach = StratoSearchM / cm;
            foreach (var r in s.Ranges)
            {
                bool own = false;
                for (int i = 0; i < r.X.Length; i++) own |= r.X[i] == spot.X && r.Y[i] == spot.Y;
                if (!own) continue;
                for (int i = 0; i < r.X.Length; i++)
                {
                    float dx = r.X[i] - spot.X, dy = r.Y[i] - spot.Y;
                    if (dx * dx + dy * dy > reach * reach) continue;
                    if (i + 1 < r.X.Length) { spine.Add((r.X[i] + r.X[i + 1]) * 0.5f * cm); spine.Add((r.Y[i] + r.Y[i + 1]) * 0.5f * cm); }
                    spine.Add(r.X[i] * cm); spine.Add(r.Y[i] * cm);
                }
                return;
            }
        }

        static float Mix(float a, float b, float t) => a + (b - a) * t;

        static VolcanoSketch Shape(float x, float z, float[] v, float minRo, float maxRo)
        {
            float ro = Mix(minRo, maxRo, v[2]), rc = Mix(MinCrater, MaxCrater, v[3]) * ro;
            float rr = (ro / (1f + OutlineShare) + ApronShare * rc) / (1f + ApronShare);
            bool breached = v[4] < BreachChance;
            float lx = 2f * v[6] - 1f, lz = 2f * v[7] - 1f, l = (float)Math.Sqrt(lx * lx + lz * lz);
            if (l < 0.2f) { lx = 1f; lz = 0f; } else { lx /= l; lz /= l; }
            bool active = v[8] < ActiveChance;
            float lmin = rr * (1f - OutlineShare) - rc * (1f + CraterNoise);
            return new VolcanoSketch
            {
                X = x, Z = z, Ro = ro, R = rr, Rc = rc, Rf = FloorShare * rc, A = ApronShare * (rr - rc), H = Slope * lmin / (2f - A0) - RimNoiseM,
                Lx = lx, Lz = lz, Breached = breached, Drop = breached ? Mix(MinBreachDrop, MaxBreachDrop, v[5]) : Mix(MinDrop, MaxDrop, v[5]),
                Active = active, Fill = active ? Mix(MinLavaFill, MaxLavaFill, v[9]) : Mix(MinWaterFill, MaxWaterFill, v[9]),
                Seed = (uint)(v[10] * 16777216f) * 0x9E3779B1u + 0x7F4A7C15u,
            };
        }

        static float Dir(VolcanoSketch v, uint k, float ux, float uz, float f) => Noise.Fbm(v.Seed ^ k, ux * f + 11.3f, uz * f + 23.7f, 2);

        static void Radii(VolcanoSketch v, float ux, float uz, out float rd, out float rcd, out float rfd, out float ad, out float rim, out float apron)
        {
            rd = v.R * (1f + OutlineShare * Dir(v, 0x68E31DA4u, ux, uz, DirK));
            rcd = v.Rc * (1f + CraterNoise * Dir(v, 0xB5297A4Du, ux, uz, DirK));
            rfd = v.Rf * rcd / v.Rc;
            ad = v.A * rd / v.R;
            float t = 0.5f * (1f + ux * v.Lx + uz * v.Lz), b = t * t;
            if (v.Breached) b = b * b * b;
            rim = v.H * (1f - v.Drop * b) + RimNoiseM * Dir(v, 0x1B56C4E9u, ux, uz, DirK);
            apron = rim * A0 / (rd - rcd) * ad * 0.5f;
        }

        public static float Top(VolcanoSketch v, float ux, float uz)
        {
            Radii(v, ux, uz, out _, out _, out _, out _, out float rim, out float apron);
            return v.Base + apron + rim;
        }

        public static void Ring(int k, int count, out float ux, out float uz)
        {
            float s = 8f * k / count;
            int side = (int)(s / 2f);
            float f = s - 2f * side - 1f;
            ux = side == 0 ? 1f : side == 1 ? -f : side == 2 ? -1f : f;
            uz = side == 0 ? f : side == 1 ? 1f : side == 2 ? -f : -1f;
            float l = (float)Math.Sqrt(ux * ux + uz * uz);
            ux /= l; uz /= l;
        }

        static float Low(VolcanoSketch v)
        {
            float low = float.MaxValue;
            for (int k = 0; k < RimDirs; k++)
            {
                Ring(k, RimDirs, out float ux, out float uz);
                low = Math.Min(low, Top(v, ux, uz));
            }
            return low - RimSlackM;
        }

        public static float Profile(VolcanoSketch v, float x, float z)
        {
            float dx = x - v.X, dz = z - v.Z, rr = (float)Math.Sqrt(dx * dx + dz * dz), ux = 1f, uz = 0f;
            if (rr > 1e-3f) { ux = dx / rr; uz = dz / rr; }
            Radii(v, ux, uz, out float rd, out float rcd, out float rfd, out float ad, out float rim, out float apron);
            float top = v.Base + apron + rim;
            if (rr <= rfd) return v.RimLow;
            if (rr <= rcd) { float t = (rr - rfd) / (rcd - rfd); return v.RimLow + (top - v.RimLow) * t * t; }
            if (rr <= rd)
            {
                float u = (rd - rr) / (rd - rcd);
                return v.Base + apron + rim * u * (A0 + (1f - A0) * u) - GullyM * 4f * u * (1f - u) * Math.Max(0f, Dir(v, 0x27D4EB2Fu, ux, uz, GullyK));
            }
            if (rr <= rd + ad) { float q = 1f - (rr - rd) / ad; return v.Base + apron * q * q; }
            return float.MinValue;
        }

        static bool[] OpenCells(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate, IReadOnlyList<BiomeDef> biomes)
        {
            float inf = float.PositiveInfinity;
            var biome = new int[h.Length];
            for (int i = 0; i < h.Length; i++)
            {
                if (sea[i]) { biome[i] = -1; continue; }
                int dry = Stabby.World.Biomes.Pick(biomes, climate.TemperatureC[i], climate.PrecipitationMm[i], h[i], inf, inf, inf);
                int wet = Stabby.World.Biomes.Pick(biomes, climate.TemperatureC[i], climate.PrecipitationMm[i] + RainPadMm, h[i], inf, inf, inf);
                biome[i] = Listed(biomes, dry) && Listed(biomes, wet) ? dry : -1;
            }
            Stabby.World.Biomes.ApplyRegions(c, biomes, s, sea, biome);
            var open = new bool[h.Length];
            for (int i = 0; i < h.Length; i++) open[i] = Listed(biomes, biome[i]);
            return open;
        }

        static bool Listed(IReadOnlyList<BiomeDef> biomes, int b) => b >= 0 && Array.IndexOf(Biomes, biomes[b].Id) >= 0;

        static bool OpenAt(WorldConfig c, bool[] open, float x, float z)
        {
            int n = c.Size, i = Math.Min(n - 1, Math.Max(0, (int)Math.Round(z / c.CellMeters))) * n + Math.Min(n - 1, Math.Max(0, (int)Math.Round(x / c.CellMeters)));
            return open[i];
        }

        public static float SegmentGap(float x, float z, float ax, float az, float bx, float bz)
        {
            float vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz, t = l2 > 0f ? Math.Max(0f, Math.Min(1f, ((x - ax) * vx + (z - az) * vz) / l2)) : 0f;
            float dx = x - ax - t * vx, dz = z - az - t * vz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static bool Site(WorldConfig c, WorldSketch s, bool[] sea, float[] h, bool[] open, VolcanoSketch b, float pad, out float drop)
        {
            drop = 0f;
            bool big = open == null;
            int n = c.Size;
            float cm = c.CellMeters;
            int cx = (int)Math.Round(b.X / cm), cy = (int)Math.Round(b.Z / cm);
            int coast = (int)Math.Ceiling((pad + b.Ro) / cm);
            if (cx - coast < 1 || cy - coast < 1 || cx + coast >= n - 1 || cy + coast >= n - 1 || sea[cy * n + cx]) return false;
            if (!big)
            {
                if (!OpenAt(c, open, b.X, b.Z)) return false;
                int closed = 0;
                for (int k = 0; k < 8; k++)
                {
                    Ring(k, 8, out float ux, out float uz);
                    if (!OpenAt(c, open, b.X + ux * b.Ro, b.Z + uz * b.Ro)) closed++;
                }
                if (closed > MaxClosed) return false;
            }
            float landmarkGap = big ? Math.Max(LandmarkGapM, b.Ro + RoadGapM) : LandmarkGapM;
            foreach (var l in s.Landmarks)
            {
                float dx = l.X * cm - b.X, dz = l.Y * cm - b.Z;
                if (dx * dx + dz * dz < landmarkGap * landmarkGap) return false;
            }
            for (int p = 0; p < s.Landmarks.Count; p++)
                for (int q = p + 1; q < s.Landmarks.Count; q++)
                {
                    float ax = s.Landmarks[p].X * cm, az = s.Landmarks[p].Y * cm, bx = s.Landmarks[q].X * cm, bz = s.Landmarks[q].Y * cm;
                    if ((ax - bx) * (ax - bx) + (az - bz) * (az - bz) > ObjectField.RoadReachM * ObjectField.RoadReachM) continue;
                    if (SegmentGap(b.X, b.Z, ax, az, bx, bz) < b.Ro + RoadGapM) return false;
                }
            foreach (var m in s.Mesas)
            {
                float dx = m.X - b.X, dz = m.Z - b.Z, gap = b.Ro + Mesas.Reach(m, m.Top - m.Base) + MesaPadM;
                if (dx * dx + dz * dz < gap * gap) return false;
            }
            foreach (var o in s.Volcanoes)
            {
                float gap = Math.Max(GapM, o.Ro + b.Ro + MesaPadM);
                if ((o.X - b.X) * (o.X - b.X) + (o.Z - b.Z) * (o.Z - b.Z) < gap * gap) return false;
            }
            foreach (var lake in s.Lakes)
                if (lake.Gap(cx, cy, 1) < (b.Ro + RiverPadM) / cm) return false;
            int step = Math.Max(1, coast / 24);
            for (int dy = -coast; dy <= coast; dy += step)
                for (int dx = -coast; dx <= coast; dx += step)
                    if (dx * dx + dy * dy <= coast * coast && sea[(cy + dy) * n + cx + dx]) return false;
            int rr = (int)Math.Ceiling(b.Ro / cm);
            float lo = float.MaxValue, hi = float.MinValue, core = float.MinValue;
            for (int dy = -rr; dy <= rr; dy++)
                for (int dx = -rr; dx <= rr; dx++)
                {
                    if (dx * dx + dy * dy > rr * rr) continue;
                    int i = (cy + dy) * n + cx + dx;
                    bool inCore = (dx * dx + dy * dy) * cm * cm <= b.R * b.R;
                    if (s.Beach[i] > 0f && s.Coast[i] * cm < c.BeachMeters) return false;
                    if (s.Landmark[i] || (!big && (s.Cliff[i] > 0f || s.Valley[i] > Mesas.ValleyMax))) return false;
                    lo = Math.Min(lo, h[i]); hi = Math.Max(hi, h[i]);
                    if (inCore) core = Math.Max(core, h[i]);
                }
            if (lo <= 0f) return false;
            if (big)
            {
                b.Base = Math.Max(lo, core - CoreShare * b.H);
                if (b.Base - lo > LiftShare * b.H) return false;
                drop = b.Base - lo;
                int far = (int)Math.Ceiling(Reach(b, drop) / cm) + 1;
                return cx - far >= 0 && cy - far >= 0 && cx + far < n && cy + far < n;
            }
            if (core - lo > CoreShare * b.H || hi - lo > ReliefShare * b.H) return false;
            b.Base = lo;
            return true;
        }

        static void Stamp(WorldConfig c, WorldSketch s, bool[] sea, float[] h, VolcanoSketch b, float drop)
        {
            int n = c.Size;
            float cm = c.CellMeters, ext = Reach(b, drop) + cm;
            int x0 = Math.Max(0, (int)Math.Floor((b.X - ext) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((b.X + ext) / cm));
            int y0 = Math.Max(0, (int)Math.Floor((b.Z - ext) / cm)), y1 = Math.Min(n - 1, (int)Math.Ceiling((b.Z + ext) / cm));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * n + x;
                    if (sea[i]) continue;
                    float g = Profile(b, x * cm, y * cm);
                    if (drop > 0f)
                    {
                        float sk = Skirt(b, x * cm, y * cm, drop);
                        if (g == float.MinValue && sk == float.MinValue) continue;
                        float k = Math.Min(1f, Math.Max(0f, Math.Min((LakeGap(s, x, y) * cm - RiverPadM) / (FadeSpan * RiverPadM), (s.Coast[i] * cm - CoastPadM) / CoastPadM)));
                        float e = g == float.MinValue ? h[i] : g + (Math.Min(h[i], g) - g) * Ease(b, x * cm, y * cm);
                        g = sk > float.MinValue ? e + (sk - e) * k : g + (e - g) * (1f - k);
                    }
                    h[i] = Math.Max(h[i], g);
                }
        }

        static float LakeGap(WorldSketch s, int x, int y)
        {
            float best = float.MaxValue;
            foreach (var lake in s.Lakes) best = Math.Min(best, lake.Gap(x, y, 1));
            return best;
        }

        static float Ease(VolcanoSketch v, float x, float z)
        {
            float dx = x - v.X, dz = z - v.Z, rr = (float)Math.Sqrt(dx * dx + dz * dz);
            if (rr < 1e-3f) return 0f;
            Radii(v, dx / rr, dz / rr, out float rd, out float rcd, out _, out float ad, out _, out _);
            float m = 0.5f * (rcd + rd), t = Math.Min(1f, Math.Max(0f, (rr - m) / (rd + ad - m)));
            return t * t * (3f - 2f * t);
        }

        static float Skirt(VolcanoSketch v, float x, float z, float drop)
        {
            float dx = x - v.X, dz = z - v.Z, rr = (float)Math.Sqrt(dx * dx + dz * dz);
            if (rr < 1e-3f) return float.MinValue;
            Radii(v, dx / rr, dz / rr, out float rd, out _, out _, out float ad, out _, out float apron);
            float m = 2f * apron / ad, l = SkirtLength(apron, ad, drop), t = rr - rd;
            return t < 0f || t > l ? float.MinValue : v.Base + apron - m * t + m * (1f - ToeShare) * t * t / (2f * l);
        }

        static float SkirtLength(float apron, float ad, float drop) => (apron + drop) * ad / (apron * (1f + ToeShare));

        static float Reach(VolcanoSketch v, float drop)
        {
            float far = v.Ro;
            for (int k = 0; k < RimDirs; k++)
            {
                Ring(k, RimDirs, out float ux, out float uz);
                Radii(v, ux, uz, out float rd, out _, out _, out float ad, out _, out float apron);
                far = Math.Max(far, rd + SkirtLength(apron, ad, drop));
            }
            return far;
        }

        public static float Near(WorldSketch s, float x, float z)
        {
            float best = float.MaxValue;
            if (s == null) return best;
            foreach (var v in s.Volcanoes)
                best = Math.Min(best, (float)Math.Sqrt((v.X - x) * (v.X - x) + (v.Z - z) * (v.Z - z)) - v.Ro);
            return best;
        }

        public static float CarveRadius(VolcanoSketch v) => Math.Min(MaxCarveM, CarveShare * v.Rf * (1f - CraterNoise));

        public static bool IsCrater(VolcanoSketch v, FeatureEdit e) => e.Shape == FeatureEdit.Spring && e.Kind == FeatureEdit.Carve && e.Ax == v.X && e.Az == v.Z;

        public static FeatureEdit Crater(TerrainDetail detail, VolcanoSketch v)
        {
            float r = CarveRadius(v), hi = float.MinValue, lo = float.MaxValue, depth = Math.Max(MinDepthM, Math.Min(MaxDepthM, 0.25f * r)), reach = r;
            for (int pass = 0; pass < ReachPasses; pass++)
            {
                for (int k = 0; k < RingDirs; k++)
                {
                    Ring(k, RingDirs, out float ux, out float uz);
                    float g = detail.Sample(v.X + ux * (reach + RingPadM), v.Z + uz * (reach + RingPadM));
                    hi = Math.Max(hi, g); lo = Math.Min(lo, g);
                }
                depth = Math.Max(depth, lo - v.RimLow + TopPadM + MinDepthM);
                float need = -CarveTaper * (hi - lo + TopPadM + depth - 2f * CarveRound) + CarveRound * (1f + (float)Math.Sqrt(1f + CarveTaper * CarveTaper));
                if (need <= r) break;
                r = need;
                reach = Math.Max(reach, r);
            }
            float seed = (float)((Rng.Mix(((ulong)(uint)(int)(v.X * 64f) << 32 | (uint)(int)(v.Z * 64f)) ^ CraterTag) >> 40) / 16777216.0);
            return ObjectField.Disc(FeatureEdit.Carve, v.X, v.Z, hi + TopPadM, lo - depth, r, CarveRound, CarveTaper, CarveP, CarveN, seed, 0f, 0f);
        }
    }
}
