using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float FumaroleU = 1 / 20f, FumaroleSpreadM = 7.5f, FumaroleVentGapM = 3.4f;
        public const int FumaroleVentStride = 4;

        public static void FumaroleVents(ulong key, List<float> list)
        {
            list.Clear();
            var rng = Rng.Stream(key, "fumarole-vents");
            int want = rng.Range(4, 7);
            for (int t = 0; t < 40 && list.Count < want * FumaroleVentStride; t++)
            {
                int d = rng.Range(0, 16);
                float r = (list.Count == 0 ? 0f : 1f) * rng.Range(1.2f, FumaroleSpreadM), x = Cos16(d) * r, z = Sin16(d) * r;
                bool apart = true;
                for (int k = 0; k < list.Count; k += FumaroleVentStride)
                    if ((list[k] - x) * (list[k] - x) + (list[k + 1] - z) * (list[k + 1] - z) < FumaroleVentGapM * FumaroleVentGapM) apart = false;
                if (!apart) continue;
                list.Add(x); list.Add(z); list.Add(rng.Range(0.8f, 1.4f)); list.Add(rng.Range(0.4f, 0.75f));
            }
        }

        static void Fumarole(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "fumarole");
            var vents = new List<float>();
            FumaroleVents(key, vents);

            int before = l.Pieces.Count, plates = rng.Range(16, 24);
            for (int k = 0; k < plates; k++)
            {
                int d = rng.Range(0, 16);
                float r = FumaroleSpreadM * 1.15f * (float)Math.Sqrt(rng.NextFloat()), x = Cos16(d) * r, z = Sin16(d) * r, w = rng.Range(1.8f, 3.4f);
                Hewn(l, rng.NextU64(), f, x, -0.05f, z, w, rng.Range(0.12f, 0.2f), w * rng.Range(0.55f, 0.85f), Cos16(d + 5), Sin16(d + 5), 0, 1, rng.Range(0.45f, 0.65f), rng.Range(-0.02f, 0.02f), rng.Range(-0.02f, 0.02f));
            }
            for (int k = 0; k < vents.Count; k += FumaroleVentStride)
            {
                float w = 3.2f * vents[k + 2];
                Hewn(l, rng.NextU64(), f, vents[k], -0.04f, vents[k + 1], w, 0.16f, w * rng.Range(0.8f, 0.95f), 1, 0, 0, 1, 0.6f);
            }
            Flat(l, before);
            Drape(l, before);

            int mounds = l.Pieces.Count;
            for (int k = 0; k < vents.Count; k += FumaroleVentStride)
            {
                float vx = vents[k], vz = vents[k + 1], vr = vents[k + 2], vh = vents[k + 3];
                int lumps = rng.Range(3, 5), a0 = rng.Range(0, 16);
                for (int m = 0; m < lumps; m++)
                {
                    int d = a0 + m * 16 / lumps + rng.Range(-1, 2);
                    float ring = 0.55f * vr, x = vx + Cos16(d) * ring, z = vz + Sin16(d) * ring, s = vr * rng.Range(0.75f, 1.05f), h = vh * rng.Range(0.7f, 1.05f);
                    int q = rng.Range(0, 16);
                    Hewn(l, rng.NextU64(), f, x, -0.22f, z, s, h + 0.22f, s * rng.Range(0.7f, 0.95f), Cos16(q), Sin16(q), 0, 2, rng.Range(0.4f, 0.55f), Cos16(d) * 0.12f, Sin16(d) * 0.12f, 0.15f);
                    Touch(l, f, x, 0, z, 0.45f * s);
                    Col(l, f, x, z, 0.4f * s, -0.2f, h);
                }
                int rocks = rng.Range(2, 5);
                for (int m = 0; m < rocks; m++)
                {
                    int d = rng.Range(0, 16);
                    float ring = vr * rng.Range(1.5f, 2.2f), x = vx + Cos16(d) * ring, z = vz + Sin16(d) * ring, s = rng.Range(0.3f, 0.65f);
                    int q = rng.Range(0, 16);
                    Hewn(l, rng.NextU64(), f, x, -0.15f, z, s, s * rng.Range(0.5f, 0.8f), s * rng.Range(0.6f, 0.9f), Cos16(q), Sin16(q), 0, 1, rng.Range(0.4f, 0.6f), rng.Range(-0.15f, 0.15f), rng.Range(-0.15f, 0.15f), rng.Range(0f, 0.12f));
                    Touch(l, f, x, 0, z, 0.45f * s);
                }
            }
            Drape(l, mounds);
        }
    }

    public sealed partial class ObjectField
    {
        public const float FumaroleProbeM = 10f, FumaroleReliefM = 1.5f, FumaroleRiverM = 40f, FumaroleHeatMin = 0.1f, FumaroleSteamM = 3f;
        public const int FumaroleSeekTries = 8;

        public float FumaroleFit(float x, float z)
        {
            float heat = Hotspots.Heat(x, z);
            if (!(heat >= FumaroleHeatMin) || RiverGap(x, z) < FumaroleRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), relief = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * FumaroleProbeM, pz = z + uz * FumaroleProbeM;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
                relief = Math.Max(relief, Math.Abs(GroundAt(detail, cm, px, pz) - y));
            }
            return relief <= FumaroleReliefM ? heat - 0.05f * relief : float.MinValue;
        }

        bool SeekFumarole(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = float.MinValue, bx = x, bz = z;
            var rng = new Rng(Rng.Mix((ulong)BitConverter.SingleToInt32Bits(x) ^ ((ulong)BitConverter.SingleToInt32Bits(z) << 32)));
            for (int h = 0; h < Hotspots.Count; h++)
            {
                if (Hotspots.X[h] < x0 || Hotspots.X[h] >= x1 || Hotspots.Z[h] < z0 || Hotspots.Z[h] >= z1) continue;
                for (int t = 0; t < FumaroleSeekTries; t++)
                {
                    FanDir(rng.Range(0, 8), 0.70710678f, out float ux, out float uz);
                    float r = Hotspots.R[h] * rng.Range(0.15f, 0.8f), px = Hotspots.X[h] + ux * r, pz = Hotspots.Z[h] + uz * r;
                    if (px < x0 || px >= x1 || pz < z0 || pz >= z1) continue;
                    float fit = FumaroleFit(px, pz);
                    if (fit > best) { best = fit; bx = px; bz = pz; }
                }
            }
            x = bx; z = bz;
            return best > float.MinValue;
        }

        public void FumaroleVentsAt(ObjectInstance o, List<float> world)
        {
            world.Clear();
            var local = new List<float>();
            Structures.FumaroleVents(ObjectMeshes.Key(lib.Objects[o.Def].Id, 0, o.Variant), local);
            float u = Structures.FumaroleU * o.Size;
            for (int k = 0; k < local.Count; k += Structures.FumaroleVentStride)
            {
                Rotate(o, local[k] * u, 0, local[k + 1] * u, out float rx, out _, out float rz);
                float x = o.X + rx, z = o.Z + rz;
                world.Add(x); world.Add(GroundAt(detail, cm, x, z) + 0.6f * local[k + 3] * u); world.Add(z); world.Add(FumaroleSteamM);
            }
        }

        public string FumaroleFault(ObjectInstance o)
        {
            if (FumaroleFit(o.X, o.Z) == float.MinValue) return "is off geothermal ground or on unsteady ground";
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
