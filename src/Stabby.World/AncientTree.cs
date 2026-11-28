using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float AncientU = 1 / 24f, AncientFootM = 4f, AncientTrunkM = 1.7f;
        static readonly float[] AncientSeats = { 0, 0, 0.1f, 0, -0.1f, 0, 0, 0.1f, 0, -0.1f, 0.07f, 0.07f, -0.07f, 0.07f, 0.07f, -0.07f, -0.07f, -0.07f };
        static readonly float[] AncientRiseY = { -0.4f, 2.6f, 5.6f, 8.4f, 10.8f };
        static readonly float[] AncientGirth = { 3.6f, 3f, 2.6f, 2.2f, 1.9f };

        static void AncientTree(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "ancient-tree");
            int rises = AncientRiseY.Length;
            var ax = new float[rises];
            var az = new float[rises];
            float drift = rng.Range(0, 6.2832f), lean = rng.Range(0.05f, 0.12f);
            for (int j = 0; j < rises; j++)
            {
                float t = AncientRiseY[j] / AncientRiseY[rises - 1];
                ax[j] = lean * AncientRiseY[j] * TurnCos(drift) + rng.Range(-0.25f, 0.25f) * t;
                az[j] = lean * AncientRiseY[j] * TurnSin(drift) + rng.Range(-0.25f, 0.25f) * t;
            }

            int bark = l.Pieces.Count;
            for (int j = 0; j + 1 < rises; j++)
                Bone(l, rng.NextU64(), f, ax[j], AncientRiseY[j], az[j], ax[j + 1], AncientRiseY[j + 1], az[j + 1], AncientGirth[j] * rng.Range(0.95f, 1.05f), AncientGirth[j + 1], 0, 2, Timber);

            int lobes = rng.Range(4, 6);
            float spin = rng.Range(0, 6.2832f);
            for (int k = 0; k < lobes; k++)
            {
                float a = spin + 6.2832f * (k + rng.Range(-0.2f, 0.2f)) / lobes, c = TurnCos(a), s = TurnSin(a), top = rng.Range(4.5f, 8f);
                Bone(l, rng.NextU64(), f, 0.95f * c, -0.3f, 0.95f * s, ax[2] + 0.55f * c, top, az[2] + 0.55f * s, rng.Range(1.6f, 2f), rng.Range(0.9f, 1.2f), 0, 1, Timber);
            }

            int buttresses = rng.Range(6, 9);
            var feet = new List<float>();
            for (int k = 0; k < buttresses; k++)
            {
                float a = spin + 6.2832f * (k + 0.5f + rng.Range(-0.25f, 0.25f)) / buttresses, c = TurnCos(a), s = TurnSin(a);
                float reach = rng.Range(3.4f, 4.6f), high = rng.Range(3.2f, 4.6f);
                Bone(l, rng.NextU64(), f, reach * c, -0.35f, reach * s, 1.9f * c, 1.3f, 1.9f * s, 0.55f, 1.05f, 0, 2, Timber);
                Bone(l, rng.NextU64(), f, 1.9f * c, 1f, 1.9f * s, 0.95f * c, high, 0.95f * s, 1.05f, 1.35f, 0, 2, Timber);
                Bone(l, rng.NextU64(), f, (reach - 0.7f) * c, -0.2f, (reach - 0.7f) * s, 1.3f * c, 0.75f * high, 1.3f * s, 0.5f, 0.75f, 0, 1, Timber);
                AncientRoot(l, ref rng, f, a, reach - 0.3f, -0.15f, 0.55f, rng.Range(2.5f, 4.5f), rng.Range(2, 4), feet);
            }
            int roots = rng.Range(4, 7);
            for (int k = 0; k < roots; k++)
            {
                float a = spin + 6.2832f * (k + rng.NextFloat()) / roots;
                AncientRoot(l, ref rng, f, a, 1.8f, 0.45f, 0.8f, rng.Range(4f, 6.5f), 3, feet);
            }

            var tips = new List<float>();
            int limbs = rng.Range(5, 8);
            float turn = rng.Range(0, 6.2832f);
            for (int k = 0; k < limbs; k++)
            {
                float a = turn + 6.2832f * (k + rng.Range(-0.2f, 0.2f)) / limbs, c = TurnCos(a), s = TurnSin(a);
                bool low = k < 2;
                int j = low ? 2 : 3 + rng.Range(0, 2);
                float y0 = low ? rng.Range(5.8f, 6.8f) : AncientRiseY[j] - rng.Range(0.2f, 1f), x0 = ax[Math.Min(j, rises - 1)] + 0.45f * c, z0 = az[Math.Min(j, rises - 1)] + 0.45f * s;
                float r1 = low ? rng.Range(5.5f, 7f) : rng.Range(3.8f, 5.5f), y1 = y0 + (low ? rng.Range(1.2f, 2f) : rng.Range(2.6f, 4.2f));
                float b1 = a + rng.Range(-0.3f, 0.3f), x1 = r1 * TurnCos(b1), z1 = r1 * TurnSin(b1);
                Bone(l, rng.NextU64(), f, x0, y0, z0, x1, y1, z1, low ? 1.2f : 1.1f, 0.7f, 0, 2, Timber);
                float r2 = r1 + rng.Range(2f, 3.5f), y2 = y1 + (low ? rng.Range(2.5f, 3.5f) : rng.Range(1.4f, 2.6f)), b2 = b1 + rng.Range(-0.35f, 0.35f);
                float x2 = r2 * TurnCos(b2), z2 = r2 * TurnSin(b2);
                Bone(l, rng.NextU64(), f, x1, y1, z1, x2, y2, z2, 0.7f, 0.4f, 0, 1, Timber);
                tips.Add(x2); tips.Add(y2); tips.Add(z2); tips.Add(1f);
                tips.Add(x1); tips.Add(y1 + 0.8f); tips.Add(z1); tips.Add(0.75f);
                for (int e = -1; e <= 1; e += 2)
                {
                    float b3 = b1 + e * rng.Range(0.45f, 0.8f), r3 = r1 + rng.Range(1.2f, 2.6f), y3 = y1 + rng.Range(1.2f, 2.4f);
                    float x3 = r3 * TurnCos(b3), z3 = r3 * TurnSin(b3);
                    Bone(l, rng.NextU64(), f, x1, y1, z1, x3, y3, z3, 0.4f, 0.22f, 0, 0, Timber);
                    tips.Add(x3); tips.Add(y3); tips.Add(z3); tips.Add(0.7f);
                }
            }
            float lx = ax[rises - 1] + rng.Range(-0.6f, 0.6f), lz = az[rises - 1] + rng.Range(-0.6f, 0.6f), ly = rng.Range(14.5f, 16f);
            Bone(l, rng.NextU64(), f, ax[rises - 1], AncientRiseY[rises - 1] - 0.4f, az[rises - 1], lx, ly, lz, 1.6f, 0.8f, 0, 1, Timber);
            tips.Add(lx); tips.Add(ly + 0.5f); tips.Add(lz); tips.Add(1.1f);
            Mossy(l, bark);

            int fungus = l.Pieces.Count, shelves = rng.Range(2, 4);
            for (int k = 0; k < shelves; k++)
            {
                float a = rng.Range(0, 6.2832f), c = TurnCos(a), s = TurnSin(a), y = rng.Range(0.8f, 4.2f), d = rng.Range(0.7f, 1.1f);
                int j = y < AncientRiseY[1] ? 0 : 1;
                float r = 0.5f * AncientGirth[j] * 0.9f;
                int brackets = rng.Range(2, 5);
                for (int b = 0; b < brackets; b++)
                {
                    float bd = d * (1 - 0.18f * b) * rng.Range(0.85f, 1.1f), ba = a + rng.Range(-0.12f, 0.12f), bc = TurnCos(ba), bs = TurnSin(ba);
                    Round(l, rng.NextU64(), f, ax[j] + (r + 0.2f * bd) * bc, y + 0.28f * b, az[j] + (r + 0.2f * bd) * bs, bd, 0.2f * bd, bd * rng.Range(0.75f, 0.95f), Fungus, GV, RoundDome, 0, b == 0 ? 1 : 0, bc, bs);
                }
            }
            Flat(l, fungus);

            int moss = l.Pieces.Count;
            Round(l, rng.NextU64(), f, ax[rises - 1], AncientRiseY[rises - 1] - 0.5f, az[rises - 1], 2.4f, 0.45f, 2.2f, Turf, GV, RoundDome, 0, 1);
            for (int k = 0; k < buttresses; k++)
            {
                if (rng.NextFloat() < 0.4f) continue;
                float a = spin + 6.2832f * (k + 0.5f) / buttresses, r = rng.Range(2f, 2.8f), c = TurnCos(a), s = TurnSin(a);
                Round(l, rng.NextU64(), f, r * c, rng.Range(0.75f, 1f), r * s, rng.Range(1.2f, 1.8f), rng.Range(0.14f, 0.2f), rng.Range(0.6f, 0.9f), Turf, GV, RoundDome, 0, 0, c, s);
            }
            Drape(l, moss);

            int leaves = l.Pieces.Count;
            for (int t = 0; t < tips.Count; t += 4)
            {
                int clumps = tips[t + 3] >= 1 ? rng.Range(3, 5) : rng.Range(1, 3);
                for (int k = 0; k < clumps; k++)
                {
                    float w = tips[t + 3] * rng.Range(2.6f, 4.6f), h = w * rng.Range(0.45f, 0.65f);
                    Round(l, rng.NextU64(), f, tips[t] + rng.Range(-1.4f, 1.4f), tips[t + 1] - 0.35f * h + rng.Range(-0.6f, 0.9f), tips[t + 2] + rng.Range(-1.4f, 1.4f), w, h, w * rng.Range(0.75f, 1.1f), Turf, GV, RoundDome, 0, k == 0 ? 2 : 1, 1, 0);
                }
            }
            int shell = rng.Range(16, 22);
            float cy = rng.Range(13.5f, 14.5f), cr = rng.Range(8.5f, 10f);
            for (int k = 0; k < shell; k++)
            {
                if (rng.NextFloat() < 0.15f) continue;
                float a = 2.39996f * k + rng.Range(-0.3f, 0.3f), up = (k + 0.5f) / shell, rr = cr * (float)Math.Sqrt(1 - up * up) * rng.Range(0.85f, 1.05f);
                float w = rng.Range(3f, 5.2f), h = w * rng.Range(0.45f, 0.6f);
                Round(l, rng.NextU64(), f, rr * TurnCos(a), cy + 4.2f * up - 1.2f + rng.Range(-0.5f, 0.5f), rr * TurnSin(a), w, h, w * rng.Range(0.8f, 1.1f), Turf, GV, RoundDome, 0, 1);
            }

            var skin = SurfaceTris(l);
            Col(l, f, ax[0], az[0], AncientTrunkM, 0, AncientRiseY[rises - 1]);
            for (int k = 0; k < buttresses; k++)
            {
                float a = spin + 6.2832f * (k + 0.5f) / buttresses, c = TurnCos(a), s = TurnSin(a);
                for (float r = 2f; r <= 3.6f; r += 0.8f)
                {
                    float top = SurfaceTop(skin, f, r * c, r * s, 5f);
                    if (top > 0.05f) Col(l, f, r * c, r * s, 0.35f, 0, top);
                }
            }
            for (int k = 0; k < feet.Count; k += 3)
            {
                float top = SurfaceTop(skin, f, feet[k], feet[k + 2], 2.5f);
                if (top > 0.05f) Col(l, f, feet[k], feet[k + 2], 0.25f, 0, top);
            }
            for (int k = 0; k < feet.Count; k += 9) Touch(l, f, feet[k], 0, feet[k + 2], 0.4f);
            for (int k = 0; k < 8; k++)
            {
                Arc(2 * k / 16f, out float c, out float s);
                Touch(l, f, 1.9f * c, 0, 1.9f * s, 0.6f);
            }
        }

        internal static float TurnCos(float a)
        {
            Arc(a * 0.15915494f, out float c, out _);
            return c;
        }

        internal static float TurnSin(float a)
        {
            Arc(a * 0.15915494f, out _, out float s);
            return s;
        }

        static void AncientRoot(StructureLayout l, ref Rng rng, in Frame f, float a, float r, float y, float d, float reach, int segs, List<float> feet)
        {
            float x = r * TurnCos(a), z = r * TurnSin(a), step = reach / segs;
            for (int k = 0; k < segs; k++)
            {
                a += rng.Range(-0.3f, 0.3f);
                r += step * rng.Range(0.8f, 1.2f);
                float nx = r * TurnCos(a), nz = r * TurnSin(a), ny = y - rng.Range(0.12f, 0.22f), nd = d * rng.Range(0.65f, 0.8f);
                Bone(l, rng.NextU64(), f, nx, ny, nz, x, y, z, nd, d, 0, k < 2 ? 1 : 0, Timber);
                feet.Add(nx); feet.Add(ny); feet.Add(nz);
                x = nx; z = nz; y = ny; d = nd;
            }
        }
    }

    public sealed partial class ObjectField
    {
        public const float AncientWoods = 0.5f, AncientReliefM = 5f, AncientFloatM = 0.45f, AncientBuryM = 1.2f;

        public string AncientFault(ObjectInstance o)
        {
            float u = o.Size * Structures.AncientU;
            float centre = GroundAt(detail, cm, o.X, o.Z) - o.Y;
            if (centre > AncientBuryM * u) return $"trunk is buried {centre:F2} m";
            for (int k = 0; k < 12; k++)
            {
                float a = 6.2832f * k / 12;
                Rotate(o, Structures.AncientFootM * u * Structures.TurnCos(a), 0, Structures.AncientFootM * u * Structures.TurnSin(a), out float wx, out _, out float wz);
                float gap = o.Y - GroundAt(detail, cm, o.X + wx, o.Z + wz);
                if (gap > AncientFloatM) return $"roots float {gap:F2} m";
            }
            int cx = Math.Min(n - 1, (int)(o.X / cm + 0.5f)), cz = Math.Min(n - 1, (int)(o.Z / cm + 0.5f));
            if (w.Sea[cz * n + cx] || w.Water.LakeId[cz * n + cx] >= 0) return "stands in water";
            if (RiverGap(o.X, o.Z) <= 10f * u + 1) return "stands on a river bed";
            float sr = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + sr >= f.MinX && o.X - sr <= f.MaxX && o.Z + sr >= f.MinZ && o.Z - sr <= f.MaxZ) return "stands over a cave";
            return null;
        }
    }
}
