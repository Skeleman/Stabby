using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float TreeU = 1 / 18f, TreeCy = 1.05f, TreeSy = 3.2f, TreeSz = 3.05f, TreeTaper = 0.3f, TreeSag = 0.05f, TreeDrop = 0.8f, TreeWallZ = 1.15f, TreeRoofM = 2.25f, TreeSeatZ = 1.1f, TreeFloorM = 0.45f, TreeLidM = TreeFloorM + 1.3f;
        static readonly float[] TreeSeats =
        {
            -7.5f / 18, 1.1f / 18, -5f / 18, 1.1f / 18, -2.5f / 18, 1.1f / 18, 0, 1.1f / 18, 2.5f / 18, 1.1f / 18, 5f / 18, 1.1f / 18, 7.5f / 18, 1.1f / 18,
            -7.5f / 18, -1.1f / 18, -5f / 18, -1.1f / 18, -2.5f / 18, -1.1f / 18, 0, -1.1f / 18, 2.5f / 18, -1.1f / 18, 5f / 18, -1.1f / 18, 7.5f / 18, -1.1f / 18,
        };

        public static float TreeLength(ulong key) => Rng.Stream(key, "fallen-tree").Range(16f, 17f);

        static float TreeRad(float x, float len, float size) => 0.48f * size * (1 - TreeTaper * (x + 0.5f * len) / len);

        static float TreeMid(float x, float len)
        {
            float t = 2 * x / len;
            return TreeCy - 0.5f * TreeSy * (TreeSag * (1 - t * t) + TreeDrop * TreeTaper * 0.5f * (t + 1));
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

            int stubs = rng.Range(3, 6);
            for (int i = 0; i < stubs; i++)
            {
                int k = (rng.NextFloat() < 0.5f ? -1 : 1) * rng.Range(2, 4);
                Arc((k + rng.Range(-0.3f, 0.3f)) / 16f, out float c, out float s);
                float x = rng.Range(half - 3.2f, half - 0.6f), r = TreeRad(x, len, TreeSy), rz = TreeRad(x, len, TreeSz), sl = rng.Range(0.3f, 0.6f), d = rng.Range(0.28f, 0.42f), y0 = TreeMid(x, len) + 0.85f * r * c, z0 = 0.85f * rz * s;
                float up = 0.6f + 0.4f * c, reach = 0.15f * r + sl, ax = rng.Range(-0.25f, 0.25f);
                Bone(l, rng.NextU64(), f, x, y0, z0, x + ax * reach, y0 + up * reach, z0 + s * reach, d, 0.55f * d, 0, 1, Timber);
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

            Drape(l, moss);

            var skin = SurfaceTris(l);
            float last = 0;
            for (float x = -half + 0.4f; x <= half - 0.6f; x += 0.9f)
            {
                float top = Math.Min(2.8f, SurfaceTop(skin, f, x, 0, 9));
                Col(l, f, x, TreeWallZ, 0.32f, 0, top);
                Col(l, f, x, -TreeWallZ, 0.32f, 0, top);
                last = x;
            }
            for (float x = -half + 0.6f; x <= last - 0.2f; x += 0.8f) Col(l, f, x, 0, 0.45f, 0, TreeFloorM);
            for (int k = 0; k < 5; k++)
            {
                float x = last + 0.2f - 0.2f * k, floor = SurfaceTop(skin, f, x, 0, TreeFloorM + 0.2f);
                if (floor > 0.02f) Col(l, f, x, 0, 0.1f, 0, Math.Min(floor, TreeFloorM));
            }
            for (float x = -half + 0.3f; x <= last + 0.2f; x += 0.25f)
            {
                float top = SurfaceTop(skin, f, x, 0, 9);
                if (top > 1) Col(l, f, x, 0, 0.12f, TreeLidM, Math.Max(top, TreeLidM + 0.01f));
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
        public const float TreeWoods = 0.45f, TreeReliefM = 6f, TreeFloatM = 0.2f, TreeHeadM = 1.8f;

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
            int cx = Math.Min(n - 1, (int)(o.X / cm + 0.5f)), cz = Math.Min(n - 1, (int)(o.Z / cm + 0.5f));
            if (w.Sea[cz * n + cx] || w.Water.LakeId[cz * n + cx] >= 0) return "lies in water";
            if (RiverGap(o.X, o.Z) <= 8f * u + 1) return "lies on a river bed";
            float sr = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + sr >= f.MinX && o.X - sr <= f.MaxX && o.Z + sr >= f.MinZ && o.Z - sr <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
