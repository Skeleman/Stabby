using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float ShrineU = 1 / 2.6f, WaysideU = 1 / 7f;

        static void RoadShrine(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "road-shrine");
            float shelf, width;
            if (rng.NextFloat() < 0.5f)
            {
                float top = rng.Range(1.3f, 1.42f), hw = rng.Range(0.36f, 0.4f), rise = rng.Range(0.28f, 0.34f), len = rng.Range(0.48f, 0.54f);
                shelf = top - 0.47f; width = 0.4f;
                for (int i = 0; i < 3; i++)
                {
                    int k = rng.Range(0, 16), a = (i * 16 + rng.Range(0, 4)) / 3;
                    float sz = rng.Range(0.18f, 0.26f);
                    Rock(l, rng.NextU64(), f, Cos16(a) * 0.17f, -0.06f, Sin16(a) * 0.17f, sz * 1.2f, sz * 0.7f, sz, Cos16(k), Sin16(k), 0, 1);
                }
                Box(l, key, f, 0, -0.1f, 0, 0.16f, shelf + 0.1f, 0.16f, Timber, GV, 0, 2);
                Box(l, key, f, 0, shelf - 0.05f, 0.02f, 0.5f, 0.05f, 0.38f, Plank, GX, 0, 1);
                Box(l, key, f, 0, shelf - 0.16f, 0.02f, 0.07f, 0.11f, 0.3f, Timber, GZ, 0, 0);
                Box(l, key, f, 0, shelf, -0.16f, 0.5f, top - shelf, 0.04f, Plank, GX, 0, 1);
                for (int sd = -1; sd <= 1; sd += 2)
                    Box(l, key, f, sd * 0.23f, shelf, 0, 0.04f, top - shelf, 0.32f, Plank, GZ, 0, 1);
                Box(l, key, f, 0, shelf - 0.05f, 0, 0.5f, top - shelf + 0.05f, 0.36f, Plank, GX, 2, 2);
                for (int sd = -1; sd <= 1; sd += 2)
                    Box(l, key, f, sd * 0.5f * hw, top - 0.03f, 0.02f, 0.03f, rise, len, Plank, GV, 0, 2, 1, 0, -sd * hw / rise);
                Box(l, key, f, 0, top + rise - 0.06f, 0.02f, 0.06f, 0.06f, len + 0.04f, Timber, GZ, 0, 1);
                Col(l, f, 0, 0, 0.2f, -0.1f, top + rise);
            }
            else
            {
                float jh = rng.Range(0.5f, 0.6f), lean = rng.Range(-0.02f, 0.02f);
                shelf = 0.3f; width = 0.4f;
                Box(l, key, f, 0, -0.1f, 0, 0.8f + rng.Range(-0.04f, 0.04f), 0.4f, 0.6f, Stone, GX, 0, 2);
                for (int sd = -1; sd <= 1; sd += 2)
                    Box(l, key, f, sd * 0.3f, shelf, -0.05f, 0.18f, jh, 0.42f, Stone, GX, 0, 1, 1, 0, lean);
                Box(l, key, f, 0, shelf, -0.21f, 0.44f, jh, 0.12f, Stone, GX, 0, 1);
                Box(l, key, f, 0, shelf + jh, -0.05f, 0.86f, 0.14f, 0.5f, Stone, GX, 0, 1);
                Box(l, key, f, 0, shelf + jh + 0.14f, -0.05f, 0.68f, 0.11f, 0.4f, Stone, GX, 0, 1);
                Box(l, key, f, 0, shelf + jh + 0.25f, -0.05f, 0.4f, 0.1f, 0.26f, Stone, GX, 0, 1);
                Box(l, key, f, 0, shelf, -0.05f, 0.8f, jh + 0.35f, 0.5f, Stone, GX, 2, 2);
                Col(l, f, 0, 0, 0.45f, -0.1f, shelf + jh + 0.35f);
            }
            Offerings(l, key, ref rng, f, shelf, width);
        }

        static void Offerings(StructureLayout l, ulong key, ref Rng rng, in Frame f, float y, float width)
        {
            float x = -0.5f * width + 0.04f, end = 0.5f * width - 0.04f;
            int first = -1, last = -1;
            while (true)
            {
                int kind = rng.Range(0, 4);
                Yaw(ref rng, 0.6f, out float c, out float s);
                float z = rng.Range(-0.04f, 0.06f);
                if (x + (kind == 0 ? 0.04f : kind == 1 ? 0.12f : 0.1f) > end) break;
                if (kind == 0)
                {
                    float h = rng.Range(0.07f, 0.13f);
                    Box(l, key, f, x + 0.02f, y, z, 0.035f, h, 0.035f, Plaster, GV, 0, 0);
                    x += 0.07f;
                }
                else if (kind == 1)
                {
                    Round(l, key, f, x + 0.06f, y, z, 0.12f, 0.04f, 0.12f, Plaster, GV, RoundDisc, 0, 0, c, s);
                    x += 0.15f;
                }
                else if (kind == 2)
                {
                    first = first < 0 ? l.Pieces.Count : first;
                    Box(l, key, f, x + 0.05f, y, z, 0.1f, 0.06f, 0.08f, Cloth, GX, 0, 0, c, s);
                    last = l.Pieces.Count;
                    x += 0.13f;
                }
                else
                {
                    float sz = rng.Range(0.05f, 0.07f);
                    for (int i = 0; i < 3; i++, sz *= 0.75f)
                        Rock(l, rng.NextU64(), f, x + 0.04f, y + 0.035f * i * 1.4f, z, sz * 1.2f, 0.035f * (1 - 0.2f * i), sz, c, s, 0, 0);
                    x += 0.1f;
                }
                x += rng.Range(0.01f, 0.04f);
            }
            ulong dye = Rng.Mix(key ^ 0x51ED270B2F7A4C3DUL);
            for (int i = first; i >= 0 && i < last; i++)
            {
                var p = l.Pieces[i];
                if (p.M != Cloth) continue;
                p.Key = dye;
                l.Pieces[i] = p;
            }
        }

        static void Lantern(StructureLayout l, ulong key, in Frame f)
        {
            Box(l, key, f, 0, -0.2f, 0, 0.1f, 2.15f, 0.1f, Timber, GV, 0, 2);
            Box(l, key, f, 0.22f, 1.82f, 0, 0.5f, 0.07f, 0.07f, Timber, GX, 0, 1);
            Box(l, key, f, 0.1f, 1.6f, 0, 0.035f, 0.26f, 0.035f, Timber, GV, 0, 0, 1, 0, 0.6f);
            Box(l, key, f, 0.4f, 1.62f, 0, 0.012f, 0.2f, 0.012f, Iron, GV, 0, 0);
            Box(l, key, f, 0.4f, 1.4f, 0, 0.13f, 0.18f, 0.13f, Plaster, GV, 0, 1);
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1 : 1, sz = (i & 2) == 0 ? -1 : 1;
                Box(l, key, f, 0.4f + sx * 0.07f, 1.38f, sz * 0.07f, 0.02f, 0.22f, 0.02f, Iron, GV, 0, 0);
            }
            Box(l, key, f, 0.4f, 1.37f, 0, 0.17f, 0.03f, 0.17f, Iron, GX, 0, 0);
            Box(l, key, f, 0.4f, 1.58f, 0, 0.19f, 0.06f, 0.19f, Iron, GV, 0, 1, 1, 0, 0, 0, 0.5f);
            Col(l, f, 0, 0, 0.1f, -0.2f, 1.95f);
        }

        static void StoneSeat(StructureLayout l, ulong key, ref Rng rng, in Frame f)
        {
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Yaw(ref rng, 0.15f, out float c, out float s);
                Box(l, key, f, sd * rng.Range(0.42f, 0.48f), -0.08f, 0, rng.Range(0.24f, 0.3f), 0.42f, rng.Range(0.3f, 0.36f), Stone, GX, 0, 2, c, s);
            }
            Box(l, key, f, rng.Range(-0.03f, 0.03f), 0.34f, 0, rng.Range(1.25f, 1.4f), 0.1f, rng.Range(0.38f, 0.44f), Stone, GX, 0, 2, 1, 0);
            for (int sd = -1; sd <= 1; sd += 2) Col(l, f, sd * 0.4f, 0, 0.3f, 0, 0.44f);
        }

        static void Wayside(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "wayside");
            Yaw(ref rng, 0.06f, out float c, out float s);
            float ox = rng.Range(-0.2f, 0.2f), sd = rng.NextFloat() < 0.5f ? -1 : 1;
            RoadShrine(l, rng.NextU64(), f.Child(ox, 0, -1.0f, c, s));

            Yaw(ref rng, 0.1f, out c, out s);
            var seat = f.Child(sd * rng.Range(1.5f, 1.75f), 0, rng.Range(-0.8f, -0.4f), c, s);
            if (rng.NextFloat() < 0.5f) Bench(l, rng.NextU64(), seat);
            else StoneSeat(l, key, ref rng, seat);

            if (rng.NextFloat() < 0.6f)
                Lantern(l, rng.NextU64(), f.Child(-sd * rng.Range(0.95f, 1.15f), 0, rng.Range(-0.9f, -0.6f), sd, 0));

            float pz = -0.35f;
            int steps = rng.Range(3, 6);
            for (int i = 0; i < steps; i++, pz += rng.Range(0.55f, 0.7f))
            {
                float sz = rng.Range(0.32f, 0.46f);
                int d = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, ox + rng.Range(-0.12f, 0.12f), -0.08f, pz, sz * rng.Range(1.0f, 1.3f), 0.12f, sz, Cos16(d), Sin16(d), 0, 1);
            }

            int stacks = rng.Range(1, 4);
            for (int j = 0; j < stacks; j++)
            {
                float bx = ox + (rng.NextFloat() < 0.5f ? -1 : 1) * rng.Range(0.55f, 0.9f), bz = rng.Range(-1.3f, -0.5f), sz = rng.Range(0.14f, 0.2f);
                int n = rng.Range(2, 5);
                for (int i = 0; i < n; i++, sz *= rng.Range(0.7f, 0.85f))
                {
                    int d = rng.Range(0, 16);
                    Rock(l, rng.NextU64(), f, bx + rng.Range(-0.015f, 0.015f), -0.03f + 0.085f * i, bz, sz * 1.25f, 0.09f, sz, Cos16(d), Sin16(d), 0, i == 0 ? 1 : 0);
                }
            }
        }
    }
}
