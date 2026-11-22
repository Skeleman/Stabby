using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        static void CheckDiscoveries(GeneratedWorld w, TerrainDetail detail, EditGrid grid, List<string> e)
        {
            var field = new ObjectField(w, detail, grid.Edits, w.Objects);
            var found = field.Discoveries();
            int n = w.Config.Size, across = field.ChunksAcross;
            float cm = w.Config.CellMeters, worldM = n * cm, span = field.ChunkMeters;
            for (int a = 0; a < found.Length; a++)
            {
                var s = found[a];
                var def = w.Objects.Objects[s.Def];
                string at = $"{def.Id} at {s.X:F0},{s.Z:F0}";
                float r = field.SiteRadius(s);
                for (int k = 0; k < 9; k++)
                {
                    float ux = k % 3 - 1, uz = k / 3 - 1, len = ux != 0 && uz != 0 ? 0.70710678f : 1f;
                    float px = s.X + r * ux * len, pz = s.Z + r * uz * len;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) { e.Add(at + " reaches past the world edge"); return; }
                    int i = Math.Min(n - 1, (int)(pz / cm)) * n + Math.Min(n - 1, (int)(px / cm));
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0) { e.Add(at + " stands in water"); return; }
                }
                foreach (var l in field.Towns)
                {
                    float dx = l.X * cm - s.X, dz = l.Y * cm - s.Z;
                    if (dx * dx + dz * dz < ObjectField.TownClearM * ObjectField.TownClearM) { e.Add(at + $" lies within {ObjectField.TownClearM:F0} m of a {l.Kind}"); return; }
                }
                if (def.Placement.Discovery == ObjectField.Camp && !(field.CampDraw(s.X, s.Z) > 0)) { e.Add(at + " is neither by a forest edge nor by a river"); return; }
                if (def.Placement.Discovery == ObjectField.Hermit && !(field.Woods(s.X, s.Z) >= ObjectField.HermitWoods)) { e.Add(at + " is not deep in the woods"); return; }
                if (def.Placement.Discovery == ObjectField.Shrine) { float rd = field.RoadSide(s.X, s.Z, out _, out _); if (!(rd >= ObjectField.ShrineRoadMinM && rd <= ObjectField.ShrineRoadM)) { e.Add(at + " is not beside a road"); return; } if (!field.SeatGrounded(s)) { e.Add(at + " has its seat buried or floating"); return; } }
                if (def.Placement.Discovery == ObjectField.Spring) { if (!(field.SpringDraw(s.X, s.Z) > 0)) { e.Add(at + " is not near a range or volcanic relief"); return; } var fault = field.PoolFault(s); if (fault != null) { e.Add(at + " " + fault); return; } }
                float gap = 2 * ObjectField.DiscoveryInset * ObjectField.DiscoveryStep(worldM, def.Placement.SpacingM);
                for (int b = a + 1; b < found.Length; b++)
                {
                    float dx = found[b].X - s.X, dz = found[b].Z - s.Z, g = found[b].Def == s.Def ? gap : ObjectField.DiscoveryGapM;
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
