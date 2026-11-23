using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        static void CheckMesas(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var seen = new int[n * n];
            var queue = new List<int>();
            for (int k = 0; k < w.Sketch.Mesas.Count; k++)
            {
                var m = w.Sketch.Mesas[k];
                string at = $"{(m.Butte ? "butte" : "mesa")} at {m.X:F0},{m.Z:F0}";
                float sketched = 2f * Mesas.Extent(m);
                if (m.Butte ? sketched >= Mesas.MinAcrossM : sketched < Mesas.MinAcrossM || sketched > Mesas.MaxAcrossM) { e.Add(at + $" is {sketched:F0} m across"); continue; }
                if (cm > 0.25f * sketched) continue;
                int cx = Math.Min(n - 1, (int)Math.Round(m.X / cm)), cz = Math.Min(n - 1, (int)Math.Round(m.Z / cm));
                float floor = m.Top - 0.5f * m.Cap, roof = m.Top + Mesas.TopNoiseM + Mesas.MaxTilt * sketched + 1f;
                if (w.Height[cz * n + cx] < floor) { e.Add(at + " lost its caprock"); continue; }
                int lim = (int)Math.Ceiling(Mesas.Reach(m, m.Top - m.Base) / cm), x0 = cx, x1 = cx, z0 = cz, z1 = cz;
                queue.Clear(); queue.Add(cz * n + cx); seen[cz * n + cx] = k + 1;
                bool spill = false, peak = false;
                for (int q = 0; q < queue.Count; q++)
                {
                    int i = queue[q], x = i % n, z = i / n;
                    if (w.Height[i] > roof) peak = true;
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z);
                    for (int d = 0; d < 4; d++)
                    {
                        int px = x + Grid.DX[2 * d], pz = z + Grid.DY[2 * d];
                        if (px < 0 || pz < 0 || px >= n || pz >= n) continue;
                        int j = pz * n + px;
                        if (seen[j] == k + 1 || w.Height[j] < floor) continue;
                        if (Math.Abs(px - cx) > lim || Math.Abs(pz - cz) > lim) { spill = true; continue; }
                        seen[j] = k + 1; queue.Add(j);
                    }
                }
                if (spill) { e.Add(at + " has no cap cliff on some side"); continue; }
                if (peak) { e.Add(at + " has ground rising above its caprock"); continue; }
                float across = (Math.Max(x1 - x0, z1 - z0) + 1) * cm;
                if (Math.Abs(across - sketched) > 2f * cm + 0.25f * sketched) e.Add(at + $" measures {across:F0} m across, sketched {sketched:F0} m");
            }
        }

        static void CheckMesaClearings(GeneratedWorld w, ObjectField field, ObjectInstance[] found, List<string> e)
        {
            if (w.Sketch == null) return;
            var bogs = field.BogPools();
            foreach (var m in w.Sketch.Mesas)
            {
                string at = $"{(m.Butte ? "butte" : "mesa")} at {m.X:F0},{m.Z:F0}";
                for (int k = 0; k < bogs.Length; k += ObjectField.BogStride)
                {
                    if (Mesas.Dist(m, bogs[k], bogs[k + 1]) - bogs[k + 2] < Mesas.Toe(m, ObjectField.BogSteadyM)) { e.Add(at + $" covers a bog pool at {bogs[k]:F0},{bogs[k + 1]:F0}"); return; }
                }
                foreach (var s in found)
                {
                    if (!ObjectField.Pooled(w.Objects.Objects[s.Def].Placement.Discovery)) continue;
                    if (Mesas.Dist(m, s.X, s.Z) - field.SiteRadius(s) < Mesas.Toe(m, ObjectField.BogSteadyM)) { e.Add(at + $" covers a {w.Objects.Objects[s.Def].Id} at {s.X:F0},{s.Z:F0}"); return; }
                }
            }
        }
    }
}
