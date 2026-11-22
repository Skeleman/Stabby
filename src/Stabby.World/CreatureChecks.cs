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
            return e;
        }

        static bool Same(CreatureSpawn a, CreatureSpawn b) =>
            a.Id == b.Id && a.Body == b.Body && a.X == b.X && a.Z == b.Z && a.Heading == b.Heading
            && a.WalkSpeed == b.WalkSpeed && a.BodySeed == b.BodySeed && a.Seed == b.Seed && a.HerdSeed == b.HerdSeed;
    }
}
