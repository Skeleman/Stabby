using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float WolfU = 1 / 12f;
        public const int DenStride = 8;

        public static void DenRocks(ulong key, List<float> local, out float mx, out float gap)
        {
            local.Clear();
            var rng = Rng.Stream(key, "wolf-den-rocks");
            mx = rng.Range(-0.4f, 0.4f);
            gap = rng.Range(1.4f, 1.8f);
            float big = rng.Range(1.2f, 1.4f), small = big * rng.Range(0.5f, 0.65f);
            bool bigLeft = rng.NextFloat() < 0.5f;
            float rl = bigLeft ? big : small, rr = bigLeft ? small : big, hl = 0, hr = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                float r = side < 0 ? rl : rr, x = mx + side * (0.5f * gap + r), zf = -0.8f - r, len = rng.Range(0.2f, 0.6f), skew = side * rng.Range(0.1f, 0.4f), lift = ((side < 0) == bigLeft ? 0.1f : 0f) * r, rise = rng.Range(0.2f, 0.5f);
                Add(local, x, zf, x + skew, zf - len, r, lift, lift + rise, rng.NextFloat());
                if (side < 0) hl = lift + 0.85f * r + 0.5f * rise; else hr = lift + 0.85f * r + 0.5f * rise;
            }
            float rs = rng.Range(0.6f, 0.72f), zs = -1.1f, deep = rng.Range(0.6f, 1.1f), sit = 0.6f * rs;
            bool frontLeft = rng.NextFloat() < 0.5f;
            float sxl = mx - 0.5f * gap - 0.9f * rl, sxr = mx + 0.5f * gap + 0.9f * rr;
            Add(local, sxl, frontLeft ? zs : zs - deep, sxr, frontLeft ? zs - deep : zs, rs, hl + sit, hr + sit, rng.NextFloat());
            float rb = rng.Range(1.3f, 1.42f), bx = mx + rng.Range(-0.6f, 0.6f), bh = rng.Range(0.6f, 1.1f), bz = -2.7f - 0.9f * rb, up = rng.Range(0.4f, 0.8f);
            bool upLeft = rng.NextFloat() < 0.5f;
            Add(local, bx - bh, bz + rng.Range(-0.3f, 0.3f), bx + bh, bz + rng.Range(-0.3f, 0.3f), rb, 0.35f * rb + (upLeft ? up : 0), 0.35f * rb + (upLeft ? 0 : up), rng.NextFloat());
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
                    S = local[k + 7],
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

        public const float DenRockM = 90f, DenFaceDropM = 40f, DenRockRiseM = 6f;

        float[] rockHeight;
        byte[] rockFace;

        public bool BesideRock(float x, float z)
        {
            float reach = Math.Max(DenRockM, 2 * cm), ground = GroundAt(detail, cm, x, z);
            var heights = System.Threading.Volatile.Read(ref rockHeight) ?? RockGrid();
            var faces = rockFace;
            int r = (int)Math.Ceiling(reach / cm), cx = (int)Math.Round(x / cm), cz = (int)Math.Round(z / cm);
            for (int gz = Math.Max(0, cz - r); gz <= Math.Min(n - 1, cz + r); gz++)
                for (int gx = Math.Max(0, cx - r); gx <= Math.Min(n - 1, cx + r); gx++)
                {
                    float ox = gx * cm - x, oz = gz * cm - z;
                    if (ox * ox + oz * oz > reach * reach) continue;
                    int i = gz * n + gx;
                    float h = heights[i];
                    if (float.IsNaN(h)) heights[i] = h = detail.HeightAt(gx, gz);
                    if (h < ground + DenRockRiseM) continue;
                    if (faces[i] == 0) faces[i] = RockFace(gx, gz, h) ? (byte)1 : (byte)2;
                    if (faces[i] == 1) return true;
                }
            return false;
        }

        float[] RockGrid()
        {
            lock (rockLock)
            {
                if (rockHeight != null) return rockHeight;
                var heights = new float[n * n];
                for (int i = 0; i < heights.Length; i++) heights[i] = float.NaN;
                rockFace = new byte[n * n];
                System.Threading.Volatile.Write(ref rockHeight, heights);
                return heights;
            }
        }

        readonly object rockLock = new object();

        bool RockFace(int gx, int gz, float h)
        {
            float run = Math.Max(cm, Features.ReferenceCellM), drop = Math.Min(DenFaceDropM, DenFaceDropM / Features.ReferenceCellM * cm);
            for (int k = 0; k < 8; k++)
            {
                float d = run / Grid.Dist[k], fx = gx * cm + Grid.DX[k] * d, fz = gz * cm + Grid.DY[k] * d;
                if (fx < 0 || fz < 0 || fx > worldM || fz > worldM) continue;
                if (h - detail.Sample(fx, fz) >= drop) return true;
            }
            return false;
        }

        public const float DenBackGapM = 4f, DenFaceRunM = 16f, DenFaceRiseM = 6f, DenFaceStepM = 0.5f;

        public const float DenScanStepM = 2f;
        public const int DenRunSteps = (int)(DenFaceRunM / DenScanStepM), DenClimbSteps = 2;

        public float DenRear(ObjectInstance o) => DenRear(o.Def, o.Variant, o.Size);

        public float DenRear(int d, int variant, float size)
        {
            var local = new List<float>();
            Structures.DenRocks(ObjectMeshes.Key(lib.Objects[d].Id, 0, variant), local, out _, out _);
            float rear = 0;
            for (int k = 0; k < local.Count; k += Structures.DenStride) rear = Math.Max(rear, local[k + 4] - Math.Min(local[k + 1], local[k + 3]));
            return rear * Structures.WolfU * size;
        }

        bool ToFace(ref float x, ref float z, float rear, float x0, float z0, float x1, float z1, out float qa, out float qb)
        {
            qa = 1; qb = 0;
            int steps = (int)(DenRockM / DenScanStepM);
            var g = new float[steps + DenRunSteps + 1];
            float rise = DenFaceRiseM, foot = float.MaxValue, bx = 0, bz = 0;
            for (int k = 0; k < 16; k++)
            {
                float ux = Structures.Cos16(k), uz = Structures.Sin16(k);
                for (int i = 0; i < g.Length && (i - DenRunSteps) * DenScanStepM < foot; i++)
                {
                    float px = x + ux * i * DenScanStepM, pz = z + uz * i * DenScanStepM;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) break;
                    g[i] = GroundAt(detail, cm, px, pz);
                    if (i >= DenRunSteps && g[i] - g[i - DenRunSteps] >= rise)
                    {
                        int j = i - DenRunSteps;
                        while (j < i && g[Math.Min(j + DenClimbSteps, i)] - g[j] < DenCoverRiseM) j++;
                        if (j * DenScanStepM < foot) { foot = j * DenScanStepM; bx = ux; bz = uz; }
                        break;
                    }
                }
            }
            if (foot == float.MaxValue) return false;
            float move = Math.Max(0, foot - DenScanStepM) - rear, nx = x + bx * move, nz = z + bz * move;
            if (nx < x0 || nz < z0 || nx > x1 || nz > z1) return false;
            x = nx; z = nz;
            qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - bz)));
            qb = (bx > 0 ? -1 : 1) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + bz)));
            return true;
        }

        public bool FaceBehind(float x, float z, float bx, float bz)
        {
            float rise = DenFaceRiseM;
            for (float s = 0; s <= DenBackGapM; s += DenFaceStepM)
            {
                float fx = x + bx * s, fz = z + bz * s;
                if (GroundAt(detail, cm, fx + bx * DenFaceRunM, fz + bz * DenFaceRunM) - GroundAt(detail, cm, fx, fz) >= rise) return true;
            }
            return false;
        }

        void DenBack(ObjectInstance o, out float x, out float z, out float bx, out float bz)
        {
            Rotate(o, 0, 0, -1, out bx, out _, out bz);
            float bl = (float)Math.Sqrt(bx * bx + bz * bz), rear = DenRear(o);
            bx /= bl; bz /= bl;
            x = o.X + bx * rear; z = o.Z + bz * rear;
        }

        public string DenBackFault(ObjectInstance o)
        {
            DenBack(o, out float x, out float z, out float bx, out float bz);
            return FaceBehind(x, z, bx, bz) ? null : "has no steep rock face at its back";
        }

        public const float DenCoverRunM = 8f, DenCoverRiseM = 1f;

        public string DenCoverFault(ObjectInstance o)
        {
            DenBack(o, out float x, out float z, out float bx, out float bz);
            float g0 = GroundAt(detail, cm, x, z);
            for (float s = DenFaceStepM; s <= DenCoverRunM; s += DenFaceStepM)
                if (GroundAt(detail, cm, x + bx * s, z + bz * s) - g0 >= DenCoverRiseM) return null;
            return "stands out on open ground, its face too far behind";
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
