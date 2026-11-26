using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float SiteInnZ = -0.12f, SiteInnScale = 0.63f;
        public const float InnU = 1 / 12f, Fy = 0.55f;
        const float YardU = 1 / 19f, Plate = 3.3f;

        static readonly float[] InnSeat = { 0, 0, 0.4f, 0.25f, -0.4f, 0.25f, 0.4f, -0.25f, -0.4f, -0.25f };
        static readonly float[] YardSeat = { 0, 0, 0.35f, 0.35f, -0.35f, 0.35f, 0.35f, -0.35f, -0.35f, -0.35f };
        static readonly float[] WaysideSeats = { 0, 0, 0.24f, -0.09f, -0.24f, -0.09f, 0, -0.16f, 0.1f, -0.16f, -0.1f, -0.16f, 0, 0.15f, 0, 0.3f };
        static readonly float[] KitSeats = { 0, 0, 0.3f, 0.2f, -0.3f, 0.2f, 0.3f, -0.2f, -0.3f, -0.2f };

        static float[] KitSeat(int shape) => shape == ObjectPart.Inn ? InnSeat : shape == ObjectPart.InnYard ? YardSeat : shape == ObjectPart.StoneRing ? RingSeat : shape == ObjectPart.Campsite || shape == ObjectPart.Hermitage || shape == ObjectPart.Ruin || shape == ObjectPart.FairyRing || shape == ObjectPart.WolfDen ? YardSeat : shape == ObjectPart.Wayside ? WaysideSeats : shape == ObjectPart.HotSpring ? SpringSeats : shape == ObjectPart.Dungeon ? DungeonSeats : shape == ObjectPart.Geyser || shape == ObjectPart.MudPot ? VentSeats : KitSeats;

        static void Built(int shape, ulong key, StructureLayout l)
        {
            switch (shape)
            {
                case ObjectPart.Inn: Inn(l, key, Frame.Root(InnU)); break;
                case ObjectPart.InnYard: Yard(l, key, Frame.Root(YardU)); break;
                case ObjectPart.Table: Table(l, key, Frame.Root(1 / 1.6f)); break;
                case ObjectPart.Bench: Bench(l, key, Frame.Root(1 / 1.4f)); break;
                case ObjectPart.Barrel: Barrel(l, key, Frame.Root(1 / 0.85f)); break;
                case ObjectPart.Crate: Crate(l, key, Frame.Root(1 / 0.7f)); break;
                case ObjectPart.Stall: Stall(l, key, Frame.Root(1 / 3f)); break;
                case ObjectPart.Well: Well(l, key, Frame.Root(1 / 2.6f)); break;
                case ObjectPart.StoneRing: StoneRing(l, key); break;
                case ObjectPart.Tent: Tent(l, key, Frame.Root(1 / 4.4f)); break;
                case ObjectPart.FirePit: FirePit(l, key, Frame.Root(1 / 1.8f)); break;
                case ObjectPart.LogSeat: LogSeat(l, key, Frame.Root(1 / 1.6f)); break;
                case ObjectPart.Campsite: Campsite(l, key, Frame.Root(CampU)); break;
                case ObjectPart.HermitHut: HermitHut(l, key, Frame.Root(HutU), out _, out _); break;
                case ObjectPart.Hermitage: Hermitage(l, key, Frame.Root(HermitU)); break;
                case ObjectPart.RoadShrine: RoadShrine(l, key, Frame.Root(ShrineU)); break;
                case ObjectPart.Wayside: Wayside(l, key, Frame.Root(WaysideU)); break;
                case ObjectPart.Ruin: Ruin(l, key, Frame.Root(RuinU)); break;
                case ObjectPart.HotSpring: HotSpring(l, key, Frame.Root(SpringU)); break;
                case ObjectPart.Mushroom: Mushroom(l, key, Frame.Root(MushroomU), key); break;
                case ObjectPart.FairyRing: FairyRing(l, key, Frame.Root(FairyU)); break;
                case ObjectPart.Geyser: Geyser(l, key, Frame.Root(SpringU)); break;
                case ObjectPart.MudPot: MudPot(l, key, Frame.Root(SpringU)); break;
                case ObjectPart.WolfDen: WolfDen(l, key, Frame.Root(WolfU)); break;
                case ObjectPart.WallTorch: WallTorch(l, key, Frame.Root(1 / 0.7f)); break;
            }
        }

        static void Inn(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "inn");
            float hx = rng.Range(4.5f, 5.0f), hz = rng.Range(2.9f, 3.2f), rr = rng.Range(1.0f, 1.2f), e = 0.7f, verge = rng.Range(0.55f, 0.6f);
            float sc = rng.NextFloat() < 0.5f ? -1 : 1;
            float y0 = Plate - e * rr, y1 = Plate + hz * rr, ytop = y1 + 0.9f;
            int nf = Math.Max(3, (int)Math.Round(2 * hx / 2.2f)), ns = Math.Max(2, (int)Math.Round(2 * hz / 2.2f));
            int door = rng.Range(1, nf - 1);
            float dx = -hx + 2 * hx * (door + 0.5f) / nf;
            l.DoorX = f.U * dx;
            l.DoorZ = f.U * hz;

            var front = f.Child(0, 0, hz, 1, 0);
            var back = f.Child(0, 0, -hz, -1, 0);
            var east = f.Child(hx, 0, 0, 0, -1);
            var west = f.Child(-hx, 0, 0, 0, 1);

            var kf = new int[nf];
            var kb = new int[nf];
            var ke = new int[ns];
            var kw = new int[ns];
            for (int i = 0; i < nf; i++)
            {
                bool end = i == 0 || i == nf - 1;
                kf[i] = i == door ? 1 : rng.NextFloat() < (end ? 0.5f : 0.85f) ? 2 : 0;
                kb[i] = rng.NextFloat() < 0.45f ? 2 : 0;
            }
            for (int i = 0; i < ns; i++)
            {
                bool end = i == 0 || i == ns - 1;
                float xm = -hz + 2 * hz * (i + 0.5f) / ns;
                ke[i] = SideWindow(ref rng, sc > 0, end, -xm) ? 2 : 0;
                kw[i] = SideWindow(ref rng, sc < 0, end, xm) ? 2 : 0;
            }

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
            Chimney(l, key, ref rng, f, sc * (hx + 0.45f), y1, ytop);
            Hearth(l, key, ref rng, f, sc, hx);
            Interior(l, ref rng, f, sc, hx, hz, dx);
            Porch(l, key, front, dx);

            for (int w = 0; w < 4; w++)
            {
                var wf = w == 0 ? front : w == 1 ? back : w == 2 ? east : west;
                float h = w < 2 ? hx : hz;
                for (float x = -h; x <= h + 0.01f; x += 0.4f)
                    if (w != 0 || Math.Abs(x - dx) >= 0.95f) Col(l, wf, x, 0, 0.25f, -0.35f, Plate);
            }
            for (float x = -hx + 0.4f; x < hx; x += 0.8f)
                for (float z = -hz + 0.4f; z < hz; z += 0.8f)
                    Col(l, f, x, z, 0.5f, -0.35f, Fy);
            Col(l, f, sc * (hx + 0.45f), 0, 0.55f, -0.35f, ytop);
        }

        static bool SideWindow(ref Rng rng, bool chimney, bool end, float z)
        {
            float r = rng.NextFloat();
            if (chimney) return end && r < 0.4f;
            return z > 0.8f && r < 0.7f;
        }

        static void InnWall(StructureLayout l, ulong key, ref Rng rng, in Frame w, float len, int[] kind)
        {
            int n = kind.Length;
            float h = 0.5f * len;
            Box(l, key, w, 0, 0.35f, 0, len, 0.25f, 0.28f, Timber, GX, 0, 0);
            Box(l, key, w, 0, 3.08f, 0, len + 0.3f, 0.22f, 0.28f, Timber, GX, 0, 1);
            int door = -1;
            for (int i = 0; i < n; i++)
            {
                float x0 = -h + len * i / n, x1 = -h + len * (i + 1) / n, mid = 0.5f * (x0 + x1);
                float a = x0 + (i == 0 ? 0.15f : 0.11f), b = x1 - (i == n - 1 ? 0.15f : 0.11f);
                if (i > 0) Box(l, key, w, x0, 0.6f, 0, 0.22f, 2.48f, 0.26f, Timber, GV, 0, 0, 1, 0, rng.Range(-0.006f, 0.006f));
                bool brace = i == 0 || i == n - 1 || rng.NextFloat() < 0.4f;
                float lean = (i < n / 2 ? 1 : -1) * (rng.NextFloat() < 0.15f ? -1 : 1);
                if (kind[i] == 1)
                {
                    door = i;
                    Cell(l, key, w, a, mid - 0.67f, false, 0);
                    Cell(l, key, w, mid + 0.67f, b, false, 0);
                    Box(l, key, w, mid, 2.88f, -0.02f, 1.34f, 0.2f, 0.2f, Plaster, GX, 0, 0);
                    for (int s = -1; s <= 1; s += 2) Box(l, key, w, mid + s * 0.61f, 0.6f, 0, 0.12f, 2.1f, 0.26f, Timber, GV, 0, 0);
                    Box(l, key, w, mid, 2.7f, 0, 1.46f, 0.18f, 0.28f, Timber, GX, 0, 0);
                    var leaf = w.Child(mid - 0.55f, 0.6f, -0.05f, 0.383f, -0.924f, 0, 0);
                    for (int p = 0; p < 5; p++) Box(l, key, leaf, 0.108f + 0.216f * p, 0, 0, 0.21f, 2.08f + rng.Range(-0.01f, 0), 0.05f, Plank, GV);
                    for (int p = 0; p < 3; p++) Box(l, key, leaf, 0.54f, 0.25f + 0.75f * p, -0.045f, 1.0f, 0.12f, 0.04f, Timber, GX);
                    for (int p = 0; p < 2; p++) Box(l, key, leaf, 0.48f, 0.45f + 1.15f * p, 0.035f, 0.92f, 0.05f, 0.015f, Iron, GX);
                    Box(l, key, leaf, 0.92f, 1.0f, 0.04f, 0.06f, 0.1f, 0.02f, Iron, GX);
                    Box(l, key, w, mid, 0.6f, 0.115f, 1.1f, 2.1f, 0.02f, Iron, GX, 1, 1);
                }
                else if (kind[i] == 2)
                {
                    Cell(l, key, w, a, mid - 0.505f, brace && mid - 0.505f - a > 0.6f, lean);
                    Cell(l, key, w, mid + 0.505f, b, brace && b - mid - 0.505f > 0.6f, -lean);
                    Box(l, key, w, mid, 1.5f, 0, 1.01f, 0.15f, 0.24f, Timber, GX, 0, 0);
                    Box(l, key, w, mid, 0.6f, -0.02f, 1.01f, 0.9f, 0.2f, Plaster, GX, 0, 0);
                    Box(l, key, w, mid, 2.8f, -0.02f, 1.01f, 0.28f, 0.2f, Plaster, GX, 0, 0);
                    for (int s = -1; s <= 1; s += 2) Box(l, key, w, mid + s * 0.465f, 1.65f, 0, 0.08f, 1.05f, 0.24f, Timber, GV, 0, 0);
                    Box(l, key, w, mid, 2.7f, 0, 1.01f, 0.1f, 0.26f, Timber, GX, 0, 0);
                    Box(l, key, w, mid, 1.6f, 0.05f, 1.15f, 0.06f, 0.34f, Timber, GX, 0, 0);
                    Box(l, key, w, mid, 1.65f, -0.01f, 0.06f, 1.05f, 0.1f, Timber, GV, 0, 0);
                    Box(l, key, w, mid, 1.65f, -0.03f, 0.85f, 1.05f, 0.02f, Iron, GX, 0, 0);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float sx = mid + s * (0.715f + rng.Range(0, 0.03f));
                        Box(l, key, w, sx, 1.65f, 0.15f, 0.42f, 1.05f, 0.04f, Plank, GV, 0, 0, 1, 0, rng.Range(-0.01f, 0.01f));
                        for (int p = 0; p < 2; p++) Box(l, key, w, sx, 1.8f + 0.6f * p, 0.18f, 0.38f, 0.07f, 0.025f, Timber, GX, 0, 0);
                    }
                    Box(l, key, w, mid, 1.65f, 0.12f, 0.85f, 1.05f, 0.02f, Iron, GX, 1, 1);
                }
                else Cell(l, key, w, a, b, brace, lean);
            }
            if (door < 0)
            {
                Box(l, key, w, 0, 0.35f, 0, len, Plate - 0.35f, 0.22f, Plaster, GX, 1, 1);
                Box(l, key, w, 0, 1.5f, 0, len, 0.15f, 0.26f, Timber, GX, 1, 1);
            }
            else
            {
                float dm = -h + len * (door + 0.5f) / n, la = dm - 0.55f + h, lb = h - dm - 0.55f;
                Box(l, key, w, -h + 0.5f * la, 0.35f, 0, la, Plate - 0.35f, 0.22f, Plaster, GX, 1, 1);
                Box(l, key, w, h - 0.5f * lb, 0.35f, 0, lb, Plate - 0.35f, 0.22f, Plaster, GX, 1, 1);
                Box(l, key, w, dm, 2.7f, 0, 1.1f, Plate - 2.7f, 0.22f, Plaster, GX, 1, 1);
                Box(l, key, w, -h + 0.5f * la, 1.5f, 0, la, 0.15f, 0.26f, Timber, GX, 1, 1);
                Box(l, key, w, h - 0.5f * lb, 1.5f, 0, lb, 0.15f, 0.26f, Timber, GX, 1, 1);
            }
        }

        static void Cell(StructureLayout l, ulong key, in Frame w, float a, float b, bool brace, float lean)
        {
            float wd = b - a, c = 0.5f * (a + b);
            if (wd <= 0.02f) return;
            Box(l, key, w, c, 1.5f, 0, wd, 0.15f, 0.24f, Timber, GX, 0, 0);
            Box(l, key, w, c, 0.6f, -0.02f, wd, 0.9f, 0.2f, Plaster, GX, 0, 0);
            Box(l, key, w, c, 1.65f, -0.02f, wd, 1.43f, 0.2f, Plaster, GX, 0, 0);
            if (brace && wd > 0.4f) Box(l, key, w, c, 0.6f, 0, 0.14f, 0.9f, 0.25f, Timber, GV, 0, 0, 1, 0, lean * Math.Min(1, (wd - 0.2f) / 0.9f));
        }

        static void Plinth(StructureLayout l, ulong key, ref Rng rng, in Frame w, float len)
        {
            float h = 0.5f * len + 0.2f;
            for (int j = 0; j < 2; j++)
            {
                float x = -h - (j == 1 ? rng.Range(0.25f, 0.4f) : 0);
                while (x < h)
                {
                    float x1 = Math.Min(x + rng.Range(0.5f, 0.9f), h);
                    if (h - x1 < 0.2f) x1 = h;
                    float xa = Math.Max(x, -h);
                    if (x1 - xa > 0.12f)
                        Box(l, key, w, 0.5f * (xa + x1), -0.35f + 0.35f * j, rng.Range(-0.02f, 0.03f), x1 - xa - 0.02f, 0.35f * rng.Range(0.93f, 1.0f), 0.35f * rng.Range(0.92f, 1.05f), Stone, GX, 0, 0);
                    x = x1;
                }
            }
        }

        static void Roof(StructureLayout l, ulong key, ref Rng rng, in Frame f, float hx, float hz, float rr, float y0, float y1, float verge, int nf)
        {
            var tr = Rng.Stream(key, "thatch");
            float rw = hx + verge, ym = 0.5f * (y0 + y1), sz = 0.45f / rr;
            const int bands = 8;
            var cut = new float[bands + 1];
            for (int sg = -1; sg <= 1; sg += 2)
            {
                float bt = -sg / rr;
                Box(l, key, f, 0, y0, sg * (hz + 0.5f * sz - (ym - Plate) / rr), 2 * rw, y1 - y0, sz, Thatch, GV, 1, 2, 1, 0, 0, bt);
                cut[0] = 0;
                for (int k = 1; k <= bands; k++) cut[k] = cut[k - 1] + tr.Range(0.75f, 1.25f);
                for (int k = 0; k < bands; k++)
                {
                    bool eave = k == 0;
                    float a = y0 + (y1 - y0) * cut[k] / cut[bands] - (eave ? 0 : 0.08f), b = y0 + (y1 - y0) * cut[k + 1] / cut[bands];
                    float x = -rw;
                    while (x < rw)
                    {
                        float x1 = Math.Min(x + (eave ? tr.Range(0.35f, 0.7f) : tr.Range(0.9f, 1.6f)), rw);
                        if (rw - x1 < (eave ? 0.2f : 0.4f)) x1 = rw;
                        float th = sz * tr.Range(1f, 1.2f) * (eave ? 1.3f : 1), ya = a + (eave ? tr.Range(-0.09f, 0.03f) : tr.Range(-0.035f, 0.025f)), yk = 0.5f * (ya + b);
                        float tuck = eave ? 0.9f * sz - 0.925f * th : 0;
                        Box(l, key, f, 0.5f * (x + x1), ya, sg * (hz + 0.5f * th + 0.5f * tuck - (yk - Plate) / rr), x1 - x + 0.02f, b - ya, 1.05f * th, Thatch, GV, 0, 0, 1, 0, 0, bt + sg * tuck / (b - ya), 0.2f * th / (b - ya));
                        x = x1;
                    }
                }
                float szb = 0.3f / rr, szr = 0.15f / rr;
                for (int s = -1; s <= 1; s += 2)
                    Box(l, key, f, s * (rw - 0.03f), y0, sg * (hz - 0.5f * szb - (ym - Plate) / rr), 0.05f, y1 - y0, szb, Plank, GV, 0, 1, 1, 0, 0, bt);
                for (float x = -hx + 0.3f; x < hx; x += 0.6f)
                    Box(l, key, f, x, y0, sg * (hz - 0.5f * szr - (ym - Plate) / rr), 0.1f, y1 - y0, szr, Timber, GV, 0, 0, 1, 0, 0, bt);
                Box(l, key, f, 0, Plate + 0.5f * hz * rr - 0.33f, sg * 0.5f * hz, 2 * hx, 0.18f, 0.18f, Timber, GX, 0, 0);
            }
            Box(l, key, f, 0, y1 - 0.1f, 0, 2 * rw + 0.1f, 0.4f, 2 * sz + 0.3f, Thatch, GX, 0, 2);
            float rd = sz + 0.32f;
            Box(l, key, f, 0, y1 + 0.22f, 0, 2 * rw + 0.15f, 0.22f, 0.8f * rd, Thatch, GX, 0, 0, 1, 0, 0, 0, 0.2f * rd / 0.22f);
            float wb = 2 * sz + 0.36f, wt = 0.6f * rd + 0.04f;
            for (float x = -rw + tr.Range(0.25f, 0.4f); x < rw - 0.15f; x += tr.Range(0.5f, 0.7f))
                Box(l, key, f, x, y1 - 0.12f, 0, 0.06f, 0.59f, 0.5f * (wb + wt), Timber, GZ, 0, 0, 1, 0, 0, 0, (wb - wt) / (2 * 0.59f));
            Box(l, key, f, 0, y1 - 0.37f, 0, 2 * hx + 0.2f, 0.22f, 0.2f, Timber, GX, 0, 0);
            for (int i = 1; i < nf; i++)
            {
                float x = -hx + 2 * hx * i / nf;
                Box(l, key, f, x, Plate, 0, 0.22f, 0.24f, 2 * hz + 0.1f, Timber, GZ, 0, 0);
                Box(l, key, f, x, Plate + 0.24f, 0, 0.18f, y1 - 0.37f - Plate - 0.24f, 0.18f, Timber, GV, 0, 0);
            }
        }

        static void Gables(StructureLayout l, ulong key, in Frame f, float hx, float hz, float rr, float sc)
        {
            float gh = hz * rr;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Box(l, key, f, sx * hx, Plate, 0, 0.2f, gh, hz + 0.05f, Plaster, GV, 0, 2, 1, 0, 0, 0, (hz - 0.05f) / gh);
                float xg = sx * (hx + 0.03f);
                Box(l, key, f, xg, Plate, 0, 0.16f, gh - 0.15f, 0.18f, Timber, GV, 0, 0);
                Box(l, key, f, xg, Plate + 0.45f * gh, 0, 0.16f, 0.16f, 1.05f * hz, Timber, GZ, 0, 0);
                for (int sg = -1; sg <= 1; sg += 2)
                    Box(l, key, f, xg, Plate, sg * (0.5f * hz - 0.08f), 0.16f, gh, 0.2f, Timber, GV, 0, 0, 1, 0, 0, -sg / rr);
            }
            Box(l, key, f, -sc * (hx + 0.105f), Plate + 0.55f * gh, 0, 0.02f, 0.3f, 0.35f, Iron, GX, 0, 1);
        }

        static void Chimney(StructureLayout l, ulong key, ref Rng rng, in Frame f, float cx, float y1, float ytop)
        {
            int j = 0;
            for (float y = -0.35f; y < ytop - 0.05f; y += 0.4f, j++)
            {
                bool low = y < Plate;
                float wx = low ? 0.9f : 0.72f, wz = low ? 1.1f : 0.86f, hy = Math.Min(0.4f, ytop - y);
                float su = (j & 1) == 0 ? 0.1f : -0.1f, sv = (j & 1) == 0 ? -0.12f : 0.12f;
                for (int q = 0; q < 4; q++)
                {
                    float u0 = (q & 1) == 0 ? -0.5f * wx : su, u1 = (q & 1) == 0 ? su : 0.5f * wx;
                    float v0 = (q & 2) == 0 ? -0.5f * wz : sv, v1 = (q & 2) == 0 ? sv : 0.5f * wz;
                    Box(l, key, f, cx + 0.5f * (u0 + u1), y, 0.5f * (v0 + v1), u1 - u0 - 0.015f, hy * rng.Range(0.96f, 1.0f), v1 - v0 - 0.015f, Stone, GX, 0, 0);
                }
            }
            Box(l, key, f, cx, ytop, 0, 0.84f, 0.1f, 0.98f, Stone, GX, 0, 1);
            Box(l, key, f, cx, ytop + 0.1f, 0, 0.22f, 0.35f, 0.22f, Iron, GV, 0, 1);
            Box(l, key, f, cx, -0.35f, 0, 0.9f, Plate + 0.35f, 1.1f, Stone, GX, 1, 2);
            Box(l, key, f, cx, Plate, 0, 0.72f, ytop - Plate, 0.86f, Stone, GX, 1, 2);
        }

        static void Hearth(StructureLayout l, ulong key, ref Rng rng, in Frame f, float sc, float hx)
        {
            float x = sc * (hx - 0.45f);
            var h = f.Child(0, 0, 0, 1, 0, 0, 0);
            Box(l, key, h, x, Fy, 0, 0.75f, 0.1f, 1.5f, Stone, GX);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(l, key, h, x, Fy + 0.1f, s * 0.6f, 0.6f, 0.95f, 0.28f, Stone, GX);
                Box(l, key, h, x - sc * 0.05f, Fy + 0.1f, s * 0.3f, 0.35f, 0.15f, 0.04f, Iron, GX);
            }
            Box(l, key, h, x, Fy + 1.05f, 0, 0.66f, 0.25f, 1.5f, Stone, GX);
            float hy = 3.08f - Fy - 1.3f;
            Box(l, key, h, sc * (hx - 0.4f), Fy + 1.3f, 0, 0.6f, hy, 1.3f, Plaster, GV, 0, 0, 1, 0, 0, 0, 0.2f / hy);
            for (int s = -1; s <= 1; s += 2)
            {
                float t = rng.Range(-0.15f, 0.15f);
                Box(l, key, h, x - sc * 0.05f + s * 0.08f, Fy + 0.22f, 0, 0.12f, 0.12f, 0.6f, Timber, GZ, 0, 0, (float)Math.Sqrt(1 - t * t), t);
            }
            Col(l, f, x, 0, 0.6f, -0.35f, Fy + 1);
        }

        static void Interior(StructureLayout l, ref Rng rng, in Frame f, float sc, float hx, float hz, float dx)
        {
            var r = f.Child(0, 0, 0, 1, 0, 0, 0);
            ulong key = rng.NextU64();
            float bx = -sc * (hx - 1.5f), z0 = -hz + 0.3f, z1 = hz - 1.4f, zb = 0.5f * (z0 + z1), bl = z1 - z0;
            Box(l, key, r, bx, Fy + 1.0f, zb, 0.6f, 0.06f, bl, Plank, GZ);
            for (float z = z0; z < z1 - 0.05f; z += 0.2f)
                Box(l, key, r, bx + sc * 0.22f, Fy, z + 0.1f, 0.04f, 1.0f + rng.Range(-0.01f, 0.0f), 0.19f, Plank, GV);
            for (int s = 0; s < 2; s++) Box(l, key, r, bx, Fy, s == 0 ? z0 + 0.05f : z1 - 0.05f, 0.42f, 1.0f, 0.1f, Timber, GV);
            Box(l, key, r, bx - sc * 0.1f, Fy + 0.45f, zb, 0.3f, 0.04f, bl - 0.1f, Plank, GZ);
            Box(l, key, r, bx + sc * 0.32f, Fy + 0.15f, zb, 0.05f, 0.05f, bl, Iron, GZ);
            int mugs = rng.Range(3, 6);
            for (int i = 0; i < mugs; i++) Box(l, key, r, bx + rng.Range(-0.15f, 0.15f), Fy + 1.06f, z0 + bl * rng.NextFloat(), 0.08f, 0.11f, 0.08f, Plank, GV);
            for (float z = z0; z <= z1 + 0.01f; z += 0.5f) Col(l, f, bx, z, 0.35f, -0.35f, Fy + 1.1f);

            float wx = -sc * (hx - 0.6f);
            for (int i = 0; i < 3; i++) Barrel(l, rng.NextU64(), r.Child(wx + rng.Range(-0.05f, 0.05f), Fy, -1.8f + 0.7f * i));
            for (int s = 0; s < 2; s++)
            {
                float y = Fy + 1.6f + 0.5f * s, sx = -sc * (hx - 0.28f);
                Box(l, key, r, sx, y, -0.8f, 0.28f, 0.04f, 2.2f, Plank, GZ);
                for (int b = -1; b <= 1; b += 2) Box(l, key, r, sx + sc * 0.02f, y - 0.15f, -0.8f + b * 0.9f, 0.2f, 0.15f, 0.03f, Iron, GV);
                int n = rng.Range(4, 8);
                for (int i = 0; i < n; i++)
                {
                    float w = rng.Range(0.07f, 0.12f);
                    Box(l, key, r, sx + rng.Range(-0.05f, 0.05f), y + 0.04f, -1.8f + 2 * (i + rng.Range(0.2f, 0.8f)) / n, w, w * rng.Range(1.0f, 1.8f), w, rng.NextFloat() < 0.7f ? Plank : Iron, GV);
                }
            }

            float xa = -sc * (hx - 2.3f), xb = sc * (hx - 1.6f), lo = Math.Min(xa, xb), hi = Math.Max(xa, xb);
            for (int i = 0; i < 2; i++)
                Seating(l, ref rng, r, lo + (hi - lo) * (i == 0 ? 0.27f : 0.73f), -0.8f + rng.Range(-0.15f, 0.15f));
            for (int i = 0; i < 2; i++)
            {
                float x = lo + (hi - lo) * (i == 0 ? 0.5f : 0.78f);
                if (Math.Abs(x - dx) <= 2.0f) continue;
                Seating(l, ref rng, r, x, 1.2f);
                break;
            }
            float cxr = -sc * (hx - 0.55f);
            Yaw(ref rng, 0.15f, out float c, out float sn);
            Crate(l, rng.NextU64(), r.Child(cxr, Fy, hz - 0.55f, c, sn));
            Crate(l, rng.NextU64(), r.Child(cxr, Fy, hz - 1.3f));
            Yaw(ref rng, 0.4f, out c, out sn);
            Crate(l, rng.NextU64(), r.Child(cxr, Fy + 0.7f, hz - 0.55f, c, sn, 0, 2, 0.8f));
        }

        static void Seating(StructureLayout l, ref Rng rng, in Frame r, float x, float z)
        {
            Yaw(ref rng, 0.08f, out float c, out float s);
            var t = r.Child(x, Fy, z, c, s);
            Table(l, rng.NextU64(), t);
            Bench(l, rng.NextU64(), t.Child(rng.Range(-0.1f, 0.1f), 0, 0.62f));
            Bench(l, rng.NextU64(), t.Child(rng.Range(-0.1f, 0.1f), 0, -0.62f));
        }

        static void Porch(StructureLayout l, ulong key, in Frame w, float dx)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float x = dx + s * 0.35f;
                Box(l, key, w, x, -0.1f, 0.315f, 0.68f, 0.45f, 0.37f, Stone, GX, 0, 1);
                Box(l, key, w, x, -0.1f, 0.675f, 0.68f, 0.25f, 0.35f, Stone, GX, 0, 1);
                Col(l, w, x, 0.315f, 0.3f, -0.1f, 0.35f);
                Col(l, w, x, 0.675f, 0.3f, -0.1f, 0.15f);
            }
            Col(l, w, dx, 0, 0.3f, -0.35f, 0.6f);
            float ax = dx + 1.0f;
            Box(l, key, w, ax, 3.0f, 0.565f, 0.04f, 0.05f, 0.87f, Iron, GZ, 0, 1);
            Box(l, key, w, ax, 2.6f, 0.365f, 0.03f, 0.4f, 0.03f, Iron, GV, 0, 0, 1, 0, 0, 0.47f / 0.4f);
            for (int s = 0; s < 2; s++) Box(l, key, w, ax, 2.7f, s == 0 ? 0.42f : 0.98f, 0.015f, 0.3f, 0.015f, Iron, GV, 0, 0);
            Box(l, key, w, ax, 2.2f, 0.7f, 0.05f, 0.5f, 0.7f, Plank, GZ, 0, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(l, key, w, ax + s * 0.03f, 2.3f, 0.66f, 0.02f, 0.22f, 0.16f, Cloth, GV, 0, 0);
                Box(l, key, w, ax + s * 0.03f, 2.34f, 0.79f, 0.02f, 0.12f, 0.06f, Cloth, GV, 0, 0);
            }
        }

        static void Yard(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "inn-yard");
            float gx = rng.Range(-0.6f, 0.6f), fx = 8.6f, fb = -7.6f, ff = 8.6f;
            Fence(l, key, ref rng, f, -fx, ff, -fx, fb);
            Fence(l, key, ref rng, f, -fx, fb, fx, fb);
            Fence(l, key, ref rng, f, fx, fb, fx, ff);
            Fence(l, key, ref rng, f, fx, ff, gx + 1.2f, ff);
            Fence(l, key, ref rng, f, gx - 1.2f, ff, -fx, ff);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(l, key, f, gx + s * 1.2f, -0.4f, ff, 0.2f, 1.9f, 0.2f, Timber, GV, 0, 1);
                Col(l, f, gx + s * 1.2f, ff, 0.15f, -0.4f, 1.5f);
            }
            var gate = f.Child(gx - 1.1f, 0, ff, 0.5f, 0.866f, 0, 0);
            for (int s = 0; s < 2; s++)
            {
                Box(l, key, gate, 0.05f + 1.0f * s, 0.1f, 0, 0.08f, 1.0f, 0.06f, Timber, GV);
                Box(l, key, gate, 0.55f, 0.25f + 0.6f * s, 0, 1.1f, 0.09f, 0.05f, Timber, GX);
            }
            Box(l, key, gate, 0.55f, 0.25f, 0.01f, 0.08f, 0.69f, 0.04f, Timber, GV, 0, 0, 1, 0, 0.9f / 0.69f);

            Yaw(ref rng, 0.35f, out float c, out float s0);
            var stall = f.Child(-5.5f + rng.Range(-0.4f, 0.4f), 0, 5.4f + rng.Range(-0.4f, 0.4f), c, s0);
            Stall(l, rng.NextU64(), stall);
            Barrel(l, rng.NextU64(), stall.Child(1.95f, 0, 0.25f));
            Barrel(l, rng.NextU64(), stall.Child(2.05f, 0, -0.45f));
            Crate(l, rng.NextU64(), stall.Child(-2.0f, 0, 0.3f));
            if (rng.NextFloat() < 0.6f) Crate(l, rng.NextU64(), stall.Child(-2.0f, 0.7f, 0.3f, 0.95f, 0.31f, 0, 2, 0.85f));

            Yaw(ref rng, 1f, out c, out s0);
            Well(l, rng.NextU64(), f.Child(5.0f + rng.Range(-0.4f, 0.4f), 0, 4.4f + rng.Range(-0.4f, 0.4f), c, s0));

            float px = gx + 1.9f + rng.Range(0, 0.3f), pz = ff - 0.7f;
            Box(l, key, f, px, -0.4f, pz, 0.16f, 3.0f, 0.16f, Timber, GV, 0, 2);
            Box(l, key, f, px + 0.45f, 2.3f, pz, 0.95f, 0.1f, 0.1f, Timber, GX, 0, 1);
            Box(l, key, f, px + 0.35f, 1.9f, pz, 0.03f, 0.45f, 0.03f, Timber, GV, 0, 0, 1, 0, -0.7f);
            for (int s = 0; s < 2; s++) Box(l, key, f, px + 0.35f + 0.5f * s, 2.0f, pz, 0.015f, 0.3f, 0.015f, Iron, GV, 0, 0);
            Box(l, key, f, px + 0.6f, 1.5f, pz, 0.7f, 0.5f, 0.05f, Plank, GX, 0, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(l, key, f, px + 0.6f, 1.62f, pz + s * 0.03f, 0.3f, 0.24f, 0.02f, Cloth, GX, 0, 0);
                Box(l, key, f, px + 0.6f, 1.58f, pz + s * 0.035f, 0.42f, 0.04f, 0.02f, Iron, GX, 0, 0);
            }
            Col(l, f, px, pz, 0.12f, -0.4f, 2.6f);

            Barrel(l, rng.NextU64(), f.Child(-7.5f, 0, -6.5f));
            Crate(l, rng.NextU64(), f.Child(-6.8f, 0, -6.7f, 0.98f, 0.2f));
            Barrel(l, rng.NextU64(), f.Child(7.6f, 0, 7.5f));

            float wpx = 7.2f, wpz = -2.0f;
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 5 - j; i++)
                {
                    float t = rng.Range(-0.05f, 0.05f);
                    Box(l, key, f, wpx + (i - 0.5f * (4 - j)) * 0.2f, 0.17f * j, wpz + rng.Range(-0.08f, 0.08f), 0.18f, 0.18f, 1.6f + rng.Range(-0.1f, 0.1f), Timber, GZ, 0, j < 2 ? 1 : 0, (float)Math.Sqrt(1 - t * t), t);
                }
            Box(l, key, f, wpx - 0.9f, 0, wpz + 1.4f, 0.45f, 0.5f, 0.45f, Timber, GV, 0, 1);
            Col(l, f, wpx, wpz, 0.6f, 0, 0.68f);
            Col(l, f, wpx - 0.9f, wpz + 1.4f, 0.25f, 0, 0.5f);

            int stones = rng.Range(6, 11);
            for (int i = 0, tries = 0; i < stones && tries < 60; tries++)
            {
                float x = rng.Range(-8f, 8f), z = rng.Range(-7f, 8f), sz = rng.Range(0.25f, 0.6f);
                if (Math.Abs(x) < 6.4f && z > -6.2f && z < 2.4f) continue;
                if (Math.Abs(x) < 2.8f && z > 1.5f) continue;
                if ((x + 5.5f) * (x + 5.5f) + (z - 5.4f) * (z - 5.4f) < 7 || (x - 5f) * (x - 5f) + (z - 4.4f) * (z - 4.4f) < 4) continue;
                if ((x - wpx) * (x - wpx) + (z - wpz) * (z - wpz) < 4) continue;
                int d = rng.Range(0, 16);
                Box(l, key, f, x, -0.4f * sz, z, sz * rng.Range(1.0f, 1.5f), sz, sz * rng.Range(0.8f, 1.2f), Stone, GX, 0, 1, Cos16(d), Sin16(d));
                i++;
            }
        }

        static void Fence(StructureLayout l, ulong key, ref Rng rng, in Frame f, float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dz * dz), c = dx / len, s = dz / len;
            int n = Math.Max(1, (int)Math.Round(len / 2));
            for (int k = 0; k < n; k++)
            {
                float t0 = (float)k / n, t1 = (float)(k + 1) / n;
                float px = x0 + dx * t0, pz = z0 + dz * t0;
                Box(l, key, f, px, -0.4f, pz, 0.14f, 1.5f * rng.Range(0.95f, 1.05f), 0.14f, Timber, GV, 0, 1, c, s, rng.Range(-0.04f, 0.04f));
                float mx = x0 + dx * 0.5f * (t0 + t1), mz = z0 + dz * 0.5f * (t0 + t1), sl = len / n + 0.15f;
                for (int r = 0; r < 2; r++)
                {
                    float y = r == 0 ? 0.45f : 0.85f;
                    if (rng.NextFloat() < 0.93f)
                        Box(l, key, f, mx - s * 0.08f, y + rng.Range(-0.03f, 0.03f), mz + c * 0.08f, sl, 0.08f, 0.05f, Timber, GX, 0, 1, c, s);
                    else if (rng.NextFloat() < 0.5f)
                        Box(l, key, f, mx - s * 0.5f, -0.02f, mz + c * 0.5f, sl * 0.9f, 0.08f, 0.06f, Timber, GX, 0, 0, c, s);
                }
                for (float t = 0; t < len / n; t += 0.5f)
                {
                    float u = t0 + t / len;
                    Col(l, f, x0 + dx * u, z0 + dz * u, 0.15f, -0.4f, 1.1f);
                }
            }
        }
    }
}
