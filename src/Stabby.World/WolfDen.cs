using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float WolfU = 1 / 12f;
        public const int DenStride = 8;
        public const float DenLump = 0.3f, SlabLump = 0.15f;

        public static void DenRocks(ulong key, List<float> local, out float mx, out float gap)
        {
            local.Clear();
            var rng = Rng.Stream(key, "wolf-den-rocks");
            mx = rng.Range(-0.4f, 0.4f);
            gap = rng.Range(1.3f, 1.7f);
            float big = rng.Range(1.2f, 1.5f), small = big * rng.Range(0.6f, 0.8f);
            bool bigLeft = rng.NextFloat() < 0.5f;
            float rl = bigLeft ? big : small, rr = bigLeft ? small : big;
            for (int side = -1; side <= 1; side += 2)
            {
                float r = side < 0 ? rl : rr, x = mx + side * (0.5f * gap + r), zf = -0.8f - r, len = rng.Range(0.2f, 0.6f), skew = side * rng.Range(0f, 0.3f);
                Add(local, x, zf, x + skew, zf - len, r, 0.25f * r, 0.25f * r + rng.Range(0.3f, 0.6f), rng.NextFloat());
            }
            float rs = rng.Range(0.6f, 0.75f), zs = -1.3f, deep = rng.Range(0.5f, 1.0f);
            bool frontLeft = rng.NextFloat() < 0.5f;
            float sxl = mx - 0.5f * gap - 0.8f * rl, sxr = mx + 0.5f * gap + 0.8f * rr;
            Add(local, sxl, frontLeft ? zs : zs - deep, sxr, frontLeft ? zs - deep : zs, rs, 1.25f * rl + 0.3f * rs, 1.25f * rr + 0.3f * rs, rng.NextFloat());
            float rb = rng.Range(1.5f, 1.9f), bx = mx + rng.Range(-0.6f, 0.6f), bh = rng.Range(0.4f, 0.9f), bz = -2.6f - rb, rise = rng.Range(0.3f, 0.7f);
            bool upLeft = rng.NextFloat() < 0.5f;
            Add(local, bx - bh, bz + rng.Range(-0.3f, 0.3f), bx + bh, bz + rng.Range(-0.3f, 0.3f), rb, 0.15f * rb + (upLeft ? rise : 0), 0.15f * rb + (upLeft ? 0 : rise), rng.NextFloat());
            if (rng.NextFloat() < 0.7f)
            {
                int side = rng.NextFloat() < 0.5f ? -1 : 1, d = rng.Range(0, 16);
                float r2 = rng.Range(0.7f, 1.0f), rj = side < 0 ? rl : rr, x = mx + side * (0.5f * gap + 2 * rj + rng.Range(0.6f, 1.0f) + r2), z = rng.Range(-2.4f, -1.2f), h = rng.Range(0.3f, 0.5f);
                Add(local, x - h * Cos16(d), z - h * Sin16(d), x + h * Cos16(d), z + h * Sin16(d), r2, 0.1f * r2, 0.1f * r2 + rng.Range(0.1f, 0.3f), rng.NextFloat());
            }
        }

        static void Add(List<float> local, float ax, float az, float bx, float bz, float r, float la, float lb, float s)
        {
            local.Add(ax); local.Add(az); local.Add(bx); local.Add(bz); local.Add(r); local.Add(la); local.Add(lb); local.Add(s);
        }

        static void WolfDen(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "wolf-den");
            var f = root;
            var rocks = new List<float>();
            DenRocks(key, rocks, out float mx, out float gap);
            float far = 0;
            for (int k = 0; k < rocks.Count; k += DenStride)
            {
                float ax = rocks[k], az = rocks[k + 1], bx = rocks[k + 2], bz = rocks[k + 3], r = rocks[k + 4];
                float bottom = Math.Max(0f, Math.Min(rocks[k + 5], rocks[k + 6]) - r), top = Math.Max(rocks[k + 5], rocks[k + 6]) + r;
                Col(l, f, ax, az, r, bottom, top);
                Col(l, f, 0.5f * (ax + bx), 0.5f * (az + bz), r, bottom, top);
                Col(l, f, bx, bz, r, bottom, top);
                if (k >= 4 * DenStride) far = ax < mx ? -1 : 1;
            }

            Touch(l, f, mx, 0, -1.3f, 0.8f);
            Touch(l, f, mx, 0, -2.1f, 0.7f);
            Touch(l, f, mx, 0, -0.2f, 1.3f);
            float ax0 = mx + rng.Range(-0.3f, 0.3f);
            Touch(l, f, ax0, 0, rng.Range(0.8f, 1.1f), rng.Range(1.6f, 2.0f));
            int trail = rng.Range(2, 4);
            float tx = ax0, tz = 1.4f;
            for (int i = 0; i < trail; i++)
            {
                tx += rng.Range(-0.5f, 0.5f);
                tz += rng.Range(0.8f, 1.1f);
                Touch(l, f, tx, 0, tz, rng.Range(0.9f, 1.3f) - 0.1f * i);
            }
            for (int side = -1; side <= 1; side += 2)
                Touch(l, f, mx + side * rng.Range(1.4f, 2.2f), 0, rng.Range(0.2f, 1.6f), rng.Range(0.6f, 0.9f));

            int stones = l.Pieces.Count, talus = rng.Range(3, 7);
            for (int i = 0; i < talus; i++)
            {
                int d = rng.Range(0, 16);
                float side = rng.NextFloat() < 0.5f ? -1 : 1, x = mx + side * (0.5f * gap + rng.Range(0.1f, 1.6f)), z = rng.Range(-1.0f, 0.2f), sz = rng.Range(0.12f, 0.32f);
                Rock(l, rng.NextU64(), f, x, -0.06f, z, sz * 1.3f, sz * 0.7f, sz, Cos16(d), Sin16(d), 0, i < 2 ? 2 : 1, rng.Range(-0.1f, 0.1f));
                Touch(l, f, x, -0.05f, z, 0.6f * sz);
            }
            Mossy(l, stones);

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
                float side = far != 0 ? -far : rng.NextFloat() < 0.5f ? -1 : 1, x = side * rng.Range(2.6f, 3.6f), z = rng.Range(1.4f, 2.8f), len = rng.Range(2.4f, 3.4f), dl = rng.Range(0.3f, 0.42f);
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
            Structures.DenRocks(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant), local, out _, out _);
            float u = Structures.WolfU * o.Size;
            for (int k = 0; k < local.Count; k += Structures.DenStride)
            {
                Rotate(o, local[k] * u, 0, local[k + 1] * u, out float ax, out _, out float az);
                Rotate(o, local[k + 2] * u, 0, local[k + 3] * u, out float bx, out _, out float bz);
                ax += o.X; az += o.Z; bx += o.X; bz += o.Z;
                float r = local[k + 4] * u;
                list.Add(new FeatureEdit
                {
                    Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = r,
                    Ax = ax, Ay = GroundAt(detail, cm, ax, az) + local[k + 5] * u, Az = az,
                    Bx = bx, By = GroundAt(detail, cm, bx, bz) + local[k + 6] * u, Bz = bz,
                    N = (k == 2 * Structures.DenStride ? Structures.SlabLump : Structures.DenLump) * r, S = local[k + 7],
                }.Finer());
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
