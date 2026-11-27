using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float FairyU = 1 / 14f, MushroomU = 1 / 0.3f;

        static void Arc(float t, out float c, out float s)
        {
            float k = (t - (float)Math.Floor(t)) * 16;
            int h = (int)k;
            float a = k - h;
            c = Cos16(h) * (1 - a) + Cos16(h + 1) * a;
            s = Sin16(h) * (1 - a) + Sin16(h + 1) * a;
            float inv = 1 / (float)Math.Sqrt(c * c + s * s);
            c *= inv; s *= inv;
        }

        static float Mushroom(StructureLayout l, ulong key, in Frame f, ulong dye)
        {
            var rng = Rng.Stream(key, "mushroom");
            int first = l.Pieces.Count, n = rng.Range(1, 4);
            float tall = 0;
            for (int i = 0; i < n; i++)
            {
                float sc = i == 0 ? 1 : rng.Range(0.45f, 0.8f), h = rng.Range(0.1f, 0.16f) * sc, cd = h * rng.Range(0.75f, 1.05f), ch = cd * rng.Range(0.42f, 0.6f), d = cd * rng.Range(0.2f, 0.28f);
                int k = rng.Range(0, 16);
                float rd = i == 0 ? 0 : rng.Range(0.05f, 0.1f), x = Cos16(k) * rd, z = Sin16(k) * rd;
                Round(l, rng.NextU64(), f, x, -0.01f, z, d, h + 0.01f, d, Fungus, GV, RoundStem, 0, 1);
                Touch(l, f, x, -0.01f, z, 0.6f * d);
                Round(l, rng.NextU64(), f, x, h - 0.22f * ch, z, cd, ch, cd, Fungus, GV, RoundDome, 0, 2);
                tall = Math.Max(tall, h + 0.78f * ch);
            }
            for (int i = first; i < l.Pieces.Count; i++)
            {
                var p = l.Pieces[i];
                p.Key = dye;
                l.Pieces[i] = p;
            }
            return tall;
        }

        static void FairyRing(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "fairy-ring");
            var f = root.Child(rng.Range(-0.3f, 0.3f), 0, rng.Range(-0.3f, 0.3f));
            float r = rng.Range(2.2f, 3.2f), t0 = rng.NextFloat();
            ulong dye = Rng.Mix(key ^ 0x3C6EF372FE94F82BUL);

            Band(l, f, 0, 0, r, rng.Range(1.2f, 1.5f));

            int clumps = rng.Range(24, 33), skip = rng.Range(0, clumps);
            float gap = rng.Range(1.5f, 3.5f);
            for (int i = 0; i < clumps; i++)
            {
                if (Math.Abs(i - skip) < gap) continue;
                Arc(t0 + (i + rng.Range(-0.35f, 0.35f)) / clumps, out float c, out float s);
                float rr = r + rng.Range(-0.15f, 0.15f);
                int d = rng.Range(0, 16);
                Mushroom(l, rng.NextU64(), f.Child(c * rr, 0, s * rr, Cos16(d), Sin16(d), 0, 2, rng.Range(2.4f, 3.4f)), dye);
            }

            Arc(t0 + (skip + rng.Range(-1f, 1f)) / clumps, out float bc, out float bs);
            float br = r * rng.Range(0.92f, 1.08f), big = rng.Range(5f, 6.5f);
            float tall = Mushroom(l, rng.NextU64(), f.Child(bc * br, 0, bs * br, 1, 0, 0, 2, big), Rng.Mix(key ^ 0xA54FF53A5F1D36F1UL));
            Col(l, f, bc * br, bs * br, 0.04f * big, 0, tall * big);

            Arc(t0 + rng.NextFloat(), out float wc, out float ws);
            int stones = rng.Range(3, 6), before = l.Pieces.Count;
            float sr = r + rng.Range(0.45f, 0.6f), drift = rng.Range(-0.08f, 0.08f);
            for (int i = 0; i < stones; i++)
            {
                float px = wc * sr - ws * drift * i * i, pz = ws * sr + wc * drift * i * i;
                int d = rng.Range(0, 16);
                float hx = px + rng.Range(-0.05f, 0.05f), hz = pz + rng.Range(-0.05f, 0.05f);
                Hewn(l, rng.NextU64(), f, hx, -0.06f, hz, rng.Range(0.36f, 0.5f), rng.Range(0.22f, 0.26f), rng.Range(0.28f, 0.4f), Cos16(d), Sin16(d), 0, 1, rng.Range(0.7f, 0.85f), rng.Range(-0.04f, 0.04f));
                Touch(l, f, hx, -0.06f, hz, 0.22f);
                sr += rng.Range(0.5f, 0.66f);
            }
            Flat(l, before);
            Mossy(l, before);
            Drape(l, before);
        }
    }
}
