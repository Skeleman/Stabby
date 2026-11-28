using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        static void CheckBogs(GeneratedWorld w, ObjectField field, List<string> e)
        {
            var bogs = field.BogPools();
            foreach (var b in field.BogEdits())
                if (!b.MeshedIn(NearStepM, b.MinX, b.MinZ, b.MaxX, b.MaxZ)) { e.Add($"bog pool at {b.Ax:F0},{b.Az:F0} is too thin to mesh near the player"); return; }
            int n = w.Config.Size, wet = Array.IndexOf(w.BiomeIds, "wetland");
            float cm = w.Config.CellMeters, worldM = n * cm;
            for (int k = 0; k < bogs.Length; k += ObjectField.BogStride)
            {
                float x = bogs[k], z = bogs[k + 1], r = bogs[k + 2];
                string at = $"bog pool at {x:F0},{z:F0}";
                if (w.Biome[Math.Min(n - 1, (int)(z / cm + 0.5f)) * n + Math.Min(n - 1, (int)(x / cm + 0.5f))] != wet) { e.Add(at + " lies outside wetland"); return; }
                if (!(bogs[k + 4] < bogs[k + 3] && bogs[k + 3] < bogs[k + 5])) { e.Add(at + " has its floor, surface and bank out of order"); return; }
                for (int p = 0; p < 9; p++)
                {
                    float ux = p == 0 ? 0 : (float)Math.Cos(p * Math.PI / 4), uz = p == 0 ? 0 : (float)Math.Sin(p * Math.PI / 4);
                    float px = x + ux * (r + ObjectField.BogLipM), pz = z + uz * (r + ObjectField.BogLipM);
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) { e.Add(at + " reaches past the world edge"); return; }
                    int i = Math.Min(n - 1, (int)(pz / cm + 0.5f)) * n + Math.Min(n - 1, (int)(px / cm + 0.5f));
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || field.RiverGap(px, pz) <= 0) { e.Add(at + " stands in open water"); return; }
                }
            }
        }

        static void CheckQuicksand(GeneratedWorld w, ObjectField field, List<string> e)
        {
            var sands = field.QuicksandPools();
            foreach (var b in field.QuicksandEdits())
                if (!b.MeshedIn(NearStepM, b.MinX, b.MinZ, b.MaxX, b.MaxZ)) { e.Add($"quicksand at {b.Ax:F0},{b.Az:F0} is too thin to mesh near the player"); return; }
            if (sands.Length > 0 && w.Materials.Liquid("quicksand") < 0) { e.Add("quicksand patches are placed but the liquid library has no quicksand"); return; }
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, worldM = n * cm;
            for (int k = 0; k < sands.Length; k += ObjectField.BogStride)
            {
                float x = sands[k], z = sands[k + 1], r = sands[k + 2];
                string at = $"quicksand at {x:F0},{z:F0}";
                if (!(sands[k + 4] < sands[k + 3] && sands[k + 3] < sands[k + 5])) { e.Add(at + " has its floor, surface and bank out of order"); return; }
                for (int p = 0; p < 9; p++)
                {
                    float ux = p == 0 ? 0 : (float)Math.Cos(p * Math.PI / 4), uz = p == 0 ? 0 : (float)Math.Sin(p * Math.PI / 4);
                    float px = x + ux * (r + ObjectField.BogLipM), pz = z + uz * (r + ObjectField.BogLipM);
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) { e.Add(at + " reaches past the world edge"); return; }
                    int i = Math.Min(n - 1, (int)(pz / cm + 0.5f)) * n + Math.Min(n - 1, (int)(px / cm + 0.5f));
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || field.RiverGap(px, pz) <= 0) { e.Add(at + " stands in open water"); return; }
                    if (!field.SandAt(px, pz)) { e.Add(at + " lies off sand-coated ground"); return; }
                }
                float span = field.RimSpan(x, z, r);
                if (span > ObjectField.QuicksandSteadyM) { e.Add(at + $" sits on a slope: its rim spans {span:F2} m"); return; }
            }
            foreach (var p in field.LiquidPools())
                if (p.Kind == ObjectField.QuicksandPool && p.Liquid != w.Materials.Liquid("quicksand")) { e.Add($"quicksand at {p.X:F0},{p.Z:F0} holds the wrong liquid"); return; }
        }

        static void CheckPools(ObjectField field, List<string> e)
        {
            int expected = field.BogPools().Length / ObjectField.BogStride + field.QuicksandPools().Length / ObjectField.BogStride + field.OasisPools().Length / ObjectField.OasisStride + field.CavePools().Length + field.CenotePools().Length + field.VolcanoPools().Length;
            var spots = new List<float>();
            foreach (var o in field.Discoveries())
                if (field.IsVent(o)) { field.SpringPoolsAt(o, spots); expected += spots.Count / ObjectField.SpringStride; }
            var pools = field.LiquidPools();
            if (pools.Length != expected) { e.Add($"{pools.Length} liquid pools are water bodies but {expected} pools were placed"); return; }
            foreach (var p in pools)
            {
                string at = $"{(p.Kind == ObjectField.BogPool ? "bog" : p.Kind == ObjectField.CavePool ? "cave" : p.Kind == ObjectField.CenotePool ? "cenote" : p.Kind == ObjectField.QuicksandPool ? "quicksand" : p.Kind == ObjectField.OasisPool ? "oasis" : p.Kind == ObjectField.VolcanoPool ? "volcano" : "vent")} pool at {p.X:F0},{p.Z:F0}";
                if (!(p.Level <= p.Rim)) { e.Add(at + $" stands at {p.Level:F2} m, above its basin rim at {p.Rim:F2} m"); return; }
                if (p.Leaks) { e.Add(at + " spills out of its basin"); return; }
                if (p.WetCount == 0 || !(p.Level - p.Floor >= ObjectField.PoolMinDepthM)) { e.Add(at + $" holds no water: level {p.Level:F2} m over floor {p.Floor:F2} m"); return; }
                if (SheetFault(p, out float sx, out float sz, out float g)) { e.Add(at + $" spreads water at {p.Level:F2} m past its basin over ground at {g:F2} m ({sx:F1},{sz:F1})"); return; }
            }
        }

        static bool SheetFault(LiquidPool p, out float x, out float z, out float g)
        {
            x = z = g = 0;
            for (int k = 0; k < p.Wet.Length; k++)
            {
                if (!p.Wet[k]) continue;
                int i = k % p.Nx, j = k / p.Nx;
                for (int nj = Math.Max(0, j - 1); nj <= Math.Min(p.Nz - 1, j + 1); nj++)
                    for (int ni = Math.Max(0, i - 1); ni <= Math.Min(p.Nx - 1, i + 1); ni++)
                    {
                        int m = nj * p.Nx + ni;
                        if (p.Wet[m] || !(p.Ground[m] < p.Level)) continue;
                        x = p.NodeX(ni); z = p.NodeZ(nj); g = p.Ground[m];
                        return true;
                    }
            }
            return false;
        }

        static void CheckSiteFootprints(GeneratedWorld w, EditGrid grid, ObjectInstance[] sites, List<string> e)
        {
            var ids = new List<int>();
            foreach (var o in sites)
            {
                var def = w.Objects.Objects[o.Def];
                int kind = def.Placement.Discovery;
                float r = def.FootprintRadius() * o.Size;
                grid.Near(o.X - r, o.Z - r, o.X + r, o.Z + r, ids);
                foreach (int q in ids)
                {
                    var f = grid.Edits[q];
                    if (f.Link || f.Shape == FeatureEdit.Spring || f.Shape == FeatureEdit.Vault || ((kind == ObjectField.WolfDen || ObjectField.Pooled(kind)) && f.Shape == FeatureEdit.Boulder)) continue;
                    if (o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) { e.Add($"{def.Id} at {o.X:F0},{o.Z:F0} has feature edit {f.Shape} at {f.Ax:F0},{f.Az:F0} inside its footprint"); return; }
                }
            }
        }

        static void CheckDiscoveries(GeneratedWorld w, ObjectField field, List<string> e)
        {
            CheckBogs(w, field, e);
            CheckOases(w, field, e);
            CheckQuicksand(w, field, e);
            CheckPools(field, e);
            var found = field.Discoveries();
            var hot = field.Hotspots;
            for (int h = 0; h < hot.Count; h++)
            {
                int sites = field.HotspotSites(h), vents = 0, geysers = 0;
                if (sites < ObjectField.VentSitesMin) continue;
                foreach (var o in found) if (field.IsVent(o) && hot.Index(o.X, o.Z) == h) { vents++; if (field.VentKind(o) == ObjectField.Geyser) geysers++; }
                if (vents < ObjectField.VentsMin) e.Add($"geothermal hotspot at {hot.X[h]:F0},{hot.Z[h]:F0} has {sites} calm vent sites but {vents} vents");
                else if (geysers == 0 && field.KindCluster(h, ObjectField.Geyser) >= ObjectField.VentSitesMin) e.Add($"geothermal hotspot at {hot.X[h]:F0},{hot.Z[h]:F0} has a geyser-ground cluster but no geyser");
            }
            CheckMesaClearings(w, field, found, e);
            CheckVolcanoPools(w, field, e);
            int n = w.Config.Size, across = field.ChunksAcross;
            float cm = w.Config.CellMeters, worldM = n * cm, span = field.ChunkMeters;
            for (int a = 0; a < found.Length; a++)
            {
                var s = found[a];
                var def = w.Objects.Objects[s.Def];
                string at = $"{def.Id} at {s.X:F0},{s.Z:F0}";
                float r = field.SiteRadius(s);
                int biome = BiomeBorder.At(w, s.X, s.Z);
                if (def.Placement.Biomes.Count > 0 && (biome < 0 || !def.Placement.Biomes.Contains(w.BiomeIds[biome]))) { e.Add(at + $" stands in {(biome < 0 ? "no biome" : w.BiomeIds[biome])}, outside its biomes"); return; }
                for (int k = 0; k < 9; k++)
                {
                    float ux = k % 3 - 1, uz = k / 3 - 1, len = ux != 0 && uz != 0 ? 0.70710678f : 1f;
                    float px = s.X + r * ux * len, pz = s.Z + r * uz * len;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) { e.Add(at + " reaches past the world edge"); return; }
                    int i = Math.Min(n - 1, (int)(pz / cm)) * n + Math.Min(n - 1, (int)(px / cm));
                    if (w.Sea[i] || (w.Water.LakeId[i] >= 0 && (k == 4 || def.Placement.Discovery != ObjectField.SunkenTemple))) { e.Add(at + " stands in water"); return; }
                }
                foreach (var l in field.Towns)
                {
                    float dx = l.X * cm - s.X, dz = l.Y * cm - s.Z;
                    if (dx * dx + dz * dz < ObjectField.TownClearM * ObjectField.TownClearM) { e.Add(at + $" lies within {ObjectField.TownClearM:F0} m of a {l.Kind}"); return; }
                }
                if (def.Placement.Discovery == ObjectField.Camp && !(field.CampDraw(s.X, s.Z) > 0)) { e.Add(at + " is neither by a forest edge nor by a river"); return; }
                if (def.Placement.Discovery == ObjectField.Hermit && !(field.Woods(s.X, s.Z) >= ObjectField.HermitWoods)) { e.Add(at + " is not deep in the woods"); return; }
                if (def.Placement.Discovery == ObjectField.Ruin && field.Fitness(ObjectField.Ruin, s.X, s.Z) == float.MinValue) { e.Add(at + " stands on ground too broken for a ruin"); return; }
                if (def.Placement.Discovery == ObjectField.FairyRing && field.Fitness(ObjectField.FairyRing, s.X, s.Z) == float.MinValue) { e.Add(at + " stands on ground too uneven for a fairy ring"); return; }
                if (def.Placement.Discovery == ObjectField.WolfDen) { if (field.Fitness(ObjectField.WolfDen, s.X, s.Z) == float.MinValue) { e.Add(at + " stands on ground unfit for a wolf den"); return; } if (!field.BesideRock(s.X, s.Z)) { e.Add(at + " is not beside steep rock"); return; } var bf = field.DenBackFault(s); if (bf != null) { e.Add(at + " " + bf); return; } var cf = field.DenCoverFault(s); if (cf != null) { e.Add(at + " " + cf); return; } var df = field.DenFault(s); if (df != null) { e.Add(at + " " + df); return; } }
                if (def.Placement.Discovery == ObjectField.FallenTree) { var tf = field.TreeFault(s); if (tf != null) { e.Add(at + " " + tf); return; } }
                if (def.Placement.Discovery == ObjectField.AncientTree) { var af = field.AncientFault(s); if (af != null) { e.Add(at + " " + af); return; } }
                if (def.Placement.Discovery == ObjectField.Overlook) { var of = field.OverlookFault(s); if (of != null) { e.Add(at + " " + of); return; } if (field.OnRoad(s.X, s.Z, field.SiteRadius(s))) { e.Add(at + " lies on a road"); return; } }
                if (def.Placement.Discovery == ObjectField.Bones) { var bf = field.BonesFault(s); if (bf != null) { e.Add(at + " " + bf); return; } if (field.OnRoad(s.X, s.Z, field.SiteRadius(s))) { e.Add(at + " lies on a road"); return; } }
                if (def.Placement.Discovery == ObjectField.SaltTower) { var tf = field.SaltFault(s); if (tf != null) { e.Add(at + " " + tf); return; } }
                if (def.Placement.Discovery == ObjectField.Dungeon) { if (field.Fitness(ObjectField.Dungeon, s.X, s.Z) == float.MinValue) { e.Add(at + " stands on ground unfit for a dungeon"); return; } var vf = field.DungeonFault(s); if (vf != null) { e.Add(at + " " + vf); return; } }
                if (def.Placement.Discovery == ObjectField.LoneInn) { var lf = field.LoneInnFault(s); if (lf != null) { e.Add(at + " " + lf); return; } }
                if (def.Placement.Discovery == ObjectField.Monument) { var mf = field.MonumentFault(s); if (mf != null) { e.Add(at + " " + mf); return; } }
                if (def.Placement.Discovery == ObjectField.Watchtower) { var wf = field.WatchFault(s); if (wf != null) { e.Add(at + " " + wf); return; } }
                if (def.Placement.Discovery == ObjectField.SunkenTemple) { var tf = field.TempleFault(s); if (tf != null) { e.Add(at + " " + tf); return; } if (field.OnRoad(s.X, s.Z, field.SiteRadius(s))) { e.Add(at + " lies on a road"); return; } }
                if (def.Placement.Discovery == ObjectField.Shipwreck) { var sf = field.ShipwreckFault(s); if (sf != null) { e.Add(at + " " + sf); return; } }
                if (def.Placement.Discovery == ObjectField.Shrine) { float rd = field.RoadSide(s.X, s.Z, out _, out _); if (!(rd >= ObjectField.ShrineRoadMinM && rd <= ObjectField.ShrineRoadM)) { e.Add(at + " is not beside a road"); return; } if (!field.SeatGrounded(s)) { e.Add(at + " has its seat buried or floating"); return; } }
                if (ObjectField.Pooled(def.Placement.Discovery)) { if (!(field.Hotspots.Heat(s.X, s.Z) > 0)) { e.Add(at + " lies outside every geothermal hotspot"); return; } var fault = field.VentFault(s); if (fault != null) { e.Add(at + " " + fault); return; } if (!field.InVentField(s, found)) { e.Add(at + $" has no other vent within {ObjectField.VentFieldM:F0} m in its hotspot"); return; } }
                float gap = 2 * ObjectField.DiscoveryInset * ObjectField.DiscoveryStep(worldM, def.Placement.SpacingM);
                for (int b = a + 1; b < found.Length; b++)
                {
                    float dx = found[b].X - s.X, dz = found[b].Z - s.Z, g = field.IsVent(s) && field.IsVent(found[b]) ? ObjectField.VentGapM : found[b].Def == s.Def ? gap : ObjectField.DiscoveryGapM;
                    if (dx * dx + dz * dz < g * g) { e.Add(at + $" is closer than {g:F0} m to {w.Objects.Objects[found[b].Def].Id} at {found[b].X:F0},{found[b].Z:F0}"); return; }
                }
                int cx = (int)(s.X / span), cz = (int)(s.Z / span), hits = 0;
                for (int z = Math.Max(0, cz - 1); z <= Math.Min(across - 1, cz + 1); z++)
                    for (int x = Math.Max(0, cx - 1); x <= Math.Min(across - 1, cx + 1); x++)
                        foreach (var o in field.Sites(x, z))
                        {
                            if (o.X == s.X && o.Z == s.Z && o.Def == s.Def) { if (x == cx && z == cz) hits++; continue; }
                            if (Math.Min(field.SiteGap(o, s.X, s.Z), field.SiteGap(s, o.X, o.Z)) < r) { e.Add(at + $" overlaps {w.Objects.Objects[o.Def].Id} at {o.X:F0},{o.Z:F0}"); return; }
                        }
                if (hits != 1) { e.Add(at + $" appears {hits} times in its chunk sites"); return; }
            }
        }
    }
}
