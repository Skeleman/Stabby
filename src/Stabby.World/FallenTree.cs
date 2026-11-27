using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float TreeU = 1 / 18f, TreeCy = 1.05f, TreeSy = 3.2f, TreeSz = 3.05f, TreePlateR = 3.9f, TreeBulge = 0.45f, TreePlateY = 1.5f, TreeTaper = 0.3f, TreeTilt = 0.08f, TreePlateSq = 0.95f, TreeSag = 0.05f, TreeDrop = 0.8f, TreeWallZ = 1.15f, TreeRoofM = 2.25f, TreeSeatZ = 1.1f;
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

        static void Twig(StructureLayout l, ulong key, in Frame f, float x0, float y0, float z0, float x1, float y1, float z1, float d, int hi)
        {
            float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            if (Math.Abs(dy) < 0.6f * Math.Abs(dz) || Math.Abs(dy) < 0.05f)
            {
                float len = Math.Max(0.05f, (float)Math.Sqrt(dx * dx + dz * dz));
                Round(l, key, f, 0.5f * (x0 + x1), Math.Max(-0.3f, 0.5f * (y0 + y1) - 0.5f * d), 0.5f * (z0 + z1), len + 0.5f * d, d, d, Timber, GX, RoundLog, 0, hi, dx / len, dz / len);
                return;
            }
            float lo = Math.Min(y0, y1) - 0.15f * d;
            Round(l, key, f, 0.5f * (x0 + x1), lo, 0.5f * (z0 + z1), d, Math.Abs(dy) + 0.3f * d, d, Timber, GV, RoundStem, 0, hi);
            Sheared(l, l.Pieces.Count - 1, dx / dy, dz / dy);
            if (lo < 0.05f) Touch(l, f, x0, 0, y0 < y1 ? z0 : z1, 0.4f);
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

            Clod(l, rng.NextU64(), f, xp - 0.05f, -0.8f, 0, 0.6f, TreePlateY + 0.6f * TreePlateR + 0.8f, 1.3f * TreePlateR, 1, 0, 2, 2);
            Sheared(l, l.Pieces.Count - 1, -TreeTilt, 0);
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

            var rim = RoundNoise(ref rng, 16, 0.78f, 1.14f);
            for (int i = 0; i < 16; i++) rim[i] *= TreePlateR;
            int plate = l.Pieces.Count;
            for (int ring = 0; ring < 6; ring++)
            {
                int count = 6 + 6 * ring;
                float rf = 0.18f + 0.16f * ring;
                for (int i = 0; i < count; i++)
                {
                    float a = (i + 0.5f * ring + rng.Range(-0.08f, 0.08f)) * 16f / count;
                    Arc(a / 16f, out float c, out float s);
                    float edge = rim[((int)(a + 16.5f)) & 15], r = rf * edge + rng.Range(-0.06f, 0.06f) + (ring == 5 ? rng.Range(-0.3f, 0.25f) : 0);
                    if (ring == 5 && c > -0.3f && rng.NextFloat() < 0.22f) continue;
                    float w = 1.7f * Math.Max(6.2832f * rf / count, 0.16f) * edge * rng.Range(1f, 1.15f) * (ring == 5 ? rng.Range(0.6f, 0.85f) : 1);
                    float yc = TreePlateY + TreePlateSq * r * c, top = yc + 0.5f * w * rng.Range(0.95f, 1.05f);
                    if (top < 0.1f) continue;
                    float q = Math.Min(1f, r / TreePlateR), mud = 1.1f - 0.25f * c;
                    float sx = (0.3f - 0.17f * q * q) * mud * rng.Range(0.9f, 1.1f);
                    float y = Math.Max(-0.9f, yc - 0.5f * w), x = xp + 0.05f - TreeBulge * (1 - q * q) - 0.06f * (1 - c) * (1 - q) + rng.Range(-0.02f, 0.02f);
                    Clod(l, rng.NextU64(), f, x - TreeTilt * y, y, r * s, sx, top - y, w * rng.Range(0.95f, 1.1f), 1, 0, 0, ring < 3 ? 1 : 0);
                    Sheared(l, l.Pieces.Count - 1, -TreeTilt, 0);
                    if (y < 0.1f) Touch(l, f, x, 0, r * s, 0.6f);
                }
            }
            int crumbs = rng.Range(10, 17);
            for (int i = 0; i < crumbs; i++)
            {
                float a = rng.Range(0f, 16f);
                Arc(a / 16f, out float c, out float s);
                float r = rim[((int)(a + 16.5f)) & 15] * rng.Range(0.98f, 1.1f), sz = rng.Range(0.18f, 0.4f), y = TreePlateY + TreePlateSq * r * c - 0.5f * sz;
                if (y < 0.05f) continue;
                Clod(l, rng.NextU64(), f, xp + 0.05f - TreeTilt * y, y, r * s, 0.5f * sz, sz, sz * rng.Range(0.8f, 1.3f), 1, 0, 0, 0);
                Sheared(l, l.Pieces.Count - 1, -TreeTilt, 0);
            }
            int ends = rng.Range(10, 17);
            for (int i = 0; i < ends; i++)
            {
                float a = rng.Range(0f, 16f), q = rng.Range(0.12f, 0.85f);
                Arc(a / 16f, out float c, out float s);
                float r = q * rim[((int)(a + 16.5f)) & 15], d = (0.3f - 0.22f * q) * rng.Range(0.8f, 1.2f), y = TreePlateY + TreePlateSq * r * c, fx = xp - 0.05f - TreeBulge * (1 - q * q) - 0.13f * (1 - q * q);
                if (y < 0.1f + 0.5f * d) continue;
                Round(l, rng.NextU64(), f, fx - TreeTilt * y, y - 0.5f * d, r * s, rng.Range(0.18f, 0.28f), d, d * rng.Range(0.9f, 1.15f), Timber, GX, RoundLog, 0, 0);
            }
            int roots = rng.Range(15, 26);
            for (int i = 0; i < roots; i++)
            {
                float a = rng.NextFloat() < 0.85f ? rng.Range(2.4f, 13.6f) : rng.Range(-2.4f, 2.4f), edge = rim[((int)(a + 16.5f)) & 15];
                float q0 = rng.NextFloat() < 0.7f ? rng.Range(0.1f, 0.45f) : rng.Range(0.45f, 0.9f), r = q0 * edge, d = (0.46f - 0.3f * q0) * rng.Range(0.85f, 1.15f);
                float run = (1.08f - q0) * edge + rng.Range(1.2f, 2.4f), step = run / 10f, wrap = ((a % 16f) + 16f) % 16f, dir = wrap < 8f ? 1 : -1, bend = rng.Range(3.2f, 5.2f) / 5f, da = a + rng.Range(-0.4f, 0.4f);
                Arc(a / 16f, out float c, out float s);
                float y = TreePlateY + TreePlateSq * r * c, z = r * s, qr = Math.Min(1f, r / edge), x = xp - 0.05f - TreeBulge * (1 - qr * qr) - 0.45f * d, dp = d, drift = 0;
                for (int k = 1; k <= 10; k++)
                {
                    float t = k / 10f;
                    if (r > 0.8f * edge)
                    {
                        float off = ((8f - ((da % 16f) + 16f) % 16f) * dir);
                        if (off > 0) da += dir * Math.Min(bend, off);
                        drift += 0.025f;
                    }
                    Arc(da / 16f, out float ck, out float sk);
                    float yk = y + step * TreePlateSq * ck, zk = z + step * sk;
                    r = (float)Math.Sqrt((yk - TreePlateY) * (yk - TreePlateY) / (TreePlateSq * TreePlateSq) + zk * zk);
                    qr = Math.Min(1f, r / edge);
                    float dk = 0.012f + d * (1 - t) * (1 - 0.4f * t), xk = xp - 0.05f - TreeBulge * (1 - qr * qr) - 0.45f * dk - drift;
                    if (yk > -0.25f || y > -0.25f) Twig(l, rng.NextU64(), f, x - TreeTilt * y, y, z, xk - TreeTilt * yk, yk, zk, 0.5f * (dp + dk), k < 4 && d > 0.25f ? 1 : 0);
                    if (k >= 3 && k <= 8 && r > 0.9f * edge && yk > 0.4f && dk > 0.05f && rng.NextFloat() < 0.4f)
                    {
                        float hang = rng.Range(0.35f, 0.8f), lz = zk + rng.Range(-0.15f, 0.15f), ld = Math.Min(0.6f * dk, rng.Range(0.035f, 0.055f));
                        Twig(l, rng.NextU64(), f, xk - TreeTilt * yk, yk, zk, xk - 0.04f - TreeTilt * (yk - 0.5f * hang), yk - 0.5f * hang, lz, ld, 0);
                        Twig(l, rng.NextU64(), f, xk - 0.04f - TreeTilt * (yk - 0.5f * hang), yk - 0.5f * hang, lz, xk - 0.06f - TreeTilt * (yk - hang), Math.Max(-0.2f, yk - hang), lz + rng.Range(-0.1f, 0.1f), 0.4f * ld, 0);
                    }
                    x = xk; y = yk; z = zk; dp = dk;
                    if (y < -0.25f) break;
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
                float r = 0.93f * rim[(i + 15) & 15];
                float hy = TreePlateY + TreePlateSq * r * c + 0.15f;
                Round(l, rng.NextU64(), f, xp + 0.3f - TreeTilt * hy, hy, r * s, rng.Range(0.6f, 0.9f), 0.1f, rng.Range(0.6f, 1f), Turf, GX, RoundDome, 0, 0);
            }
            Flat(l, plate);

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
                Col(l, f, xp, side * 2.95f, 0.35f, 0, 3f);
                Col(l, f, xp, side * 3.55f, 0.35f, 0, 2.6f);
            }
            Col(l, f, xp, 0, 0.6f, 2.3f, 3.4f);
            for (float x = -half; x <= half; x += 1f)
            {
                float m = TreeMid(x, len), rz = TreeRad(x, len, TreeSz), z = rz > m + 0.2f ? (float)Math.Sqrt(rz * rz - m * m) : 0.6f;
                Touch(l, f, x, 0, z, 0.45f);
                Touch(l, f, x, 0, -z, 0.45f);
            }
            for (float z = -TreePlateR; z <= TreePlateR + 0.01f; z += 0.7f) Touch(l, f, xp - 0.1f, 0, z, 0.6f);
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
