using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float TreeU = 1 / 18f, TreeCy = 1.05f, TreeA = 1.2f, TreeB = 1.5f, TreeStave = 0.8f, TreeWallZ = 1.15f, TreeRoofM = 2.25f, TreeSeatZ = 1.1f;
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
            int before = l.Pieces.Count;

            Round(l, rng.NextU64(), f, 0, -0.45f, 0, len, 3.2f, 3f, Timber, GX, RoundLog, 2, 2);
            Rock(l, rng.NextU64(), f, xp - 0.3f, -0.6f, 0, 1.1f, 5.6f, 5.4f, 1, 0, 2, 2);

            int breaks = rng.Range(2, 5);
            int[] broken = new int[breaks];
            for (int b = 0; b < breaks; b++) broken[b] = (rng.NextFloat() < 0.5f ? -1 : 1) * rng.Range(3, 7);
            for (int k = -7; k <= 7; k++)
            {
                Arc(k / 22f, out float c, out float s);
                float y = TreeCy + TreeB * c - 0.5f * TreeStave, z = TreeA * s, end = half + rng.Range(-0.5f, 0.25f);
                bool split = Array.IndexOf(broken, k) >= 0;
                if (!split)
                {
                    Round(l, rng.NextU64(), f, 0.5f * (end - half), y, z, end + half, TreeStave, TreeStave, Timber, GX, RoundLog, 0, 1);
                    continue;
                }
                float g0 = rng.Range(-half + 2f, half - 4f), g1 = g0 + rng.Range(0.6f, 1.4f);
                Round(l, rng.NextU64(), f, 0.5f * (g0 - half), y, z, g0 + half, TreeStave, TreeStave, Timber, GX, RoundLog, 0, 1);
                Round(l, rng.NextU64(), f, 0.5f * (g1 + end), y, z, end - g1, TreeStave, TreeStave, Timber, GX, RoundLog, 0, 1);
                Round(l, rng.NextU64(), f, 0.5f * (g0 + g1), y + 0.04f, z * 0.94f, g1 - g0 + 0.2f, TreeStave - 0.12f, TreeStave - 0.12f, Timber, GX, RoundLog, 0, 0);
                Box(l, rng.NextU64(), f, 0.5f * (g0 + g1), y + 0.1f, z * 0.93f, g1 - g0, 0.4f, 0.4f, Timber, GX, 0, 0, 1, 0, 0, 0, 0.1f);
            }
            for (int side = -1; side <= 1; side += 2)
                Round(l, rng.NextU64(), f, 0, 0, side * 1.0f, len, 0.6f, 0.62f, Timber, GX, RoundLog, 0, 1);
            Mossy(l, before);

            int splinters = rng.Range(3, 7);
            for (int i = 0; i < splinters; i++)
            {
                Arc(rng.Range(-5, 6) / 16f, out float c, out float s);
                float sl = rng.Range(0.6f, 1.4f);
                Box(l, rng.NextU64(), f, half + 0.3f * sl - rng.Range(0f, 0.6f), TreeCy + TreeB * c - 0.1f, TreeA * s, sl, rng.Range(0.08f, 0.16f), rng.Range(0.1f, 0.22f), Timber, GX, 0, 0, 1, 0, 0, 0, 0.6f);
            }

            int moss = l.Pieces.Count;
            for (int k = -1; k <= 1; k++)
            {
                Arc(k / 16f, out float c, out float s);
                float y = TreeCy + TreeB * c + 0.5f * TreeStave - 0.05f, z = TreeA * s;
                int patches = rng.Range(2, 4);
                for (int i = 0; i < patches; i++)
                {
                    float sx = rng.Range(1.5f, 4f);
                    Round(l, rng.NextU64(), f, rng.Range(-half + 0.5f * sx, half - 0.5f * sx - 1f), y, z, sx, rng.Range(0.08f, 0.12f), rng.Range(0.4f, 0.6f), Turf, GX, RoundDome, 0, 0);
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
                float y = TreeCy + TreeB * c - 0.3f, outer = TreeA * s + 0.5f * TreeStave;
                for (int j = 0; j < shelves; j++)
                {
                    float sz = rng.Range(0.25f, 0.35f);
                    Round(l, rng.NextU64(), f, x + rng.Range(-0.15f, 0.15f), y + j * rng.Range(0.12f, 0.2f), side * (outer + 0.3f * sz), rng.Range(0.3f, 0.5f), rng.Range(0.06f, 0.09f), sz, Fungus, GX, RoundDome, 0, 0);
                }
            }
            for (int k = fungus; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                p.Key = dye;
                l.Pieces[k] = p;
            }

            int saplings = rng.Range(2, 5);
            float crest = TreeCy + TreeB + 0.5f * TreeStave - 0.08f;
            for (int i = 0; i < saplings; i++)
            {
                float x = rng.Range(-half + 1.5f, half - 2f), z = rng.Range(-0.15f, 0.15f), h = rng.Range(0.5f, 1f), dd = rng.Range(0.04f, 0.06f);
                Round(l, rng.NextU64(), f, x, crest, z, dd, h, dd, Timber, GV, RoundStem, 0, 0);
                float leaf = rng.Range(0.3f, 0.45f);
                Round(l, rng.NextU64(), f, x, crest + 0.6f * h, z, leaf, 0.05f, leaf * rng.Range(0.8f, 1f), Turf, GX, RoundDisc, 0, 0);
                Round(l, rng.NextU64(), f, x, crest + h, z, 0.7f * leaf, 0.05f, 0.7f * leaf, Turf, GX, RoundDisc, 0, 0);
            }
            Drape(l, moss);

            int plate = l.Pieces.Count, clods = rng.Range(9, 13);
            for (int i = 0; i < clods; i++)
            {
                Arc((i + rng.Range(-0.3f, 0.3f)) / clods, out float c, out float s);
                float r = rng.Range(1.9f, 2.9f), sy = rng.Range(0.7f, 1.4f), y = Math.Max(-0.6f, TreeCy + r * c - 0.5f * sy), z = r * s;
                int d = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, xp - rng.Range(0f, 0.3f), y, z, rng.Range(0.6f, 1f), sy, rng.Range(0.7f, 1.2f), Cos16(d), Sin16(d), i % 3 == 0 ? 0 : 1, 1, 0);
                if (y < 0.1f) Touch(l, f, xp, 0, z, 0.6f);
            }
            int roots = rng.Range(10, 15);
            for (int i = 0; i < roots; i++)
            {
                Arc((i + rng.Range(-0.35f, 0.35f)) / roots, out float c, out float s);
                float rl = rng.Range(1f, 2f), d = rng.Range(0.12f, 0.25f), x = xp - rng.Range(0.1f, 0.4f), z = 1.45f * s, y = TreeCy + 1.45f * c, lean = -rng.Range(0f, 0.15f);
                int hi = d > 0.19f ? 1 : 0;
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
                    float side = s < 0 ? -1 : 1;
                    Round(l, rng.NextU64(), f, x, Math.Max(0, y - 0.5f * d), z + side * 0.5f * rl, rl, d, d, Timber, GX, RoundLog, 0, hi, 0, 1);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                Arc((i - 1) / 16f, out float c, out float s);
                float r = rng.Range(2f, 2.4f);
                Round(l, rng.NextU64(), f, xp - 0.2f, TreeCy + r * c + 0.25f, r * s, rng.Range(0.6f, 0.9f), 0.1f, rng.Range(0.6f, 1f), Turf, GX, RoundDome, 0, 0);
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
