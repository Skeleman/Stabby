using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public static List<string> Creatures(GeneratedWorld w, CreatureLibrary lib)
        {
            var e = new List<string>();
            var detail = new TerrainDetail(w);
            var cells = detail.Cells;
            var pools = CreaturePlan.Pools(w, detail);
            var a = CreaturePlan.Build(w, cells, lib, pools, out var drinks);
            var b = CreaturePlan.Build(w, cells, lib, pools, out var again);
            if (drinks.Count != again.Count) e.Add("drink spot count differs between runs");
            else
                for (int i = 0; i < drinks.Count; i++)
                {
                    DrinkSpot p = drinks.Spots[i], q = again.Spots[i];
                    if (p.X != q.X || p.Z != q.Z || p.WaterX != q.WaterX || p.WaterZ != q.WaterZ || p.Steep != q.Steep || p.Cost != q.Cost) { e.Add("drink spots differ between runs at " + i); break; }
                }
            if (a.Count != b.Count) e.Add("creature plan count differs between runs");
            else
                for (int i = 0; i < a.Count; i++)
                    if (!Same(a[i], b[i])) { e.Add("creature plan differs between runs at " + a[i].Id); break; }

            var ids = new HashSet<int>();
            var bySite = new Dictionary<int, List<CreatureSpawn>>();
            var herdSeeds = new Dictionary<int, ulong>();
            var seedOwner = new Dictionary<ulong, int>();
            foreach (var s in a)
            {
                if (!ids.Add(s.Id)) e.Add("duplicate creature id " + s.Id);
                var body = lib.Bodies[s.Body];
                if (Footing.SteepAt(cells, s.X, s.Z, Footing.Grade(body.MaxSlope))) e.Add(body.Id + " " + s.Id + " wakes on ground steeper than its maxSlope");
                int hx = (int)(s.Route.X0 / w.Config.CellMeters), hz = (int)(s.Route.Z0 / w.Config.CellMeters);
                int home = Math.Max(0, Math.Min(w.Config.Size - 1, hz)) * w.Config.Size + Math.Max(0, Math.Min(w.Config.Size - 1, hx));
                if (w.Biome == null || !body.Biomes.Contains(w.BiomeIds[w.Biome[home]])) e.Add(body.Id + " " + s.Id + " has its herd home outside its biomes");
                int herd = CreaturePlan.HerdOf(s.Id);
                if (herdSeeds.TryGetValue(herd, out ulong hs)) { if (hs != s.HerdSeed) e.Add("creature herd " + herd + " members disagree on herd seed"); }
                else
                {
                    herdSeeds[herd] = s.HerdSeed;
                    if (!seedOwner.TryAdd(s.HerdSeed, herd)) e.Add("creature herds " + seedOwner[s.HerdSeed] + " and " + herd + " share a herd seed");
                }
                int site = CreaturePlan.SiteOf(s.Id);
                if (!bySite.TryGetValue(site, out var l)) bySite[site] = l = new List<CreatureSpawn>();
                l.Add(s);
            }

            ulong root = CreaturePlan.Root(w);
            int across = CreaturePlan.SitesAcross(w.Config.Size);
            float cm = w.Config.CellMeters, siteM = CreaturePlan.SiteCells * cm;
            var slice = new List<CreatureSpawn>();
            foreach (var kv in bySite)
            {
                slice.Clear();
                CreaturePlan.Site(w, cells, lib, root, kv.Key, drinks, slice);
                bool same = slice.Count == kv.Value.Count;
                for (int i = 0; same && i < slice.Count; i++) same = Same(slice[i], kv.Value[i]);
                if (!same) e.Add("creature site " + kv.Key + " does not rebuild on its own");

                float x0 = kv.Key % across * siteM - CreaturePlan.HerdSpread, z0 = kv.Key / across * siteM - CreaturePlan.HerdSpread;
                float x1 = x0 + siteM + 2 * CreaturePlan.HerdSpread, z1 = z0 + siteM + 2 * CreaturePlan.HerdSpread;
                var herds = new Dictionary<int, int>();
                foreach (var s in kv.Value)
                {
                    if (s.X < x0 || s.X > x1 || s.Z < z0 || s.Z > z1) e.Add("creature " + s.Id + " outside its site");
                    herds.TryGetValue(s.Body, out int c);
                    herds[s.Body] = c + 1;
                }
                foreach (var h in herds)
                {
                    var def = lib.Bodies[h.Key];
                    if (h.Value < def.HerdMin || h.Value > def.HerdMax) e.Add("creature site " + kv.Key + " herd of " + h.Value + " " + def.Id);
                }
            }
            FleeStaysAshore(w, lib, a, e);
            RoutesStayAshore(w, cells, lib, a, e);
            NeedsHold(w, cells, lib, a, pools, e);
            DrivesStayBounded(lib, a, e);
            DrinkSpotsHold(w, cells, lib, a, drinks, e);
            return e;
        }

        public const float RouteStepSeconds = 2f;

        static void RoutesStayAshore(GeneratedWorld w, NearArrays cells, CreatureLibrary lib, List<CreatureSpawn> plan, List<string> e)
        {
            var wet = new WorldWet { World = w };
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, reach = (HerdRoute.Cells + 1) * cm * 1.5f;
            var done = new HashSet<int>();
            int moving = 0;
            foreach (var s in plan)
            {
                int herd = CreaturePlan.HerdOf(s.Id);
                if (!done.Add(herd)) continue;
                var r = s.Route;
                if (r.Extent > 0) moving++;
                var def = lib.Bodies[s.Body];
                if (!(r.Speed > 0 && r.Speed <= def.WalkSpeed && r.Dwell >= HerdRoute.DwellMin && r.Dwell <= HerdRoute.DwellMax && r.Phase >= 0 && r.Phase < 1))
                { e.Add("creature herd " + herd + " route speed, dwell or phase out of range"); continue; }
                if (r.Extent > reach) e.Add("creature herd " + herd + " route reaches " + (int)r.Extent + " m from home");
                for (int i = 0; i < HerdRoute.Points; i++)
                {
                    int j = (i + 1) % HerdRoute.Points;
                    if (!CreaturePlan.Habitable(w, cells, def, (int)(r.X(i) / cm), (int)(r.Z(i) / cm))) e.Add("creature herd " + herd + " route point " + i + " is not habitable");
                    if ((r.X(i) != r.X(j) || r.Z(i) != r.Z(j)) && HerdMind.CrossesWet(ref wet, n, cm, r.X(i), r.Z(i), r.X(j), r.Z(j))) e.Add("creature herd " + herd + " route leg " + i + " crosses water");
                }
                float period = r.Period, limit = r.Speed * HerdRoute.PeakRatio * RouteStepSeconds * 1.01f + 1e-3f;
                r.At(0, out float px, out float pz, out _);
                for (double t = RouteStepSeconds; t <= 2 * period + RouteStepSeconds; t += RouteStepSeconds)
                {
                    r.At(t, out float x, out float z, out _);
                    float dx = x - px, dz = z - pz;
                    if (dx * dx + dz * dz > limit * limit) { e.Add("creature herd " + herd + " route jumps " + (float)Math.Sqrt(dx * dx + dz * dz) + " m at " + t + " s"); break; }
                    px = x; pz = z;
                }
                r.At(1e6, out float ax, out float az, out _);
                r.At(1e6 + period, out float bx, out float bz, out _);
                if (Math.Abs(ax - bx) > 0.01f || Math.Abs(az - bz) > 0.01f) e.Add("creature herd " + herd + " route does not loop");
            }
            if (done.Count >= 4 && moving * 2 < done.Count) e.Add("only " + moving + " of " + done.Count + " creature herds have a grazing route");
        }

        static void NeedsHold(GeneratedWorld w, NearArrays cells, CreatureLibrary lib, List<CreatureSpawn> plan, float[] pools, List<string> e)
        {
            var wet = new WorldWet { World = w };
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var done = new HashSet<int>();
            int drinkers = 0;
            foreach (var s in plan)
            {
                int herd = CreaturePlan.HerdOf(s.Id);
                if (!done.Add(herd)) continue;
                var def = lib.Bodies[s.Body];
                var needs = s.Needs;
                float hx = s.Route.X0, hz = s.Route.Z0, far = (CreaturePlan.NeedM + cm) * 1.5f;
                for (byte act = Drives.Graze; act <= Drives.Bed; act++)
                {
                    if (!needs.Can(act)) continue;
                    needs.Target(act, out float x, out float z);
                    if ((x - hx) * (x - hx) + (z - hz) * (z - hz) > far * far) e.Add("creature herd " + herd + " need " + act + " target lies " + (int)Math.Sqrt((x - hx) * (x - hx) + (z - hz) * (z - hz)) + " m from home");
                    if (HerdMind.CrossesWet(ref wet, n, cm, hx, hz, x, z)) e.Add("creature herd " + herd + " need " + act + " target lies across water");
                    if (act != Drives.Drink && !CreaturePlan.Habitable(w, cells, def, (int)(x / cm), (int)(z / cm))) e.Add("creature herd " + herd + " need " + act + " target is not habitable");
                }
                if (needs.Can(Drives.Graze) && !CreaturePlan.Cover(w, (int)(needs.GrazeZ / cm) * n + (int)(needs.GrazeX / cm))) e.Add("creature herd " + herd + " grazes off ground cover");
                if (needs.Can(Drives.Drink))
                {
                    drinkers++;
                    if (!CreaturePlan.AtShore(w, cells, def, needs, pools)) e.Add("creature herd " + herd + " drinks away from a walkable lake, river or pool edge at " + (int)needs.DrinkX + "," + (int)needs.DrinkZ);
                }
            }
            if (done.Count >= 8 && drinkers * 10 < done.Count) e.Add("only " + drinkers + " of " + done.Count + " creature herds have water to drink");
        }

        static void DrinkSpotsHold(GeneratedWorld w, NearArrays cells, CreatureLibrary lib, List<CreatureSpawn> plan, DrinkSpotList spots, List<string> e)
        {
            int n = w.Config.Size, across = CreaturePlan.SitesAcross(n);
            float cm = w.Config.CellMeters, top = 0;
            foreach (var def in lib.Bodies) top = Math.Max(top, Footing.Grade(def.MaxSlope));
            if (spots.Start == null || spots.Start.Length != across * across + 1 || spots.Start[0] != 0 || spots.Start[across * across] != spots.Count) { e.Add("drink spot site index does not cover the spot list"); return; }
            var keys = new HashSet<long>();
            var pooled = new List<DrinkSpot>();
            for (int site = 0; site < across * across; site++)
                for (int k = spots.Start[site]; k < spots.Start[site + 1]; k++)
                {
                    var s = spots.Spots[k];
                    string at = " at " + (int)s.X + "," + (int)s.Z;
                    if (HerdMind.OffMap(n, cm, s.X, s.Z, out int c)) { e.Add("drink spot off the map" + at); continue; }
                    if (w.Sea[c]) e.Add("drink spot on sea" + at);
                    if (CreaturePlan.Wet(w, c)) e.Add("drink spot is not dry" + at);
                    if (c % n / CreaturePlan.SiteCells + c / n / CreaturePlan.SiteCells * across != site) e.Add("drink spot filed under the wrong site" + at);
                    bool bank = CreaturePlan.Bank(w, s.X, s.Z, s.WaterX, s.WaterZ);
                    if (!bank && !CreaturePlan.AtPool(w, cells, spots.Pools, s.X, s.Z, s.WaterX, s.WaterZ)) e.Add("drink spot is not beside drinkable water" + at);
                    if (!s.Fits(top) || Footing.SlopeSq(cells, s.X, s.Z) != s.Steep) e.Add("drink spot is not walkable" + at);
                    if (!(s.Cost >= 0)) e.Add("drink spot cost below zero" + at);
                    if (bank && !keys.Add((long)c * 4 + (s.WaterX + 2 * s.WaterZ > 0 ? 1 : 0) * 2 + (s.WaterX != 0 ? 1 : 0))) e.Add("drink spot repeats a bank" + at);
                    if (!bank)
                    {
                        foreach (var q in pooled)
                            if ((q.X - s.X) * (q.X - s.X) + (q.Z - s.Z) * (q.Z - s.Z) < cm * cm) { e.Add("pool drink spots crowd each other" + at); break; }
                        pooled.Add(s);
                    }
                }
            int banks = 0;
            for (int z = 1; z < n - 1; z++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = z * n + x;
                    if (CreaturePlan.Wet(w, i)) continue;
                    if (CreaturePlan.Drinkable(w, i + 1) || CreaturePlan.Drinkable(w, i - 1) || CreaturePlan.Drinkable(w, i + n) || CreaturePlan.Drinkable(w, i - n)) banks++;
                }
            if (banks >= 16 && spots.Count * 4 < banks) e.Add("only " + spots.Count + " drink spots along " + banks + " drinkable bank cells");
            var done = new HashSet<int>();
            foreach (var s in plan)
            {
                if (!done.Add(CreaturePlan.HerdOf(s.Id)) || !s.Needs.Can(Drives.Drink)) continue;
                bool listed = false;
                for (int k = 0; k < spots.Count && !listed; k++) listed = spots.Spots[k].X == s.Needs.DrinkX && spots.Spots[k].Z == s.Needs.DrinkZ;
                if (!listed) e.Add("creature herd " + CreaturePlan.HerdOf(s.Id) + " drinks at a point that is not a drink spot");
            }
        }

        public const float DriveStepSeconds = 5f;

        static void DrivesStayBounded(CreatureLibrary lib, List<CreatureSpawn> plan, List<string> e)
        {
            var done = new HashSet<int>();
            foreach (var s in plan)
            {
                if (!done.Add(CreaturePlan.HerdOf(s.Id))) continue;
                var drives = lib.Bodies[s.Body].Drives;
                for (double t = 0; t < 7200; t += 997)
                {
                    byte want = Drives.Wake(drives, s.Needs, t, s.HerdSeed, s.Seed, out var levels);
                    for (int k = 0; k < 400; k++)
                    {
                        want = Drives.Choose(levels, drives, s.Needs, want);
                        Drives.Advance(ref levels, drives, want, DriveStepSeconds);
                    }
                    for (int i = 0; i < Drives.Count; i++)
                        if (!(levels[i] >= 0 && levels[i] <= 1)) { e.Add("creature " + s.Id + " drive " + i + " left 0..1"); return; }
                    if (want != Drives.None && !s.Needs.Can(want)) { e.Add("creature " + s.Id + " wants a need its herd has no target for"); return; }
                }
            }
        }

        public const int FleeDirections = 8;

        static void FleeStaysAshore(GeneratedWorld w, CreatureLibrary lib, List<CreatureSpawn> plan, List<string> e)
        {
            var ground = CreaturePlan.Ground(w);
            var done = new HashSet<int>();
            const float dt = 0.25f;
            foreach (var s in plan)
            {
                int herd = CreaturePlan.HerdOf(s.Id);
                if (!done.Add(herd)) continue;
                var def = lib.Bodies[s.Body];
                var wary = CreaturePlan.Wary(def);
                for (int d = 0; d < FleeDirections; d++)
                {
                    float a = d * 6.2831853f / FleeDirections;
                    float dx = (float)Math.Sin(a), dz = (float)Math.Cos(a);
                    float px = s.X - dx * wary.Flee * 0.5f, pz = s.Z - dz * wary.Flee * 0.5f;
                    float x = s.X, z = s.Z;
                    var mind = HerdMind.Start(s.HerdSeed);
                    for (float t = 0; t < HerdMind.FleeMax + 10f; t += dt)
                    {
                        float ax = x - px, az = z - pz;
                        mind.Step(wary, (float)Math.Sqrt(ax * ax + az * az), ax, az, x, z, ref ground, dt);
                        if (mind.Mode != HerdMode.Flee) continue;
                        x += (float)Math.Sin(mind.Heading) * def.RunSpeed * dt;
                        z += (float)Math.Cos(mind.Heading) * def.RunSpeed * dt;
                        if (ground.Blocked(x, z)) { e.Add("creature herd " + herd + " fleeing from heading " + d + " ran into water or off the map at " + (int)x + "," + (int)z); break; }
                    }
                }
            }
        }

        static bool Same(CreatureSpawn a, CreatureSpawn b) =>
            a.Id == b.Id && a.Body == b.Body && a.X == b.X && a.Z == b.Z && a.Heading == b.Heading
            && a.WalkSpeed == b.WalkSpeed && a.BodySeed == b.BodySeed && a.Seed == b.Seed && a.HerdSeed == b.HerdSeed
            && a.Route.X0 == b.Route.X0 && a.Route.Z0 == b.Route.Z0 && a.Route.X1 == b.Route.X1 && a.Route.Z1 == b.Route.Z1
            && a.Route.X2 == b.Route.X2 && a.Route.Z2 == b.Route.Z2 && a.Route.X3 == b.Route.X3 && a.Route.Z3 == b.Route.Z3
            && a.Route.Speed == b.Route.Speed && a.Route.Dwell == b.Route.Dwell && a.Route.Phase == b.Route.Phase
            && a.Needs.Has == b.Needs.Has && a.Needs.GrazeX == b.Needs.GrazeX && a.Needs.GrazeZ == b.Needs.GrazeZ
            && a.Needs.DrinkX == b.Needs.DrinkX && a.Needs.DrinkZ == b.Needs.DrinkZ && a.Needs.WaterX == b.Needs.WaterX && a.Needs.WaterZ == b.Needs.WaterZ
            && a.Needs.BedX == b.Needs.BedX && a.Needs.BedZ == b.Needs.BedZ;
    }
}
