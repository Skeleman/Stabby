using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const string ErraticId = "erratic";

        static void CheckGlacial(GeneratedWorld w, List<string> e)
        {
            if (w.Water == null) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            foreach (var m in w.Sketch.Moraines)
            {
                string at = $"moraine at {m.X:F0},{m.Z:F0}";
                for (int k = 0; k < m.Kettles.Count; k += 4)
                {
                    int cx = Math.Min(n - 1, (int)Math.Round(m.Kettles[k] / cm)), cz = Math.Min(n - 1, (int)Math.Round(m.Kettles[k + 1] / cm));
                    if (w.Water.LakeId[cz * n + cx] < 0) { e.Add($"kettle at {m.Kettles[k]:F0},{m.Kettles[k + 1]:F0} of {at} holds no water"); return; }
                }
                float reach = Glacial.Reach(m);
                int x0 = Math.Max(0, (int)Math.Floor((m.X - reach) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((m.X + reach) / cm));
                int z0 = Math.Max(0, (int)Math.Floor((m.Z - reach) / cm)), z1 = Math.Min(n - 1, (int)Math.Ceiling((m.Z + reach) / cm));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = z * n + x;
                        if (Glacial.Ridge(m, x * cm, z * cm) <= Glacial.CrestM) continue;
                        if (w.Water.IsRiver[i]) { e.Add($"{at} raises its crest across a river at {x * cm:F0},{z * cm:F0}"); return; }
                        if (w.Sketch.Landmark[i]) { e.Add($"{at} raises its crest over a landmark at {x * cm:F0},{z * cm:F0}"); return; }
                    }
            }
        }

        static void CheckGlacialGround(GeneratedWorld w, TerrainDetail detail, EditGrid grid, ObjectField field, List<string> e)
        {
            float cm = w.Config.CellMeters;
            if (w.Sketch != null)
                foreach (var m in w.Sketch.Moraines)
                {
                    string at = $"moraine at {m.X:F0},{m.Z:F0}";
                    foreach (var p in field.TownPlans())
                    {
                        if (Glacial.CrestGap(m, p.X, p.Z) < p.Radius + m.W) { e.Add($"{at} raises its crest inside the town at {p.X:F0},{p.Z:F0}"); return; }
                    }
                    for (int k = 0; k < m.Kettles.Count; k += 4)
                        if (field.InTownGround(m.Kettles[k], m.Kettles[k + 1], Glacial.WaterShare * m.Kettles[k + 2], false)) { e.Add($"kettle of {at} floods town ground at {m.Kettles[k]:F0},{m.Kettles[k + 1]:F0}"); return; }
                    for (int k = 0; k < m.Kettles.Count; k += 4)
                        if (field.OnRoad(m.Kettles[k], m.Kettles[k + 1], Glacial.WaterShare * m.Kettles[k + 2])) { e.Add($"kettle of {at} floods a road"); return; }
                }
            var defs = w.Objects.Objects;
            int d = defs.FindIndex(o => o.Id == ErraticId);
            if (d < 0) return;
            float least = ObjectField.BoulderMainR * defs[d].FootprintRadius() * defs[d].Placement.SizeMinM - 0.01f;
            foreach (var b in grid.Edits)
            {
                if (b.Shape != FeatureEdit.Boulder || b.R < least) continue;
                float x = 0.5f * (b.MinX + b.MaxX), z = 0.5f * (b.MinZ + b.MaxZ);
                string at = $"erratic at {x:F0},{z:F0}";
                if (field.OnRoad(x, z, b.R)) { e.Add(at + " sits on a road"); return; }
                if (field.InTownGround(x, z, b.R, true)) { e.Add(at + " sits in a town"); return; }
                int before = e.Count;
                CheckBoulderFoot(detail, cm, b, e);
                if (e.Count > before) return;
            }
        }
    }
}
