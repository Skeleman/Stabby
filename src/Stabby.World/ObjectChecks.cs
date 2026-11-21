using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const int ObjectCheckChunks = 4;

        static void CheckObjects(GeneratedWorld w, TerrainDetail detail, FeatureEdit[] edits, List<string> e)
        {
            var field = new ObjectField(w, detail, edits, w.Objects);
            int n = w.Config.Size, across = field.ChunksAcross;
            float cm = w.Config.CellMeters, span = WorldChunks.Size * cm;
            var bed = RiverBedCells(w, out var segs);
            bool first = true;
            var seen = new HashSet<int>();
            void Fail(int kind, string message) { if (seen.Add(kind)) e.Add(message); }
            foreach (int c in ObjectChunks(w, edits, across, span))
            {
                int cx = c % across, cz = c / across;
                var a = field.Chunk(cx, cz);
                if (first && !SameObjects(a, new ObjectField(w, detail, edits, w.Objects).Place(cx, cz))) { e.Add($"objects in chunk {cx},{cz} differ between runs"); return; }
                first = false;
                foreach (var o in a)
                {
                    var def = w.Objects.Objects[o.Def];
                    float r = field.Footprint(o.Def) * o.Size;
                    string at = $"{def.Id} at {o.X:F0},{o.Z:F0}";
                    if (o.X < cx * span || o.X >= (cx + 1) * span || o.Z < cz * span || o.Z >= (cz + 1) * span) Fail(0, at + " lies outside its chunk");
                    int gx = Math.Min(n - 1, (int)(o.X / cm + 0.5f)), gz = Math.Min(n - 1, (int)(o.Z / cm + 0.5f)), i = gz * n + gx;
                    float ground = ObjectField.GroundAt(detail, cm, o.X, o.Z);
                    if (w.Sea[i]) Fail(1, at + " stands in the sea");
                    int lake = w.Water.LakeId[i];
                    if (lake >= 0 || (lake = NearLake(w, gx, gz, ground)) >= 0) Fail(2, at + $" stands in lake {lake}");
                    if (OnBed(bed, segs, i, o.X, o.Z, 0.5f * r)) Fail(3, at + " lies on a river bed");
                    foreach (var f in edits)
                        if (o.X + 0.5f * r > f.MinX && o.X - 0.5f * r < f.MaxX && o.Z + 0.5f * r > f.MinZ && o.Z - 0.5f * r < f.MaxZ) { Fail(4, at + " sits inside a feature edit"); break; }
                    if (o.Y > ground + 0.05f || o.Y < ground - o.Size) Fail(5, at + $" is not grounded ({o.Y:F2} vs ground {ground:F2})");
                    var biomes = def.Placement.Biomes;
                    if (biomes.Count > 0 && (w.Biome[i] < 0 || !biomes.Contains(w.BiomeIds[w.Biome[i]]))) Fail(6, at + " is outside its biomes");
                }
            }
        }

        static List<int> ObjectChunks(GeneratedWorld w, FeatureEdit[] edits, int across, float span)
        {
            var picked = new List<int>();
            foreach (var f in edits)
            {
                int c = Math.Min(across - 1, (int)(f.Az / span)) * across + Math.Min(across - 1, (int)(f.Ax / span));
                if (!picked.Contains(c)) picked.Add(c);
                if (picked.Count >= ObjectCheckChunks / 2) break;
            }
            var rivers = new List<int>();
            foreach (var r in w.Water.Rivers)
            {
                if (r.X.Count == 0) continue;
                int k = r.X.Count / 2;
                int c = Math.Min(across - 1, (int)(r.Y[k] * w.Config.CellMeters / span)) * across + Math.Min(across - 1, (int)(r.X[k] * w.Config.CellMeters / span));
                if (!picked.Contains(c)) picked.Add(c);
                if (picked.Count >= ObjectCheckChunks) break;
            }
            return picked;
        }

        static int NearLake(GeneratedWorld w, int gx, int gz, float y)
        {
            int n = w.Config.Size;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                    int l = w.Water.LakeId[az * n + ax];
                    if (l >= 0 && y < w.Water.Lakes[l].Level) return l;
                }
            return -1;
        }

        static List<int>[] RiverBedCells(GeneratedWorld w, out List<float[]> segs)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var cells = new List<int>[n * n];
            segs = new List<float[]>();
            foreach (var r in w.Water.Rivers)
                for (int k = 0; k + 1 < r.X.Count; k++)
                {
                    float ax = r.X[k] * cm, az = r.Y[k] * cm, bx = r.X[k + 1] * cm, bz = r.Y[k + 1] * cm;
                    float hw = 0.5f * Math.Max(r.Width[k], r.Width[k + 1]), pad = hw + 30f;
                    int id = segs.Count;
                    segs.Add(new[] { ax, az, bx, bz, hw });
                    int x0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, bx) - pad) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(ax, bx) + pad) / cm));
                    int z0 = Math.Max(0, (int)Math.Floor((Math.Min(az, bz) - pad) / cm)), z1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(az, bz) + pad) / cm));
                    for (int gz = z0; gz <= z1; gz++)
                        for (int gx = x0; gx <= x1; gx++)
                            (cells[gz * n + gx] ??= new List<int>()).Add(id);
                }
            return cells;
        }

        static bool OnBed(List<int>[] cells, List<float[]> segs, int i, float x, float z, float r)
        {
            if (cells[i] == null) return false;
            foreach (int id in cells[i])
            {
                var s = segs[id];
                float bx = s[2] - s[0], bz = s[3] - s[1], l2 = bx * bx + bz * bz;
                float t = l2 > 0 ? Math.Max(0, Math.Min(1, ((x - s[0]) * bx + (z - s[1]) * bz) / l2)) : 0;
                float ex = x - s[0] - bx * t, ez = z - s[1] - bz * t;
                if (ex * ex + ez * ez < (s[4] + r) * (s[4] + r)) return true;
            }
            return false;
        }

        static bool SameObjects(ObjectInstance[] a, ObjectInstance[] b)
        {
            if (a.Length != b.Length) return false;
            for (int k = 0; k < a.Length; k++)
            {
                var p = a[k]; var q = b[k];
                if (p.Def != q.Def || p.Variant != q.Variant || p.X != q.X || p.Y != q.Y || p.Z != q.Z || p.Size != q.Size || p.Qx != q.Qx || p.Qy != q.Qy || p.Qz != q.Qz || p.Qw != q.Qw) return false;
            }
            return true;
        }
    }
}
