using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float HermitU = 1 / 12f, HutU = 1 / 7.5f, HutScale = 0.88f, HutZ = -2.2f;

        static void HermitHut(StructureLayout l, ulong key, in Frame f, out float sc, out float dx)
        {
            var rng = Rng.Stream(key, "hermit-hut");
            float hx = rng.Range(1.9f, 2.2f), hz = rng.Range(1.5f, 1.7f), rr = rng.Range(1.0f, 1.15f), e = 0.7f, verge = rng.Range(0.5f, 0.6f);
            sc = rng.NextFloat() < 0.5f ? -1 : 1;
            bool stone = rng.NextFloat() < 0.5f;
            float y0 = Plate - e * rr, y1 = Plate + hz * rr, ytop = y1 + 0.7f;
            const int nf = 2;
            int door = rng.Range(0, nf);
            dx = -hx + 2 * hx * (door + 0.5f) / nf;

            var front = f.Child(0, 0, hz, 1, 0);
            var back = f.Child(0, 0, -hz, -1, 0);
            var east = f.Child(hx, 0, 0, 0, -1);
            var west = f.Child(-hx, 0, 0, 0, 1);

            var kf = new int[nf];
            var kb = new int[nf];
            for (int i = 0; i < nf; i++)
            {
                kf[i] = i == door ? 1 : rng.NextFloat() < 0.7f ? 2 : 0;
                kb[i] = rng.NextFloat() < 0.25f ? 2 : 0;
            }
            var ke = new[] { !(stone && sc > 0) && rng.NextFloat() < 0.5f ? 2 : 0 };
            var kw = new[] { !(stone && sc < 0) && rng.NextFloat() < 0.5f ? 2 : 0 };

            InnWall(l, key, ref rng, front, 2 * hx, kf);
            InnWall(l, key, ref rng, back, 2 * hx, kb);
            InnWall(l, key, ref rng, east, 2 * hz, ke);
            InnWall(l, key, ref rng, west, 2 * hz, kw);
            Plinth(l, key, ref rng, front, 2 * hx);
            Plinth(l, key, ref rng, back, 2 * hx);
            Plinth(l, key, ref rng, east, 2 * hz);
            Plinth(l, key, ref rng, west, 2 * hz);
            Box(l, key, f, 0, -0.35f, 0, 2 * hx + 0.35f, 0.7f, 2 * hz + 0.35f, Stone, GX, 1, 2);
            Box(l, key, f, 0, 0.35f, 0, 2 * hx + 0.2f, Plate - 0.35f, 2 * hz + 0.2f, Plaster, GX, 2, 2);

            for (int i = 0; i < 4; i++)
            {
                float px = (i & 1) == 0 ? -hx : hx, pz = (i & 2) == 0 ? -hz : hz;
                Box(l, key, f, px, 0.35f, pz, 0.3f, Plate - 0.35f, 0.3f, Timber, GV, 0, 1);
            }

            for (float z = -hz + 0.25f; z < hz - 0.1f; z += 0.2f)
            {
                float cut = rng.Range(-0.6f * hx, 0.6f * hx), w = Math.Min(0.19f, hz - 0.12f - z);
                Box(l, key, f, 0.5f * (-hx + 0.15f + cut), 0.5f, z, cut + hx - 0.15f - 0.01f, 0.05f, w, Plank, GX, 0, 0);
                Box(l, key, f, 0.5f * (hx - 0.15f + cut), 0.5f, z, hx - 0.15f - cut - 0.01f, 0.05f, w, Plank, GX, 0, 0);
            }

            Roof(l, key, ref rng, f, hx, hz, rr, y0, y1, verge, nf);
            Gables(l, key, f, hx, hz, rr, sc);
            if (stone)
            {
                Chimney(l, key, ref rng, f, sc * (hx + 0.45f), y1, ytop);
                Col(l, f, sc * (hx + 0.45f), 0, 0.55f, -0.35f, ytop);
            }
            else
            {
                float px = sc * (hx - 0.7f), pz = -0.45f * hz, py = Plate + (hz + pz) * rr - 0.3f;
                Box(l, key, f, px, Fy, pz, 0.7f, 0.7f, 0.6f, Iron, GV, 0, 1);
                Box(l, key, f, px, Fy + 0.7f, pz, 0.16f, py + 1.1f - Fy - 0.7f, 0.16f, Iron, GV, 0, 1);
                Box(l, key, f, px, py + 1.1f, pz, 0.3f, 0.05f, 0.3f, Iron, GX, 0, 0);
                Box(l, key, f, px, py, pz, 0.36f, 0.12f, 0.36f, Iron, GX, 0, 0);
                Col(l, f, px, pz, 0.4f, -0.35f, Fy + 0.7f);
            }

            var r = f.Child(0, 0, 0, 1, 0, 0, 0);
            float bx = -sc * (hx - 0.55f), bz = -hz + 1.1f;
            Box(l, key, r, bx, Fy, bz, 0.8f, 0.4f, 1.9f, Plank, GZ);
            Box(l, key, r, bx, Fy + 0.4f, bz + 0.1f, 0.72f, 0.1f, 1.6f, Cloth, GZ);
            Box(l, key, r, bx, Fy + 0.4f, bz - 0.75f, 0.5f, 0.14f, 0.3f, Cloth, GX);
            Col(l, f, bx, bz, 0.5f, -0.35f, Fy + 0.5f);
            Yaw(ref rng, 0.3f, out float c, out float s);
            Box(l, key, r, -sc * (hx - 1.6f), Fy, -hz + 0.6f, 0.35f, 0.45f, 0.35f, Timber, GV, 0, 0, c, s);
            Crate(l, rng.NextU64(), r.Child((dx > 0 ? -1 : 1) * (hx - 0.6f), Fy, hz - 0.5f, c, s, 0, 0));

            for (int wl = 0; wl < 4; wl++)
            {
                var wf = wl == 0 ? front : wl == 1 ? back : wl == 2 ? east : west;
                float h = wl < 2 ? hx : hz;
                for (float x = -h; x <= h + 0.01f; x += 0.4f)
                    if (wl != 0 || Math.Abs(x - dx) >= 0.95f) Col(l, wf, x, 0, 0.25f, -0.35f, Plate);
            }
            for (float x = -hx + 0.4f; x < hx; x += 0.8f)
                for (float z = -hz + 0.4f; z < hz; z += 0.8f)
                    Col(l, f, x, z, 0.5f, -0.35f, Fy);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Box(l, key, front, dx + sd * 0.3f, -0.1f, 0.32f, 0.55f, 0.4f, 0.4f, Stone, GX, 0, 1);
                Col(l, front, dx + sd * 0.3f, 0.32f, 0.28f, -0.1f, 0.3f);
            }
            Col(l, front, dx, 0, 0.3f, -0.35f, 0.6f);
        }

        static void LogPile(StructureLayout l, ref Rng rng, in Frame pile)
        {
            float d = rng.Range(0.11f, 0.14f), gap = 0.96f * d;
            int bottom = rng.Range(3, 5), rows = rng.Range(2, 4);
            for (int row = 0; row < rows; row++)
            {
                int cnt = bottom - row;
                if (cnt <= 0) break;
                for (int i = 0; i < cnt; i++)
                {
                    float ld = d * rng.Range(0.82f, 1.12f);
                    Yaw(ref rng, 0.14f, out float c, out float s);
                    float z = (i - 0.5f * (cnt - 1)) * gap + rng.Range(-0.015f, 0.015f), y = row * 0.84f * d - 0.04f * ld + rng.Range(0, 0.01f);
                    Log(l, rng.NextU64(), pile, rng.Range(-0.08f, 0.08f), y, z, rng.Range(0.48f, 0.8f), ld, c, s, Timber, 0, row < 2 ? 1 : 0);
                }
            }
            if (rng.NextFloat() < 0.6f)
            {
                Yaw(ref rng, 0.5f, out float c, out float s);
                Log(l, rng.NextU64(), pile, rng.Range(0.1f, 0.25f), -0.04f * d, (0.5f * bottom + 0.6f) * gap * (rng.NextFloat() < 0.5f ? -1 : 1), rng.Range(0.45f, 0.65f), d, s, c, Timber, 0, 0);
            }
            Col(l, pile, 0, 0, 0.4f, 0, 0.25f);
        }

        static void ChoppingBlock(StructureLayout l, ulong key, ref Rng rng, in Frame f)
        {
            float h = rng.Range(0.42f, 0.52f), w = rng.Range(0.42f, 0.5f);
            Box(l, key, f, 0, -0.03f, 0, w, h, w, Timber, GV, 0, 1);
            Box(l, key, f, 0, h - 0.03f, 0, w - 0.03f, 0.03f, w - 0.03f, Plank, GX, 0, 0);
            float ay = h - 0.09f, lean = rng.Range(0.45f, 0.7f);
            Box(l, key, f, 0.04f, ay, 0, 0.05f, 0.1f, 0.2f, Iron, GX, 0, 0);
            Box(l, key, f, 0.05f + 0.5f * 0.62f * lean, ay + 0.05f, 0, 0.045f, 0.62f, 0.045f, Timber, GV, 0, 0, 1, 0, lean);
            int chips = rng.Range(3, 7);
            for (int i = 0; i < chips; i++)
            {
                int k = rng.Range(0, 16);
                float rd = rng.Range(0.4f, 0.9f), cl = rng.Range(0.18f, 0.32f);
                Box(l, key, f, Cos16(k) * rd, -0.02f, Sin16(k) * rd, cl, rng.Range(0.05f, 0.08f), rng.Range(0.06f, 0.1f), Plank, GX, 0, 0, Cos16(k + 4), Sin16(k + 4));
            }
            Col(l, f, 0, 0, 0.3f, 0, h);
        }

        static void DryingRack(StructureLayout l, ulong key, ref Rng rng, in Frame f)
        {
            float span = rng.Range(1.8f, 2.2f), top = rng.Range(1.6f, 1.8f);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(l, key, f, s * 0.5f * span, -0.2f, 0, 0.09f, top + 0.3f, 0.09f, Timber, GV, 0, 1, 1, 0, rng.Range(-0.03f, 0.03f));
                Col(l, f, s * 0.5f * span, 0, 0.12f, -0.2f, top);
            }
            Box(l, key, f, 0, top, 0, span + 0.3f, 0.07f, 0.07f, Timber, GX, 0, 1);
            int n = rng.Range(2, 4);
            float x = -0.5f * span + 0.15f;
            for (int i = 0; i < n && x < 0.5f * span - 0.3f; i++)
            {
                float hw = rng.Range(0.45f, 0.7f), hh = rng.Range(0.6f, 0.95f);
                if (x + hw > 0.5f * span - 0.1f) break;
                Box(l, key, f, x + 0.5f * hw, top - hh, rng.Range(-0.01f, 0.01f), hw, hh, 0.025f, Cloth, GX, 0, 1, 1, 0, rng.Range(-0.06f, 0.06f));
                x += hw + rng.Range(0.05f, 0.15f);
            }
            int herbs = rng.Range(2, 5);
            for (int i = 0; i < herbs; i++)
            {
                float hx = rng.Range(-0.45f, 0.45f) * span, hl = rng.Range(0.2f, 0.32f);
                Box(l, key, f, hx, top - 0.02f - hl, 0.05f, 0.1f, hl, 0.1f, Thatch, GV, 0, 0, 1, 0, 0, 0, 0.3f);
            }
        }

        static void Garden(StructureLayout l, ulong key, ref Rng rng, in Frame f, float gw, float gd)
        {
            float hw = 0.5f * gw, hd = 0.5f * gd, gx = rng.Range(-0.3f * hw, 0.3f * hw);
            var post = f.Child(0, 0, 0, 1, 0, 0, 2, 0.75f);
            float k = 1 / 0.75f;
            Fence(l, key, ref rng, post, -hw * k, hd * k, -hw * k, -hd * k);
            Fence(l, key, ref rng, post, -hw * k, -hd * k, hw * k, -hd * k);
            Fence(l, key, ref rng, post, hw * k, -hd * k, hw * k, hd * k);
            Fence(l, key, ref rng, post, hw * k, hd * k, (gx + 0.5f) * k, hd * k);
            Fence(l, key, ref rng, post, (gx - 0.5f) * k, hd * k, -hw * k, hd * k);
            int rows = Math.Max(2, (int)((gw - 0.6f) / 0.75f));
            float bl = gd - 0.9f;
            for (int i = 0; i < rows; i++)
            {
                float x = -hw + 0.45f + (gw - 0.9f) * (i + 0.5f) / rows, z = -0.1f;
                for (int s = -1; s <= 1; s += 2)
                {
                    Box(l, key, f, x + s * 0.24f, -0.1f, z, 0.06f, 0.32f + rng.Range(-0.03f, 0.03f), bl, Plank, GZ, 0, 1);
                    Box(l, key, f, x, -0.1f, z + s * 0.5f * (bl + 0.06f), 0.54f, 0.32f + rng.Range(-0.03f, 0.03f), 0.06f, Plank, GX, 0, 1);
                }
                bool poles = rng.NextFloat() < 0.35f;
                if (poles)
                {
                    for (float pz = z - 0.5f * bl + 0.35f; pz < z + 0.5f * bl - 0.2f; pz += 0.7f)
                        for (int s = -1; s <= 1; s += 2)
                            Box(l, key, f, x + s * 0.12f, 0, pz, 0.025f, 1.3f, 0.025f, Timber, GV, 0, 0, 1, 0, -s * 0.18f);
                }
                else
                {
                    for (float pz = z - 0.5f * bl + 0.2f; pz < z + 0.5f * bl - 0.1f; pz += rng.Range(0.25f, 0.4f))
                    {
                        float sz = rng.Range(0.12f, 0.2f);
                        int d = rng.Range(0, 16);
                        Box(l, key, f, x + rng.Range(-0.05f, 0.05f), 0.01f, pz, sz, sz * rng.Range(0.6f, 0.9f), sz, Thatch, GV, 0, 0, Cos16(d), Sin16(d), 0, 0, 0.3f);
                    }
                }
            }
        }

        static void Hermitage(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "hermitage");
            Yaw(ref rng, 0.08f, out float c, out float s);
            float ox = rng.Range(-0.3f, 0.3f);
            var hut = f.Child(ox, 0, HutZ, c, s, 0, 2, HutScale);
            HermitHut(l, rng.NextU64(), hut, out float sc, out float dx);
            float gable = HutScale * 2.2f, front = HutZ + HutScale * 1.7f, ds = ox + dx * HutScale;

            LogPile(l, ref rng, hut.Child(-sc * (2.2f + 0.65f), 0, rng.Range(0.2f, 0.5f), 0, 1, 0, 2, 1.6f));
            if (rng.NextFloat() < 0.6f)
                LogPile(l, ref rng, hut.Child(-sc * (2.2f + 0.65f), 0, -1.15f + rng.Range(-0.05f, 0.05f), 0, 1, 0, 2, 1.4f));
            ChoppingBlock(l, rng.NextU64(), ref rng, f.Child(ox - sc * (gable + rng.Range(1.0f, 1.4f)), 0, front + rng.Range(0.4f, 0.9f)));

            Yaw(ref rng, 0.15f, out c, out s);
            DryingRack(l, rng.NextU64(), ref rng, f.Child(-sc * rng.Range(3.3f, 3.8f), 0, rng.Range(2.4f, 3.0f), c, s));

            if (rng.NextFloat() < 0.75f)
            {
                Yaw(ref rng, 0.06f, out c, out s);
                Garden(l, rng.NextU64(), ref rng, f.Child(sc * rng.Range(2.8f, 3.1f), 0, rng.Range(2.4f, 2.8f), c, s), rng.Range(2.6f, 3.2f), rng.Range(2.4f, 2.9f));
            }
            else
            {
                Barrel(l, rng.NextU64(), f.Child(sc * 3.0f, 0, 2.0f));
                Yaw(ref rng, 0.5f, out c, out s);
                Crate(l, rng.NextU64(), f.Child(sc * 3.6f, 0, 2.6f, c, s));
            }

            Barrel(l, rng.NextU64(), f.Child(ox + sc * (gable + 0.35f), 0, front - 0.1f));
            Yaw(ref rng, 0.12f, out c, out s);
            LogSeat(l, rng.NextU64(), f.Child(ds + (ds > 0 ? -1 : 1) * rng.Range(1.3f, 1.5f), 0, front + 0.75f, c, s));

            if (rng.NextFloat() < 0.5f)
                FirePit(l, rng.NextU64(), f.Child((ds > 0 ? -1 : 1) * rng.Range(0.9f, 1.3f), 0, rng.Range(3.6f, 4.0f), 1, 0, 0, 2, 0.75f));

            float pz = front + 0.75f;
            int steps = rng.Range(3, 5);
            for (int i = 0; i < steps; i++, pz += rng.Range(0.6f, 0.75f))
            {
                float sz = rng.Range(0.35f, 0.5f);
                int d = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, ds + rng.Range(-0.12f, 0.12f), -0.08f, pz, sz * rng.Range(1.0f, 1.3f), 0.12f, sz, Cos16(d), Sin16(d), 0, 1);
            }
        }
    }
}
