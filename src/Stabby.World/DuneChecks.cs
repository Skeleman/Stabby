using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        const float DuneStep = 0.5f;

        static void CheckDunes(GeneratedWorld w, TerrainDetail d, List<string> e)
        {
            if (!d.HasDunes) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var hy = w.Water;
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int i = y * n + x;
                    int p00 = d.DunePack(i), p10 = d.DunePack(i + 1), p01 = d.DunePack(i + n), p11 = d.DunePack(i + n + 1);
                    if ((p00 | p10 | p01 | p11) == 0) continue;
                    bool wet = w.Sea[i] || (hy != null && hy.LakeId[i] >= 0) || d.Cells.PieceStart(i) != d.Cells.PieceStart(i + 1);
                    if (wet) { e.Add($"dune over water at cell {i}"); return; }
                    bool full = (p00 & p10 & p01 & p11 & 255) == 255;
                    for (int k = 0; k < 3; k++)
                    {
                        float px = (x + 0.25f + 0.25f * k) * cm, pz = (y + 0.75f - 0.25f * k) * cm;
                        float s = full ? Slope(d.Sample, px, pz) : Slope(d.DuneAt, px, pz);
                        if (s >= Dunes.MaxSlope) { e.Add($"dune slope {s:F3} over 35 degrees at ({px:F0}, {pz:F0})"); return; }
                    }
                }
        }

        static float Slope(Func<float, float, float> f, float x, float z)
        {
            float c = f(x, z);
            float gx = Math.Max(Math.Abs(f(x + DuneStep, z) - c), Math.Abs(c - f(x - DuneStep, z))) / DuneStep;
            float gz = Math.Max(Math.Abs(f(x, z + DuneStep) - c), Math.Abs(c - f(x, z - DuneStep))) / DuneStep;
            return (float)Math.Sqrt(gx * gx + gz * gz);
        }

        static void CheckDuneSites(TerrainDetail d, ObjectField field, List<string> e)
        {
            if (!d.HasDunes) return;
            foreach (var r in field.Roads())
            {
                if (r.Kind == RoadSegment.Tunnel) continue;
                float dx = r.X1 - r.X0, dz = r.Z1 - r.Z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
                int steps = Math.Max(1, (int)Math.Ceiling(len / 2f));
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps, x = r.X0 + dx * t, z = r.Z0 + dz * t;
                    if (d.DuneAt(x, z) > Dunes.ClearM) { e.Add($"road crosses a dune at ({x:F0}, {z:F0})"); return; }
                }
            }
            foreach (var t in field.TownPlans())
                if (d.DuneNear(t.X - t.Radius, t.Z - t.Radius, t.X + t.Radius, t.Z + t.Radius)) { e.Add($"town at ({t.X:F0}, {t.Z:F0}) sits on dunes"); return; }
        }
    }
}
