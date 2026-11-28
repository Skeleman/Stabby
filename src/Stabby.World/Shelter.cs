namespace Stabby.World
{
    public struct Roof
    {
        public float X, Z, Cos, Sin, Hx, Hz, Y;
        public bool Round;

        public bool Over(float x, float z)
        {
            float dx = x - X, dz = z - Z, u = (dx * Cos + dz * Sin) / Hx, v = (dz * Cos - dx * Sin) / Hz;
            return Round ? u * u + v * v <= 1 : u * u <= 1 && v * v <= 1;
        }
    }

    public struct RoofTemplate
    {
        public float OffsetX, OffsetZ, Hx, Hz, Y;
        public bool Round, Valid;
    }

    public interface IRoofList
    {
        int Count { get; }
        Roof this[int k] { get; }
    }

    public struct RoofArray : IRoofList
    {
        public Roof[] Items;
        public int Count => Items == null ? 0 : Items.Length;
        public Roof this[int k] => Items[k];
    }

    public static class Shelter
    {
        public const float ReachM = 32f, MarginM = 0.25f;

        public static float Floor<TG, TE>(TG g, TE e, float s, float x, float z, float top, float bottom) where TG : struct, IGroundHeight where TE : struct, IEditList
            => Walk.Surface(g, e, s, x, z, top, bottom);

        public static float Floor<TG, TE, TR>(TG g, TE e, TR roofs, float s, float x, float z, float top, float bottom) where TG : struct, IGroundHeight where TE : struct, IEditList where TR : struct, IRoofList
        {
            float f = Walk.Surface(g, e, s, x, z, top, bottom);
            for (int k = 0; k < roofs.Count; k++)
            {
                var r = roofs[k];
                if (r.Y > f && r.Y <= top && r.Over(x, z)) f = r.Y;
            }
            return f;
        }

        public static bool Covered<TG, TE>(TG g, TE e, float s, float x, float y, float z) where TG : struct, IGroundHeight where TE : struct, IEditList
            => Floor(g, e, s, x, z, y + ReachM, y - ReachM) > y + MarginM;

        public static bool Covered<TG, TE, TR>(TG g, TE e, TR roofs, float s, float x, float y, float z) where TG : struct, IGroundHeight where TE : struct, IEditList where TR : struct, IRoofList
            => Floor(g, e, roofs, s, x, z, y + ReachM, y - ReachM) > y + MarginM;

        public static RoofTemplate Template(ObjectDef d)
        {
            foreach (var p in d.Parts)
            {
                var t = Unit(p.Shape);
                if (!t.Valid) continue;
                float wide = System.Math.Max(p.ScaleX, p.ScaleZ);
                t.OffsetX = p.OffsetX;
                t.OffsetZ = p.OffsetZ;
                t.Hx *= t.Round ? wide : p.ScaleX;
                t.Hz *= t.Round ? wide : p.ScaleZ;
                t.Y = p.OffsetY + t.Y * p.ScaleY;
                return t;
            }
            return default;
        }

        static RoofTemplate Unit(int shape)
        {
            switch (shape)
            {
                case ObjectPart.Broadleaf: return new RoofTemplate { Hx = 0.36f, Hz = 0.36f, Y = 0.45f, Round = true, Valid = true };
                case ObjectPart.Conifer: return new RoofTemplate { Hx = 0.2f, Hz = 0.2f, Y = 0.3f, Round = true, Valid = true };
                case ObjectPart.DatePalm: return new RoofTemplate { Hx = 0.28f, Hz = 0.28f, Y = 0.85f, Round = true, Valid = true };
                case ObjectPart.House: return new RoofTemplate { Hx = 0.42f, Hz = 0.31f, Y = 2.5f * Structures.HouseU, Valid = true };
                case ObjectPart.Inn: return new RoofTemplate { Hx = 0.41f, Hz = 0.27f, Y = 2.5f * Structures.InnU, Valid = true };
                case ObjectPart.HermitHut: return new RoofTemplate { Hx = 0.3f, Hz = 0.23f, Y = 2.5f * Structures.HutU, Valid = true };
                case ObjectPart.Stall: return new RoofTemplate { Hx = 0.5f, Hz = 0.33f, Y = 0.8f, Valid = true };
                default: return default;
            }
        }

        public static Roof Place(in RoofTemplate t, in ObjectInstance o)
        {
            float c = 1 - 2 * (o.Qy * o.Qy + o.Qz * o.Qz), s = 2 * (o.Qx * o.Qz - o.Qw * o.Qy), len = (float)System.Math.Sqrt(c * c + s * s);
            if (len > 1e-5f) { c /= len; s /= len; } else { c = 1; s = 0; }
            float ox = t.OffsetX * o.Size, oz = t.OffsetZ * o.Size;
            return new Roof { X = o.X + ox * c - oz * s, Z = o.Z + ox * s + oz * c, Cos = c, Sin = s, Hx = t.Hx * o.Size, Hz = t.Hz * o.Size, Y = o.Y + t.Y * o.Size, Round = t.Round };
        }
    }
}
