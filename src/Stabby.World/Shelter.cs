namespace Stabby.World
{
    public static class Shelter
    {
        public const float ReachM = 32f, MarginM = 0.25f;

        public static float Floor<TG, TE>(TG g, TE e, float s, float x, float z, float top, float bottom) where TG : struct, IGroundHeight where TE : struct, IEditList
            => Walk.Surface(g, e, s, x, z, top, bottom);

        public static bool Covered<TG, TE>(TG g, TE e, float s, float x, float y, float z) where TG : struct, IGroundHeight where TE : struct, IEditList
            => Floor(g, e, s, x, z, y + ReachM, y - ReachM) > y + MarginM;
    }
}
