using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct FeatureEdit
    {
        public const int Add = 0, Carve = 1;

        public int Kind;
        public float Ax, Ay, Az, Bx, By, Bz, R;

        public float MinX => Math.Min(Ax, Bx) - R;
        public float MaxX => Math.Max(Ax, Bx) + R;
        public float MinY => Math.Min(Ay, By) - R;
        public float MaxY => Math.Max(Ay, By) + R;
        public float MinZ => Math.Min(Az, Bz) - R;
        public float MaxZ => Math.Max(Az, Bz) + R;

        public float Apply(float d, float x, float y, float z)
        {
            if (x < MinX || x > MaxX || y < MinY || y > MaxY || z < MinZ || z > MaxZ) return d;
            float ex = Bx - Ax, ey = By - Ay, ez = Bz - Az, px = x - Ax, py = y - Ay, pz = z - Az;
            float ll = ex * ex + ey * ey + ez * ez;
            float t = ll > 0 ? Math.Max(0f, Math.Min(1f, (px * ex + py * ey + pz * ez) / ll)) : 0f;
            float qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
            float dist = (float)Math.Sqrt(qx * qx + qy * qy + qz * qz);
            if (dist >= R) return d;
            return Kind == Add ? Math.Max(d, R - dist) : Math.Min(d, dist - R);
        }
    }

    public interface IEditList
    {
        int Count { get; }
        FeatureEdit At(int k);
    }

    public struct NoEdits : IEditList
    {
        public int Count => 0;
        public FeatureEdit At(int k) => default;
    }

    public struct EditArray : IEditList
    {
        public FeatureEdit[] Items;
        public int Count => Items == null ? 0 : Items.Length;
        public FeatureEdit At(int k) => Items[k];
    }

    public struct EditedSolid<TCols, TEdits> : ISolidField where TCols : struct, IColumnField where TEdits : struct, IEditList
    {
        public TCols Columns;
        public TEdits Edits;
        public float StepM;

        public float Density(int i, int j, int k) => Features.Apply(Edits, Columns.Height(i, k) - j * StepM, i, j, k, StepM);
    }

    public static class Features
    {
        public const int TileCells = 32;
        public const int MarginCells = 3;
        public const float OverhangChance = 0.6f, ArchChance = 0.3f;
        public const float OverhangMinDrop = 50f;
        public const float LatticeStepCells = 0.25f, MinRadiusSteps = 1.25f;

        public static float Apply<TEdits>(TEdits edits, float d, int i, int j, int k, float s) where TEdits : struct, IEditList
        {
            int count = edits.Count;
            if (count == 0) return d;
            float x = i * s, y = j * s, z = k * s;
            for (int q = 0; q < count; q++) d = edits.At(q).Apply(d, x, y, z);
            return d;
        }

        public static int TileOf(FeatureEdit e, float cellMeters)
        {
            float span = TileCells * cellMeters;
            int tx = (int)Math.Floor(e.MinX / span), tz = (int)Math.Floor(e.MinZ / span);
            return (int)Math.Floor(e.MaxX / span) == tx && (int)Math.Floor(e.MaxZ / span) == tz ? tz * 65536 + tx : -1;
        }

        public static bool Inside(FeatureEdit e, float cellMeters)
        {
            float span = TileCells * cellMeters, m = MarginCells * cellMeters;
            float x0 = (float)Math.Floor(e.MinX / span) * span, z0 = (float)Math.Floor(e.MinZ / span) * span;
            return e.MinX >= x0 + m && e.MaxX <= x0 + span - m && e.MinZ >= z0 + m && e.MaxZ <= z0 + span - m;
        }

        public static FeatureEdit[] Place(GeneratedWorld w, TerrainDetail detail)
        {
            var list = new List<FeatureEdit>();
            if (w.Biome == null || 16f < MinRadiusSteps * LatticeStepCells * w.Config.CellMeters) return list.ToArray();
            int tiles = w.Config.Size / TileCells;
            ulong ro = Rng.Stream(w.Config.Seed, "overhangs").NextU64(), ra = Rng.Stream(w.Config.Seed, "arches").NextU64();
            var tile = new List<FeatureEdit>();
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    ulong t = (ulong)(ty * tiles + tx);
                    var r = new Rng(Rng.Mix(ro + t));
                    tile.Clear();
                    Overhang(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                    r = new Rng(Rng.Mix(ra + t));
                    tile.Clear();
                    Arch(w, detail, tx, ty, ref r, tile);
                    Keep(w, tile, list);
                }
            return list.ToArray();
        }

        static void Keep(GeneratedWorld w, List<FeatureEdit> tile, List<FeatureEdit> list)
        {
            foreach (var e in tile) if (!Inside(e, w.Config.CellMeters)) return;
            list.AddRange(tile);
        }

        static bool Dry(GeneratedWorld w, int i) => !w.Sea[i] && w.Water.LakeId[i] < 0 && !w.Water.IsRiver[i] && w.Height[i] > 0;

        static void Overhang(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), radius = r.Range(16f, 26f), length = r.Range(40f, 90f), lift = r.Range(0.2f, 0.5f);
            if (roll >= OverhangChance) return;
            int n = w.Config.Size, best = -1, bestDir = 0;
            float bestDrop = OverhangMinDrop;
            for (int y = ty * TileCells + MarginCells; y < (ty + 1) * TileCells - MarginCells; y++)
                for (int x = tx * TileCells + MarginCells; x < (tx + 1) * TileCells - MarginCells; x++)
                {
                    int i = y * n + x;
                    if (!Dry(w, i)) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = i + Grid.DY[k] * n + Grid.DX[k];
                        float drop = (w.Height[i] - w.Height[j]) / Grid.Dist[k];
                        if (drop > bestDrop && Dry(w, j)) { bestDrop = drop; best = i; bestDir = k; }
                    }
                }
            if (best < 0) return;
            float cm = w.Config.CellMeters, inv = 1f / Grid.Dist[bestDir];
            float gx = Grid.DX[bestDir] * inv, gz = Grid.DY[bestDir] * inv;
            float fx = best % n + 0.7f * gx, fz = best / n + 0.7f * gz;
            float foot = detail.HeightAt(fx, fz);
            float inset = radius * 0.6f / cm, cx = fx - gx * inset, cz = fz - gz * inset;
            float y0 = foot + radius * lift;
            if (detail.HeightAt(cx, cz) < y0 + radius * 0.3f) return;
            if (detail.HeightAt(cx - gx * radius / cm, cz - gz * radius / cm) < y0 + radius + 8f) return;
            float half = length * 0.5f;
            output.Add(new FeatureEdit
            {
                Kind = FeatureEdit.Carve, R = radius,
                Ax = cx * cm - gz * half, Az = cz * cm + gx * half, Ay = y0,
                Bx = cx * cm + gz * half, Bz = cz * cm - gx * half, By = y0,
            });
        }

        static void Arch(GeneratedWorld w, TerrainDetail detail, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), px = r.Range(0.3f, 0.7f), pz = r.Range(0.3f, 0.7f);
            float dx = r.Range(-1f, 1f), dz = r.Range(-1f, 1f), span = r.Range(110f, 200f), radius = r.Range(16f, 22f), rise = r.Range(0.45f, 0.7f);
            if (roll >= ArchChance) return;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 0.2f) { dx = 1f; dz = 0f; len = 1f; }
            dx /= len; dz /= len;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            float mx = (tx + px) * TileCells, mz = (ty + pz) * TileCells, half = span * 0.5f / cm;
            float ax = mx - dx * half, az = mz - dz * half, bx = mx + dx * half, bz = mz + dz * half;
            if (!Dry(w, (int)az * n + (int)ax) || !Dry(w, (int)bz * n + (int)bx) || !Dry(w, (int)mz * n + (int)mx)) return;
            float ha = detail.HeightAt(ax, az), hb = detail.HeightAt(bx, bz);
            float ground = Math.Max(ha, hb);
            for (int s = 1; s < 8; s++) ground = Math.Max(ground, detail.HeightAt(ax + (bx - ax) * s / 8f, az + (bz - az) * s / 8f));
            float apex = Math.Max(ha, hb) + span * rise;
            if (apex - radius < ground + 25f) return;
            float y0 = ha - radius * 0.5f, y1 = hb - radius * 0.5f, ctrl = 2f * apex - 0.5f * (y0 + y1);
            const int Pieces = 5;
            float lx = ax, lz = az, ly = y0;
            for (int s = 1; s <= Pieces; s++)
            {
                float t = s / (float)Pieces, u = 1 - t;
                float nx = ax + (bx - ax) * t, nz = az + (bz - az) * t, ny = u * u * y0 + 2 * u * t * ctrl + t * t * y1;
                output.Add(new FeatureEdit { Kind = FeatureEdit.Add, R = radius, Ax = lx * cm, Ay = ly, Az = lz * cm, Bx = nx * cm, By = ny, Bz = nz * cm });
                lx = nx; ly = ny; lz = nz;
            }
        }
    }
}
