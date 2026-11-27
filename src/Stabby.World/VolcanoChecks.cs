using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const int VolcanoSlopeDirs = 64;
        public const float VolcanoStepM = 2f, VolcanoBiomeShare = 0.75f, VolcanoSummitShare = 0.5f, VolcanoTownPadM = 200f;

        public static List<string> VolcanoFaults(GeneratedWorld w, ObjectField field)
        {
            var e = new List<string>();
            CheckVolcanoes(w, e);
            CheckVolcanoPools(w, field, e);
            return e;
        }

        static void CheckVolcanoes(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            foreach (var v in w.Sketch.Volcanoes)
            {
                string at = $"volcano at {v.X:F0},{v.Z:F0}";
                bool big = Volcanoes.Big(v);
                if (big ? v.Ro < Volcanoes.BigMinRoM || v.Ro > Volcanoes.BigMaxRoM : v.Ro < Volcanoes.MinRoM) { e.Add(at + $" has base radius {v.Ro:F0} m"); continue; }
                float share = v.Rc / v.Ro;
                if (share < 0.25f || share > 0.35f) { e.Add(at + $" has crater share {share:F2}"); continue; }
                if (v.H < 50f) e.Add(at + $" is only {v.H:F0} m high");
                bool steep = false, walk = false;
                for (int k = 0; k < VolcanoSlopeDirs && !steep; k++)
                {
                    Volcanoes.Ring(k, VolcanoSlopeDirs, out float ux, out float uz);
                    float worst = FlankSlope(v, ux, uz);
                    if (worst > Volcanoes.SlopeLimit) { e.Add(at + $" has a {worst:F2} flank slope"); steep = true; }
                    if (worst <= Underground.FloorSlope) walk = true;
                }
                if (steep) continue;
                if (!walk) e.Add(at + " has no walkable flank");
                if (Volcanoes.Top(v, v.Lx, v.Lz) - v.RimLow < 0f) e.Add(at + " has a rim below its crater floor");
                int cx = (int)Math.Round(v.X / cm), cz = (int)Math.Round(v.Z / cm), rr = (int)Math.Ceiling(v.R / cm), cells = 0, open = 0;
                bool river = false;
                float summit = float.MinValue;
                for (int dz = -rr; dz <= rr; dz++)
                    for (int dx = -rr; dx <= rr; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= n || z >= n || (dx * dx + dz * dz) * cm * cm > v.R * v.R) continue;
                        int i = z * n + x;
                        if (w.Water != null && w.Water.IsRiver[i] && (!big || ((dx * dx + dz * dz) * cm * cm <= v.Rc * v.Rc && w.Height[i] < v.RimLow - Volcanoes.TopPadM))) river = true;
                        summit = Math.Max(summit, w.Height[i]);
                        if (w.Biome == null || w.BiomeIds == null || w.Biome[i] < 0) continue;
                        cells++;
                        if (Array.IndexOf(Volcanoes.Biomes, w.BiomeIds[w.Biome[i]]) >= 0) open++;
                    }
                if (river) e.Add(at + " has a river on its cone");
                if (!big && cells > 0 && open < VolcanoBiomeShare * cells) e.Add(at + $" stands in {open}/{cells} dry open cells");
                if (summit < v.Base + VolcanoSummitShare * v.H) e.Add(at + $" lost its cone ({summit - v.Base:F0} m of {v.H:F0} m)");
                foreach (var l in w.Sketch.Landmarks)
                {
                    float dx = l.X * cm - v.X, dz = l.Y * cm - v.Z;
                    if (dx * dx + dz * dz < Volcanoes.LandmarkGapM * Volcanoes.LandmarkGapM) e.Add(at + " is too close to a landmark");
                }
            }
        }

        static float FlankSlope(VolcanoSketch v, float ux, float uz)
        {
            float worst = 0f, from = v.Rc * (1f + Volcanoes.CraterNoise) + VolcanoStepM, prev = Volcanoes.Profile(v, v.X + ux * from, v.Z + uz * from);
            for (float r = from + VolcanoStepM; r <= v.Ro; r += VolcanoStepM)
            {
                float g = Volcanoes.Profile(v, v.X + ux * r, v.Z + uz * r);
                if (g == float.MinValue) break;
                worst = Math.Max(worst, Math.Abs(g - prev) / VolcanoStepM);
                prev = g;
            }
            return worst;
        }

        static void CheckVolcanoPools(GeneratedWorld w, ObjectField field, List<string> e)
        {
            if (w.Sketch == null || w.Sketch.Volcanoes.Count == 0) return;
            var pools = field.VolcanoPools();
            int n = w.Config.Size, lava = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("lava"));
            float cm = w.Config.CellMeters;
            var towns = field.TownPlans();
            var roads = field.Roads();
            foreach (var v in w.Sketch.Volcanoes)
            {
                string at = $"volcano at {v.X:F0},{v.Z:F0}";
                foreach (var t in towns)
                    if ((t.X - v.X) * (t.X - v.X) + (t.Z - v.Z) * (t.Z - v.Z) < (t.Radius + v.Ro) * (t.Radius + v.Ro)) e.Add(at + " overlaps a town");
                foreach (var s in roads)
                    if (Volcanoes.SegmentGap(v.X, v.Z, s.X0, s.Z0, s.X1, s.Z1) < v.Rc + s.HalfWidth) e.Add(at + " has a road through its crater");
                LiquidPool p = null;
                foreach (var q in pools) if (field.VolcanoOf(q) == v) p = q;
                if (p == null) { e.Add(at + " has no crater pool"); continue; }
                if ((p.Liquid == lava) != v.Active) e.Add(at + $" holds the wrong liquid for an {(v.Active ? "active" : "inactive")} cone");
                if (p.Level >= v.RimLow + Volcanoes.RimSlackM) e.Add(at + $" pool level {p.Level:F1} is not below its rim {v.RimLow:F1}");
                bool outside = false, touches = false;
                for (int j = 0; j < p.Nz && !outside; j++)
                    for (int i = 0; i < p.Nx; i++)
                    {
                        if (!p.Wet[j * p.Nx + i]) continue;
                        float x = p.NodeX(i), z = p.NodeZ(j);
                        if ((x - v.X) * (x - v.X) + (z - v.Z) * (z - v.Z) > v.Rc * v.Rc) { outside = true; break; }
                        if (!v.Active || touches) continue;
                        int ci = Math.Max(0, Math.Min(n - 1, (int)Math.Round(x / cm))), cj = Math.Max(0, Math.Min(n - 1, (int)Math.Round(z / cm)));
                        if (w.Water != null && w.Water.IsRiver[cj * n + ci]) touches = true;
                        foreach (var t in towns)
                            if ((t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < (t.Radius + VolcanoTownPadM) * (t.Radius + VolcanoTownPadM)) touches = true;
                    }
                if (outside) e.Add(at + " pool spills past its crater");
                if (touches) e.Add(at + " lava touches a river or a town");
            }
        }
    }
}
