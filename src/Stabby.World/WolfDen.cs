using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float WolfU = 1 / 12f;
        public const int DenStride = 6;

        public static void DenRocks(ulong key, List<float> local)
        {
            local.Clear();
            var rng = Rng.Stream(key, "wolf-den-rocks");
            float r = rng.Range(1.7f, 2.1f), half = rng.Range(1.0f, 1.6f), zc = -1.4f - 0.8f * r, skew = rng.Range(-0.3f, 0.3f);
            local.Add(-half); local.Add(zc + skew); local.Add(half); local.Add(zc - skew); local.Add(r); local.Add(0.6f * r);
            for (int side = -1; side <= 1; side += 2)
            {
                float fr = rng.Range(1.1f, 1.4f), x = side * (half + rng.Range(0.6f, 1.2f)), z = zc + rng.Range(0.4f, 0.9f);
                local.Add(x); local.Add(z); local.Add(x); local.Add(z); local.Add(fr); local.Add(0.5f * fr);
            }
            float br = rng.Range(1.3f, 1.6f), bx = rng.Range(-1f, 1f), bz = zc - 0.5f * r;
            local.Add(bx); local.Add(bz); local.Add(bx); local.Add(bz); local.Add(br); local.Add(0.9f * br);
            if (rng.NextFloat() < 0.5f)
            {
                float tr = rng.Range(0.9f, 1.2f), tx = rng.Range(-half, half), tz = zc - 0.2f;
                local.Add(tx); local.Add(tz); local.Add(tx); local.Add(tz); local.Add(tr); local.Add(1.2f * r);
            }
        }

        static void WolfDen(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "wolf-den");
            var f = root;
            var rocks = new List<float>();
            DenRocks(key, rocks);
            for (int k = 0; k < rocks.Count; k += DenStride)
            {
                float ax = rocks[k], az = rocks[k + 1], bx = rocks[k + 2], bz = rocks[k + 3], r = rocks[k + 4], y = rocks[k + 5];
                Col(l, f, ax, az, r, 0, y + r);
                if (ax != bx || az != bz) { Col(l, f, 0.5f * (ax + bx), 0.5f * (az + bz), r, 0, y + r); Col(l, f, bx, bz, r, 0, y + r); }
            }

            float mx = rng.Range(-0.3f, 0.3f), mw = rng.Range(1.0f, 1.3f), mh = rng.Range(0.65f, 0.8f);
            int before = l.Pieces.Count;
            Box(l, rng.NextU64(), f, mx, -0.1f, -1.55f, mw, mh + 0.1f, 0.8f, Ash, GV, 0, 2);
            Drape(l, before);
            Touch(l, f, mx, -0.05f, -1.1f, 0.9f);

            int stones = l.Pieces.Count;
            for (int side = -1; side <= 1; side += 2)
            {
                float cx = mx + side * (0.5f * mw + rng.Range(0.15f, 0.3f)), cz = -1.3f + rng.Range(-0.1f, 0.1f);
                int d = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, cx, -0.15f, cz, rng.Range(0.45f, 0.65f), mh * rng.Range(1.0f, 1.25f), rng.Range(0.5f, 0.7f), Cos16(d), Sin16(d), 0, 2, side * rng.Range(0.05f, 0.15f));
                Touch(l, f, cx, -0.1f, cz, 0.35f);
            }
            Rock(l, rng.NextU64(), f, mx + rng.Range(-0.1f, 0.1f), mh - 0.12f, -1.25f, mw + rng.Range(0.4f, 0.7f), rng.Range(0.3f, 0.42f), rng.Range(0.5f, 0.65f), 1, rng.Range(-0.1f, 0.1f), 0, 2);
            int talus = rng.Range(4, 8);
            for (int i = 0; i < talus; i++)
            {
                int k = rng.Range(0, 16), d = rng.Range(0, 16);
                float rd = rng.Range(0.5f, 1.8f), x = mx + Cos16(k) * (rd + 1.2f), z = -1.2f + Math.Abs(Sin16(k)) * rd * 0.6f, sz = rng.Range(0.12f, 0.32f);
                Rock(l, rng.NextU64(), f, x, -0.06f, z, sz * 1.3f, sz * 0.7f, sz, Cos16(d), Sin16(d), 0, 1, rng.Range(-0.1f, 0.1f));
                Touch(l, f, x, -0.05f, z, 0.6f * sz);
            }
            Mossy(l, stones);

            int patches = rng.Range(3, 6);
            for (int i = 0; i < patches; i++)
                Touch(l, f, mx + rng.Range(-1.4f, 1.4f), 0, rng.Range(-0.4f, 2.2f), rng.Range(1.0f, 1.6f));

            int bones = l.Pieces.Count, longs = rng.Range(3, 6);
            for (int i = 0; i < longs; i++)
            {
                int d = rng.Range(0, 16);
                float x = mx + rng.Range(-1.8f, 1.8f), z = rng.Range(0.3f, 3f), d0 = rng.Range(0.05f, 0.08f);
                Log(l, rng.NextU64(), f, x, -0.4f * d0, z, rng.Range(0.3f, 0.6f), d0, Cos16(d), Sin16(d), Plaster, 0, i < 2 ? 1 : 0);
                Touch(l, f, x, -0.02f, z, 0.12f);
            }
            int ribs = rng.Range(2, 5);
            float rx = mx + rng.Range(-1.2f, 1.2f), rz = rng.Range(0.8f, 2.4f);
            int rd0 = rng.Range(0, 16);
            for (int i = 0; i < ribs; i++)
            {
                float t = (i - 0.5f * (ribs - 1)) * 0.09f, x = rx - Sin16(rd0) * t, z = rz + Cos16(rd0) * t;
                Log(l, rng.NextU64(), f, x, -0.01f, z, rng.Range(0.26f, 0.38f), 0.028f, Cos16(rd0 + rng.Range(-1, 2)), Sin16(rd0 + rng.Range(-1, 2)), Plaster, 0, 0);
            }
            Touch(l, f, rx, -0.02f, rz, 0.25f);
            int sd = rng.Range(0, 16);
            float sx = mx + rng.Range(-1.4f, 1.4f), sz2 = rng.Range(0.5f, 2.6f);
            Log(l, rng.NextU64(), f, sx, -0.03f, sz2, 0.2f, 0.15f, Cos16(sd + 4), Sin16(sd + 4), Plaster, 0, 1);
            Round(l, rng.NextU64(), f, sx, 0.13f, sz2, 0.17f, 0.1f, 0.24f, Plaster, GZ, RoundDome, 0, 1, Cos16(sd), Sin16(sd));
            Touch(l, f, sx, -0.02f, sz2, 0.16f);
            int discs = rng.Range(2, 5);
            for (int i = 0; i < discs; i++)
            {
                float x = sx + rng.Range(-0.6f, 0.6f), z = sz2 + rng.Range(-0.6f, 0.6f), dd = rng.Range(0.06f, 0.09f);
                Log(l, rng.NextU64(), f, x, -0.01f, z, dd, 0.05f, Cos16(sd + 2 * i), Sin16(sd + 2 * i), Plaster, 0, 0);
            }
            Drape(l, bones);

            if (rng.NextFloat() < 0.6f)
            {
                int d = rng.Range(0, 16), logs = l.Pieces.Count;
                float side = rng.NextFloat() < 0.5f ? -1 : 1, x = side * rng.Range(2.6f, 3.6f), z = rng.Range(0.6f, 2.4f), len = rng.Range(2.4f, 3.4f), dl = rng.Range(0.3f, 0.42f);
                Log(l, rng.NextU64(), f, x, -0.25f * dl, z, len, dl, Cos16(d), Sin16(d), Timber, 0, 2);
                Drape(l, logs);
                Mossy(l, logs);
                for (int e = -1; e <= 1; e++)
                {
                    float px = x + e * 0.4f * len * Cos16(d), pz = z + e * 0.4f * len * Sin16(d);
                    Touch(l, f, px, -0.05f, pz, 0.6f * dl);
                }
                Col(l, f, x, z, 0.5f * dl, 0, 0.75f * dl);
            }
        }
    }

    public sealed partial class ObjectField
    {
        public void DenEditsOf(ObjectInstance o, List<FeatureEdit> list)
        {
            var local = new List<float>();
            Structures.DenRocks(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant), local);
            float u = Structures.WolfU * o.Size;
            for (int k = 0; k < local.Count; k += Structures.DenStride)
            {
                Rotate(o, local[k] * u, 0, local[k + 1] * u, out float ax, out _, out float az);
                Rotate(o, local[k + 2] * u, 0, local[k + 3] * u, out float bx, out _, out float bz);
                ax += o.X; az += o.Z; bx += o.X; bz += o.Z;
                float lift = local[k + 5] * u;
                list.Add(new FeatureEdit
                {
                    Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = local[k + 4] * u,
                    Ax = ax, Ay = GroundAt(detail, cm, ax, az) + lift, Az = az,
                    Bx = bx, By = GroundAt(detail, cm, bx, bz) + lift, Bz = bz,
                });
            }
        }

        public FeatureEdit[] DenEdits()
        {
            var list = new List<FeatureEdit>();
            foreach (var o in Discoveries())
                if (lib.Objects[o.Def].Placement.Discovery == WolfDen) DenEditsOf(o, list);
            return list.ToArray();
        }

        public string DenFault(ObjectInstance o)
        {
            var rocks = new List<FeatureEdit>();
            DenEditsOf(o, rocks);
            float span = WorldChunks.Size * cm;
            foreach (var b in rocks)
            {
                float x = 0.5f * (b.MinX + b.MaxX), z = 0.5f * (b.MinZ + b.MaxZ), r = 0.5f * Math.Max(b.MaxX - b.MinX, b.MaxZ - b.MinZ);
                if (b.MinX < 0 || b.MinZ < 0) return "den rock leaves its object chunk";
                float cx0 = (float)Math.Floor(x / span) * span, cz0 = (float)Math.Floor(z / span) * span;
                if (b.MinX < cx0 || b.MaxX >= Math.Min(cx0 + span, worldM) || b.MinZ < cz0 || b.MaxZ >= Math.Min(cz0 + span, worldM)) return "den rock leaves its object chunk";
                int gx = Math.Min(n - 1, (int)(x / cm + 0.5f)), gz = Math.Min(n - 1, (int)(z / cm + 0.5f));
                if (w.Sea[gz * n + gx]) return "den rock stands in water";
                float ground = GroundAt(detail, cm, x, z);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ax = gx + dx, az = gz + dz;
                        if (ax < 0 || az < 0 || ax >= n || az >= n) continue;
                        int lake = w.Water.LakeId[az * n + ax];
                        if (lake >= 0 && ((dx == 0 && dz == 0) || ground < w.Water.Lakes[lake].Level)) return "den rock stands in water";
                    }
                if (RiverGap(x, z) <= r + 1) return "den rock lies on a river bed";
                foreach (var f in edits)
                    if (f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring && b.MaxX >= f.MinX && b.MinX <= f.MaxX && b.MaxZ >= f.MinZ && b.MinZ <= f.MaxZ) return "den rock overlaps a terrain feature";
            }
            return null;
        }
    }
}
