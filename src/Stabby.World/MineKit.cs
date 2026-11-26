using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float MineSizeM = KitCellM, MinePostX = 1.7f, MinePostM = 0.22f, MineCapTopM = 3f, MineCapM = 0.25f, MineCapX = 2.2f;
        public const float MineGaugeX = 0.3f, MineRailTopM = 0.16f, MineSleeperM = 0.6f;

        static void Mine(int shape, ulong key, StructureLayout l)
        {
            var f = Frame.Root(1 / MineSizeM);
            var rng = new Rng(Rng.Mix(key ^ 0x3A1E5EEDUL));
            switch (shape)
            {
                case ObjectPart.MineTimber: MineTimber(l, key, f, ref rng); break;
                case ObjectPart.MineRail: MineRail(l, key, f, ref rng); break;
                case ObjectPart.MineCart: MineCart(l, key, f, ref rng); break;
            }
        }

        static void MineTimber(StructureLayout l, ulong key, in Frame f, ref Rng rng)
        {
            float capBottom = MineCapTopM - MineCapM;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (MinePostX + rng.Range(-0.04f, 0.04f)), w = MinePostM * rng.Range(0.9f, 1.15f), z = rng.Range(-0.05f, 0.05f);
                Box(l, key, f, x, -0.3f, z, w, capBottom + 0.3f, w, Timber, GV, 0, Tiers - 1, 1, 0, side * rng.Range(0f, 0.03f));
                Col(l, f, x, z, 0.5f * w, -0.3f, capBottom);
                Touch(l, f, x, 0, z, 0.6f * w);
                Touch(l, f, side * MineCapX, MineCapTopM, 0, 0.2f);
                Box(l, key, f, x - side * 0.2f, capBottom - 0.18f, z, rng.Range(0.14f, 0.2f), 0.18f, 0.24f, Timber, GX, 0, 0);
            }
            Box(l, key, f, 0, capBottom, 0, 2f * MineCapX, MineCapM, MineCapM * rng.Range(0.95f, 1.2f), Timber, GX);
            bool full = rng.NextFloat() < 0.6f;
            for (int b = -1; b <= 1; b++)
            {
                if (b != 0 && (!full || rng.NextFloat() < 0.25f)) continue;
                Box(l, key, f, b * 0.8f + rng.Range(-0.1f, 0.1f), MineCapTopM, rng.Range(-0.1f, 0.1f), 0.24f, 0.06f, rng.Range(1.0f, 1.5f), Plank, GZ, 0, 1);
            }
        }

        static void MineRail(StructureLayout l, ulong key, in Frame f, ref Rng rng)
        {
            float half = 0.5f * MineSizeM;
            for (float z = -half + 0.5f * MineSleeperM; z < half; z += MineSleeperM)
            {
                float jog = rng.Range(-0.06f, 0.06f), len = rng.Range(0.95f, 1.1f);
                Box(l, key, f, rng.Range(-0.04f, 0.04f), -0.08f, z + jog, len, 0.14f, 0.16f, Timber, GX, 0, 1);
                for (int side = -1; side <= 1; side += 2) Box(l, key, f, side * (MineGaugeX + 0.06f), 0.06f, z + jog, 0.04f, 0.03f, 0.04f, Iron, GX, 0, 0);
            }
            for (int side = -1; side <= 1; side += 2)
                Box(l, key, f, side * MineGaugeX, 0.06f, 0, 0.06f, MineRailTopM - 0.06f, MineSizeM, Iron, GZ);
            Box(l, key, f, 0, -0.08f, 0, 1f, 0.14f, MineSizeM, Timber, GZ, 2, 2);
            Touch(l, f, 0, 0, 0, 0.5f);
        }

        static void MineCart(StructureLayout l, ulong key, in Frame f, ref Rng rng)
        {
            float lift = MineRailTopM + 0.1f, w = rng.Range(0.95f, 1.05f), d = rng.Range(1.3f, 1.5f), h = rng.Range(0.7f, 0.85f), wall = 0.06f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Box(l, key, f, sx * MineGaugeX, MineRailTopM, sz * 0.45f * d, 0.08f, 0.24f, 0.24f, Iron, GX, 0, 1);
            Box(l, key, f, 0, lift, 0, w, 0.08f, d, Plank, GZ);
            Box(l, key, f, 0, lift + 0.08f, 0.5f * (d - wall), w, h, wall, Plank, GX, 0, 1);
            Box(l, key, f, 0, lift + 0.08f, -0.5f * (d - wall), w, h, wall, Plank, GX, 0, 1);
            Box(l, key, f, 0.5f * (w - wall), lift + 0.08f, 0, wall, h, d - 2f * wall, Plank, GZ, 0, 1);
            Box(l, key, f, -0.5f * (w - wall), lift + 0.08f, 0, wall, h, d - 2f * wall, Plank, GZ, 0, 1);
            Box(l, key, f, 0, lift + 0.08f, 0, w, h, d, Plank, GZ, 2, 2);
            for (int sz = -1; sz <= 1; sz += 2) Box(l, key, f, 0, lift + h + 0.02f, sz * 0.5f * d, w + 0.04f, 0.05f, 0.05f, Iron, GX, 0, 0);
            if (rng.NextFloat() < 0.7f) Box(l, key, f, rng.Range(-0.15f, 0.15f), lift + 0.2f, rng.Range(-0.2f, 0.2f), 0.6f * w, 0.5f * h, 0.6f * d, Stone, GX, 0, 1);
            Col(l, f, 0, 0, 0.5f * w, 0, lift + h);
            Touch(l, f, 0, MineRailTopM, 0, 0.4f);
        }
    }

    public sealed partial class ObjectField
    {
        const float MineFlatM = 0.15f;

        void EmitMine(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list, ref int ordinal)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            int kind = pl.Cave;
            var ground = new DetailGround { Detail = detail };
            float s = Features.LatticeStepM, cell = Structures.KitCellM, chance = pl.PerHundredM * cell / 100f;
            var mine = new List<FeatureEdit>();
            foreach (int e in caveCarves)
                if (solid[e].Shape == FeatureEdit.Mine && !Mines.Vertical(solid[e])) mine.Add(solid[e]);
            foreach (var c in mine)
            {
                if (c.MaxX < x0 || c.MinX >= x0 + sx || c.MaxZ < z0 || c.MinZ >= z0 + sz) continue;
                float ex = c.Bx - c.Ax, ez = c.Bz - c.Az;
                float dx = Math.Abs(ex) >= Math.Abs(ez) ? Math.Sign(ex) : 0f, dz = dx == 0f ? Math.Sign(ez) : 0f;
                float a0 = dx != 0f ? c.Ax : c.Az, a1 = dx != 0f ? c.Bx : c.Bz, sign = dx + dz;
                var spots = new List<float>();
                if (kind == CaveMineCart)
                {
                    bool dead = true;
                    foreach (var o in mine) if (Underground.Same(o.Ax, o.Az, c.Bx, c.Bz)) dead = false;
                    if (dead) spots.Add(a1 - sign * 0.5f * cell);
                }
                else
                {
                    float shift = kind == CaveMineRail ? 0.5f * cell : 0f;
                    int k = sign > 0 ? (int)Math.Ceiling((a0 - shift) / cell - 1e-3f) : (int)Math.Floor((a0 - shift) / cell + 1e-3f);
                    for (; sign > 0 ? k * cell + shift < a1 - 1e-3f : k * cell + shift > a1 + 1e-3f; k += (int)sign) spots.Add(k * cell + shift);
                }
                foreach (float at in spots)
                {
                    float x = dx != 0f ? at : c.Ax, z = dx != 0f ? c.Az : at;
                    if (x < x0 || z < z0 || x >= x0 + sx || z >= z0 + sz) continue;
                    var rng = new Rng(Rng.Mix(caveStream ^ key[d] ^ SpotKey(x, z)));
                    if (rng.NextFloat() >= chance) continue;
                    int variant = (int)(rng.NextU32() % (uint)def.Variants);
                    float top = c.Ay, bottom = c.Ay - c.R - 2f;
                    float floor = Walk.Surface(ground, RockAt(x, z), s, x, z, top, bottom);
                    if (floor <= bottom) continue;
                    if (kind == CaveMineRail)
                    {
                        float half = 0.45f * cell;
                        float fa = Walk.Surface(ground, RockAt(x - dx * half, z - dz * half), s, x - dx * half, z - dz * half, top, bottom);
                        float fb = Walk.Surface(ground, RockAt(x + dx * half, z + dz * half), s, x + dx * half, z + dz * half, top, bottom);
                        if (Math.Max(floor, Math.Max(fa, fb)) - Math.Min(floor, Math.Min(fa, fb)) > MineFlatM) continue;
                    }
                    var inst = new ObjectInstance { Def = d, Variant = variant, X = x, Y = floor, Z = z, Size = pl.SizeMinM };
                    Yaw((float)Math.Sqrt(0.5f * (1f + dz)), (dx < 0f ? -1f : 1f) * (float)Math.Sqrt(0.5f * (1f - dz)), false, ref inst);
                    if (MineFault(kind, inst) != null) continue;
                    inst.Id = ObjectIds.Make(chunk, d, ordinal++);
                    list.Add(inst);
                }
            }
        }

        static ulong SpotKey(float x, float z) => Rng.Mix((ulong)(uint)(int)Math.Floor(x * 2f) ^ ((ulong)(uint)(int)Math.Floor(z * 2f) << 32) ^ 0x111E5A17UL);

        bool MineSolid(ObjectInstance o, float lx, float ly, float lz)
        {
            float u = o.Size / Structures.MineSizeM, fz = 1f - 2f * o.Qy * o.Qy, fx = 2f * o.Qy * o.Qw;
            float x = o.X + u * (lx * fz + lz * fx), z = o.Z + u * (lz * fz - lx * fx), y = o.Y + u * ly;
            return Walk.Solid(new DetailGround { Detail = detail }, RockAt(x, z), Features.LatticeStepM, x, y, z) >= 0;
        }

        string MineFault(int kind, ObjectInstance o)
        {
            if (kind == CaveMineTimber)
            {
                float px = Structures.MinePostX, cap = Structures.MineCapTopM;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!MineSolid(o, side * px, -0.15f, 0)) return "post floats above the floor";
                    if (!MineSolid(o, side * (Structures.MineCapX - 0.05f), cap - 0.1f, 0)) return "cap end floats free of the rock";
                    if (MineSolid(o, side * px, 1.5f, 0)) return "post is buried in rock";
                    if (MineSolid(o, side * 1.2f, cap - 0.15f, 0)) return "cap is buried in rock";
                }
                if (MineSolid(o, 0, 0.3f, 0) || MineSolid(o, 0, cap - 0.4f, 0)) return "blocks the passage";
                return null;
            }
            float reach = kind == CaveMineRail ? 0.45f * Structures.MineSizeM : 0.6f;
            for (int t = -1; t <= 1; t++)
            {
                if (!MineSolid(o, 0, -0.1f, t * reach)) return "floats above the floor";
                if (MineSolid(o, 0, kind == CaveMineRail ? 0.5f : 1.1f, t * reach)) return "is buried in rock";
            }
            return null;
        }
    }
}
