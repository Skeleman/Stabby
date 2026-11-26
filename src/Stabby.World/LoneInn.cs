using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float RoadsideU = 1 / 19f;
        static readonly float[] RoadsideSeats = { -0.35f, 0.5f, 0, 0.52f, 0.35f, 0.5f };

        static void Roadside(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "inn-roadside");
            float side = rng.NextFloat() < 0.5f ? -1 : 1, rz = rng.Range(9.4f, 9.8f);
            Yaw(ref rng, 0.08f, out float c, out float s);
            var rail = f.Child(side * rng.Range(3.2f, 4.0f), 0, rz, c, s);
            float half = rng.Range(1.0f, 1.3f);
            for (int e = -1; e <= 1; e += 2)
            {
                Box(l, key, rail, e * half, -0.3f, 0, 0.14f, 1.35f, 0.14f, Timber, GV, 0, 1, 1, 0, rng.Range(-0.03f, 0.03f));
                Col(l, rail, e * half, 0, 0.1f, -0.3f, 1.05f);
                Touch(l, rail, e * half, 0, 0, 0.2f);
            }
            Box(l, key, rail, 0, 0.92f, 0, 2 * half + 0.3f, 0.12f, 0.12f, Timber, GX, 0, 2);
            for (int i = 0, rings = rng.Range(2, 4); i < rings; i++)
                Box(l, key, rail, rng.Range(-0.7f, 0.7f) * half, 0.78f, 0.08f, 0.02f, 0.14f, 0.02f, Iron, GV, 0, 0);

            var trough = rail.Child(side * (half + rng.Range(1.3f, 1.6f)), 0, rng.Range(-0.2f, 0.2f));
            float tl = rng.Range(1.5f, 1.8f), tw = 0.6f, th = 0.5f;
            for (int e = -1; e <= 1; e += 2)
            {
                Box(l, key, trough, 0, 0.12f, e * 0.5f * (tw - 0.06f), tl, th - 0.12f, 0.06f, Plank, GX, 0, 1);
                Box(l, key, trough, e * 0.5f * (tl - 0.06f), 0.12f, 0, 0.06f, th - 0.12f, tw - 0.12f, Plank, GZ, 0, 1);
                Box(l, key, trough, e * 0.32f * tl, -0.1f, 0, 0.14f, 0.22f, tw + 0.1f, Timber, GZ, 0, 1);
                Box(l, key, trough, e * 0.42f * tl, 0.12f, 0, 0.02f, th - 0.1f, tw + 0.01f, Iron, GV, 0, 0);
            }
            Box(l, key, trough, 0, 0.12f, 0, tl - 0.1f, 0.06f, tw - 0.1f, Plank, GX, 0, 1);
            Box(l, key, trough, 0, th - 0.12f, 0, tl - 0.12f, 0.02f, tw - 0.12f, Iron, GX, 0, 1);
            Box(l, key, trough, 0, -0.1f, 0, tl, th + 0.1f, tw, Plank, GX, 2, 2);
            Col(l, trough, 0, 0, 0.5f * tw, -0.1f, th);
            Touch(l, trough, 0, 0, 0, 0.55f * tl);
            if (rng.NextFloat() < 0.6f) Barrel(l, rng.NextU64(), trough.Child(-side * (0.5f * tl + 0.55f), 0, rng.Range(-0.3f, 0.1f)));

            float px = -side * rng.Range(2.4f, 3.2f), pz = rng.Range(9.6f, 10.0f), arm = side * rng.Range(-0.15f, 0.15f);
            Yaw(ref rng, 0.1f, out c, out s);
            var post = f.Child(px, 0, pz, c, s);
            Box(l, key, post, 0, -0.4f, 0, 0.18f, 3.0f, 0.18f, Timber, GV, 0, 2);
            Box(l, key, post, 0, 2.62f, 0, 0.26f, 0.08f, 0.26f, Timber, GV, 0, 0);
            Box(l, key, post, -side * 0.55f, 2.05f, 0.12f, 1.1f, 0.34f, 0.05f, Plank, GX, 0, 1, 1, arm);
            Box(l, key, post, -side * 1.12f, 2.1f, 0.12f, 0.14f, 0.24f, 0.05f, Plank, GX, 0, 0, 1, arm);
            if (rng.NextFloat() < 0.7f) Box(l, key, post, side * 0.45f, 1.55f, 0.12f, 0.9f, 0.28f, 0.05f, Plank, GX, 0, 1, 1, rng.Range(-0.12f, 0.12f));
            for (int e = -1; e <= 1; e += 2) Box(l, key, post, -side * 0.55f + e * 0.42f, 2.18f, 0.15f, 0.04f, 0.04f, 0.02f, Iron, GX, 0, 0);
            Col(l, post, 0, 0, 0.12f, -0.4f, 2.6f);
            Touch(l, post, 0, 0, 0, 0.3f);
        }
    }

    public sealed partial class ObjectField
    {
        public const float LoneInnRoadMinM = 17f, LoneInnRoadM = 30f, LoneInnRoadAimM = 19f, LoneInnProbeM = 12f, LoneInnReliefM = 0.8f, LoneInnBuildingM = 300f;
        public const int LoneInnTries = 64;

        public string LoneInnFault(ObjectInstance o)
        {
            float rd = RoadSide(o.X, o.Z, out _, out _);
            if (!(rd >= LoneInnRoadMinM && rd <= LoneInnRoadM)) return "is not on a road shoulder";
            if (OnRoad(o.X, o.Z, SiteRadius(o))) return "lies on a road";
            SeatRange(detail, cm, Seats(lib.Objects[o.Def]), o, out float lo, out float hi);
            if (hi - lo > Structures.SeatM) return "stands on uneven ground";
            float span = WorldChunks.Size * cm;
            int reach = (int)Math.Ceiling(LoneInnBuildingM / span), cx = (int)(o.X / span), cz = (int)(o.Z / span);
            TownPlans();
            for (int z = Math.Max(0, cz - reach); z <= Math.Min(across - 1, cz + reach); z++)
                for (int x = Math.Max(0, cx - reach); x <= Math.Min(across - 1, cx + reach); x++)
                {
                    var near = NearBuilding(o, Built(x, z));
                    if (near < 0 && townChunks.TryGetValue(z * across + x, out var town)) near = NearBuilding(o, town);
                    if (near >= 0) return $"stands within {LoneInnBuildingM:F0} m of {lib.Objects[near].Id}";
                }
            return null;
        }

        static int NearBuilding(ObjectInstance o, IEnumerable<ObjectInstance> sites)
        {
            foreach (var s in sites)
            {
                float dx = s.X - o.X, dz = s.Z - o.Z;
                if (dx * dx + dz * dz < LoneInnBuildingM * LoneInnBuildingM) return s.Def;
            }
            return -1;
        }
    }
}
