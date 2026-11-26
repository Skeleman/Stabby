using System;

namespace Stabby.Game
{
    public static class PoolSize
    {
        public const float TrimSeconds = 0.25f;

        public static bool Oversized(int held, int need) => held > 3 * need + 1024;

        public static int Trimmed(int need) => Math.Max(need + need / 2, 64);

        public static bool TrimDue(int held, int need, float slack, float delay) => Oversized(held, need) && (need == 0 || slack >= delay);
    }
}
