using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        static void Scorch(StructureLayout l, int before)
        {
            for (int k = before; k < l.Pieces.Count; k++)
            {
                var p = l.Pieces[k];
                p.M = p.M == Timber || p.M == Plank || p.M == Cloth ? Char : p.M == Ember ? Ash : p.M;
                l.Pieces[k] = p;
            }
        }

        static void BurntCamp(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "burnt-camp");
            int ground = l.Pieces.Count;
            int off = rng.Range(0, 16);
            Round(l, rng.NextU64(), f, 0, -0.006f, 0, rng.Range(9.8f, 10.8f), 0.012f, rng.Range(9.8f, 10.8f), Char, GV, RoundDisc, 0, 2, Cos16(off), Sin16(off));
            Round(l, rng.NextU64(), f, rng.Range(-0.3f, 0.3f), -0.003f, rng.Range(-0.3f, 0.3f), rng.Range(6.2f, 7.2f), 0.012f, rng.Range(6.2f, 7.2f), Char, GV, RoundDisc, 0, 2, Cos16(off + 5), Sin16(off + 5));
            int patches = rng.Range(3, 6);
            for (int i = 0; i < patches; i++)
            {
                int k = rng.Range(0, 16);
                float r = rng.Range(2.4f, 4.4f), d = rng.Range(0.9f, 2.2f);
                Round(l, rng.NextU64(), f, Cos16(k) * r, 0, Sin16(k) * r, d, 0.012f, d * rng.Range(0.6f, 0.9f), Char, GV, RoundDisc, 0, 1, Cos16(k + 3), Sin16(k + 3));
            }
            Drape(l, ground);

            int pit = l.Pieces.Count, lights = l.Lights.Count;
            FirePit(l, rng.NextU64(), f);
            l.Lights.RemoveRange(lights, l.Lights.Count - lights);
            Scorch(l, pit);

            int k0 = rng.Range(0, 16);
            float dx = Cos16(k0), dz = Sin16(k0), td = rng.Range(2.7f, 3.0f);
            var tent = f.Child(dx * td, 0, dz * td, -dz, dx);
            int frame = l.Pieces.Count;
            float len = rng.Range(2.2f, 2.6f), side = rng.NextFloat() < 0.5f ? -1 : 1;
            Yaw(ref rng, 0.3f, out float rc, out float rs);
            Log(l, rng.NextU64(), tent, 0, 0.035f, -0.1f * side, len, 0.07f, rc, rs, Char, 0, 1);
            Yaw(ref rng, 0.35f, out float sc, out float ss);
            Log(l, rng.NextU64(), tent, 0.2f * side, 0.035f, 0.6f * side, 1.7f, 0.055f, sc, ss, Char, 0, 0);
            int poles = rng.Range(2, 4);
            for (int i = 0; i < poles; i++)
            {
                float e = i == 0 ? -1 : 1, h = rng.Range(0.85f, 1.45f);
                Yaw(ref rng, 0.5f, out float c, out float s);
                Box(l, rng.NextU64(), tent, e * rng.Range(0.5f, 0.7f) * len * 0.5f, 0, rng.Range(-0.5f, 0.5f), 0.06f, h, 0.06f, Char, GV, 0, 1, c, s, rng.Range(0.45f, 0.9f) * e, rng.Range(-0.3f, 0.3f), -0.25f);
                Box(l, key, tent, e * 0.5f * len * 0.6f, 0, 0, 0.09f, 0.12f, 0.09f, Char, GV, 0, 0);
            }
            int scraps = rng.Range(3, 6);
            for (int i = 0; i < scraps; i++)
            {
                Yaw(ref rng, 0.9f, out float c, out float s);
                Box(l, rng.NextU64(), tent, rng.Range(-1.3f, 1.3f), 0.004f * i, rng.Range(-1.1f, 1.1f), rng.Range(0.25f, 0.6f), 0.008f, rng.Range(0.2f, 0.45f), Char, GV, 0, 0, c, s);
            }
            Scorch(l, frame);
            Drape(l, frame + 2 + 2 * poles);
            Col(l, tent, 0, 0, 1f, 0, 0.3f);

            int seats = rng.Range(1, 3);
            for (int i = 0; i < seats; i++)
            {
                int k = k0 + 6 + 4 * i + rng.Range(-1, 2);
                float sx = Cos16(k), sz = Sin16(k), sr = rng.Range(1.5f, 1.7f);
                var sf = f.Child(sx * sr, 0, sz * sr, -sz, sx);
                int seat = l.Pieces.Count;
                LogSeat(l, rng.NextU64(), sf);
                var main = l.Pieces[seat];
                float ml = main.Sx / sf.U, md = main.Sy / sf.U, e = rng.NextFloat() < 0.5f ? -1 : 1;
                if (i == 0)
                {
                    Log(l, rng.NextU64(), sf, e * 0.3f * ml, -0.04f * md, 0, 0.4f * ml, md * 1.04f, 1, 0, Char, 0, 2);
                }
                else Scorch(l, seat);
            }

            int gear = l.Pieces.Count;
            int gk = k0 + (rng.NextFloat() < 0.5f ? 3 : -3);
            float gr = rng.Range(1.8f, 2.4f);
            Yaw(ref rng, 0.9f, out float pc, out float ps);
            Round(l, rng.NextU64(), f, Cos16(gk) * gr, 0.15f, Sin16(gk) * gr, 0.32f, 0.3f, 0.3f, Iron, GV, RoundTube, 0, 1, pc, ps);
            Round(l, rng.NextU64(), f, Cos16(gk) * gr + 0.45f * pc, 0.025f, Sin16(gk) * gr + 0.45f * ps, 0.3f, 0.05f, 0.3f, Iron, GV, RoundDome, 0, 0, pc, ps);
            Yaw(ref rng, 0.5f, out float cc, out float cs);
            int ck = k0 - 4 - rng.Range(0, 3);
            int crate = l.Pieces.Count;
            Crate(l, rng.NextU64(), f.Child(Cos16(ck) * rng.Range(2.6f, 3.4f), 0, Sin16(ck) * rng.Range(2.6f, 3.4f), cc, cs));
            Scorch(l, crate);

            int snags = rng.Range(2, 5);
            int sk0 = rng.Range(0, 16);
            for (int i = 0; i < snags; i++)
            {
                int k = sk0 + i * 16 / snags + rng.Range(0, 2);
                float r = rng.Range(4.0f, 5.2f), x = Cos16(k) * r, z = Sin16(k) * r;
                bool tall = rng.NextFloat() < 0.5f;
                float d = tall ? rng.Range(0.2f, 0.3f) : rng.Range(0.34f, 0.5f), h = tall ? rng.Range(1.6f, 2.6f) : rng.Range(0.3f, 0.6f);
                Round(l, rng.NextU64(), f, x, -0.1f, z, d, h + 0.1f, d, Char, GV, RoundStem, 0, 1);
                Touch(l, f, x, 0, z, 0.5f * d);
                Col(l, f, x, z, 0.45f * d, 0, h);
            }
        }
    }

    public sealed partial class ObjectField
    {
        public string BurntCampFault(ObjectInstance o)
        {
            float woods = Woods(o.X, o.Z);
            if (woods < BurntWoodsMin || woods > BurntWoodsMax) return "is not in open or dry woods";
            if (RiverGap(o.X, o.Z) < BurntRiverM) return "stands by a river";
            return null;
        }
    }
}
