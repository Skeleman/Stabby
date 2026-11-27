using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float TreeU = 1 / 18f, TreeCy = 1.05f, TreeSy = 3.2f, TreeSz = 3.05f, TreeLap = 1f, TreeStep = 0.965f, TreePlateR = 2.5f, TreeWallZ = 1.15f, TreeRoofM = 2.25f, TreeSeatZ = 1.1f;
        static readonly float[] TreeSeats =
        {
            -7.5f / 18, 1.1f / 18, -5f / 18, 1.1f / 18, -2.5f / 18, 1.1f / 18, 0, 1.1f / 18, 2.5f / 18, 1.1f / 18, 5f / 18, 1.1f / 18, 7.5f / 18, 1.1f / 18,
            -7.5f / 18, -1.1f / 18, -5f / 18, -1.1f / 18, -2.5f / 18, -1.1f / 18, 0, -1.1f / 18, 2.5f / 18, -1.1f / 18, 5f / 18, -1.1f / 18, 7.5f / 18, -1.1f / 18,
        };

        public static float TreeLength(ulong key) => Rng.Stream(key, "fallen-tree").Range(16f, 17f);

        public static float TreePlateX(ulong key) => -0.5f * TreeLength(key) - 0.2f;

        static float DyeLight(ulong dye)
        {
            var tr = new Rng(Rng.Mix(dye ^ 0x6A09E667BB67AE85UL));
            tr.Range(-1f, 1f);
            tr.Range(-1f, 1f);
            return tr.NextFloat();
        }

        static float TreeRad(float x, float len, float size) => 0.48f * size * (1 - 0.06f * (x + 0.5f * len) / len);

        static void Sheared(StructureLayout l, int at, float lean, float batter)
        {
            if (at >= l.Pieces.Count) return;
            var p = l.Pieces[at];
            p.L = lean; p.B = batter;
            l.Pieces[at] = p;
        }

        static void FallenTree(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "fallen-tree");
            float len = rng.Range(16f, 17f), half = 0.5f * len, xp = -half - 0.2f;

            Rock(l, rng.NextU64(), f, xp - 0.3f, -0.6f, 0, 1.1f, 5.6f, 5.4f, 1, 0, 2, 2);

            int segs = rng.Range(2, 4);
            float seg = (len + TreeLap * (segs - 1)) / segs, x0 = -half, sy = TreeSy, sz = TreeSz, cy = TreeCy, zc = 0;
            for (int i = 0; i < segs; i++)
            {
                float yaw = rng.Range(-0.03f, 0.03f);
                Round(l, rng.NextU64(), f, x0 + 0.5f * seg, cy - 0.5f * sy, zc, seg, sy, sz, Timber, GX, RoundTube, 0, 2, (float)Math.Sqrt(1 - yaw * yaw), yaw);
                x0 += seg - TreeLap; sy *= TreeStep; sz *= TreeStep; cy += rng.Range(-0.03f, 0.03f); zc += rng.Range(-0.06f, 0.06f);
            }

            int bark = l.Pieces.Count, breaks = rng.Range(2, 5);
            for (int b = 0; b < breaks; b++)
            {
                Arc(rng.Range(-1, 2) / 16f, out float c, out float s);
                float g0 = rng.Range(-half + 2f, half - 3f), gl = rng.Range(0.6f, 1.6f), gx = g0 + 0.5f * gl, r = TreeRad(gx, len, TreeSy), rz = TreeRad(gx, len, TreeSz), gw = rng.Range(0.35f, 0.6f);
                Box(l, rng.NextU64(), f, gx, TreeCy + 0.94f * r * c - 0.08f, 0.94f * rz * s, gl, 0.16f, gw, Timber, GX, 0, 1, 1, 0, 0, 0, 0.1f);
                for (int e = -1; e <= 1; e += 2)
                {
                    float d = rng.Range(0.1f, 0.18f);
                    Round(l, rng.NextU64(), f, gx + rng.Range(-0.1f, 0.1f), TreeCy + 0.97f * r * c - 0.5f * d, 0.97f * rz * s + e * (0.5f * gw + 0.4f * d), gl * rng.Range(0.7f, 1.1f), d, d * 1.3f, Timber, GX, RoundLog, 0, 1);
                }
            }
            Mossy(l, bark);

            float tip = half, tr = TreeRad(tip, len, TreeSy), tz = TreeRad(tip, len, TreeSz);
            int splinters = rng.Range(3, 7);
            for (int i = 0; i < splinters; i++)
            {
                Arc(rng.Range(-5, 6) / 16f, out float c, out float s);
                float sl = rng.Range(0.6f, 1.4f);
                Box(l, rng.NextU64(), f, tip + 0.3f * sl - rng.Range(0.3f, 0.9f), TreeCy + 0.88f * tr * c - 0.08f, 0.88f * tz * s, sl, rng.Range(0.08f, 0.16f), rng.Range(0.1f, 0.22f), Timber, GX, 0, 0, 1, 0, 0, 0, 0.6f);
            }

            int moss = l.Pieces.Count;
            for (int k = -1; k <= 1; k++)
            {
                Arc(k / 16f, out float c, out float s);
                int patches = rng.Range(2, 4);
                for (int i = 0; i < patches; i++)
                {
                    float sx = rng.Range(1.5f, 4f), x = rng.Range(-half + 0.5f * sx, half - 0.5f * sx - 1f), r = TreeRad(x, len, TreeSy);
                    Round(l, rng.NextU64(), f, x, TreeCy + 0.93f * r * c, TreeRad(x, len, TreeSz) * s, sx, rng.Range(0.08f, 0.12f), rng.Range(0.4f, 0.6f), Turf, GX, RoundDome, 0, 0);
                }
            }

            ulong dye = rng.NextU64();
            while (DyeLight(dye) < 0.4f) dye = Rng.Mix(dye + 1);
            int fungus = l.Pieces.Count, clusters = rng.Range(3, 6);
            for (int i = 0; i < clusters; i++)
            {
                float side = rng.NextFloat() < 0.5f ? -1 : 1, x = rng.Range(-half + 1f, half - 2f);
                int k = rng.NextFloat() < 0.5f ? 3 : 4, shelves = rng.Range(2, 5);
                Arc(k / 16f, out float c, out float s);
                float y = TreeCy + TreeRad(x, len, TreeSy) * c - 0.3f, outer = 0.95f * TreeRad(x, len, TreeSz) * s;
                for (int j = 0; j < shelves; j++)
                {
                    float shz = rng.Range(0.25f, 0.35f);
                    Round(l, rng.NextU64(), f, x + rng.Range(-0.15f, 0.15f), y + j * rng.Range(0.12f, 0.2f), side * (outer + 0.3f * shz), rng.Range(0.3f, 0.5f), rng.Range(0.06f, 0.09f), shz, Fungus, GX, RoundDome, 0, 0);
                }
            }
            for (int k = fungus; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                p.Key = dye;
                l.Pieces[k] = p;
            }

            int saplings = rng.Range(2, 5);
            for (int i = 0; i < saplings; i++)
            {
                float x = rng.Range(-half + 1.5f, half - 2f), z = rng.Range(-0.15f, 0.15f), h = rng.Range(0.5f, 1f), dd = rng.Range(0.04f, 0.06f), crest = TreeCy + 0.93f * TreeRad(x, len, TreeSy);
                Round(l, rng.NextU64(), f, x, crest, z, dd, h, dd, Timber, GV, RoundStem, 0, 0);
                float leaf = rng.Range(0.3f, 0.45f);
                Round(l, rng.NextU64(), f, x, crest + 0.6f * h, z, leaf, 0.05f, leaf * rng.Range(0.8f, 1f), Turf, GX, RoundDisc, 0, 0);
                Round(l, rng.NextU64(), f, x, crest + h, z, 0.7f * leaf, 0.05f, 0.7f * leaf, Turf, GX, RoundDisc, 0, 0);
            }
            Drape(l, moss);

            var rim = RoundNoise(ref rng, 16, 0.72f, 1.1f);
            for (int i = 0; i < 16; i++) rim[i] *= TreePlateR;
            int plate = l.Pieces.Count, core = rng.Range(6, 9);
            for (int i = 0; i < core + 16; i++)
            {
                bool edge = i >= core;
                int d = edge ? i - core : rng.Range(0, 16);
                float a = edge ? d + rng.Range(-0.35f, 0.35f) : rng.Range(0f, 16f);
                Arc(a / 16f, out float c, out float s);
                float r = edge ? 0.7f * rim[d] : rng.Range(0f, 0.45f) * rim[d], big = edge ? 0.6f * rim[d] * rng.Range(0.85f, 1.15f) : rng.Range(1.3f, 1.8f);
                float bh = big * rng.Range(0.8f, 1.2f), yc = TreeCy + r * c, y = Math.Max(-0.6f, yc - 0.5f * bh), x = xp - rng.Range(-0.3f, 0.15f) - 0.12f * (yc - TreeCy);
                int yawk = rng.Range(-1, 2);
                Rock(l, rng.NextU64(), f, x, y, r * s, rng.Range(0.8f, 1.4f), bh, big, Cos16(yawk), Sin16(yawk), 0, 1, 0);
                if (y < 0.1f) Touch(l, f, x, 0, r * s, 0.6f);
            }
            int smears = rng.Range(8, 12);
            for (int i = 0; i < smears; i++)
            {
                int d = rng.Range(0, 16);
                float r = rng.Range(0.1f, 0.65f) * rim[d], yc = TreeCy + r * Cos16(d), big = rng.Range(0.9f, 1.5f);
                int yawk = rng.Range(-1, 2);
                Box(l, rng.NextU64(), f, xp - 0.66f - 0.12f * (yc - TreeCy), Math.Max(-0.4f, yc - 0.4f * big), r * Sin16(d), 0.12f, 0.8f * big, big, Turf, GX, 0, 1, Cos16(yawk), Sin16(yawk), 0, 0, 0.3f);
            }
            int roots = rng.Range(10, 15);
            for (int i = 0; i < roots; i++)
            {
                float a = i * 16f / roots + rng.Range(-0.35f, 0.35f);
                Arc(a / 16f, out float c, out float s);
                float edge = rim[((int)(a + 16.5f)) & 15], rl = rng.Range(0.3f, 1.2f) + 0.3f * edge, d = rng.Range(0.15f, 0.3f), x = xp - rng.Range(0.1f, 0.5f), z = 0.7f * edge * s, y = TreeCy + 0.7f * edge * c, lean = -rng.Range(0f, 0.15f);
                int hi = d > 0.22f ? 1 : 0;
                if (c >= 0.5f)
                {
                    float h = rl * c;
                    Round(l, rng.NextU64(), f, x, y, z + 0.5f * s * rl, d, h, d, Timber, GV, RoundStem, 0, hi);
                    Sheared(l, l.Pieces.Count - 1, lean, s / c);
                }
                else if (c <= -0.5f)
                {
                    float h = Math.Min(rl * -c, y + 0.6f);
                    if (h < 0.2f) continue;
                    float run = h / -c;
                    Round(l, rng.NextU64(), f, x, y - h, z + 0.5f * s * run, d, h, d, Timber, GV, RoundStem, 0, hi);
                    Sheared(l, l.Pieces.Count - 1, lean, s / c);
                    if (y - h < 0.05f) Touch(l, f, x, 0, z + s * run, 0.4f);
                }
                else
                {
                    float side = s < 0 ? -1 : 1, run = 0.3f * edge + rl;
                    Round(l, rng.NextU64(), f, x, Math.Max(0, y - 0.5f * d), z + side * 0.5f * run, run, d, d, Timber, GX, RoundLog, 0, hi, 0, 1);
                }
            }
            int trail = rng.Range(4, 8);
            for (int i = 0; i < trail; i++)
            {
                float z = rng.Range(-1.6f, 1.6f), top = rng.Range(0.4f, 1.6f), drop = top + 0.4f, run = rng.Range(0.3f, 0.8f), d = rng.Range(0.05f, 0.1f);
                Round(l, rng.NextU64(), f, xp - 0.45f - 0.5f * run, -0.4f, z, d, drop, d, Timber, GV, RoundStem, 0, 0);
                Sheared(l, l.Pieces.Count - 1, run / drop, rng.Range(-0.2f, 0.2f));
                Touch(l, f, xp - 0.45f - run, 0, z, 0.3f);
            }
            for (int i = 0; i < 3; i++)
            {
                Arc((i - 1) / 16f, out float c, out float s);
                float r = 0.8f * rim[(i + 15) & 15];
                Round(l, rng.NextU64(), f, xp + 0.4f, TreeCy + r * c + 0.15f, r * s, rng.Range(0.6f, 0.9f), 0.1f, rng.Range(0.6f, 1f), Turf, GX, RoundDome, 0, 0);
            }
            Flat(l, plate);
            Mossy(l, plate);

            for (float x = -half + 0.4f; x <= half - 0.6f; x += 0.9f)
            {
                Col(l, f, x, TreeWallZ, 0.32f, 0, 2.8f);
                Col(l, f, x, -TreeWallZ, 0.32f, 0, 2.8f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Col(l, f, xp, side * 1.15f, 0.35f, 0, 3f);
                Col(l, f, xp, side * 1.75f, 0.35f, 0, 3f);
                Col(l, f, xp, side * 2.35f, 0.35f, 0, 3f);
            }
            Col(l, f, xp, 0, 0.6f, 2.3f, 3.4f);
            for (float x = -half; x <= half; x += 1.5f)
            {
                Touch(l, f, x, 0, 1.0f, 0.5f);
                Touch(l, f, x, 0, -1.0f, 0.5f);
            }
            Touch(l, f, xp, 0, 1.4f, 0.6f);
            Touch(l, f, xp, 0, -1.4f, 0.6f);
        }
    }

    public sealed partial class ObjectField
    {
        public const float TreeWoods = 0.45f, TreeReliefM = 6f, TreeFloatM = 0.2f, TreeHeadM = 1.8f, TreePitBack = 2f, TreePitR = 2.2f, TreePitRise = 0.8f, TreePitDepth = 0.6f, TreePitRound = 0.4f, TreePitBank = 1.2f, TreePitLumpM = 0.12f;

        public void TreeEditsOf(ObjectInstance o, List<FeatureEdit> list)
        {
            float u = o.Size * Structures.TreeU, px = Structures.TreePlateX(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant)) - TreePitBack;
            Rotate(o, px * u, 0, 0, out float x, out _, out float z);
            x += o.X; z += o.Z;
            float g = GroundAt(detail, cm, x, z);
            float seed = (float)((Rng.Mix(((ulong)(uint)(int)(x * 64f) << 32 | (uint)(int)(z * 64f)) ^ 0x3C6EF372FE94F82BUL) >> 40) / 16777216.0);
            list.Add(Disc(FeatureEdit.Carve, x, z, g + TreePitRise * u, g - TreePitDepth * u, TreePitR * u, TreePitRound, -TreePitBank, -0.15f, TreePitLumpM, seed, 0, 0).Finer());
        }

        public FeatureEdit[] TreeEdits()
        {
            var list = new List<FeatureEdit>();
            foreach (var o in Discoveries())
                if (lib.Objects[o.Def].Placement.Discovery == FallenTree) TreeEditsOf(o, list);
            return list.ToArray();
        }

        public string TreeFault(ObjectInstance o)
        {
            float u = o.Size * Structures.TreeU;
            for (float x = -8f; x <= 8f; x += 1f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Rotate(o, x * u, 0, side * Structures.TreeSeatZ * u, out float wx, out _, out float wz);
                    float gap = o.Y - GroundAt(detail, cm, o.X + wx, o.Z + wz);
                    if (gap > TreeFloatM) return $"trunk underside floats {gap:F2} m";
                }
                Rotate(o, x * u, 0, 0, out float ax, out _, out float az);
                if (GroundAt(detail, cm, o.X + ax, o.Z + az) - o.Y > (Structures.TreeRoofM - TreeHeadM) * u) return "hollow is blocked";
            }
            var pits = new List<FeatureEdit>();
            TreeEditsOf(o, pits);
            float span = WorldChunks.Size * cm;
            foreach (var b in pits)
            {
                float x = 0.5f * (b.MinX + b.MaxX), z = 0.5f * (b.MinZ + b.MaxZ), r = 0.5f * Math.Max(b.MaxX - b.MinX, b.MaxZ - b.MinZ);
                if (b.MinX < 0 || b.MinZ < 0) return "root pit leaves its object chunk";
                float cx0 = (float)Math.Floor(o.X / span) * span, cz0 = (float)Math.Floor(o.Z / span) * span;
                if (b.MinX < cx0 || b.MaxX >= Math.Min(cx0 + span, worldM) || b.MinZ < cz0 || b.MaxZ >= Math.Min(cz0 + span, worldM)) return "root pit leaves its object chunk";
                int gx = Math.Min(n - 1, (int)(x / cm + 0.5f)), gz = Math.Min(n - 1, (int)(z / cm + 0.5f));
                if (w.Sea[gz * n + gx]) return "root pit lies in water";
                float ground = GroundAt(detail, cm, x, z);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int qx = gx + dx, qz = gz + dz;
                        if (qx < 0 || qz < 0 || qx >= n || qz >= n) continue;
                        int lake = w.Water.LakeId[qz * n + qx];
                        if (lake >= 0 && ((dx == 0 && dz == 0) || ground < w.Water.Lakes[lake].Level)) return "root pit lies in water";
                    }
                if (RiverGap(x, z) <= r + 1) return "root pit lies on a river bed";
                foreach (var f in edits)
                    if (f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring && b.MaxX >= f.MinX && b.MinX <= f.MaxX && b.MaxZ >= f.MinZ && b.MinZ <= f.MaxZ) return "root pit overlaps a terrain feature";
            }
            float sr = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + sr >= f.MinX && o.X - sr <= f.MaxX && o.Z + sr >= f.MinZ && o.Z - sr <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
