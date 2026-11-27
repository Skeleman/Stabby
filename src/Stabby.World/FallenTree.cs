using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float TreeU = 1 / 18f, TreeCy = 1.05f, TreeSy = 3.2f, TreeSz = 3.05f, TreeTaper = 0.3f, TreeSag = 0.05f, TreeDrop = 0.8f, TreeWallZ = 1.15f, TreeRoofM = 2.25f, TreeSeatZ = 1.1f;
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

        static float TreeRad(float x, float len, float size) => 0.48f * size * (1 - TreeTaper * (x + 0.5f * len) / len);

        static float TreeMid(float x, float len)
        {
            float t = 2 * x / len;
            return TreeCy - 0.5f * TreeSy * (TreeSag * (1 - t * t) + TreeDrop * TreeTaper * 0.5f * (t + 1));
        }

        static void Clod(StructureLayout l, ulong key, in Frame f, float x, float y, float z, float sx, float sy, float sz, float c, float s, int lo, int hi)
        {
            int at = l.Pieces.Count;
            Rock(l, key, f, x, y, z, sx, sy, sz, c, s, lo, hi);
            if (at >= l.Pieces.Count) return;
            var p = l.Pieces[at];
            p.M = Soil;
            l.Pieces[at] = p;
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
            float len = rng.Range(16f, 17f), half = 0.5f * len;

            Round(l, rng.NextU64(), f, 0, TreeCy - 0.5f * TreeSy, 0, len, TreeSy, TreeSz, Timber, GX, RoundTube, 0, 2);

            int bark = l.Pieces.Count, breaks = rng.Range(2, 5);
            for (int b = 0; b < breaks; b++)
            {
                Arc(rng.Range(-1, 2) / 16f, out float c, out float s);
                float g0 = rng.Range(-half + 2f, half - 3f), gl = rng.Range(0.6f, 1.6f), gx = g0 + 0.5f * gl, r = TreeRad(gx, len, TreeSy), rz = TreeRad(gx, len, TreeSz), gw = rng.Range(0.35f, 0.6f);
                Box(l, rng.NextU64(), f, gx, TreeMid(gx, len) + 0.94f * r * c - 0.08f, 0.94f * rz * s, gl, 0.16f, gw, Timber, GX, 0, 1, 1, 0, 0, 0, 0.1f);
                for (int e = -1; e <= 1; e += 2)
                {
                    float d = rng.Range(0.1f, 0.18f);
                    Round(l, rng.NextU64(), f, gx + rng.Range(-0.1f, 0.1f), TreeMid(gx, len) + 0.97f * r * c - 0.5f * d, 0.97f * rz * s + e * (0.5f * gw + 0.4f * d), gl * rng.Range(0.7f, 1.1f), d, d * 1.3f, Timber, GX, RoundLog, 0, 1);
                }
            }
            Mossy(l, bark);

            float tip = half - 0.1f, tr = TreeRad(tip, len, TreeSy), tz = TreeRad(tip, len, TreeSz), ty = TreeMid(tip, len);
            int splinters = rng.Range(3, 7);
            for (int i = 0; i < splinters; i++)
            {
                Arc(rng.Range(-5, 6) / 16f, out float c, out float s);
                float sl = rng.Range(0.6f, 1.4f);
                Box(l, rng.NextU64(), f, tip + 0.3f * sl - rng.Range(0.3f, 0.9f), ty + 0.88f * tr * c - 0.08f, 0.88f * tz * s, sl, rng.Range(0.08f, 0.16f), rng.Range(0.1f, 0.22f), Timber, GX, 0, 0, 1, 0, 0, 0, 0.6f);
            }

            int stubs = rng.Range(3, 6);
            for (int i = 0; i < stubs; i++)
            {
                int k = (rng.NextFloat() < 0.5f ? -1 : 1) * rng.Range(2, 4);
                Arc((k + rng.Range(-0.3f, 0.3f)) / 16f, out float c, out float s);
                float x = rng.Range(half - 3.2f, half - 0.6f), r = TreeRad(x, len, TreeSy), rz = TreeRad(x, len, TreeSz), sl = rng.Range(0.5f, 1.1f), d = rng.Range(0.28f, 0.42f), y0 = TreeMid(x, len) + 0.85f * r * c, z0 = 0.85f * rz * s;
                Round(l, rng.NextU64(), f, x, y0, z0 + 0.5f * sl * s, d, sl * c, d * rng.Range(0.85f, 1.1f), Timber, GV, RoundStem, 0, 1);
                Sheared(l, l.Pieces.Count - 1, rng.Range(0.3f, 0.6f), s / c);
            }

            int moss = l.Pieces.Count;
            for (int k = -1; k <= 1; k++)
            {
                Arc(k / 16f, out float c, out float s);
                int patches = rng.Range(2, 4);
                for (int i = 0; i < patches; i++)
                {
                    float sx = rng.Range(1.5f, 4f), x = rng.Range(-half + 0.5f * sx, half - 0.5f * sx - 1f), r = TreeRad(x, len, TreeSy);
                    Round(l, rng.NextU64(), f, x, TreeMid(x, len) + 0.93f * r * c, TreeRad(x, len, TreeSz) * s, sx, rng.Range(0.08f, 0.12f), rng.Range(0.4f, 0.6f), Turf, GX, RoundDome, 0, 0);
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
                float y = TreeMid(x, len) + TreeRad(x, len, TreeSy) * c - 0.3f, outer = 0.95f * TreeRad(x, len, TreeSz) * s;
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

            Drape(l, moss);

            float bx = -half + 0.1f, br = TreeRad(bx, len, TreeSy), bz = TreeRad(bx, len, TreeSz), by = TreeMid(bx, len);
            int shards = rng.Range(5, 9);
            for (int i = 0; i < shards; i++)
            {
                Arc(rng.Range(-6, 7) / 16f, out float c, out float s);
                float sl = rng.Range(0.5f, 1.3f), rr = rng.Range(0.45f, 0.9f);
                Box(l, rng.NextU64(), f, bx - 0.3f * sl + rng.Range(0.25f, 0.8f), by + rr * br * c - 0.08f, rr * bz * s, sl, rng.Range(0.1f, 0.2f), rng.Range(0.12f, 0.3f), Timber, GX, 0, 0, 1, 0, 0, 0, 0.6f);
            }

            float top = 0.55f * TreeSz, sr = top / 0.88f, hb = rng.Range(0.4f, 0.7f), ux = Math.Max(-half - 0.15f, -9.9f + 1.12f * sr), uz = -(bz + 0.8f + 1.05f * sr);
            int stump = l.Pieces.Count;
            Round(l, rng.NextU64(), f, ux, -0.3f, uz, 2 * sr, hb + 0.3f, 2 * sr * rng.Range(0.94f, 1f), Timber, GV, RoundStem, 0, 2);
            Mossy(l, stump);
            int jag = rng.Range(20, 27);
            float turn = rng.Range(0f, 16f), tall = rng.Range(0.9f, 1.3f);
            Arc(rng.Range(0f, 1f), out float hc, out float hs);
            for (int i = 0; i < jag; i++)
            {
                Arc((turn + (i + rng.Range(-0.3f, 0.3f)) * 16f / jag) / 16f, out float c, out float s);
                float w = 0.5f * (1 + c * hc + s * hs), h = 0.2f + (tall - 0.2f) * w * w * w * rng.Range(0.6f, 1.1f) + rng.Range(0f, 0.15f);
                float wb = h * rng.Range(0.17f, 0.24f), tx = rng.Range(0.1f, 0.16f), rr = top - 0.5f * tx, lean = rng.Range(0f, 0.1f), sy = h + 0.15f;
                int at = l.Pieces.Count;
                Round(l, rng.NextU64(), f, ux + rr * c, hb - 0.15f, uz + rr * s, tx, sy, 0.5f * (wb + 0.03f), Timber, GV, RoundStem, 0, 1, c, s);
                if (at < l.Pieces.Count)
                {
                    var p = l.Pieces[at];
                    p.T = (wb - 0.03f) / (2 * sy); p.L = lean; p.Mossy = true;
                    l.Pieces[at] = p;
                }
                float iy = 0.8f * h + 0.1f, iw = 0.8f * wb, ir = rr - 0.5f * tx;
                Box(l, rng.NextU64(), f, ux + ir * c, hb - 0.1f, uz + ir * s, 0.06f, iy, 0.5f * (iw + 0.02f), Timber, GV, 0, 1, c, s, lean, 0, (iw - 0.02f) / (2 * iy));
            }
            int core = rng.Range(7, 11);
            for (int i = 0; i < core; i++)
            {
                Arc(rng.Range(0f, 1f), out float c, out float s);
                float rr = top * rng.Range(0f, 0.75f), ch = rng.Range(0.08f, 0.3f) * (0.6f + 0.6f * rr / top) + 0.05f, cw = rng.Range(0.1f, 0.22f);
                Box(l, rng.NextU64(), f, ux + rr * c, hb - 0.05f, uz + rr * s, rng.Range(0.12f, 0.3f), ch, cw, Timber, GV, 0, 1, c, s, 0, 0, 0.85f * cw / ch);
            }
            int lumps = rng.Range(3, 6);
            for (int i = 0; i < lumps; i++)
            {
                Arc(rng.Range(0f, 1f), out float c, out float s);
                float rr = 1.05f * sr + rng.Range(0f, 0.25f), lx = ux + rr * c, w = rng.Range(0.35f, 0.7f);
                if (lx - 0.5f * w < -9.95f) continue;
                Clod(l, rng.NextU64(), f, lx, -0.3f, uz + rr * s, w, rng.Range(0.4f, 0.6f), w * rng.Range(0.8f, 1.2f), 1, 0, 0, 1);
                Touch(l, f, lx, 0, uz + rr * s, 0.4f);
            }
            int chips = rng.Range(5, 9);
            for (int i = 0; i < chips; i++)
            {
                Arc(rng.Range(0f, 1f), out float c, out float s);
                float d = 1.15f * sr + rng.Range(0.1f, 1.2f), cx = ux + d * c, cz = uz + d * s;
                if (cx < -9.6f || (cx > -half - 0.3f && cz > -bz - 0.2f)) continue;
                Arc(rng.Range(0f, 1f), out float oc, out float os);
                Box(l, rng.NextU64(), f, cx, -0.05f, cz, rng.Range(0.25f, 0.6f), rng.Range(0.05f, 0.09f), rng.Range(0.07f, 0.15f), Timber, GX, 0, 0, oc, os, 0, 0, 0.5f);
                Touch(l, f, cx, 0, cz, 0.3f);
            }

            for (float x = -half + 0.4f; x <= half - 0.6f; x += 0.9f)
            {
                Col(l, f, x, TreeWallZ, 0.32f, 0, 2.8f);
                Col(l, f, x, -TreeWallZ, 0.32f, 0, 2.8f);
            }
            Col(l, f, ux, uz, top, 0, hb + 0.3f);
            for (int i = 0; i < 10; i++)
            {
                Arc(i / 10f, out float c, out float s);
                Touch(l, f, ux + 1.05f * sr * c, 0, uz + 1.05f * sr * s, 0.5f);
            }
            for (float x = -half; x <= half; x += 1f)
            {
                float m = TreeMid(x, len), rz = TreeRad(x, len, TreeSz), z = rz > m + 0.2f ? (float)Math.Sqrt(rz * rz - m * m) : 0.6f;
                Touch(l, f, x, 0, z, 0.45f);
                Touch(l, f, x, 0, -z, 0.45f);
            }
        }
    }

    public sealed partial class ObjectField
    {
        public const float TreeWoods = 0.45f, TreeReliefM = 6f, TreeFloatM = 0.2f, TreeHeadM = 1.8f, TreePitBack = 2.2f, TreePitR = 3f, TreePitRise = 0.8f, TreePitDepth = 0.45f, TreePitRound = 0.4f, TreePitBank = 1.2f, TreePitLumpM = 0.12f;

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
