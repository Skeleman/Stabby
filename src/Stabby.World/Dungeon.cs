using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float KitCellM = 4f, KitStoreyM = 4f, PortalWidthM = 2.4f, PortalHeightM = 2.8f;
        public const float KitWallM = 0.8f, KitFloorM = 0.4f, KitFrameM = 0.3f, KitRoomHeightM = 4f, KitSlabM = 0.4f;
        public const float RoomM = 3 * KitCellM, StairsM = 2 * KitCellM, StairRiseM = 0.25f, StairTreadM = 0.45f, StairLandingM = 0.4f;
        public const int StairSteps = 16;
        const float JambM = 0.4f, LineStep = 0.4f, LineR = 0.4f;

        public const float DungeonM = 16f, DungeonPlotM = 48f, FloorColumnStepM = 1f, FloorColumnR = 0.5f;

        public static float DungeonSizeM(int shape) => shape == ObjectPart.Dungeon ? DungeonM : shape == ObjectPart.DungeonRoom ? RoomM : shape == ObjectPart.DungeonStairs ? StairsM : KitCellM;

        static void Dungeon(int shape, ulong key, StructureLayout l, int trim)
        {
            var f = Frame.Root(1 / DungeonSizeM(shape));
            switch (shape)
            {
                case ObjectPart.DungeonCorridor: Corridor(l, key, f, false); break;
                case ObjectPart.DungeonRoom: Room(l, key, f, trim, RoomOpenZ); break;
                case ObjectPart.DungeonStairs: Stairs(l, key, f); break;
                case ObjectPart.DungeonDoor: Door(l, key, f, trim); break;
                case ObjectPart.DungeonPillar: Pillar(l, key, f, trim); break;
                case ObjectPart.DungeonCollapse: Corridor(l, key, f, true); break;
                case ObjectPart.Dungeon: Composite(l, key, f, trim); break;
            }
        }

        public static float StairTop(float z)
        {
            float t = z + 0.5f * StairsM - StairLandingM;
            if (t < 0) return 0;
            return StairRiseM * Math.Min(StairSteps, (int)(t / StairTreadM) + 1);
        }

        static void Masonry(StructureLayout l, ulong key, in Frame f, float x0, float x1, float y0, float y1, float z0, float z1, int lo = 0, int hi = Tiers - 1)
        {
            if (x1 - x0 < 0.01f || y1 - y0 < 0.01f || z1 - z0 < 0.01f) return;
            int n = l.Pieces.Count;
            Box(l, key, f, 0.5f * (x0 + x1), y0, 0.5f * (z0 + z1), x1 - x0, y1 - y0, z1 - z0, Stone, GX, lo, hi);
            if (l.Pieces.Count == n) return;
            var p = l.Pieces[n];
            p.Coursed = true;
            l.Pieces[n] = p;
        }

        static void Dressed(StructureLayout l, ulong key, in Frame f, float x0, float x1, float y0, float y1, float z0, float z1, int lo = 0, int hi = Tiers - 1)
        {
            if (x1 - x0 < 0.01f || y1 - y0 < 0.01f || z1 - z0 < 0.01f) return;
            Box(l, key, f, 0.5f * (x0 + x1), y0, 0.5f * (z0 + z1), x1 - x0, y1 - y0, z1 - z0, Stone, GX, lo, hi);
        }

        static void Flags(StructureLayout l, ulong key, ref Rng rng, in Frame f, float x0, float x1, float z0, float z1)
        {
            int start = l.Pieces.Count;
            float z = z0;
            while (z < z1 - 0.01f)
            {
                float d = Math.Min(rng.Range(0.7f, 1.1f), z1 - z);
                if (z1 - z - d < 0.35f) d = z1 - z;
                float x = x0 - rng.Range(0f, 0.5f);
                while (x < x1 - 0.01f)
                {
                    float xa = Math.Max(x, x0), xb = Math.Min(x + rng.Range(0.6f, 1.2f), x1);
                    if (x1 - xb < 0.3f) xb = x1;
                    float top = rng.Range(-0.012f, 0.008f);
                    Dressed(l, key, f, xa + 0.01f, xb - 0.01f, -KitFloorM, top, z + 0.01f, z + d - 0.01f, 0, 0);
                    x = xb;
                }
                z += d;
            }
            Dressed(l, key, f, x0, x1, -KitFloorM, 0, z0, z1, 1, 2);
            Flat(l, start);
            int nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / FloorColumnStepM)), nz = Math.Max(1, (int)Math.Ceiling((z1 - z0) / FloorColumnStepM));
            for (int a = 0; a < nz; a++)
                for (int b = 0; b < nx; b++) Col(l, f, x0 + (b + 0.5f) * (x1 - x0) / nx, z0 + (a + 0.5f) * (z1 - z0) / nz, FloorColumnR, -KitFloorM, 0);
        }

        static void Line(StructureLayout l, in Frame f, float x0, float z0, float x1, float z1, float bottom, float top)
        {
            float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            int n = Math.Max(1, (int)Math.Ceiling(len / LineStep));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Col(l, f, x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, LineR, bottom, top);
            }
        }

        static void Edge(StructureLayout l, ulong key, in Frame f, float e, float top, float depth)
        {
            float h = 0.5f * PortalWidthM, c = 0.5f * KitCellM, j = h + JambM, zf = e - KitFrameM, zr = e - depth, lt = PortalHeightM + KitSlabM;
            for (int sg = -1; sg <= 1; sg += 2)
            {
                Dressed(l, key, f, sg < 0 ? -j : h, sg < 0 ? -h : j, -KitFloorM, PortalHeightM, zf, e);
                Masonry(l, key, f, sg < 0 ? -c : j, sg < 0 ? -j : c, -KitFloorM, top, zr, e);
                Masonry(l, key, f, sg < 0 ? -j : h, sg < 0 ? -h : j, -KitFloorM, top, zr, zf);
                Col(l, f, sg * (h + LineR), e - 0.2f, LineR, -KitFloorM, top);
            }
            Dressed(l, key, f, -j, j, PortalHeightM, lt, zf, e);
            Masonry(l, key, f, -j, j, lt, top, zf, e);
            Masonry(l, key, f, -h, h, PortalHeightM, top, zr, zf);
        }

        static void Corridor(StructureLayout l, ulong key, in Frame f, bool collapse)
        {
            var rng = Rng.Stream(key, collapse ? "dungeon-collapse" : "dungeon-corridor");
            float h = 0.5f * PortalWidthM, c = 0.5f * KitCellM, e = c - KitFrameM, top = PortalHeightM + KitSlabM;
            Flags(l, key, ref rng, f, -h, h, -c, c);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var g = f.Child(0, 0, 0, sg);
                Masonry(l, key, g, h, c, -KitFloorM, top, -e, e);
                Line(l, g, h + LineR, -e, h + LineR, e, -KitFloorM, top);
                Edge(l, key, f.Child(0, 0, 0, sg), c, top, KitFrameM);
            }
            if (!collapse)
            {
                float cut = rng.Range(-0.6f, 0.6f);
                Dressed(l, key, f, -h, h, PortalHeightM, top, -e, cut - 0.01f, 0, 1);
                Dressed(l, key, f, -h, h, PortalHeightM, top, cut + 0.01f, e, 0, 1);
                Dressed(l, key, f, -h, h, PortalHeightM, top, -e, e, 2, 2);
                int chips = rng.Range(1, 4);
                for (int i = 0; i < chips; i++)
                {
                    float sz = rng.Range(0.1f, 0.22f), side = rng.NextFloat() < 0.5f ? -1 : 1;
                    int d = rng.Range(0, 16);
                    Rock(l, rng.NextU64(), f, side * (h - 0.5f * sz - 0.02f), -0.02f, rng.Range(-e, e), sz * 1.3f, sz * 0.6f, sz, Cos16(d), Sin16(d), 0, 0);
                }
                return;
            }
            float ha = rng.Range(-0.9f, -0.6f), hb = rng.Range(0.6f, 0.9f), mz = 0.5f * (ha + hb);
            Dressed(l, key, f, -h, h, PortalHeightM, top, -e, ha);
            Dressed(l, key, f, -h, h, PortalHeightM, top, hb, e);
            for (int sg = -1; sg <= 1; sg += 2)
                Dressed(l, key, f, sg < 0 ? -h : h - 0.5f, sg < 0 ? -h + 0.5f : h, PortalHeightM - 0.15f, PortalHeightM, sg < 0 ? ha - 0.25f : hb, sg < 0 ? ha : hb + 0.25f, 0, 1);
            int n = rng.Range(11, 16);
            for (int i = 0; i < n; i++)
            {
                int k = rng.Range(0, 16), d = rng.Range(0, 16);
                float rd = 1.05f * (float)Math.Sqrt(rng.NextFloat()), fall = 1 - rd / 1.05f, sz = rng.Range(0.22f, 0.5f) * (0.6f + 0.4f * fall);
                float x = Math.Max(-h + 0.3f, Math.Min(h - 0.3f, Cos16(k) * rd)), z = mz + Sin16(k) * rd, y = -0.08f + 0.62f * fall * rng.Range(0.7f, 1f);
                if (rng.NextFloat() < 0.35f) Hewn(l, rng.NextU64(), f, x, y - 0.05f, z, sz * 1.5f, sz * 0.7f, sz * 1.1f, Cos16(d), Sin16(d), 0, 1, rng.Range(0.9f, 0.95f), rng.Range(-0.15f, 0.15f));
                else Rock(l, rng.NextU64(), f, x, y, z, sz * 1.25f, sz * 0.75f, sz, Cos16(d), Sin16(d), 0, 1, rng.Range(-0.1f, 0.1f));
            }
            Yaw(ref rng, 0.3f, out float sc, out float ss);
            Hewn(l, rng.NextU64(), f, rng.Range(-0.2f, 0.2f), 0.25f, mz + 0.2f, 1.7f, 0.32f, 1.1f, sc, ss, 0, 2, 0.95f, 0, rng.Range(0.35f, 0.6f));
            Rock(l, rng.NextU64(), f, 0, -0.1f, mz, 2.2f, 0.75f, 2f, 1, 0, 2, 2);
            Col(l, f, 0, mz, 0.55f, -KitFloorM, 0.95f);
            for (int k = 0; k < 16; k += 3) Col(l, f, 0.75f * Cos16(k), mz + 0.75f * Sin16(k), 0.35f, -KitFloorM, 0.45f);
        }

        public const int RoomOpenZ = 1 | 4;
        public static readonly int[] RoomDx = { 0, 1, 0, -1 }, RoomDz = { 1, 0, -1, 0 };

        static void Room(StructureLayout l, ulong key, in Frame f, int trim, int open)
        {
            var rng = Rng.Stream(key, "dungeon-room");
            float c = 0.5f * RoomM, i = c - KitWallM, top = KitRoomHeightM + KitSlabM, p = 0.5f * KitCellM, h = 0.5f * PortalWidthM;
            Flags(l, key, ref rng, f, -i, i, -i, i);
            for (int d = 0; d < 4; d++)
            {
                var side = f.Child(0, 0, 0, RoomDx[d], RoomDz[d]);
                float a = (d & 1) == 1 ? c : i, lx = c - 0.5f * KitWallM;
                if ((open & (1 << d)) == 0)
                {
                    Masonry(l, key, side, i, c, -KitFloorM, top, -a, a);
                    Line(l, side, lx, -a, lx, a, -KitFloorM, top);
                    Pilaster(l, key, ref rng, side, i);
                }
                else
                {
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        Masonry(l, key, side, i, c, -KitFloorM, top, s2 < 0 ? -a : p, s2 < 0 ? -p : a);
                        Line(l, side, lx, s2 * (h + LineR), lx, s2 * a, -KitFloorM, top);
                    }
                    Flags(l, key, ref rng, side, i, c, -h, h);
                    Edge(l, key, side.Child(0, 0, 0, 0, -1), c, top, KitWallM);
                }
                float gap = (open & (1 << d)) == 0 ? 0.45f : h;
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    Plinth(l, key, ref rng, side, i, s2 < 0 ? -i : gap, s2 < 0 ? -gap : i);
                    Sconce(l, key, side, i, s2 * rng.Range(2.4f, 2.8f), trim);
                }
            }
            int strips = 4;
            float w = 2 * i / strips;
            for (int k = 0; k < strips; k++) Dressed(l, key, f, -i, i, KitRoomHeightM, top, -i + k * w + 0.01f, -i + (k + 1) * w - 0.01f, 0, 1);
            Dressed(l, key, f, -i, i, KitRoomHeightM, top, -i, i, 2, 2);
            Dressed(l, key, f, -i, i, KitRoomHeightM - 0.5f, KitRoomHeightM, -0.3f, 0.3f, 0, 1);
            Dressed(l, key, f, -0.3f, 0.3f, KitRoomHeightM - 0.5f, KitRoomHeightM, -i, -0.3f, 0, 1);
            Dressed(l, key, f, -0.3f, 0.3f, KitRoomHeightM - 0.5f, KitRoomHeightM, 0.3f, i, 0, 1);
            Box(l, key, f, 0, KitRoomHeightM - 0.56f, 0, 0.5f, 0.06f, 0.5f, trim, GX, 0, 0, Cos16(2), Sin16(2));
        }

        static void Pilaster(StructureLayout l, ulong key, ref Rng rng, in Frame side, float i)
        {
            float w = 0.4f + rng.Range(-0.02f, 0.02f);
            Dressed(side, l, key, i - 0.3f, i, -KitFloorM, KitRoomHeightM - 0.5f, -w, w);
        }

        static void Dressed(in Frame f, StructureLayout l, ulong key, float x0, float x1, float y0, float y1, float z0, float z1) =>
            Dressed(l, key, f, x0, x1, y0, y1, z0, z1, 0, 1);

        static void Plinth(StructureLayout l, ulong key, ref Rng rng, in Frame side, float i, float a, float b)
        {
            float x = a;
            while (x < b - 0.05f)
            {
                float x1 = Math.Min(x + rng.Range(0.5f, 0.9f), b);
                if (b - x1 < 0.25f) x1 = b;
                Dressed(l, key, side, i - 0.15f * rng.Range(0.85f, 1.05f), i, -0.02f, 0.4f * rng.Range(0.94f, 1f), x + 0.01f, x1 - 0.01f, 0, 0);
                x = x1;
            }
        }

        static void Sconce(StructureLayout l, ulong key, in Frame side, float i, float z, int trim)
        {
            Box(l, key, side, i - 0.02f, 1.8f, z, 0.04f, 0.32f, 0.14f, trim, GV, 0, 0);
            Box(l, key, side, i - 0.17f, 2.0f, z, 0.3f, 0.04f, 0.04f, trim, GX, 0, 0);
            Box(l, key, side, i - 0.3f, 2.04f, z, 0.14f, 0.1f, 0.14f, trim, GV, 0, 0);
        }

        static void Stairs(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "dungeon-stairs");
            float c = 0.5f * StairsM, e = c - KitFrameM, h = 0.5f * PortalWidthM, w = 0.5f * KitCellM, z0 = -c + StairLandingM;
            Flags(l, key, ref rng, f, -h, h, -c, z0);
            for (int s = 1; s <= StairSteps; s++)
            {
                float za = z0 + (s - 1) * StairTreadM, zb = s == StairSteps ? c : za + StairTreadM, y = s * StairRiseM;
                int start = l.Pieces.Count;
                float cut = rng.Range(-0.6f, 0.6f);
                Dressed(l, key, f, -h, cut - 0.01f, -KitFloorM, y, za + 0.005f, zb - 0.005f, 0, 0);
                Dressed(l, key, f, cut + 0.01f, h, -KitFloorM, y, za + 0.005f, zb - 0.005f, 0, 0);
                Dressed(l, key, f, -h, h, -KitFloorM, y, za, zb, 1, 1);
                if (s % 4 == 0) Masonry(l, key, f, -h, h, -KitFloorM, y, za - 3 * StairTreadM, zb, 2, 2);
                Flat(l, start);
                for (float x = -0.9f; x <= 0.91f; x += 0.45f)
                    for (float z = za + 0.5f * StairTreadM; z < zb; z += StairTreadM) Col(l, f, x, z, 0.22f, -KitFloorM, y);
            }
            int segs = (int)Math.Round(StairsM);
            for (int j = 0; j < segs; j++)
            {
                float za = Math.Max(-e, -c + j), zb = Math.Min(e, -c + j + 1), cb = Ceiling(j), ct = (j + 1 < segs ? Ceiling(j + 1) : cb) + KitSlabM;
                Dressed(l, key, f, -h, h, cb, ct, za, zb);
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Masonry(l, key, f, sg < 0 ? -w : h, sg < 0 ? -h : w, -KitFloorM, ct, za, zb);
                    Line(l, f, sg * (h + LineR), za, sg * (h + LineR), zb, -KitFloorM, ct);
                }
            }
            Edge(l, key, f.Child(0, 0, 0, -1), c, Ceiling(0) + KitSlabM, KitFrameM);
            Edge(l, key, f.Child(0, KitStoreyM, 0), c, PortalHeightM + KitSlabM, KitFrameM);
        }

        static float Ceiling(int j) => PortalHeightM + StairTop(-0.5f * StairsM + j + 0.999f);

        static void Door(StructureLayout l, ulong key, in Frame f, int trim)
        {
            Corridor(l, key, f, false);
            const float dw = 0.8f, dh = 2.4f, t = 0.25f, fj = 0.25f;
            float h = 0.5f * PortalWidthM;
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var g = f.Child(0, 0, 0, sg);
                Dressed(l, key, g, dw, dw + fj, -KitFloorM, dh, -t, t);
                Masonry(l, key, g, dw + fj, h, -KitFloorM, PortalHeightM, -t, t);
                Col(l, g, h, 0, LineR, -KitFloorM, PortalHeightM);
            }
            Dressed(l, key, f, -dw - fj, dw + fj, dh, PortalHeightM, -t, t);
            Dressed(l, key, f, -dw, dw, -KitFloorM, 0.04f, -t, t, 0, 1);
            float lx = dw - 0.04f, len = 2 * dw;
            for (int k = 0; k < 4; k++)
                Box(l, key, f, lx, 0.04f, t + 0.02f + len * (k + 0.5f) / 4, 0.07f, dh - 0.08f, len / 4 - 0.01f, Plank, GV, 0, 0);
            Box(l, key, f, lx, 0.04f, t + 0.02f + 0.5f * len, 0.07f, dh - 0.08f, len, Plank, GV, 1, 2);
            for (int k = 0; k < 2; k++)
            {
                float y = 0.45f + 1.4f * k;
                Box(l, key, f, lx - 0.06f, y, t + 0.02f + 0.5f * len, 0.05f, 0.14f, len - 0.1f, Timber, GZ, 0, 0);
                Box(l, key, f, lx + 0.04f, y, t + 0.02f + 0.45f * len, 0.012f, 0.07f, 0.9f * len, trim, GZ, 0, 0);
                Box(l, key, f, dw + 0.03f, y, t + 0.03f, 0.06f, 0.1f, 0.06f, trim, GV, 0, 0);
            }
            Box(l, key, f, lx - 0.1f, 1.05f, t + 0.02f + 0.9f * len, 0.04f, 0.12f, 0.12f, trim, GV, 0, 0);
            for (float z = t + 0.2f; z < t + len; z += 0.4f) Col(l, f, lx, z, 0.12f, 0, dh);
        }

        static void Pillar(StructureLayout l, ulong key, in Frame f, int trim)
        {
            var rng = Rng.Stream(key, "dungeon-pillar");
            const float shaft = 0.8f, foot = 0.3f, neck = 3.6f;
            float c8 = Cos16(2), s8 = Sin16(2);
            Dressed(l, key, f, -0.6f, 0.6f, -0.2f, foot, -0.6f, 0.6f);
            int drums = 6;
            float y = foot;
            for (int k = 0; k < drums; k++)
            {
                float y1 = k == drums - 1 ? neck : y + (neck - foot) / drums * rng.Range(0.9f, 1.1f), sz = shaft * rng.Range(0.98f, 1.02f);
                for (int r = 0; r < 2; r++) Box(l, key, f, 0, y + 0.005f, 0, sz, y1 - y - 0.01f, sz, Stone, GX, 0, 0, r == 0 ? 1 : c8, r == 0 ? 0 : s8);
                y = y1;
            }
            for (int r = 0; r < 2; r++) Box(l, key, f, 0, foot, 0, shaft, neck - foot, shaft, Stone, GX, 1, r == 0 ? 2 : 1, r == 0 ? 1 : c8, r == 0 ? 0 : s8);
            Box(l, key, f, 0, 1.25f, 0, shaft + 0.04f, 0.07f, shaft + 0.04f, trim, GX, 0, 0, c8, s8);
            Dressed(l, key, f, -0.55f, 0.55f, neck, neck + 0.25f, -0.55f, 0.55f);
            Dressed(l, key, f, -0.6f, 0.6f, neck + 0.25f, KitRoomHeightM, -0.6f, 0.6f);
            Col(l, f, 0, 0, 0.5f, -0.2f, KitRoomHeightM);
        }
    }
}
