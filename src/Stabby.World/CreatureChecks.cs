using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public static List<string> Creatures(GeneratedWorld w, CreatureLibrary lib)
        {
            var e = new List<string>();
            var a = CreaturePlan.Build(w, lib);
            var b = CreaturePlan.Build(w, lib);
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
                CreaturePlan.Site(w, lib, root, kv.Key, slice);
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
            return e;
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
            && a.WalkSpeed == b.WalkSpeed && a.BodySeed == b.BodySeed && a.Seed == b.Seed && a.HerdSeed == b.HerdSeed;
    }
}
