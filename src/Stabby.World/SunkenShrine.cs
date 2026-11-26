using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float SunkenU = 1 / 14f, SunkenPlinthM = 0.68f;
        public static readonly float[] SunkenSeat = { 0, 0 };
        static readonly float[] SunkenStep = { -0.9f, 0.3f, 0.5f, SunkenPlinthM };

        static void SunkenTier(StructureLayout l, ref Rng rng, in Frame f, float hx, float hz, int t)
        {
            float y = SunkenStep[t], h = SunkenStep[t + 1] - y;
            int nx = Math.Max(2, (int)Math.Round(2 * hx / 1.3f)), nz = Math.Max(2, (int)Math.Round(2 * hz / 1.1f));
            float sx = 2 * hx / nx, sz = 2 * hz / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    bool edge = i == 0 || j == 0 || i == nx - 1 || j == nz - 1;
                    if (t == 2 && edge && rng.NextFloat() < 0.2f) continue;
                    Yaw(ref rng, 0.03f, out float c, out float s);
                    float x = -hx + (i + 0.5f) * sx, z = -hz + (j + 0.5f) * sz;
                    float sag = edge && t > 0 ? rng.Range(-0.06f, 0) : 0;
                    Hewn(l, rng.NextU64(), f, x + rng.Range(-0.02f, 0.02f), y + sag, z + rng.Range(-0.02f, 0.02f), sx - 0.03f, h - 0.01f, sz - 0.03f, c, s, 0, 1, rng.Range(0.82f, 0.93f), rng.Range(-0.02f, 0.02f), 0, 0.03f);
                    if (t == 0) Touch(l, f, x, 0, z, 0.75f * Math.Max(sx, sz));
                }
            Hewn(l, rng.NextU64(), f, 0, y, 0, 2 * hx, h, 2 * hz, 1, 0, 2, 2, 0.8f);
        }

        static float SunkenColumn(StructureLayout l, ref Rng rng, in Frame f, float x, float z, float tall, bool broken, out float top)
        {
            float y = SunkenPlinthM, r = rng.Range(0.38f, 0.44f);
            Yaw(ref rng, 0.06f, out float bc, out float bs);
            Hewn(l, rng.NextU64(), f, x, y, z, 2.4f * r, 0.28f, 2.4f * r, bc, bs, 0, 2, rng.Range(0.86f, 0.93f));
            y += 0.28f;
            int drums = broken ? rng.Range(1, 3) : (int)Math.Round(tall / 1f);
            float dh = broken ? rng.Range(0.8f, 1f) : (tall - 0.28f - 0.36f) / drums;
            for (int k = 0; k < drums; k++)
            {
                int start = l.Pieces.Count;
                float w = 2 * r * (1 - 0.04f * k / Math.Max(1, drums)), lean = broken && k == drums - 1 ? rng.Range(-0.12f, 0.12f) : rng.Range(-0.01f, 0.01f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x + rng.Range(-0.025f, 0.025f), y, z + rng.Range(-0.025f, 0.025f), w, dh - 0.02f, w, Cos16(q), Sin16(q), 0, 2, rng.Range(0.3f, 0.38f), lean);
                if (y < 1.8f) Mossy(l, start);
                y += dh;
            }
            if (!broken)
            {
                Yaw(ref rng, 0.05f, out float cc, out float cs);
                Hewn(l, rng.NextU64(), f, x, y, z, 2.5f * r, 0.36f, 2.5f * r, cc, cs, 0, 2, rng.Range(0.86f, 0.93f), 0, 0, -0.12f);
                y += 0.36f;
            }
            Col(l, f, x, z, r, SunkenPlinthM, y);
            top = y;
            return r;
        }

        static void SunkenShrine(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "sunken-shrine");
            Yaw(ref rng, 0.12f, out float c0, out float s0);
            var f = root.Child(rng.Range(-0.2f, 0.2f), 0, rng.Range(-0.2f, 0.2f), c0, s0);
            float hx = rng.Range(3.2f, 3.7f), hz = rng.Range(2.5f, 2.9f);
            int start = l.Pieces.Count;
            for (int t = 0; t < 3; t++) SunkenTier(l, ref rng, f, hx - 0.45f * t, hz - 0.45f * t, t);
            Mossy(l, start);
            for (float x = -hx + 0.9f; x < hx; x += 1.6f)
                for (float z = -hz + 0.9f; z < hz; z += 1.6f)
                    Col(l, f, x, z, 1.1f, SunkenStep[0], SunkenPlinthM);

            float ix = hx - 0.9f - 0.45f, iz = hz - 0.9f - 0.45f, tall = rng.Range(4.6f, 5.6f);
            int front = rng.Range(2, 4), back = rng.Range(0, 4);
            var tops = new float[front];
            var xs = new float[front];
            int broke = rng.Range(-1, front);
            for (int i = 0; i < front; i++)
            {
                xs[i] = front == 1 ? 0 : -ix + 2 * ix * i / (front - 1);
                SunkenColumn(l, ref rng, f, xs[i], iz, tall * rng.Range(0.97f, 1.03f), i == broke, out tops[i]);
            }
            for (int i = 0; i < back; i++)
            {
                float x = back == 1 ? rng.Range(-ix, ix) : -ix + 2 * ix * i / (back - 1);
                SunkenColumn(l, ref rng, f, x, -iz, tall * rng.Range(0.97f, 1.03f), rng.NextFloat() < 0.5f, out _);
            }
            int pair = -1;
            for (int i = 0; i + 1 < front; i++) if (i != broke && i + 1 != broke) { pair = i; break; }
            if (pair >= 0)
            {
                float lx = 0.5f * (xs[pair] + xs[pair + 1]), span = xs[pair + 1] - xs[pair] + 1.1f, ly = Math.Min(tops[pair], tops[pair + 1]);
                Yaw(ref rng, 0.02f, out float c, out float s);
                Hewn(l, rng.NextU64(), f, lx, ly, iz + rng.Range(-0.04f, 0.04f), span, rng.Range(0.5f, 0.6f), 1f, c, s, 0, 2, rng.Range(0.9f, 0.94f), rng.Range(-0.015f, 0.015f), 0, 0.04f);
                Col(l, f, lx, iz, 0.5f, ly, ly + 0.5f);
            }

            int fallen = l.Pieces.Count, drums = rng.Range(2, 5);
            float side = rng.NextFloat() < 0.5f ? -1 : 1, fz = rng.Range(-0.6f, 0.6f) * hz;
            Yaw(ref rng, 0.25f, out float dc, out float ds);
            var lie = f.Child(side * (hx + 1.6f), 0, fz, dc, ds);
            for (int k = 0; k < drums; k++)
            {
                float z = k * 1.05f + rng.Range(-0.08f, 0.08f), x = rng.Range(-0.15f, 0.15f) * k, r = rng.Range(0.38f, 0.44f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), lie, x, -0.45f, z, 2 * r, 2 * r, 0.95f, Cos16(q) * 0.1f + 0.95f, Sin16(q) * 0.1f, 0, 2, rng.Range(0.3f, 0.38f), rng.Range(-0.1f, 0.1f));
                Touch(l, lie, x, 0, z, 0.9f);
                Col(l, lie, x, z, r, -0.45f, 2 * r - 0.45f);
            }
            int rocks = rng.Range(3, 7);
            for (int k = 0; k < rocks; k++)
            {
                int d = rng.Range(0, 16);
                float rr = rng.Range(1.1f, 1.5f), x = Cos16(d) * (hx + rr * 0.9f), z = Sin16(d) * (hz + rr * 0.9f), sz = rng.Range(0.35f, 0.7f);
                Hewn(l, rng.NextU64(), f, x, -0.55f, z, 1.4f * sz, 0.6f * sz + 0.6f, sz, Cos16(d + 3), Sin16(d + 3), 0, 1, rng.Range(0.8f, 0.92f), rng.Range(-0.12f, 0.12f));
                Touch(l, f, x, 0, z, 0.9f * sz + 0.3f);
            }
            Mossy(l, fallen);
        }
    }

    public sealed partial class ObjectField
    {
        public const float SunkenDepthMinM = 0.95f, SunkenDepthMaxM = 2.1f, SunkenDepthAimM = 1.4f, SunkenProbeM = 7f, SunkenReliefM = 0.5f, SunkenWetM = 0.3f;
        public const float SunkenFloatM = 0.15f, SunkenBuryM = 0.1f, SunkenUnderM = 0.2f, SunkenAboveM = 2.5f;
        public const int SunkenSeekSteps = 6;
        int[][] shallows;

        bool LakeWater(float x, float z, out int lake, out float level)
        {
            lake = -1; level = 0;
            if (x < 0 || z < 0 || x >= worldM || z >= worldM) return false;
            int n = w.Config.Size, i = Math.Min(n - 1, (int)(z / cm)) * n + Math.Min(n - 1, (int)(x / cm));
            lake = w.Water.LakeId[i];
            if (w.Sea[i] || lake < 0) return false;
            int water = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("water"));
            if (w.Water.Lakes[lake].Liquid != water) return false;
            level = w.Water.Lakes[lake].Level;
            return true;
        }

        public float SunkenWaterLevel(float x, float z) => LakeWater(x, z, out _, out float level) ? level : float.NaN;

        int[][] Shallows(int cells, float step)
        {
            var lists = new List<int>[cells * cells];
            int n = w.Config.Size;
            foreach (var lake in w.Water.Lakes)
                foreach (int i in lake.Cells)
                {
                    float x = (i % n + 0.5f) * cm, z = (i / n + 0.5f) * cm;
                    if (!LakeWater(x, z, out _, out float level)) continue;
                    float depth = level - GroundAt(detail, cm, x, z);
                    if (depth < SunkenDepthMinM - 0.5f || depth > SunkenDepthMaxM + 0.5f) continue;
                    int c = Math.Min(cells - 1, (int)(z / step)) * cells + Math.Min(cells - 1, (int)(x / step));
                    (lists[c] ??= new List<int>()).Add(i);
                }
            var all = new int[cells * cells][];
            for (int c = 0; c < all.Length; c++)
            {
                if (lists[c] == null) continue;
                lists[c].Sort();
                all[c] = lists[c].ToArray();
            }
            return all;
        }

        bool SeekShallows(int cell, float fx, float fz, ref float px, ref float pz)
        {
            var list = shallows?[cell];
            if (list == null || list.Length == 0) return false;
            float t = Math.Max(0, Math.Min(0.99999f, fx)) * list.Length, jz = Math.Max(0, Math.Min(0.99999f, fz)), best = float.MinValue;
            int k = (int)t, n = w.Config.Size, i = list[Math.Min(list.Length - 1, k)];
            float jx = t - k;
            for (int a = 0; a < SunkenSeekSteps; a++)
                for (int b = 0; b < SunkenSeekSteps; b++)
                {
                    float x = (i % n + (a + jx) / SunkenSeekSteps) * cm, z = (i / n + (b + jz) / SunkenSeekSteps) * cm, fit = SunkenFit(x, z);
                    if (fit <= best) continue;
                    best = fit; px = x; pz = z;
                }
            return best > float.MinValue;
        }

        public float SunkenFit(float x, float z)
        {
            if (!LakeWater(x, z, out int lake, out float level)) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), depth = level - y, relief = 0;
            if (depth < SunkenDepthMinM || depth > SunkenDepthMaxM || RiverGap(x, z) < SunkenProbeM) return float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * SunkenProbeM, pz = z + uz * SunkenProbeM;
                if (!LakeWater(px, pz, out int pl, out _) || pl != lake) return float.MinValue;
                float g = GroundAt(detail, cm, px, pz);
                if (level - g < SunkenWetM) return float.MinValue;
                relief = Math.Max(relief, Math.Abs(g - y));
            }
            return relief <= SunkenReliefM ? -Math.Abs(depth - SunkenDepthAimM) - 0.2f * relief : float.MinValue;
        }

        public string SunkenFault(ObjectInstance o)
        {
            if (SunkenFit(o.X, o.Z) == float.MinValue) return "is not on steady ground in lake shallows";
            LakeWater(o.X, o.Z, out _, out float level);
            var def = lib.Objects[o.Def];
            float foot = o.Y + def.Placement.Sink * o.Size, top = 0;
            foreach (var p in Structures.Layout(ObjectPart.SunkenShrine, ObjectMeshes.Key(def.Id, 0, o.Variant)).Pieces)
            {
                top = Math.Max(top, (p.Y + p.Sy) * o.Size);
                if (p.Y - p.Base > Structures.GroundY) continue;
                Rotate(o, p.X * o.Size, 0, p.Z * o.Size, out float rx, out _, out float rz);
                float g = GroundAt(detail, cm, o.X + rx, o.Z + rz) - foot;
                if (g < p.Y * o.Size - SunkenFloatM || g > (p.Y + p.Sy) * o.Size - SunkenBuryM) return "has a stone floating or buried";
            }
            if (Structures.SunkenPlinthM * Structures.SunkenU * o.Size > level - foot - SunkenUnderM) return "has its plinth above the water";
            if (top < level - foot + SunkenAboveM) return "has no column standing clear of the water";
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
