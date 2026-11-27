using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float SaltU = 1 / 12f, SaltBaseM = -0.3f;

        static float SaltSpire(StructureLayout l, ref Rng rng, in Frame f, float x, float z, float w0, float tall, int bands)
        {
            float y = SaltBaseM, lx = rng.Range(-0.05f, 0.05f), lz = rng.Range(-0.05f, 0.05f), px = x, pz = z, step = (tall - SaltBaseM) / bands;
            for (int b = 0; b < bands; b++)
            {
                float t = (float)b / bands;
                bool hard = ((b + bands) & 1) == 1;
                float h = step * (hard ? rng.Range(0.55f, 0.75f) : rng.Range(1.25f, 1.45f));
                float wb = w0 * (1 - 0.55f * t) * (hard ? rng.Range(1.08f, 1.2f) : rng.Range(0.8f, 0.92f));
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, px, y, pz, wb, h, wb * rng.Range(0.72f, 0.94f), Cos16(q), Sin16(q), 0, 2, rng.Range(0.82f, 0.94f), rng.Range(-0.04f, 0.04f), rng.Range(-0.04f, 0.04f), hard ? -0.1f : 0.12f);
                y += h;
                px += lx * h + rng.Range(-0.05f, 0.05f);
                pz += lz * h + rng.Range(-0.05f, 0.05f);
            }
            float cap = w0 * 0.45f * rng.Range(1.1f, 1.35f), ch = rng.Range(0.28f, 0.42f);
            int qc = rng.Range(0, 16);
            Hewn(l, rng.NextU64(), f, px, y, pz, cap, ch, cap * rng.Range(0.75f, 0.95f), Cos16(qc), Sin16(qc), 0, 2, rng.Range(0.35f, 0.5f), rng.Range(-0.05f, 0.05f), rng.Range(-0.05f, 0.05f), -0.12f);
            y += ch;
            if (rng.NextFloat() < 0.5f)
            {
                float k = cap * rng.Range(0.35f, 0.5f), kh = rng.Range(0.4f, 0.8f);
                Hewn(l, rng.NextU64(), f, px + rng.Range(-0.15f, 0.15f) * cap, y, pz + rng.Range(-0.15f, 0.15f) * cap, k, kh, k, 1, 0, 0, 1, 0.4f, rng.Range(-0.1f, 0.1f), rng.Range(-0.1f, 0.1f), 0.3f);
                y += kh;
            }
            Col(l, f, 0.5f * (x + px), 0.5f * (z + pz), 0.36f * w0, SaltBaseM, y);
            Touch(l, f, x, 0, z, 0.7f * w0);
            return y;
        }

        static void SaltTower(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "salt-tower");
            int d0 = rng.Range(0, 16);
            var f = root.Child(rng.Range(-0.2f, 0.2f), 0, rng.Range(-0.2f, 0.2f), Cos16(d0), Sin16(d0));
            float w0 = rng.Range(2.3f, 2.9f), tall = rng.Range(6.5f, 8.8f);

            int before = l.Pieces.Count, plates = rng.Range(7, 11);
            for (int k = 0; k < plates; k++)
            {
                int d = rng.Range(0, 16);
                float r = rng.Range(0.9f, 3.6f), x = Cos16(d) * r, z = Sin16(d) * r, w = rng.Range(0.9f, 1.7f);
                Hewn(l, rng.NextU64(), f, x, -0.14f, z, w, rng.Range(0.18f, 0.26f), w * rng.Range(0.55f, 0.85f), Cos16(d + 5), Sin16(d + 5), 0, 1, rng.Range(0.45f, 0.65f), rng.Range(-0.03f, 0.03f), rng.Range(-0.03f, 0.03f));
                Touch(l, f, x, 0, z, 0.6f * w);
            }
            Flat(l, before);
            Drape(l, before);

            SaltSpire(l, ref rng, f, 0, 0, w0, tall, rng.Range(7, 11));
            if (rng.NextFloat() < 0.6f)
            {
                int d = rng.Range(0, 16);
                float r = w0 * rng.Range(0.6f, 0.75f);
                SaltSpire(l, ref rng, f, Cos16(d) * r, Sin16(d) * r, w0 * rng.Range(0.4f, 0.5f), tall * rng.Range(0.35f, 0.55f), rng.Range(3, 6));
            }

            int fallen = l.Pieces.Count, chunks = rng.Range(3, 7);
            for (int k = 0; k < chunks; k++)
            {
                int d = rng.Range(0, 16);
                float r = 0.6f * w0 + rng.Range(0.8f, 2.6f), x = Cos16(d) * r, z = Sin16(d) * r, s = rng.Range(0.45f, 1.1f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x, -0.22f, z, s, s * rng.Range(0.5f, 0.8f), s * rng.Range(0.6f, 0.9f), Cos16(q), Sin16(q), 0, 1, rng.Range(0.4f, 0.6f), rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f), rng.Range(0f, 0.15f));
                Touch(l, f, x, 0, z, 0.6f * s + 0.2f);
            }
            Drape(l, fallen);
        }
    }

    public sealed partial class ObjectField
    {
        public const float SaltDryMm = 650f, SaltPanM = 12f, SaltPanReliefM = 1.5f, SaltBasinM = 150f, SaltRimM = 1f, SaltHollowM = 1f, SaltHollowCapM = 10f, SaltRiverM = 60f, SaltSeekM = 80f;
        public const int SaltRims = 5, SaltSeekRings = 5;

        public float SaltFit(float x, float z)
        {
            if (x < SaltBasinM || z < SaltBasinM || x >= worldM - SaltBasinM || z >= worldM - SaltBasinM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0 || w.Climate == null || w.Climate.PrecipitationMm[i] > SaltDryMm || RiverGap(x, z) < SaltRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), relief = 0, hollow = 0;
            int rims = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                relief = Math.Max(relief, Math.Abs(GroundAt(detail, cm, x + ux * SaltPanM, z + uz * SaltPanM) - y));
                if (relief > SaltPanReliefM) return float.MinValue;
                float g = GroundAt(detail, cm, x + ux * SaltBasinM, z + uz * SaltBasinM) - y;
                hollow += 0.125f * g;
                if (g > SaltRimM) rims++;
            }
            return rims >= SaltRims && hollow >= SaltHollowM ? Math.Min(hollow, SaltHollowCapM) - 2 * relief : float.MinValue;
        }

        bool SeekSalt(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = float.MinValue, bx = x, bz = z;
            for (int ring = 0; ring <= SaltSeekRings; ring++)
                for (int k = 0; k < (ring == 0 ? 1 : 8); k++)
                {
                    FanDir(k, 0.70710678f, out float ux, out float uz);
                    float px = Math.Min(x1, Math.Max(x0, x + ux * ring * SaltSeekM)), pz = Math.Min(z1, Math.Max(z0, z + uz * ring * SaltSeekM));
                    float fit = SaltFit(px, pz);
                    if (fit > best) { best = fit; bx = px; bz = pz; }
                }
            x = bx; z = bz;
            return best > float.MinValue;
        }

        public string SaltFault(ObjectInstance o)
        {
            if (SaltFit(o.X, o.Z) == float.MinValue) return "is not on a dry flat basin floor";
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
