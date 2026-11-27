using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float OverlookU = 1 / 8f, OverlookSeatZ = 0.6f;

        static void Overlook(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "overlook");
            float sx = rng.Range(-0.3f, 0.3f);
            Yaw(ref rng, 0.12f, out float c, out float s);
            var seat = f.Child(sx, 0, OverlookSeatZ, c, s);
            if (rng.NextFloat() < 0.5f) LogSeat(l, rng.NextU64(), seat);
            else StoneSeat(l, key, ref rng, seat);
            Touch(l, seat, 0, 0, 0, 0.9f);
            Touch(l, seat, 0, 0, 0.7f, 0.6f);

            float side = rng.NextFloat() < 0.5f ? -1 : 1, cx = sx + side * rng.Range(1.7f, 2.4f), cz = rng.Range(0f, 1.2f), scale = rng.Range(1.5f, 2.1f);
            int d = rng.Range(0, 16);
            Cairn(l, rng.NextU64(), f.Child(cx, -0.02f, cz, Cos16(d), Sin16(d), 0, Tiers - 1, scale));
            Touch(l, f, cx, 0, cz, 0.5f * scale);

            int stones = rng.Range(4, 7), before = l.Pieces.Count;
            float px = sx + rng.Range(-0.15f, 0.15f), pz = OverlookSeatZ - rng.Range(0.8f, 1f), drift = rng.Range(-0.12f, 0.12f);
            for (int i = 0; i < stones; i++)
            {
                int k = rng.Range(0, 16);
                float shrink = 1 - 0.08f * i, hx = px + rng.Range(-0.06f, 0.06f), hz = pz + rng.Range(-0.06f, 0.06f);
                Hewn(l, rng.NextU64(), f, hx, -0.06f, hz, rng.Range(0.36f, 0.5f) * shrink, rng.Range(0.2f, 0.24f), rng.Range(0.28f, 0.4f) * shrink, Cos16(k), Sin16(k), 0, 1, rng.Range(0.7f, 0.85f), rng.Range(-0.04f, 0.04f));
                Touch(l, f, hx, -0.06f, hz, 0.22f);
                if ((i & 1) == 0) Touch(l, f, hx, 0, hz, rng.Range(0.5f, 0.65f));
                px += drift + rng.Range(-0.1f, 0.1f);
                pz -= rng.Range(0.45f, 0.6f);
            }
            Flat(l, before);
            Mossy(l, before);
            Drape(l, before);
        }
    }

    public sealed partial class ObjectField
    {
        public const float OverlookSteadyM = 3f, OverlookReliefM = 1.2f, OverlookEyeM = 1.6f, OverlookDropM = 15f, OverlookNearM = 40f, OverlookHorizon = 0.03f, OverlookOpenFit = 2f, OverlookSeekM = 30f, OverlookEdgeM = 12f, OverlookEdgeDropM = 4f, OverlookEdgeStepM = 1f, OverlookEdgeRiseM = 0.5f, OverlookCreepM = 36f, OverlookCreepStepM = 2f;
        public const int OverlookSeekRings = 3;
        static readonly float[] OverlookReach = { 20f, 80f, 160f, 320f };

        public float OverlookFit(float x, float z, out float vx, out float vz) => OverlookFit(x, z, true, out vx, out vz);

        float OverlookFit(float x, float z, bool edge, out float vx, out float vz)
        {
            vx = 0; vz = 1;
            float y = GroundAt(detail, cm, x, z);
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * OverlookSteadyM, pz = z + uz * OverlookSteadyM;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM || Math.Abs(GroundAt(detail, cm, px, pz) - y) > OverlookReliefM) return float.MinValue;
            }
            float best = float.MinValue;
            int open = 0;
            for (int k = 0; k < 16; k++)
            {
                float ux = Structures.Cos16(k), uz = Structures.Sin16(k), drop = OverlookView(x, z, y, ux, uz, edge);
                if (drop == float.MinValue) continue;
                open++;
                if (drop > best) { best = drop; vx = ux; vz = uz; }
            }
            return best == float.MinValue ? float.MinValue : best + OverlookOpenFit * open;
        }

        public float OverlookView(float x, float z, float y, float ux, float uz) => OverlookView(x, z, y, ux, uz, true);

        float OverlookView(float x, float z, float y, float ux, float uz, bool edge)
        {
            float px = x + ux * OverlookNearM, pz = z + uz * OverlookNearM;
            if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
            float drop = y - GroundAt(detail, cm, px, pz);
            if (drop < OverlookDropM || edge && !OverlookEdge(x, z, y, ux, uz)) return float.MinValue;
            foreach (float r in OverlookReach)
            {
                px = x + ux * r; pz = z + uz * r;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM || GroundAt(detail, cm, px, pz) - y - OverlookEyeM > OverlookHorizon * r) return float.MinValue;
            }
            return drop;
        }

        bool OverlookEdge(float x, float z, float y, float ux, float uz)
        {
            for (float r = OverlookEdgeStepM; r <= OverlookEdgeM; r += OverlookEdgeStepM)
            {
                float px = x + ux * r, pz = z + uz * r;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return false;
                float g = GroundAt(detail, cm, px, pz);
                if (g > y + OverlookEdgeRiseM) return false;
                if (y - g >= OverlookEdgeDropM) return true;
            }
            return false;
        }

        bool SeekOverlook(int d, int variant, float size, ref float x, ref float z, float x0, float z0, float x1, float z1, out float qa, out float qb)
        {
            float best = float.MinValue, bx = x, bz = z, vx = 0, vz = 1;
            for (int ring = 0; ring <= OverlookSeekRings; ring++)
                for (int k = 0; k < (ring == 0 ? 1 : 8); k++)
                {
                    FanDir(k, 0.70710678f, out float ux, out float uz);
                    float px = Math.Min(x1, Math.Max(x0, x + ux * ring * OverlookSeekM)), pz = Math.Min(z1, Math.Max(z0, z + uz * ring * OverlookSeekM));
                    if (OverlookFit(px, pz, false, out float rx, out float rz) == float.MinValue) continue;
                    for (float step = 0; step <= OverlookCreepM; step += OverlookCreepStepM)
                    {
                        float cx = Math.Min(x1, Math.Max(x0, px + rx * step)), cz = Math.Min(z1, Math.Max(z0, pz + rz * step));
                        float fit = OverlookFit(cx, cz, out float wx, out float wz);
                        if (fit <= best) continue;
                        Heading(wx, wz, out float ha, out float hb);
                        if (SiteFits(d, variant, cx, cz, size, ha, hb, out _)) { best = fit; bx = cx; bz = cz; vx = wx; vz = wz; }
                    }
                }
            x = bx; z = bz;
            Heading(vx, vz, out qa, out qb);
            return best > float.MinValue;
        }

        static void Heading(float vx, float vz, out float qa, out float qb)
        {
            qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + vz)));
            qb = (vx < 0 ? -1 : 1) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - vz)));
        }

        public string OverlookFault(ObjectInstance o)
        {
            if (OverlookFit(o.X, o.Z, out _, out _) == float.MinValue) return "stands on no steady open rim";
            Rotate(o, 0, 0, 1, out float bx, out _, out float bz);
            float bl = (float)Math.Sqrt(bx * bx + bz * bz);
            if (OverlookView(o.X, o.Z, GroundAt(detail, cm, o.X, o.Z), bx / bl, bz / bl) == float.MinValue) return "has its bench facing no open view";
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
